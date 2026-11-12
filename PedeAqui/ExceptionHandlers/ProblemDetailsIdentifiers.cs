using System.Diagnostics;
using PedeAqui.Domain.Services;

namespace PedeAqui.ExceptionHandlers;

public static class ProblemDetailsIdentifiers
{
    public const string TRACE_ID = "traceId";
    public const string CORRELATION_ID = "correlationId";

    public static void Add(ProblemDetailsContext context)
    {
        var correlationContext = context.HttpContext.RequestServices.GetRequiredService<ICorrelationContext>();

        context.ProblemDetails.Extensions[TRACE_ID] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Extensions[CORRELATION_ID] = correlationContext.CorrelationId;
    }
}
