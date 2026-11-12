using AutoMapper;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Entities;

namespace PedeAqui.Application.Mapper;

internal class AutoMapping : Profile
{
    public AutoMapping()
    {
        RequestToEntity();
        EntityToResponse();
    }

    private void RequestToEntity()
    {
        CreateMap<RegisterUserRequest, User>()
            .ForMember(user => user.Password, config => config.Ignore());

        CreateMap<ProductRequest, Product>();
    }

    private void EntityToResponse()
    {
        CreateMap<Product, ProductResponse>();

        CreateMap<StockMovement, StockMovementResponse>();

        CreateMap<Order, OrderResponse>();
        CreateMap<Order, ShortOrderResponse>();
        CreateMap<OrderItem, OrderItemResponse>();
    }
}
