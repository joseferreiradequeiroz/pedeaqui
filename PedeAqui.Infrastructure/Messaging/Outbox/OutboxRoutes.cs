using PedeAqui.Domain.Events;
using PedeAqui.Infrastructure.Messaging.RabbitMq;

namespace PedeAqui.Infrastructure.Messaging.Outbox;

public sealed record OutboxRoute(string Exchange, string RoutingKey);

public static class OutboxRoutes
{
    private static readonly IReadOnlyDictionary<string, OutboxRoute> RoutesByType = new Dictionary<string, OutboxRoute>
    {
        [nameof(OrderCreatedEvent)] = new(RabbitMqTopology.EVENTS_EXCHANGE, RabbitMqTopology.ORDER_CREATED_ROUTING_KEY)
    };

    public static OutboxRoute For(string type) =>
        RoutesByType.TryGetValue(type, out var route)
            ? route
            : throw new InvalidOperationException($"No route is mapped for outbox message type '{type}'.");
}
