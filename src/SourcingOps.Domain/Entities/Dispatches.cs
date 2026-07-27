namespace SourcingOps.Domain.Entities;

/// <summary>
/// WhatsApp click-to-chat dispatch log entry (FR-WA-04). TECH_SPEC §6 models this
/// against a catalog document only; E8-06 (invoice dispatch, milestone M6) will need a
/// follow-up migration to also reference an invoice — not built here as it is out of
/// this milestone's scope.
/// </summary>
public class Dispatch
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid CatalogDocumentId { get; set; }
    public CatalogDocument CatalogDocument { get; set; } = null!;

    public Guid StaffUserId { get; set; }
    public User StaffUser { get; set; } = null!;

    public string Message { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}
