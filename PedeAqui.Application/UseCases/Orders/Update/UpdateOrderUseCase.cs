using AutoMapper;
using PedeAqui.Application.Validation;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Repositories.Orders;
using PedeAqui.Domain.Repositories.Products;
using PedeAqui.Domain.Repositories.Stock;
using PedeAqui.Domain.Services;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.UseCases.Orders.Update;

public class UpdateOrderUseCase : IUpdateOrderUseCase
{
    private readonly IOrderUpdateOnlyRepository _orderRepository;
    private readonly IProductUpdateOnlyRepository _productRepository;
    private readonly IStockMovementRepository _movementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILoggedUser _loggedUser;
    private readonly IMapper _mapper;

    public UpdateOrderUseCase(
        IOrderUpdateOnlyRepository orderRepository,
        IProductUpdateOnlyRepository productRepository,
        IStockMovementRepository movementRepository,
        IUnitOfWork unitOfWork,
        ILoggedUser loggedUser,
        IMapper mapper)
    {
        _orderRepository = orderRepository;
        _productRepository = productRepository;
        _movementRepository = movementRepository;
        _unitOfWork = unitOfWork;
        _loggedUser = loggedUser;
        _mapper = mapper;
    }

    public async Task<OrderResponse> Execute(long id, OrderRequest request)
    {
        new OrderValidator().Validate(request).ThrowIfInvalid();

        var user = await _loggedUser.Get();
        var order = await _orderRepository.GetById(user, id);

        if (order is null || order.UserId != user.Id)
            throw new NotFoundException(ResourceErrorMessages.ORDER_NOT_FOUND);

        order.EnsureIsPending(ResourceErrorMessages.ORDER_CANNOT_BE_CHANGED);

        var productIds = request.Items.Select(item => item.ProductId)
            .Union(order.Items.Select(item => item.ProductId));
        var products = await _productRepository.GetByIds(productIds);

        var movements = new List<StockMovement>();
        var reason = $"Alteração do pedido #{order.Id}";

        var removedItems = order.Items
            .Where(item => request.Items.All(requested => requested.ProductId != item.ProductId))
            .ToList();

        foreach (var removed in removedItems)
        {
            var product = products.First(p => p.Id == removed.ProductId);
            movements.Add(product.AddStock(removed.Quantity, reason, order));
            order.RemoveItem(removed.ProductId);
            _productRepository.Update(product);
        }

        foreach (var requested in request.Items)
        {
            var product = products.FirstOrDefault(p => p.Id == requested.ProductId)
                ?? throw new NotFoundException($"{ResourceErrorMessages.PRODUCT_NOT_FOUND} Id: {requested.ProductId}.");

            var currentQuantity = order.Items.FirstOrDefault(item => item.ProductId == product.Id)?.Quantity ?? 0;
            var difference = requested.Quantity - currentQuantity;

            if (difference == 0)
                continue;

            if (difference > 0)
            {
                product.EnsureCanBeSold();
                movements.Add(product.RemoveStock(difference, reason, order));
            }
            else
            {
                movements.Add(product.AddStock(-difference, reason, order));
            }

            order.SetItem(product, requested.Quantity);
            _productRepository.Update(product);
        }

        _orderRepository.Update(order);
        await _movementRepository.AddRange(movements);
        await _unitOfWork.Commit();

        return _mapper.Map<OrderResponse>(order);
    }
}
