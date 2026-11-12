using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Repositories.Inbox;

public interface IInboxRepository
{
    Task<bool> Exists(Guid messageId, string consumer, CancellationToken cancellationToken);

    Task Add(InboxMessage message, CancellationToken cancellationToken);
}
