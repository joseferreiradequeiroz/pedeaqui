using AutoMapper;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Repositories.Orders;
using PedeAqui.Domain.Services;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.UseCases.Orders.GetById;

public class GetOrderByIdUseCase : IGetOrderByIdUseCase
{
    private readonly IOrderReadOnlyRepository _orderRepository;
    private readonly ILoggedUser _loggedUser;
    private readonly IMapper _mapper;

    public GetOrderByIdUseCase(IOrderReadOnlyRepository orderRepository, ILoggedUser loggedUser, IMapper mapper)
    {
        _orderRepository = orderRepository;
        _loggedUser = loggedUser;
        _mapper = mapper;
    }

    public async Task<OrderResponse> Execute(long id, CancellationToken cancellationToken)
    {
        var user = await _loggedUser.Get(cancellationToken);
        var order = await _orderRepository.GetById(user, id, cancellationToken)
            ?? throw new NotFoundException(ResourceErrorMessages.ORDER_NOT_FOUND);

        return _mapper.Map<OrderResponse>(order);
    }
}
