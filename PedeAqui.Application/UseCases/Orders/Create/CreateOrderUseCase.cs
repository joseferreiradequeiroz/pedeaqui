using AutoMapper;
using Microsoft.Extensions.Options;
using PedeAqui.Application.Settings;
using PedeAqui.Application.Validation;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Caching;
using PedeAqui.Domain.Events;
using PedeAqui.Domain.Messaging;
using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Repositories.Orders;
using PedeAqui.Domain.Repositories.Products;
using PedeAqui.Domain.Repositories.Stock;
using PedeAqui.Domain.Services;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.UseCases.Orders.Create;

public class CreateOrderUseCase : ICreateOrderUseCase
{
    public const string STOCK_REASON = "Venda";

    private readonly IOrderWriteOnlyRepository _orderRepository;
    private readonly IProductUpdateOnlyRepository _productRepository;
    private readonly IStockMovementRepository _movementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOutbox _outbox;
    private readonly IProductCacheInvalidator _cacheInvalidator;
    private readonly ILoggedUser _loggedUser;
    private readonly IMapper _mapper;
    private readonly OrderValidator _validator;

    public CreateOrderUseCase(
        IOrderWriteOnlyRepository orderRepository,
        IProductUpdateOnlyRepository productRepository,
        IStockMovementRepository movementRepository,
        IUnitOfWork unitOfWork,
        IOutbox outbox,
        IProductCacheInvalidator cacheInvalidator,
        ILoggedUser loggedUser,
        IMapper mapper,
        IOptions<RequestLimits> requestLimits)
    {
        _orderRepository = orderRepository;
        _productRepository = productRepository;
        _movementRepository = movementRepository;
        _unitOfWork = unitOfWork;
        _outbox = outbox;
        _cacheInvalidator = cacheInvalidator;
        _loggedUser = loggedUser;
        _mapper = mapper;
        _validator = new OrderValidator(requestLimits.Value);
    }

    public async Task<OrderResponse> Execute(OrderRequest request, CancellationToken cancellationToken)
    {
        _validator.Validate(request).ThrowIfInvalid();

        var user = await _loggedUser.Get(cancellationToken);

        var order = await _unitOfWork.ExecuteInTransaction(
            transactionCancellationToken => PlaceOrder(user, request, transactionCancellationToken),
            cancellationToken);
        await _cacheInvalidator.Invalidate(order.Items.Select(item => item.ProductId), CancellationToken.None);

        return _mapper.Map<OrderResponse>(order);
    }

    private async Task<Order> PlaceOrder(User user, OrderRequest request, CancellationToken cancellationToken)
    {
        var products = await _productRepository.GetByIdsForUpdate(request.Items.Select(item => item.ProductId), cancellationToken);

        var order = new Order { UserId = user.Id };
        await _orderRepository.Add(order, cancellationToken);

        var movements = new List<StockMovement>();

        foreach (var item in request.Items)
        {
            var product = products.FirstOrDefault(p => p.Id == item.ProductId)
                ?? throw new NotFoundException($"{ResourceErrorMessages.PRODUCT_NOT_FOUND} Id: {item.ProductId}.");

            product.EnsureCanBeSold();

            movements.Add(product.RemoveStock(item.Quantity, STOCK_REASON, order));
            order.SetItem(product, item.Quantity);
            _productRepository.Update(product);
        }

        await _movementRepository.AddRange(movements, cancellationToken);

        await _unitOfWork.SaveChanges(cancellationToken);
        await _outbox.Enqueue(new OrderCreatedEvent(order.Id, order.UserId, order.Total, DateTime.UtcNow), cancellationToken);

        return order;
    }
}
