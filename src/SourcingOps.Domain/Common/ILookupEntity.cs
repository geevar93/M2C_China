namespace SourcingOps.Domain.Common;

/// <summary>Shape shared by every Code/Label/SortOrder configurable-master-data lookup table (FSD §3.3).</summary>
public interface ILookupEntity
{
    Guid Id { get; set; }
    string Code { get; set; }
    string Label { get; set; }
    bool IsActive { get; set; }
    int SortOrder { get; set; }

    /// <summary>
    /// True only for rows created by <c>DbSeeder</c> (ACTION_PLAN N-8). Retire-only: the
    /// E3-08 delete path rejects a row with this flag set with a 409, regardless of
    /// reference count — hard-deleting a seeded default (e.g. the "ACTIVE" vendor status)
    /// silently breaks the app, and the idempotent seeder only fills gaps left by a missing
    /// code, so it would never restore a wrongly-deleted default on its own. DbSeeder also
    /// self-heals this flag onto any pre-existing row that already matches a seed default's
    /// code/name, so an existing database picks it up on next startup without a data
    /// migration. User-added custom rows always have this false and remain hard-deletable
    /// while unreferenced, unchanged from the original E3-08 behaviour.
    /// </summary>
    bool IsSystemDefault { get; set; }
}
