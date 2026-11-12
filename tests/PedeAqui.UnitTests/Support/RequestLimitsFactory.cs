using Microsoft.Extensions.Options;
using PedeAqui.Application.Settings;

namespace PedeAqui.UnitTests.Support;

public static class RequestLimitsFactory
{
    public const int MAX_ORDER_ITEMS = 50;
    public const int MAX_ITEM_QUANTITY = 1000;

    public static RequestLimits Create() => new()
    {
        MaxOrderItems = MAX_ORDER_ITEMS,
        MaxItemQuantity = MAX_ITEM_QUANTITY
    };

    public static IOptions<RequestLimits> CreateOptions() => Options.Create(Create());
}
