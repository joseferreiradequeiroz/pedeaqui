using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Repositories.Inbox;

namespace PedeAqui.UnitTests.Fakes;

public sealed class InMemoryInboxRepository : IInboxRepository
{
    private readonly List<InboxMessage> _messages = [];

    public IReadOnlyList<InboxMessage> Messages => _messages;

    public Task<bool> Exists(Guid messageId, string consumer, CancellationToken cancellationToken) =>
        Task.FromResult(_messages.Any(message => message.MessageId == messageId && message.Consumer == consumer));

    public Task Add(InboxMessage message, CancellationToken cancellationToken)
    {
        _messages.Add(message);
        return Task.CompletedTask;
    }
}
