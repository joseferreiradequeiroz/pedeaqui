using Microsoft.Extensions.Options;
using PedeAqui.Application.UseCases.Orders.Expire;

namespace PedeAqui.Worker.HostedServices;

public sealed class PendingOrderExpirerService : PeriodicJobService<IExpirePendingOrdersUseCase>
{
    public PendingOrderExpirerService(IServiceScopeFactory scopeFactory, IOptions<JobsOptions> options, ILogger<PendingOrderExpirerService> logger)
        : base(scopeFactory, logger, "pending order expirer", TimeSpan.FromSeconds(options.Value.ExpirationIntervalSeconds))
    {
    }
}
