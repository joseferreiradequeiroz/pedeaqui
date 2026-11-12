using Microsoft.Extensions.Options;
using PedeAqui.Application.UseCases.Payments.Reconcile;

namespace PedeAqui.Worker.HostedServices;

public sealed class PaymentReconcilerService : PeriodicJobService<IReconcilePaymentsUseCase>
{
    public PaymentReconcilerService(IServiceScopeFactory scopeFactory, IOptions<JobsOptions> options, ILogger<PaymentReconcilerService> logger)
        : base(scopeFactory, logger, "payment reconciler", TimeSpan.FromSeconds(options.Value.ReconciliationIntervalSeconds))
    {
    }
}
