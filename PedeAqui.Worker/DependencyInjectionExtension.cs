using Microsoft.Extensions.Options;
using PedeAqui.Application;
using PedeAqui.Application.Settings;
using PedeAqui.Domain.Services;
using PedeAqui.Domain.Tokens;
using PedeAqui.Infrastructure;
using PedeAqui.Worker.HostedServices;
using PedeAqui.Worker.Services;

namespace PedeAqui.Worker;

public static class DependencyInjectionExtension
{
    public static IServiceCollection AddWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WorkerOptions>(configuration.GetSection(WorkerOptions.SECTION));
        services.Configure<JobsOptions>(configuration.GetSection(JobsOptions.SECTION));
        services.AddOptions<HostOptions>().Configure<IOptions<WorkerOptions>>((hostOptions, workerOptions) =>
            hostOptions.ShutdownTimeout = TimeSpan.FromSeconds(workerOptions.Value.ShutdownTimeoutSeconds));

        services.AddScoped<MessageCorrelationContext>();
        services.AddScoped<ICorrelationContext>(serviceProvider => serviceProvider.GetRequiredService<MessageCorrelationContext>());
        services.AddScoped<ITokenProvider, NoUserTokenProvider>();

        services.AddApplication(configuration);
        services.AddPaymentProcessing();
        services.AddInfrastructure(configuration);
        services.AddPaymentGateway(configuration);
        services.AddMessaging(configuration);

        services.AddSingleton<IValidateOptions<PaymentsOptions>, ReconciliationWindowValidator>();
        services.AddOptions<PaymentsOptions>().ValidateOnStart();

        services.AddHostedService<OutboxPublisherService>();
        services.AddHostedService<OrderCreatedConsumerService>();
        services.AddHostedService<PendingOrderExpirerService>();
        services.AddHostedService<PaymentReconcilerService>();
        services.AddHostedService<IdempotencyCleanupService>();

        return services;
    }
}
