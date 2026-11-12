using Microsoft.AspNetCore.Mvc;

namespace PedeAqui.PaymentGateway.Payments;

public record PaymentRequest(string OrderReference, decimal Amount);

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/payments", (
            PaymentRequest request,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            PaymentStore store) =>
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return Results.Problem("Idempotency-Key header is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (request.Amount <= 0)
            {
                return Results.Problem("Amount must be greater than zero.", statusCode: StatusCodes.Status400BadRequest);
            }

            var payment = store.Process(idempotencyKey, request.Amount);

            return payment.Amount == request.Amount
                ? Results.Ok(payment)
                : Results.Problem("Idempotency-Key was already used with a different amount.", statusCode: StatusCodes.Status422UnprocessableEntity);
        });

        app.MapGet("/payments/{idempotencyKey}", (string idempotencyKey, PaymentStore store) =>
            store.Find(idempotencyKey) is { } payment ? Results.Ok(payment) : Results.NotFound());
    }
}
