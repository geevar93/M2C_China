namespace SourcingOps.Application.Interfaces;

/// <summary>
/// Writes to `audit_logs` (TECH_SPEC §4.3; FR-ADM-04). Every mutating service call site
/// goes through this — wired into auth events now (E1-09); ready for every mutating
/// feature service from M2 onward.
/// </summary>
public interface IAuditLogger
{
    /// <param name="userId">Null when the actor is unknown (e.g. a login attempt against a non-existent email).</param>
    /// <param name="details">Serialized to JSON and stored in the jsonb `details` column. Pass an anonymous object or null.</param>
    Task LogAsync(Guid? userId, string action, string entityType, string? entityId, object? details = null, CancellationToken ct = default);
}
