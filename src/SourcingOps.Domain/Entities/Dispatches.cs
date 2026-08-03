namespace SourcingOps.Domain.Entities;

/// <summary>
/// WhatsApp click-to-chat dispatch log entry (FR-WA-04). Originally modelled against a catalog
/// document only (TECH_SPEC §6); M6/E8-06 added the invoice half predicted by this class's own
/// earlier doc comment. Exactly one of <see cref="CatalogDocumentId"/>/<see cref="InvoiceId"/>
/// is ever set — enforced by a DB CHECK constraint (see <c>DispatchConfiguration</c>), the same
/// database-enforced-invariant precedent <c>CompanySettings</c> already uses for its
/// single-row constraint, rather than leaving "exactly one" as an unenforced convention.
/// </summary>
public class Dispatch
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid? CatalogDocumentId { get; set; }
    public CatalogDocument? CatalogDocument { get; set; }

    /// <summary>M6/E8-06: the invoice half of the dispatch target. See this class's doc comment for the CHECK constraint.</summary>
    public Guid? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public Guid StaffUserId { get; set; }
    public User StaffUser { get; set; } = null!;

    public string Message { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}
