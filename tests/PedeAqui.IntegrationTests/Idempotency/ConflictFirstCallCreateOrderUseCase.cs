using PedeAqui.Application.UseCases.Orders.Create;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.IntegrationTests.Idempotency;

public sealed class ConflictFirstCallCreateOrderUseCase : ICreateOrderUseCase
{
    private readonly CreateOrderUseCase _realUseCase;
    private readonly FirstCallFailureProbe _probe;

    public ConflictFirstCallCreateOrderUseCase(CreateOrderUseCase realUseCase, FirstCallFailureProbe probe)
    {
        _realUseCase = realUseCase;
        _probe = probe;
    }

    public Task<OrderResponse> Execute(OrderRequest request, CancellationToken cancellationToken)
    {
        if (_probe.IsFirstCall())
            throw new ConflictException(ResourceErrorMessages.CONCURRENCY_CONFLICT);

        return _realUseCase.Execute(request, cancellationToken);
    }
}
