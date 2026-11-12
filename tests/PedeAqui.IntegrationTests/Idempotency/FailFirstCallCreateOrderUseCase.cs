using PedeAqui.Application.UseCases.Orders.Create;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;

namespace PedeAqui.IntegrationTests.Idempotency;

public sealed class FirstCallFailureProbe
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public bool IsFirstCall() => Interlocked.Increment(ref _calls) == 1;
}

public sealed class FailFirstCallCreateOrderUseCase : ICreateOrderUseCase
{
    private readonly CreateOrderUseCase _realUseCase;
    private readonly FirstCallFailureProbe _probe;

    public FailFirstCallCreateOrderUseCase(CreateOrderUseCase realUseCase, FirstCallFailureProbe probe)
    {
        _realUseCase = realUseCase;
        _probe = probe;
    }

    public Task<OrderResponse> Execute(OrderRequest request, CancellationToken cancellationToken)
    {
        if (_probe.IsFirstCall())
            throw new InvalidOperationException("Falha inesperada antes de criar o pedido.");

        return _realUseCase.Execute(request, cancellationToken);
    }
}
