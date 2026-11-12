namespace PedeAqui.Domain.Events;

public record OrderCreatedEvent(long OrderId, long UserId, decimal Total, DateTime OccurredAt);
