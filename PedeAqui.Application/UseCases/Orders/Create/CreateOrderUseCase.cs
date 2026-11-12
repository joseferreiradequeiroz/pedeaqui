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

namespace PedeAqui.Application.UseCases.Orders.Create;

public class CreateOrderUseCase : ICreateOrderUseCase
{
    public const string STOCK_REASON = "Venda";

    private readonly IOrderWriteOnlyRepository _orderRepository;
    private readonly IProductUpdateOnlyRepository _productRepository;
    private readonly IStockMovementRepository _movementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILoggedUser _loggedUser;
    private readonly IMapper _mapper;

    public CreateOrderUseCase(
        IOrderWriteOnlyRepository orderRepository,
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

    public async Task<OrderResponse> Execute(OrderRequest request)
    {
        new OrderValidator().Validate(request).ThrowIfInvalid();

        var user = await _loggedUser.Get();
        var products = await _productRepository.GetByIds(request.Items.Select(item => item.ProductId));

        var order = new Order { UserId = user.Id };
        await _orderRepository.Add(order);

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

        await _movementRepository.AddRange(movements);
        await _unitOfWork.Commit();

        return _mapper.Map<OrderResponse>(order);
    }
}
