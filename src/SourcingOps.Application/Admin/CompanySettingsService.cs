using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Constants;
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
        // Uppercased as well as trimmed, matching the BUYER-side normalisation in
        // CustomerService.NormalizeAndValidateGstin (N-37). Both GSTINs print on the same
        // invoice PDF — the seller's in the issuer block, the buyer's in Bill To — so
        // normalising them differently would render the same kind of identifier two ways
        // on one document. GSTINs are canonically uppercase.
        // NOT length/alphanumeric-validated here, deliberately, unlike the buyer side:
        // this is a Super-Admin-only settings screen, and an over-strict check that
        // refuses to save would block the one path that unblocks invoicing at all (H-1).
        settings.Gstin = Trim(request.Gstin)?.ToUpperInvariant();
        // Same rule CustomerService applies to the buyer's: an explicit code must be a real
        // one, blank derives from the GSTIN prefix. This value decides CGST+SGST versus IGST
        // on every invoice issued, so a junk code must not be storable.
        settings.StateCode = NormalizeAndValidateStateCode(request.StateCode, settings.Gstin);
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
        new(null, null, null, null, null, null, null, null, null, null, null, null, null);

    private static CompanySettingsDto MapDto(CompanySettings s, string? updatedByNameOverride = null) => new(
        s.LegalEntityName, s.Gstin,
        s.StateCode, IndianStateCodes.NameFor(s.StateCode),
        s.RegisteredAddress,
        s.BankAccountName, s.BankAccountNumber, s.BankIfsc, s.BankBranch,
        s.InvoiceNumberPrefix, s.DeclarationText,
        s.UpdatedAt.HasValue ? DateTime.SpecifyKind(s.UpdatedAt.Value, DateTimeKind.Utc) : null,
        updatedByNameOverride ?? s.UpdatedBy?.Name);

    private static string? NormalizeAndValidateStateCode(string? value, string? gstin)
    {
        var trimmed = Trim(value);
        if (trimmed is null)
        {
            return IndianStateCodes.FromGstin(gstin);
        }

        if (!IndianStateCodes.IsValid(trimmed))
        {
            throw new AppValidationException("stateCode", $"'{trimmed}' is not a valid Indian GST state code.");
        }

        return trimmed;
    }
}
