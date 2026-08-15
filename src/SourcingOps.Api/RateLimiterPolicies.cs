namespace SourcingOps.Api;

public static class RateLimiterPolicies
{
    public const string Login = "login";

    /// <summary>
    /// E9-10: the anonymous share-link endpoint. Not the control that makes it safe (a 256-bit
    /// token is), but it stops an unauthenticated scanner from spending the box's CPU for free.
    /// </summary>
    public const string SharedDocument = "shared-document";
}
