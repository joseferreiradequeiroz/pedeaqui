using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.ExceptionHandlers;

public static class ExceptionProblemDetails
{
    public const string ERRORS = "errors";

    public static ProblemDetails Create(ProblemDetailsFactory problemDetailsFactory, HttpContext httpContext, System.Exception exception)
    {
        var (statusCode, detail, errors) = Describe(exception);

        var problemDetails = problemDetailsFactory.CreateProblemDetails(httpContext, statusCode: statusCode, detail: detail);
        problemDetails.Extensions[ERRORS] = errors;

        return problemDetails;
    }

    private static (int StatusCode, string Detail, IList<string> Errors) Describe(System.Exception exception)
    {
        return exception switch
        {
            PedeAquiException pedeAquiException => (
                (int)pedeAquiException.GetStatusCode(),
                pedeAquiException.Message,
                pedeAquiException.GetErrorMessages()),
            BadHttpRequestException badRequest when badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge => (
                StatusCodes.Status413PayloadTooLarge,
                ResourceErrorMessages.REQUEST_BODY_TOO_LARGE,
                [ResourceErrorMessages.REQUEST_BODY_TOO_LARGE]),
            _ => (
                StatusCodes.Status500InternalServerError,
                ResourceErrorMessages.UNKNOWN_ERROR,
                [ResourceErrorMessages.UNKNOWN_ERROR])
        };
    }
}
