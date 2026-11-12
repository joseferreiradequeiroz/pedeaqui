namespace PedeAqui.RateLimiting;

public class RateLimitOptions
{
    public const string SECTION = "RateLimit";

    public RateLimitRule Login { get; set; } = new();
    public RateLimitRule Authenticated { get; set; } = new();

    public RateLimitRule RuleOf(RateLimitPolicy policy)
    {
        return policy switch
        {
            RateLimitPolicy.Login => Login,
            RateLimitPolicy.Authenticated => Authenticated,
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, null)
        };
    }
}

public class RateLimitRule
{
    public int Limit { get; set; }
    public int WindowSeconds { get; set; }
}
