using PedeAqui.Application.UseCases.Products;
using PedeAqui.Exception;
using PedeAqui.UnitTests.Builders;

namespace PedeAqui.UnitTests.Validators;

public class ProductValidatorTests
{
    private readonly ProductValidator _validator = new();

    [Fact]
    public void Validate_ValidRequest_ReturnsValid()
    {
        var result = _validator.Validate(RequestBuilder.Product());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithoutDescription_ReturnsValid()
    {
        var request = RequestBuilder.Product();
        request.Description = null;

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_EmptyName_ReturnsNameRequired(string name)
    {
        var request = RequestBuilder.Product();
        request.Name = name;

        var error = Assert.Single(_validator.Validate(request).Errors);

        Assert.Equal(ResourceErrorMessages.PRODUCT_NAME_REQUIRED, error.ErrorMessage);
    }

    [Fact]
    public void Validate_NameTooLong_ReturnsNameMaxLength()
    {
        var request = RequestBuilder.Product();
        request.Name = new string('a', ProductValidator.NAME_MAX_LENGTH + 1);

        var error = Assert.Single(_validator.Validate(request).Errors);

        Assert.Equal(ResourceErrorMessages.PRODUCT_NAME_MAX_LENGTH, error.ErrorMessage);
    }

    [Fact]
    public void Validate_EmptySku_ReturnsSkuRequired()
    {
        var request = RequestBuilder.Product();
        request.Sku = string.Empty;

        var error = Assert.Single(_validator.Validate(request).Errors);

        Assert.Equal(ResourceErrorMessages.PRODUCT_SKU_REQUIRED, error.ErrorMessage);
    }

    [Fact]
    public void Validate_SkuTooLong_ReturnsSkuMaxLength()
    {
        var request = RequestBuilder.Product();
        request.Sku = new string('X', ProductValidator.SKU_MAX_LENGTH + 1);

        var error = Assert.Single(_validator.Validate(request).Errors);

        Assert.Equal(ResourceErrorMessages.PRODUCT_SKU_MAX_LENGTH, error.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-10)]
    public void Validate_PriceZeroOrNegative_ReturnsPriceError(double price)
    {
        var request = RequestBuilder.Product();
        request.Price = (decimal)price;

        var error = Assert.Single(_validator.Validate(request).Errors);

        Assert.Equal(ResourceErrorMessages.PRODUCT_PRICE_MUST_BE_GREATER_THAN_ZERO, error.ErrorMessage);
    }

    [Fact]
    public void Validate_PriceOneCent_ReturnsValid()
    {
        var request = RequestBuilder.Product();
        request.Price = 0.01m;

        Assert.True(_validator.Validate(request).IsValid);
    }
}
