using AutoMapper;
using Microsoft.Extensions.Options;
using PedeAqui.Application.Settings;
using PedeAqui.Application.Validation;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Caching;
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
    private readonly IProductCacheInvalidator _cacheInvalidator;
    private readonly ILoggedUser _loggedUser;
    private readonly IMapper _mapper;
    private readonly OrderValidator _validator;

    public UpdateOrderUseCase(
        IOrderUpdateOnlyRepository orderRepository,
        IProductUpdateOnlyRepository productRepository,
        IStockMovementRepository movementRepository,
        IUnitOfWork unitOfWork,
        IProductCacheInvalidator cacheInvalidator,
        ILoggedUser loggedUser,
        IMapper mapper,
        IOptions<RequestLimits> requestLimits)
    {
        _orderRepository = orderRepository;
        _productRepository = productRepository;
        _movementRepository = movementRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidator = cacheInvalidator;
        _loggedUser = loggedUser;
        _mapper = mapper;
        _validator = new OrderValidator(requestLimits.Value);
    }

    public async Task<OrderResponse> Execute(long id, OrderRequest request, CancellationToken cancellationToken)
    {
        _validator.Validate(request).ThrowIfInvalid();

        var user = await _loggedUser.Get(cancellationToken);

        var change = await _unitOfWork.ExecuteInTransaction(
            transactionCancellationToken => ChangeOrder(user, id, request, transactionCancellationToken),
            cancellationToken);
        await _cacheInvalidator.Invalidate(change.AffectedProductIds, CancellationToken.None);

        return _mapper.Map<OrderResponse>(change.Order);
    }

    private async Task<OrderChange> ChangeOrder(User user, long id, OrderRequest request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetById(user, id, cancellationToken);

        if (order is null || order.UserId != user.Id)
            throw new NotFoundException(ResourceErrorMessages.ORDER_NOT_FOUND);

        order.EnsureCanBeChanged();

        var productIds = request.Items.Select(item => item.ProductId)
            .Union(order.Items.Select(item => item.ProductId))
            .ToList();
        var products = await _productRepository.GetByIdsForUpdate(productIds, cancellationToken);

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
        await _movementRepository.AddRange(movements, cancellationToken);

        return new OrderChange(order, productIds);
    }

    private sealed record OrderChange(Order Order, IReadOnlyCollection<long> AffectedProductIds);
}
