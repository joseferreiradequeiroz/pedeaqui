using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Repositories.Orders;
using PedeAqui.Domain.Services;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.UseCases.Orders.Confirm;

public class ConfirmOrderUseCase : IConfirmOrderUseCase
{
    private readonly IOrderUpdateOnlyRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILoggedUser _loggedUser;

    public ConfirmOrderUseCase(IOrderUpdateOnlyRepository orderRepository, IUnitOfWork unitOfWork, ILoggedUser loggedUser)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _loggedUser = loggedUser;
    }

    public async Task Execute(long id)
    {
        var user = await _loggedUser.Get();
        var order = await _orderRepository.GetById(user, id)
            ?? throw new NotFoundException(ResourceErrorMessages.ORDER_NOT_FOUND);

        order.Confirm();

        _orderRepository.Update(order);
        await _unitOfWork.Commit();
    }
}
