namespace SourcingOps.Infrastructure.Persistence.Seed;

/// <summary>
/// Bound from configuration section "Bootstrap". Not in TECH_SPEC/ACTION_PLAN as written —
/// see the E1-03 addition note in DbSeeder for why this exists.
///
/// <para>
/// <b>The defaults below are NOT the effective values for the API host.</b>
/// <c>Infrastructure/DependencyInjection.cs</c> does
/// <c>configuration.GetSection("Bootstrap").Bind(bootstrapOptions)</c>, and <c>Bind</c>
/// OVERWRITES a property whenever the configuration carries a key for it — including with
/// an empty string. So an <c>appsettings.json</c> entry of <c>""</c> (or a
/// <c>${BOOTSTRAP_ADMIN_PASSWORD:-}</c> compose fallback) does not "fall back" to the
/// default here: it wins, and the seeder then generates a random password. The explicit
/// values in <c>appsettings.json</c> and <c>docker-compose.yml</c> are therefore REQUIRED,
/// not redundant duplication of these defaults. These defaults only apply where nothing
/// binds over them (e.g. a directly-constructed instance in tests).
/// </para>
/// </summary>
public sealed class BootstrapAdminOptions
{
    public string AdminEmail { get; set; } = "owner@sourcingops.local";

    /// <summary>
    /// If empty, a random temp password is generated and logged once on first run — and that
    /// generated case is also the one that leaves <c>MustChangePassword=true</c> on the seeded
    /// account (a secret written to a log must be rotated). An explicitly configured password
    /// is a deliberately chosen credential and is seeded with <c>MustChangePassword=false</c>,
    /// so it is usable exactly as configured. See <see cref="DbSeeder"/>.
    /// </summary>
    public string? AdminPassword { get; set; } = "Welcome@123";
}
