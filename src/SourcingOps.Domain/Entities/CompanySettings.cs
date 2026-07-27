namespace SourcingOps.Domain.Entities;

/// <summary>
/// Single-row table holding the invoice billing block (FR-BIL-07, FSD Q9; TECH_SPEC §6).
/// Shape only this pass — deliberately NOT seeded with any values. FSD Q9c (the actual
/// legal entity name, GSTIN, registered address, bank details) is still outstanding with
/// the business (ACTION_PLAN OI-9). Anything built later that needs this data (invoice PDF
/// rendering, E8) must fail with a clear, explicit error when the row is absent or a
/// required field is still null — never render a blank/placeholder value onto what is
/// meant to be a real financial document.
///
/// Enforced as single-row via a fixed-id CHECK constraint (see
/// Infrastructure/Persistence/Configurations/CompanySettingsConfiguration), not a data
/// migration that inserts an empty row — inserting nothing means "not configured yet" is
/// representable as "table is empty", rather than as a row full of nulls that looks the
/// same as a row someone forgot to fill in.
/// </summary>
public class CompanySettings
{
    /// <summary>Always <see cref="SingletonId"/> — enforced by a DB CHECK constraint, not just convention.</summary>
    public Guid Id { get; set; } = SingletonId;

    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public string? LegalEntityName { get; set; }
    public string? Gstin { get; set; }
    public string? RegisteredAddress { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfsc { get; set; }
    public string? BankBranch { get; set; }
    public string? InvoiceNumberPrefix { get; set; }
    public string? LogoFilePath { get; set; }
    public string? DeclarationText { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedByUserId { get; set; }
    public User? UpdatedBy { get; set; }
}
