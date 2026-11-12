using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Repositories.Orders;
using PedeAqui.Domain.Repositories.Products;
using PedeAqui.Domain.Repositories.Stock;
using PedeAqui.Domain.Services;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.UseCases.Orders.Cancel;

public class CancelOrderUseCase : ICancelOrderUseCase
{
    private readonly IOrderUpdateOnlyRepository _orderRepository;
    private readonly IProductUpdateOnlyRepository _productRepository;
    private readonly IStockMovementRepository _movementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILoggedUser _loggedUser;

    public CancelOrderUseCase(
        IOrderUpdateOnlyRepository orderRepository,
        IProductUpdateOnlyRepository productRepository,
        IStockMovementRepository movementRepository,
        IUnitOfWork unitOfWork,
        ILoggedUser loggedUser)
    {
        _orderRepository = orderRepository;
        _productRepository = productRepository;
        _movementRepository = movementRepository;
        _unitOfWork = unitOfWork;
        _loggedUser = loggedUser;
    }

    public async Task Execute(long id)
    {
        var user = await _loggedUser.Get();
        var order = await _orderRepository.GetById(user, id)
            ?? throw new NotFoundException(ResourceErrorMessages.ORDER_NOT_FOUND);

        order.Cancel();

        var products = await _productRepository.GetByIds(order.Items.Select(item => item.ProductId));
        var movements = new List<StockMovement>();

        foreach (var item in order.Items)
        {
            var product = products.First(p => p.Id == item.ProductId);
            movements.Add(product.AddStock(item.Quantity, $"Cancelamento do pedido #{order.Id}", order));
            _productRepository.Update(product);
        }

        _orderRepository.Update(order);
        await _movementRepository.AddRange(movements);
        await _unitOfWork.Commit();
    }
}
