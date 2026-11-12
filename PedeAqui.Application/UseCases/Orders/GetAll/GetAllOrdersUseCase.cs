using AutoMapper;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Repositories.Orders;
using PedeAqui.Domain.Services;

namespace PedeAqui.Application.UseCases.Orders.GetAll;

public class GetAllOrdersUseCase : IGetAllOrdersUseCase
{
    private readonly IOrderReadOnlyRepository _orderRepository;
    private readonly ILoggedUser _loggedUser;
    private readonly IMapper _mapper;

    public GetAllOrdersUseCase(IOrderReadOnlyRepository orderRepository, ILoggedUser loggedUser, IMapper mapper)
    {
        _orderRepository = orderRepository;
        _loggedUser = loggedUser;
        _mapper = mapper;
    }

    public async Task<OrdersResponse> Execute(CancellationToken cancellationToken)
    {
        var user = await _loggedUser.Get(cancellationToken);
        var orders = await _orderRepository.GetAll(user, cancellationToken);

        return new OrdersResponse
        {
            Orders = _mapper.Map<List<ShortOrderResponse>>(orders)
        };
    }
}
