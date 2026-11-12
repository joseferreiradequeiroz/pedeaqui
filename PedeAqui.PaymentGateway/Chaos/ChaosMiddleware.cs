namespace PedeAqui.PaymentGateway.Chaos;

public class ChaosMiddleware(RequestDelegate next, ChaosState state, Random random)
{
    private static readonly PathString[] ExemptPaths = ["/chaos", "/health"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsExempt(context.Request.Path))
        {
            await next(context);
            return;
        }

        var chaos = state.Current;

        if (chaos.Down || random.NextDouble() < chaos.FailureRate)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (random.NextDouble() < chaos.SlowRate)
        {
            await Task.Delay(chaos.SlowDelayMs, context.RequestAborted);
        }

        await next(context);
    }

    private static bool IsExempt(PathString path) =>
        ExemptPaths.Any(exempt => path.StartsWithSegments(exempt));
}
