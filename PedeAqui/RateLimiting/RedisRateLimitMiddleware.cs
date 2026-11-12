using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using PedeAqui.Domain.Services;
using PedeAqui.Exception;

namespace PedeAqui.RateLimiting;

public class RedisRateLimitMiddleware
{
    private const string UNKNOWN_CLIENT = "unknown";

    private readonly RequestDelegate _next;
    private readonly RateLimitOptions _options;

    public RedisRateLimitMiddleware(RequestDelegate next, IOptions<RateLimitOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext httpContext, IRateLimiter rateLimiter, IProblemDetailsService problemDetailsService)
    {
        var policy = PolicyOf(httpContext);
        var subject = policy is null ? null : SubjectOf(httpContext, policy.Value);

        if (policy is null || subject is null)
        {
            await _next(httpContext);
            return;
        }

        var rule = _options.RuleOf(policy.Value);
        var decision = await rateLimiter.Acquire(
            $"ratelimit:{policy.Value.ToString().ToLowerInvariant()}:{subject}",
            rule.Limit,
            TimeSpan.FromSeconds(rule.WindowSeconds),
            httpContext.RequestAborted);

        if (decision.Allowed)
        {
            await _next(httpContext);
            return;
        }

        await Reject(httpContext, problemDetailsService, decision);
    }

    private static RateLimitPolicy? PolicyOf(HttpContext httpContext)
    {
        var metadata = httpContext.GetEndpoint()?.Metadata;

        if (metadata?.GetMetadata<RateLimitPolicyAttribute>() is { } declared)
            return declared.Policy;

        return metadata?.GetMetadata<IAuthorizeData>() is null ? null : RateLimitPolicy.Authenticated;
    }

    private static string? SubjectOf(HttpContext httpContext, RateLimitPolicy policy)
    {
        return policy == RateLimitPolicy.Login
            ? httpContext.Connection.RemoteIpAddress?.ToString() ?? UNKNOWN_CLIENT
            : httpContext.User.FindFirstValue(ClaimTypes.Sid);
    }

    private static async Task Reject(HttpContext httpContext, IProblemDetailsService problemDetailsService, RateLimitDecision decision)
    {
        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds));

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();

        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = StatusCodes.Status429TooManyRequests, Detail = ResourceErrorMessages.RATE_LIMIT_EXCEEDED }
        });
    }
}
