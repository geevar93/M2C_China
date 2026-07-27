namespace SourcingOps.Domain.Constants;

/// <summary>Seeded role names (TECH_SPEC §4.3). Roles are additive at the data level.</summary>
public static class RoleNames
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Associate = "Associate";

    public static readonly string[] All = [SuperAdmin, Associate];
}
