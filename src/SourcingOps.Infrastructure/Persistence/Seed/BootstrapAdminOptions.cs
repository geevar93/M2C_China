namespace SourcingOps.Infrastructure.Persistence.Seed;

/// <summary>
/// Bound from configuration section "Bootstrap". Not in TECH_SPEC/ACTION_PLAN as written —
/// see the E1-03 addition note in DbSeeder for why this exists.
/// </summary>
public sealed class BootstrapAdminOptions
{
    public string AdminEmail { get; set; } = "admin@sourcingops.local";

    /// <summary>If empty, a random temp password is generated and logged once on first run.</summary>
    public string? AdminPassword { get; set; }
}
