namespace PedeAqui.RateLimiting;

public enum RateLimitPolicy
{
    Login,
    Authenticated
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RateLimitPolicyAttribute : Attribute
{
    public RateLimitPolicyAttribute(RateLimitPolicy policy)
    {
        Policy = policy;
    }

    public RateLimitPolicy Policy { get; }
}
