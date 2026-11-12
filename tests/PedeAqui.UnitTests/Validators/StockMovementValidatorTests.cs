using PedeAqui.Application.UseCases.Stock;
using PedeAqui.Exception;
using PedeAqui.UnitTests.Builders;

namespace PedeAqui.UnitTests.Validators;

public class StockMovementValidatorTests
{
    private readonly StockMovementValidator _validator = new();

    [Fact]
    public void Validate_ValidRequest_ReturnsValid()
    {
        Assert.True(_validator.Validate(RequestBuilder.StockMovement()).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_QuantityZeroOrNegative_ReturnsQuantityError(int quantity)
    {
        var request = RequestBuilder.StockMovement(quantity);

        var error = Assert.Single(_validator.Validate(request).Errors);

        Assert.Equal(ResourceErrorMessages.STOCK_QUANTITY_MUST_BE_GREATER_THAN_ZERO, error.ErrorMessage);
    }

    [Fact]
    public void Validate_EmptyReason_ReturnsReasonRequired()
    {
        var request = RequestBuilder.StockMovement();
        request.Reason = string.Empty;

        var error = Assert.Single(_validator.Validate(request).Errors);

        Assert.Equal(ResourceErrorMessages.STOCK_REASON_REQUIRED, error.ErrorMessage);
    }

    [Fact]
    public void Validate_ReasonTooLong_ReturnsReasonMaxLength()
    {
        var request = RequestBuilder.StockMovement();
        request.Reason = new string('a', StockMovementValidator.REASON_MAX_LENGTH + 1);

        var error = Assert.Single(_validator.Validate(request).Errors);

        Assert.Equal(ResourceErrorMessages.STOCK_REASON_MAX_LENGTH, error.ErrorMessage);
    }
}
