using Microsoft.Extensions.Options;
using PedeAqui.Application.UseCases.Idempotency.Cleanup;

namespace PedeAqui.Worker.HostedServices;

public sealed class IdempotencyCleanupService : PeriodicJobService<ICleanupIdempotencyKeysUseCase>
{
    public IdempotencyCleanupService(IServiceScopeFactory scopeFactory, IOptions<JobsOptions> options, ILogger<IdempotencyCleanupService> logger)
        : base(scopeFactory, logger, "idempotency key cleanup", TimeSpan.FromMinutes(options.Value.IdempotencyCleanupIntervalMinutes))
    {
    }
}
