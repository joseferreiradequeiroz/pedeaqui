using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Messaging;
using PedeAqui.Infrastructure.Database;

namespace PedeAqui.Infrastructure.Messaging.Outbox;

internal sealed class OutboxProcessor : IOutboxProcessor
{
    private const int LAST_ERROR_MAX_LENGTH = 2000;

    private readonly PedeAquiDbContext _dbContext;
    private readonly IMessagePublisher _publisher;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(
        PedeAquiDbContext dbContext,
        IMessagePublisher publisher,
        IOptions<OutboxOptions> options,
        ILogger<OutboxProcessor> logger)
    {
        _dbContext = dbContext;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    public Task<int> ProcessBatch(CancellationToken cancellationToken)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(async attemptCancellationToken =>
        {
            _dbContext.ChangeTracker.Clear();

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(attemptCancellationToken);

            var messages = await LockPendingBatch(attemptCancellationToken);
            var published = await PublishInOrder(messages, attemptCancellationToken);

            await _dbContext.SaveChangesAsync(attemptCancellationToken);
            await transaction.CommitAsync(attemptCancellationToken);

            return published;
        }, cancellationToken);
    }

    private Task<List<OutboxMessage>> LockPendingBatch(CancellationToken cancellationToken)
    {
        return _dbContext.Set<OutboxMessage>()
            .FromSql($"""
                SELECT * FROM outbox_messages
                WHERE "ProcessedAt" IS NULL
                ORDER BY "OccurredAt"
                LIMIT {_options.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);
    }

    private async Task<int> PublishInOrder(List<OutboxMessage> messages, CancellationToken cancellationToken)
    {
        var published = 0;

        foreach (var message in messages)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["CorrelationId"] = message.CorrelationId,
                ["MessageId"] = message.Id
            });

            try
            {
                var route = OutboxRoutes.For(message.Type);

                await _publisher.Publish(
                    route.Exchange,
                    route.RoutingKey,
                    message.Id.ToString(),
                    message.Type,
                    message.Payload,
                    message.CorrelationId,
                    headers: null,
                    cancellationToken);

                message.ProcessedAt = DateTime.UtcNow;
                published++;
            }
            catch (System.Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                message.Attempts++;
                message.LastError = Truncate($"{exception.GetType().Name}: {exception.Message}");

                _logger.LogWarning(exception,
                    "Failed to publish outbox message {MessageId} of type {Type} (attempt {Attempts}); stopping the batch",
                    message.Id, message.Type, message.Attempts);

                break;
            }
        }

        if (published > 0)
            _logger.LogInformation("Published {Published} of {Locked} outbox messages", published, messages.Count);

        return published;
    }

    private static string Truncate(string error) =>
        error.Length <= LAST_ERROR_MAX_LENGTH ? error : error[..LAST_ERROR_MAX_LENGTH];
}
