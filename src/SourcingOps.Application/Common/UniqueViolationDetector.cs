using Microsoft.EntityFrameworkCore;

namespace SourcingOps.Application.Common;

/// <summary>
/// Recognises a database unique-constraint violation inside a <see cref="DbUpdateException"/>
/// so <c>ShipmentService</c> can retry reference generation on a lost race (deviation D-i).
///
/// Reads the provider exception's <c>SqlState</c> by duck typing rather than by catching
/// <c>Npgsql.PostgresException</c> directly, because <c>Application</c> must not reference the
/// database provider (TECH_SPEC §4.1 — the whole point of the narrow <c>IAppDbContext</c>
/// seam). Npgsql's exception exposes <c>SqlState</c> as a public string property, and
/// <c>23505</c> is the SQL-standard unique_violation code, so this is stable against Npgsql
/// version changes in a way that message-string matching would not be.
///
/// Note this predicate can only ever return true against a real relational provider. The EF
/// Core InMemory provider used by the Application-layer unit tests does not enforce unique
/// indexes at all, so the retry loop's behaviour under a genuine collision is proven by the
/// integration suite (Testcontainers Postgres) and the live run, not by unit tests.
/// </summary>
public static class UniqueViolationDetector
{
    private const string PostgresUniqueViolation = "23505";

    public static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            var sqlState = inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string;
            if (string.Equals(sqlState, PostgresUniqueViolation, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
