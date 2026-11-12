using FluentValidation;
using PedeAqui.Communication.Requests;
using PedeAqui.Exception;

namespace PedeAqui.Application.UseCases.Products;

public class ProductValidator : AbstractValidator<ProductRequest>
{
    public const int NAME_MAX_LENGTH = 150;
    public const int SKU_MAX_LENGTH = 50;

    public ProductValidator()
    {
        RuleFor(product => product.Name)
            .NotEmpty().WithMessage(ResourceErrorMessages.PRODUCT_NAME_REQUIRED)
            .MaximumLength(NAME_MAX_LENGTH).WithMessage(ResourceErrorMessages.PRODUCT_NAME_MAX_LENGTH);

        RuleFor(product => product.Sku)
            .NotEmpty().WithMessage(ResourceErrorMessages.PRODUCT_SKU_REQUIRED)
            .MaximumLength(SKU_MAX_LENGTH).WithMessage(ResourceErrorMessages.PRODUCT_SKU_MAX_LENGTH);

        RuleFor(product => product.Price)
            .GreaterThan(0).WithMessage(ResourceErrorMessages.PRODUCT_PRICE_MUST_BE_GREATER_THAN_ZERO);
    }
}
