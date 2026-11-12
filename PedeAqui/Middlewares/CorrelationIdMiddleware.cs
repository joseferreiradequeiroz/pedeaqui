using System.Text.RegularExpressions;
using Serilog.Context;

namespace PedeAqui.Middlewares;

public partial class CorrelationIdMiddleware
{
    public const string HEADER_NAME = "X-Correlation-Id";
    public const string LOG_PROPERTY_NAME = "CorrelationId";
    public const string HTTP_CONTEXT_ITEM_KEY = "PedeAqui.CorrelationId";
    private const int MAX_LENGTH = 64;

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var correlationId = ReadValidIncomingId(httpContext) ?? Guid.NewGuid().ToString("N");

        httpContext.Items[HTTP_CONTEXT_ITEM_KEY] = correlationId;
        httpContext.Response.OnStarting(() =>
        {
            httpContext.Response.Headers[HEADER_NAME] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(LOG_PROPERTY_NAME, correlationId))
        {
            await _next(httpContext);
        }
    }

    private static string? ReadValidIncomingId(HttpContext httpContext)
    {
        var incoming = httpContext.Request.Headers[HEADER_NAME].ToString();

        return incoming.Length is > 0 and <= MAX_LENGTH && AllowedCharacters().IsMatch(incoming)
            ? incoming
            : null;
    }

    [GeneratedRegex("^[A-Za-z0-9-]+$")]
    private static partial Regex AllowedCharacters();
}
