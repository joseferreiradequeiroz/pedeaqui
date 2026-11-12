using System.Text.Json.Serialization;
using PedeAqui.PaymentGateway.Chaos;
using PedeAqui.PaymentGateway.Payments;

namespace PedeAqui.PaymentGateway;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.Configure<ChaosOptions>(builder.Configuration.GetSection(ChaosOptions.SectionName));
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddSingleton<ChaosState>();
        builder.Services.AddSingleton<PaymentStore>();
        builder.Services.AddSingleton(Random.Shared);
        builder.Services.AddHealthChecks();

        var app = builder.Build();

        app.UseMiddleware<ChaosMiddleware>();
        app.MapHealthChecks("/health");
        app.MapChaosEndpoints();
        app.MapPaymentEndpoints();

        app.Run();
    }
}
