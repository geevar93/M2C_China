using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Common;
using SourcingOps.Domain.Constants;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotent startup seeding (ACTION_PLAN E1-03): the full permission catalog, the
/// SuperAdmin/Associate roles and their mapping, the six FSD categories, both service
/// types, and the default lead/shipment/invoice/vendor status lookups. Re-running never
/// duplicates rows and always tops up anything missing (e.g. a permission added in a
/// later release).
///
/// ADDITION beyond the written story: also seeds one bootstrap Super Admin user with
/// MustChangePassword=true, whose generated password is logged once on the run that
/// creates it and never again. Rationale: M1's exit criterion is "a user logs in", but
/// account creation (E11-01) is M2 scope — without this seed there is no way to
/// authenticate at all between M1 and M2. See the build report for the full rationale.
/// </summary>
public sealed class DbSeeder
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly BootstrapAdminOptions _bootstrapOptions;
    private readonly ILogger<DbSeeder> _logger;

    public DbSeeder(AppDbContext db, IPasswordHasher passwordHasher, BootstrapAdminOptions bootstrapOptions, ILogger<DbSeeder> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _bootstrapOptions = bootstrapOptions;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var permissionsByCode = await SeedPermissionsAsync(ct);
        var rolesByName = await SeedRolesAsync(ct);
        await SeedRolePermissionMappingAsync(rolesByName, permissionsByCode, ct);

        await SeedCategoriesAsync(ct);
        await SeedLookupAsync(_db.ServiceTypes, SeedDefaults.ServiceTypes, ct);
        await SeedLookupAsync(_db.LeadStatuses, SeedDefaults.LeadStatuses, ct);
        await SeedLookupAsync(_db.ShipmentStatuses, SeedDefaults.ShipmentStatuses, ct);
        await SeedLookupAsync(_db.InvoiceStatuses, SeedDefaults.InvoiceStatuses, ct);
        await SeedLookupAsync(_db.VendorStatuses, SeedDefaults.VendorStatuses, ct);
        await SeedLookupAsync(_db.DocumentTypes, SeedDefaults.DocumentTypes, ct);

        await _db.SaveChangesAsync(ct);

        // Bootstrap admin needs the SuperAdmin role row to already be committed.
        await SeedBootstrapAdminAsync(rolesByName, ct);
    }

    private async Task<Dictionary<string, Permission>> SeedPermissionsAsync(CancellationToken ct)
    {
        var existing = await _db.Permissions.ToDictionaryAsync(p => p.Code, ct);

        foreach (var code in PermissionCodes.All)
        {
            if (existing.ContainsKey(code))
            {
                continue;
            }

            var permission = new Permission { Id = Guid.NewGuid(), Code = code };
            _db.Permissions.Add(permission);
            existing[code] = permission;
        }

        return existing;
    }

    private async Task<Dictionary<string, Role>> SeedRolesAsync(CancellationToken ct)
    {
        var existing = await _db.Roles.ToDictionaryAsync(r => r.Name, ct);

        foreach (var name in RoleNames.All)
        {
            if (existing.ContainsKey(name))
            {
                continue;
            }

            var role = new Role { Id = Guid.NewGuid(), Name = name };
            _db.Roles.Add(role);
            existing[name] = role;
        }

        return existing;
    }

    private async Task SeedRolePermissionMappingAsync(
        Dictionary<string, Role> rolesByName,
        Dictionary<string, Permission> permissionsByCode,
        CancellationToken ct)
    {
        // Roles/permissions created above may not have an Id assigned in the DB yet if
        // they're new — EF Core client-generated Guids are already set, so this is safe
        // to read even before SaveChanges.
        var existingLinks = await _db.RolePermissions
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .ToListAsync(ct);
        var existingLinkSet = existingLinks.Select(l => (l.RoleId, l.PermissionId)).ToHashSet();

        void EnsureLink(Role role, Permission permission)
        {
            if (existingLinkSet.Contains((role.Id, permission.Id)))
            {
                return;
            }

            _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            existingLinkSet.Add((role.Id, permission.Id));
        }

        var superAdmin = rolesByName[RoleNames.SuperAdmin];
        var associate = rolesByName[RoleNames.Associate];

        foreach (var code in PermissionCodes.All)
        {
            var permission = permissionsByCode[code];
            EnsureLink(superAdmin, permission); // SuperAdmin: every permission

            if (!PermissionCodes.AdminOnly.Contains(code))
            {
                EnsureLink(associate, permission); // Associate: everything except Admin.*
            }
        }
    }

    private async Task SeedCategoriesAsync(CancellationToken ct)
    {
        var existing = await _db.Categories.ToDictionaryAsync(c => c.Name, ct);

        foreach (var (name, sortOrder) in SeedDefaults.Categories)
        {
            if (existing.TryGetValue(name, out var category))
            {
                // Self-heal (N-8): a row that already existed before IsSystemDefault was
                // introduced — or was otherwise reset — is re-flagged on this run rather
                // than needing a one-off data migration. Never touches IsActive/SortOrder:
                // a Super Admin's own retire/reorder choices on a default row must survive.
                if (!category.IsSystemDefault)
                {
                    category.IsSystemDefault = true;
                }
                continue;
            }

            _db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = name, IsActive = true, SortOrder = sortOrder, IsSystemDefault = true });
        }
    }

    private async Task SeedLookupAsync<TEntity>(DbSet<TEntity> set, (string Code, string Label, int SortOrder)[] defaults, CancellationToken ct)
        where TEntity : class, ILookupEntity, new()
    {
        var existing = await set.ToDictionaryAsync(e => e.Code, ct);

        foreach (var (code, label, sortOrder) in defaults)
        {
            if (existing.TryGetValue(code, out var entity))
            {
                // Self-heal (N-8) — see the identical comment in SeedCategoriesAsync above.
                if (!entity.IsSystemDefault)
                {
                    entity.IsSystemDefault = true;
                }
                continue;
            }

            set.Add(new TEntity { Id = Guid.NewGuid(), Code = code, Label = label, IsActive = true, SortOrder = sortOrder, IsSystemDefault = true });
        }
    }

    private async Task SeedBootstrapAdminAsync(Dictionary<string, Role> rolesByName, CancellationToken ct)
    {
        var email = (_bootstrapOptions.AdminEmail ?? "admin@sourcingops.local").Trim().ToLowerInvariant();
        var alreadyExists = await _db.Users.AnyAsync(u => u.Email == email, ct);
        if (alreadyExists)
        {
            return; // already bootstrapped on a prior run — never reprint or reset the password
        }

        var isGenerated = string.IsNullOrWhiteSpace(_bootstrapOptions.AdminPassword);
        var password = isGenerated ? TempPasswordGenerator.Generate() : _bootstrapOptions.AdminPassword!;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "Super Admin",
            Email = email,
            MustChangePassword = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.Hash(user, password);

        _db.Users.Add(user);

        var superAdminRoleId = rolesByName[RoleNames.SuperAdmin].Id;
        _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = superAdminRoleId });

        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "================================================================\n" +
            "BOOTSTRAP SUPER ADMIN CREATED (shown once — will not be shown again)\n" +
            "  Email:    {Email}\n" +
            "  Password: {Password}\n" +
            "  Source:   {Source}\n" +
            "  This account must change its password on first login.\n" +
            "================================================================",
            email, password, isGenerated ? "generated" : "Bootstrap:AdminPassword config");
    }
}
