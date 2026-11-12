using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;
using PedeAqui.Application.Idempotency;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Services;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;
using PedeAqui.ExceptionHandlers;

namespace PedeAqui.Filters;

public sealed class IdempotencyFilter : IAsyncResourceFilter, IAsyncActionFilter
{
    public const string IDEMPOTENCY_KEY_HEADER = "Idempotency-Key";
    public const string IDEMPOTENT_REPLAYED_HEADER = "Idempotent-Replayed";
    private const string JSON_CONTENT_TYPE = "application/json; charset=utf-8";
    private const string PROBLEM_JSON_CONTENT_TYPE = "application/problem+json; charset=utf-8";

    private readonly IIdempotencyService _idempotencyService;
    private readonly ILoggedUser _loggedUser;
    private readonly ProblemDetailsFactory _problemDetailsFactory;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public IdempotencyFilter(
        IIdempotencyService idempotencyService,
        ILoggedUser loggedUser,
        ProblemDetailsFactory problemDetailsFactory,
        IOptions<JsonOptions> jsonOptions)
    {
        _idempotencyService = idempotencyService;
        _loggedUser = loggedUser;
        _problemDetailsFactory = problemDetailsFactory;
        _jsonSerializerOptions = jsonOptions.Value.JsonSerializerOptions;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        context.HttpContext.Request.EnableBuffering();

        await next();
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var cancellationToken = context.HttpContext.RequestAborted;

        var key = IdempotencyKeyOf(request);
        var user = await _loggedUser.Get(cancellationToken);
        var requestBody = await ReadBody(request, cancellationToken);

        var start = await _idempotencyService.Begin(user.Id, key, request.Method, request.Path.Value!, requestBody, cancellationToken);

        switch (start)
        {
            case IdempotencyStart.Started started:
                await ExecuteActionAndRecordResponse(context, next, started.RequestId);
                return;
            case IdempotencyStart.Replay replay:
                context.Result = ReplayOf(context.HttpContext, replay);
                return;
            case IdempotencyStart.PayloadMismatch:
                throw new BusinessRuleException(ResourceErrorMessages.IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST);
            case IdempotencyStart.InProgressConflict:
                throw new ConflictException(ResourceErrorMessages.IDEMPOTENCY_REQUEST_IN_PROGRESS);
        }
    }

    private async Task ExecuteActionAndRecordResponse(ActionExecutingContext context, ActionExecutionDelegate next, long requestId)
    {
        ActionExecutedContext executed;

        try
        {
            executed = await next();
        }
        catch
        {
            await _idempotencyService.Release(requestId, CancellationToken.None);
            throw;
        }

        var failedWithTransientConflict = !executed.ExceptionHandled && executed.Exception is ConflictException;

        if (!executed.ExceptionHandled && executed.Exception is PedeAquiException exception && IsClientError(exception))
            ReplaceExceptionWithProblemDetails(executed, exception);

        var actionFailedUnexpectedly = executed.Exception is not null && !executed.ExceptionHandled;
        var (statusCode, body) = ResponseOf(executed.Result);

        if (actionFailedUnexpectedly || failedWithTransientConflict || statusCode >= StatusCodes.Status500InternalServerError)
        {
            await _idempotencyService.Release(requestId, CancellationToken.None);
            return;
        }

        await _idempotencyService.Complete(requestId, statusCode, Serialize(body), CancellationToken.None);
    }

    private void ReplaceExceptionWithProblemDetails(ActionExecutedContext executed, PedeAquiException exception)
    {
        var problemDetails = ExceptionProblemDetails.Create(_problemDetailsFactory, executed.HttpContext, exception);

        executed.Result = new ObjectResult(problemDetails)
        {
            StatusCode = problemDetails.Status,
            ContentTypes = { PROBLEM_JSON_CONTENT_TYPE }
        };
        executed.ExceptionHandled = true;
    }

    private static (int StatusCode, object? Body) ResponseOf(IActionResult? result)
    {
        return result switch
        {
            ObjectResult objectResult => (objectResult.StatusCode ?? StatusCodes.Status200OK, objectResult.Value),
            IStatusCodeActionResult statusCodeResult => (statusCodeResult.StatusCode ?? StatusCodes.Status200OK, null),
            _ => (StatusCodes.Status200OK, null)
        };
    }

    private static IActionResult ReplayOf(HttpContext httpContext, IdempotencyStart.Replay replay)
    {
        httpContext.Response.Headers[IDEMPOTENT_REPLAYED_HEADER] = "true";

        if (replay.Body is null)
            return new StatusCodeResult(replay.StatusCode);

        return new ContentResult
        {
            StatusCode = replay.StatusCode,
            Content = replay.Body,
            ContentType = replay.StatusCode >= StatusCodes.Status400BadRequest ? PROBLEM_JSON_CONTENT_TYPE : JSON_CONTENT_TYPE
        };
    }

    private string? Serialize(object? body)
    {
        return body is null ? null : JsonSerializer.Serialize(body, body.GetType(), _jsonSerializerOptions);
    }

    private static bool IsClientError(PedeAquiException exception)
    {
        var statusCode = (int)exception.GetStatusCode();

        return statusCode is >= StatusCodes.Status400BadRequest and < StatusCodes.Status500InternalServerError;
    }

    private static string IdempotencyKeyOf(HttpRequest request)
    {
        var values = request.Headers[IDEMPOTENCY_KEY_HEADER];
        var key = values.Count == 1 ? values[0] : null;

        if (string.IsNullOrWhiteSpace(key) || key.Length > IdempotencyRequest.KEY_MAX_LENGTH)
            throw new ErrorOnValidationException([ResourceErrorMessages.IDEMPOTENCY_KEY_INVALID]);

        return key;
    }

    private static async Task<string> ReadBody(HttpRequest request, CancellationToken cancellationToken)
    {
        request.Body.Position = 0;

        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);

        request.Body.Position = 0;

        return body;
    }
}
