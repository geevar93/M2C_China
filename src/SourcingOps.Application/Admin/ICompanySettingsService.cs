namespace SourcingOps.Application.Admin;

/// <summary>M6 contract §0 — the mechanism that unblocks FSD Q9c without inventing its values.</summary>
public interface ICompanySettingsService
{
    /// <summary>Never null — returns an all-null shape when the singleton row does not exist yet.</summary>
    Task<CompanySettingsDto> GetAsync(CancellationToken ct = default);

    /// <summary>Creates the singleton row on first call, updates it thereafter. Always succeeds (no validation beyond field length, enforced by the column configuration).</summary>
    Task<CompanySettingsDto> UpsertAsync(UpsertCompanySettingsRequest request, Guid actorUserId, CancellationToken ct = default);
}
