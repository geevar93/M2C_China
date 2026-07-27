namespace SourcingOps.Domain.Entities;

/// <summary>Append-only audit trail row (FR-ADM-04, FSD Auditability NFR).</summary>
public class AuditLog
{
    public Guid Id { get; set; }

    /// <summary>Nullable — some audited events (e.g. a failed login for an unknown email) have no known user.</summary>
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Stored as text so it can hold a Guid or a natural key without a second nullable column.</summary>
    public string? EntityId { get; set; }
    public DateTime OccurredAt { get; set; }

    /// <summary>
    /// Raw JSON text, mapped by Infrastructure to a Postgres jsonb column
    /// (Domain stays framework-free — no System.Text.Json.JsonDocument reference here).
    /// </summary>
    public string? DetailsJson { get; set; }
}
