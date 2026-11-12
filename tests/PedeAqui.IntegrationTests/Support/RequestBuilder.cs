using Bogus;
using PedeAqui.Communication.Requests;

namespace PedeAqui.IntegrationTests.Support;

public static class RequestBuilder
{
    public static RegisterUserRequest RegisterUser()
    {
        return new Faker<RegisterUserRequest>()
            .RuleFor(user => user.Name, faker => faker.Person.FullName)
            .RuleFor(user => user.Email, faker => $"{Guid.NewGuid():N}@{faker.Internet.DomainName()}")
            .RuleFor(user => user.Password, faker => $"Aa1!{faker.Internet.Password(8)}")
            .Generate();
    }

    public static ProductRequest Product()
    {
        return new Faker<ProductRequest>()
            .RuleFor(product => product.Name, faker => faker.Commerce.ProductName())
            .RuleFor(product => product.Description, faker => faker.Commerce.ProductDescription())
            .RuleFor(product => product.Sku, _ => $"SKU-{Guid.NewGuid():N}"[..20])
            .RuleFor(product => product.Price, faker => faker.Finance.Amount(1, 500))
            .Generate();
    }

    public static StockMovementRequest StockMovement(int quantity) => new() { Quantity = quantity, Reason = "Movimentação de teste" };

    public static OrderRequest Order(params (long productId, int quantity)[] items)
    {
        return new OrderRequest
        {
            Items = items.Select(item => new OrderItemRequest { ProductId = item.productId, Quantity = item.quantity }).ToList()
        };
    }
}
