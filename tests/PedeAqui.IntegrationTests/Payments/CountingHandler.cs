using System.Collections.Concurrent;
using System.Net;

namespace PedeAqui.IntegrationTests.Payments;

public sealed record ObservedAttempt(HttpMethod Method, string? IdempotencyKey, string? CorrelationId);

public sealed class CountingHandler : DelegatingHandler
{
    private readonly int _failFirstCalls;
    private int _calls;

    public CountingHandler(int failFirstCalls)
    {
        _failFirstCalls = failFirstCalls;
    }

    public int Calls => Volatile.Read(ref _calls);

    public ConcurrentQueue<ObservedAttempt> Attempts { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _calls);

        Attempts.Enqueue(new ObservedAttempt(
            request.Method,
            HeaderValue(request, "Idempotency-Key"),
            HeaderValue(request, "X-Correlation-Id")));

        if (call <= _failFirstCalls)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            response.Dispose();
            throw new TaskCanceledException("O cliente abortou a requisição.", null, cancellationToken);
        }

        return response;
    }

    private static string? HeaderValue(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
}
