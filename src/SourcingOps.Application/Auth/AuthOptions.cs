namespace SourcingOps.Application.Auth;

/// <summary>Plain settings POCO bound from configuration at the composition root (kept out of `IOptions&lt;T&gt;` here so Application stays framework-light).</summary>
public sealed class AuthOptions
{
    /// <summary>~8h per TECH_SPEC §4.2.</summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 480;

    public int RefreshTokenLifetimeDays { get; set; } = 30;
}
