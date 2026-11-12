using Microsoft.EntityFrameworkCore;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Repositories.Inbox;

namespace PedeAqui.Infrastructure.Database.Repositories;

internal class InboxRepository : IInboxRepository
{
    private readonly PedeAquiDbContext _dbContext;

    public InboxRepository(PedeAquiDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> Exists(Guid messageId, string consumer, CancellationToken cancellationToken)
    {
        return await _dbContext.InboxMessages
            .AsNoTracking()
            .AnyAsync(message => message.MessageId == messageId && message.Consumer == consumer, cancellationToken);
    }

    public async Task Add(InboxMessage message, CancellationToken cancellationToken)
    {
        await _dbContext.InboxMessages.AddAsync(message, cancellationToken);
    }
}
