using FluentValidation;
using PedeAqui.Application.Settings;
using PedeAqui.Communication.Requests;
using PedeAqui.Exception;

namespace PedeAqui.Application.UseCases.Orders;

public class OrderValidator : AbstractValidator<OrderRequest>
{
    public OrderValidator(RequestLimits limits)
    {
        RuleFor(order => order.Items)
            .NotEmpty().WithMessage(ResourceErrorMessages.ORDER_MUST_HAVE_ITEMS);

        RuleFor(order => order.Items)
            .Must(items => items.Count <= limits.MaxOrderItems)
            .WithMessage(string.Format(ResourceErrorMessages.ORDER_TOO_MANY_ITEMS, limits.MaxOrderItems))
            .When(order => order.Items is not null);

        RuleFor(order => order.Items)
            .Must(items => items.Select(item => item.ProductId).Distinct().Count() == items.Count)
            .WithMessage(ResourceErrorMessages.ORDER_DUPLICATED_PRODUCT)
            .When(order => order.Items is { Count: > 0 });

        RuleForEach(order => order.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId)
                .GreaterThan(0).WithMessage(ResourceErrorMessages.ORDER_ITEM_PRODUCT_REQUIRED);

            item.RuleFor(i => i.Quantity)
                .GreaterThan(0).WithMessage(ResourceErrorMessages.ORDER_ITEM_QUANTITY_MUST_BE_GREATER_THAN_ZERO);

            item.RuleFor(i => i.Quantity)
                .LessThanOrEqualTo(limits.MaxItemQuantity)
                .WithMessage(string.Format(ResourceErrorMessages.ORDER_ITEM_QUANTITY_ABOVE_LIMIT, limits.MaxItemQuantity));
        });
    }
}
