using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Admin;

/// <summary>
/// Implements M6 contract §0: the mechanism that converts FSD Q9c from an engineering blocker
/// into a data-entry task. The singleton is genuinely absent until the first PUT (see
/// <c>CompanySettings</c>'s doc comment on why no row is seeded) — <see cref="GetAsync"/>
/// synthesizes an all-null <see cref="CompanySettingsDto"/> in that case rather than 404ing,
/// since "not configured" is a legitimate, expected state for every admin screen to render,
/// not an error.
/// </summary>
public sealed class CompanySettingsService : ICompanySettingsService
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;

    public CompanySettingsService(IAppDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<CompanySettingsDto> GetAsync(CancellationToken ct = default)
    {
        var settings = await _db.CompanySettings
            .Include(s => s.UpdatedBy)
            .FirstOrDefaultAsync(s => s.Id == CompanySettings.SingletonId, ct);

        return settings is null ? EmptyDto() : MapDto(settings);
    }

    public async Task<CompanySettingsDto> UpsertAsync(UpsertCompanySettingsRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var settings = await _db.CompanySettings.FirstOrDefaultAsync(s => s.Id == CompanySettings.SingletonId, ct);

        var isCreate = settings is null;
        if (settings is null)
        {
            settings = new CompanySettings { Id = CompanySettings.SingletonId };
            _db.CompanySettings.Add(settings);
        }

        settings.LegalEntityName = Trim(request.LegalEntityName);
        settings.Gstin = Trim(request.Gstin);
        settings.RegisteredAddress = Trim(request.RegisteredAddress);
        settings.BankAccountName = Trim(request.BankAccountName);
        settings.BankAccountNumber = Trim(request.BankAccountNumber);
        settings.BankIfsc = Trim(request.BankIfsc);
        settings.BankBranch = Trim(request.BankBranch);
        settings.InvoiceNumberPrefix = Trim(request.InvoiceNumberPrefix);
        settings.DeclarationText = Trim(request.DeclarationText);
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedByUserId = actorUserId;

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, isCreate ? "CompanySettingsCreated" : "CompanySettingsUpdated",
            "CompanySettings", CompanySettings.SingletonId.ToString(),
            new { settings.LegalEntityName, HasGstin = settings.Gstin != null, settings.InvoiceNumberPrefix }, ct);

        var updatedBy = await _db.Users.FindAsync([actorUserId], ct);
        return MapDto(settings, updatedBy?.Name);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CompanySettingsDto EmptyDto() =>
        new(null, null, null, null, null, null, null, null, null, null, null);

    private static CompanySettingsDto MapDto(CompanySettings s, string? updatedByNameOverride = null) => new(
        s.LegalEntityName, s.Gstin, s.RegisteredAddress,
        s.BankAccountName, s.BankAccountNumber, s.BankIfsc, s.BankBranch,
        s.InvoiceNumberPrefix, s.DeclarationText,
        s.UpdatedAt.HasValue ? DateTime.SpecifyKind(s.UpdatedAt.Value, DateTimeKind.Utc) : null,
        updatedByNameOverride ?? s.UpdatedBy?.Name);
}
