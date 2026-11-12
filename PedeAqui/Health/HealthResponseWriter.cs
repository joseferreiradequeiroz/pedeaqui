using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PedeAqui.Health;

public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task Write(HttpContext httpContext, HealthReport report)
    {
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMilliseconds = (long)entry.Value.Duration.TotalMilliseconds
            })
        };

        httpContext.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, body, JsonOptions, httpContext.RequestAborted);
    }
}
