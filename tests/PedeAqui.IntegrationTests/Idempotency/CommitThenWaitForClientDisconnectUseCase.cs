using PedeAqui.Application.UseCases.Orders.Create;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;

namespace PedeAqui.IntegrationTests.Idempotency;

public sealed class ClientDisconnectProbe
{
    public static readonly TimeSpan MaximumWaitForDisconnect = TimeSpan.FromSeconds(5);

    public TaskCompletionSource OrderCommitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool ServerSawClientDisconnect { get; set; }
}

public sealed class CommitThenWaitForClientDisconnectUseCase : ICreateOrderUseCase
{
    private readonly CreateOrderUseCase _realUseCase;
    private readonly ClientDisconnectProbe _probe;

    public CommitThenWaitForClientDisconnectUseCase(CreateOrderUseCase realUseCase, ClientDisconnectProbe probe)
    {
        _realUseCase = realUseCase;
        _probe = probe;
    }

    public async Task<OrderResponse> Execute(OrderRequest request, CancellationToken cancellationToken)
    {
        var response = await _realUseCase.Execute(request, cancellationToken);
        _probe.OrderCommitted.TrySetResult();

        try
        {
            await Task.Delay(ClientDisconnectProbe.MaximumWaitForDisconnect, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _probe.ServerSawClientDisconnect = true;
        }

        return response;
    }
}
