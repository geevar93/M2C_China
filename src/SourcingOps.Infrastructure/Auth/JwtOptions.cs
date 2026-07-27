namespace SourcingOps.Infrastructure.Auth;

/// <summary>Bound from configuration section "Jwt". Signing key comes from env (`Jwt__SigningKey`), never committed.</summary>
public sealed class JwtOptions
{
    public string SigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "SourcingOps";
    public string Audience { get; set; } = "SourcingOps.Client";
}
