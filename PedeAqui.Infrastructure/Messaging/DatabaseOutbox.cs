using System.Text.Json;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Messaging;
using PedeAqui.Domain.Services;
using PedeAqui.Infrastructure.Database;

namespace PedeAqui.Infrastructure.Messaging;

internal class DatabaseOutbox : IOutbox
{
    private readonly PedeAquiDbContext _dbContext;
    private readonly ICorrelationContext _correlationContext;

    public DatabaseOutbox(PedeAquiDbContext dbContext, ICorrelationContext correlationContext)
    {
        _dbContext = dbContext;
        _correlationContext = correlationContext;
    }

    public Task Enqueue<TEvent>(TEvent @event, CancellationToken cancellationToken)
    {
        _dbContext.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = typeof(TEvent).Name,
            Payload = JsonSerializer.Serialize(@event, OutboxJson.Options),
            CorrelationId = string.IsNullOrEmpty(_correlationContext.CorrelationId) ? null : _correlationContext.CorrelationId,
            OccurredAt = DateTime.UtcNow
        });

        return Task.CompletedTask;
    }
}
