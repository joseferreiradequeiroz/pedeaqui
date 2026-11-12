using FluentValidation;
using PedeAqui.Communication.Requests;
using PedeAqui.Exception;

namespace PedeAqui.Application.UseCases.Stock;

public class StockMovementValidator : AbstractValidator<StockMovementRequest>
{
    public const int REASON_MAX_LENGTH = 200;

    public StockMovementValidator()
    {
        RuleFor(movement => movement.Quantity)
            .GreaterThan(0).WithMessage(ResourceErrorMessages.STOCK_QUANTITY_MUST_BE_GREATER_THAN_ZERO);

        RuleFor(movement => movement.Reason)
            .NotEmpty().WithMessage(ResourceErrorMessages.STOCK_REASON_REQUIRED)
            .MaximumLength(REASON_MAX_LENGTH).WithMessage(ResourceErrorMessages.STOCK_REASON_MAX_LENGTH);
    }
}
