namespace SourcingOps.Application.Admin;

/// <summary>
/// The billing-block singleton (M6 contract §0). Returned as an all-null shape when the row is
/// absent — "not configured yet" is representable without inventing placeholder values, per
/// <c>CompanySettings</c>'s own doc comment. <see cref="UpdatedAt"/>/<see cref="UpdatedByName"/>
/// are null until the first save.
/// </summary>
public sealed record CompanySettingsDto(
    string? LegalEntityName,
    string? Gstin,
    string? RegisteredAddress,
    string? BankAccountName,
    string? BankAccountNumber,
    string? BankIfsc,
    string? BankBranch,
    string? InvoiceNumberPrefix,
    string? DeclarationText,
    DateTime? UpdatedAt,
    string? UpdatedByName);

/// <summary>
/// <c>PUT /admin/company-settings</c> body — upserts the singleton. Every field is optional
/// free text; the service trims blank strings to null so "cleared in the form" and "never set"
/// are indistinguishable, which is the correct behaviour for a row a Super Admin edits
/// incrementally over time (M6 contract §0 — FSD Q9c's values remain outstanding).
/// </summary>
public sealed record UpsertCompanySettingsRequest(
    string? LegalEntityName,
    string? Gstin,
    string? RegisteredAddress,
    string? BankAccountName,
    string? BankAccountNumber,
    string? BankIfsc,
    string? BankBranch,
    string? InvoiceNumberPrefix,
    string? DeclarationText);
