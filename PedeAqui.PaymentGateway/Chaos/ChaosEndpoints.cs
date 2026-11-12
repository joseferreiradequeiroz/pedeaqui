namespace PedeAqui.PaymentGateway.Chaos;

public static class ChaosEndpoints
{
    public static void MapChaosEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/chaos", (ChaosState state) => state.Current);

        app.MapPost("/chaos", (ChaosOptions options, ChaosState state) =>
        {
            if (!options.IsValid())
            {
                return Results.Problem("Rates must be between 0 and 1 and SlowDelayMs cannot be negative.", statusCode: StatusCodes.Status400BadRequest);
            }

            state.Current = options;
            return Results.Ok(options);
        });
    }
}
