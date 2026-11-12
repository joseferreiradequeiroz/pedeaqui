using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.ExceptionHandlers;

public class PedeAquiExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;

    public PedeAquiExceptionHandler(IProblemDetailsService problemDetailsService)
    {
        _problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        System.Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not PedeAquiException pedeAquiException)
            return false;

        var statusCode = (int)pedeAquiException.GetStatusCode();

        httpContext.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Detail = pedeAquiException.Message
        };

        problemDetails.Extensions["errors"] = pedeAquiException.GetErrorMessages();

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }
}
