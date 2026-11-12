using PedeAqui.Application.UseCases.Orders;
using PedeAqui.Exception;
using PedeAqui.UnitTests.Builders;
using PedeAqui.UnitTests.Support;

namespace PedeAqui.UnitTests.Validators;

public class OrderValidatorTests
{
    private readonly OrderValidator _validator = new(RequestLimitsFactory.Create());

    [Fact]
    public void Validate_ValidRequest_ReturnsValid()
    {
        var request = RequestBuilder.Order((1, 2), (2, 1));

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Validate_NoItems_ReturnsMustHaveItems()
    {
        var request = RequestBuilder.Order();

        var error = Assert.Single(_validator.Validate(request).Errors);

        Assert.Equal(ResourceErrorMessages.ORDER_MUST_HAVE_ITEMS, error.ErrorMessage);
    }

    [Fact]
    public void Validate_DuplicatedProduct_ReturnsDuplicatedProduct()
    {
        var request = RequestBuilder.Order((1, 2), (1, 3));

        var error = Assert.Single(_validator.Validate(request).Errors);

        Assert.Equal(ResourceErrorMessages.ORDER_DUPLICATED_PRODUCT, error.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ItemQuantityZeroOrNegative_ReturnsQuantityError(int quantity)
    {
        var request = RequestBuilder.Order((1, quantity));

        var error = Assert.Single(_validator.Validate(request).Errors);

        Assert.Equal(ResourceErrorMessages.ORDER_ITEM_QUANTITY_MUST_BE_GREATER_THAN_ZERO, error.ErrorMessage);
    }

    [Fact]
    public void Validate_ItemWithoutProduct_ReturnsProductRequired()
    {
        var request = RequestBuilder.Order((0, 1));

        var error = Assert.Single(_validator.Validate(request).Errors);

        Assert.Equal(ResourceErrorMessages.ORDER_ITEM_PRODUCT_REQUIRED, error.ErrorMessage);
    }

    [Fact]
    public void Validate_ExactlyMaxItems_ReturnsValid()
    {
        var request = RequestBuilder.Order(ItemsWithDistinctProducts(RequestLimitsFactory.MAX_ORDER_ITEMS));

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Validate_MoreThanMaxItems_ReturnsTooManyItems()
    {
        var request = RequestBuilder.Order(ItemsWithDistinctProducts(RequestLimitsFactory.MAX_ORDER_ITEMS + 1));

        var error = Assert.Single(_validator.Validate(request).Errors);

        Assert.Equal(string.Format(ResourceErrorMessages.ORDER_TOO_MANY_ITEMS, RequestLimitsFactory.MAX_ORDER_ITEMS), error.ErrorMessage);
    }

    [Fact]
    public void Validate_ItemQuantityEqualToMax_ReturnsValid()
    {
        var request = RequestBuilder.Order((1, RequestLimitsFactory.MAX_ITEM_QUANTITY));

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Validate_ItemQuantityAboveMax_ReturnsQuantityAboveLimit()
    {
        var request = RequestBuilder.Order((1, RequestLimitsFactory.MAX_ITEM_QUANTITY + 1));

        var error = Assert.Single(_validator.Validate(request).Errors);

        Assert.Equal(string.Format(ResourceErrorMessages.ORDER_ITEM_QUANTITY_ABOVE_LIMIT, RequestLimitsFactory.MAX_ITEM_QUANTITY), error.ErrorMessage);
    }

    private static (long productId, int quantity)[] ItemsWithDistinctProducts(int count)
    {
        return Enumerable.Range(1, count).Select(productId => ((long)productId, 1)).ToArray();
    }
}
