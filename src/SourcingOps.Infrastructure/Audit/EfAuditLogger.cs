using System.Text.Json;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Infrastructure.Audit;

/// <summary>
/// Writes to `audit_logs` (TECH_SPEC §4.3; FR-ADM-04). Adds its own row directly and calls
/// <c>SaveChangesAsync</c> immediately, independent of whatever unit of work the caller is
/// in — an audit write should not be lost just because a later step in the same request
/// fails, and should not depend on the caller remembering to save.
/// </summary>
public sealed class EfAuditLogger : IAuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db;

    public EfAuditLogger(AppDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(Guid? userId, string action, string entityType, string? entityId, object? details = null, CancellationToken ct = default)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OccurredAt = DateTime.UtcNow,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details, JsonOptions)
        });

        await _db.SaveChangesAsync(ct);
    }
}
