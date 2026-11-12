using Bogus;
using PedeAqui.Domain.Entities;

namespace PedeAqui.UnitTests.Builders;

public static class ProductBuilder
{
    public static Product Build(long id = 1, int stock = 10, decimal? price = null)
    {
        var product = new Faker<Product>()
            .RuleFor(product => product.Id, _ => id)
            .RuleFor(product => product.Name, faker => faker.Commerce.ProductName())
            .RuleFor(product => product.Description, faker => faker.Commerce.ProductDescription())
            .RuleFor(product => product.Sku, faker => faker.Commerce.Ean8())
            .RuleFor(product => product.Price, faker => price ?? faker.Finance.Amount(1, 500))
            .Generate();

        if (stock > 0)
            product.AddStock(stock, "Estoque inicial");

        return product;
    }

    public static Product Inactive(long id = 1, int stock = 10)
    {
        var product = Build(id, stock);
        product.Deactivate();
        return product;
    }
}
