using Microsoft.Extensions.Options;
using PedeAqui.Exception;
using PedeAqui.Settings;

namespace PedeAqui.Middlewares;

public class RequestBodySizeLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly long _maxRequestBodyBytes;

    public RequestBodySizeLimitMiddleware(RequestDelegate next, IOptions<ApiEdgeSettings> settings)
    {
        _next = next;
        _maxRequestBodyBytes = settings.Value.MaxRequestBodyBytes;
    }

    public async Task InvokeAsync(HttpContext httpContext, IProblemDetailsService problemDetailsService)
    {
        if (httpContext.Request.ContentLength.GetValueOrDefault() <= _maxRequestBodyBytes)
        {
            await _next(httpContext);
            return;
        }

        httpContext.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;

        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = StatusCodes.Status413PayloadTooLarge, Detail = ResourceErrorMessages.REQUEST_BODY_TOO_LARGE }
        });
    }
}
