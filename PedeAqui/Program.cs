using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.Options;
using PedeAqui.Application;
using PedeAqui.Domain.Services;
using PedeAqui.Domain.Tokens;
using PedeAqui.ExceptionHandlers;
using PedeAqui.Extensions;
using PedeAqui.Health;
using PedeAqui.Infrastructure;
using PedeAqui.Infrastructure.Database;
using PedeAqui.Middlewares;
using PedeAqui.RateLimiting;
using PedeAqui.Security;
using PedeAqui.Services;
using PedeAqui.Settings;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services.Configure<ApiEdgeSettings>(builder.Configuration.GetSection(ApiEdgeSettings.SECTION));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SECTION));

builder.Services.AddOptions<HostOptions>().Configure<IOptions<ApiEdgeSettings>>((hostOptions, edgeSettings) =>
    hostOptions.ShutdownTimeout = TimeSpan.FromSeconds(edgeSettings.Value.ShutdownTimeoutSeconds));

builder.WebHost.ConfigureKestrel((context, kestrel) =>
    kestrel.Limits.MaxRequestBodySize = context.Configuration.GetSection(ApiEdgeSettings.SECTION).Get<ApiEdgeSettings>()!.MaxRequestBodyBytes);

builder.Services.AddExceptionHandler<PedeAquiExceptionHandler>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsIdentifiers.Add);

builder.Services.AddRequestTimeouts();
builder.Services.AddOptions<RequestTimeoutOptions>().Configure<IOptions<ApiEdgeSettings>>((timeouts, edgeSettings) =>
    timeouts.DefaultPolicy = new RequestTimeoutPolicy { Timeout = TimeSpan.FromSeconds(edgeSettings.Value.RequestTimeoutSeconds) });

builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerWithJwt();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITokenProvider, HttpContextTokenProvider>();
builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPedeAquiHealthChecks(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseMiddleware<RequestBodySizeLimitMiddleware>();
app.UseRequestTimeouts();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RedisRateLimitMiddleware>();

app.MapControllers();
app.MapPedeAquiHealthChecks();

await PrepareDatabase();

app.Run();

async Task PrepareDatabase()
{
    await using var scope = app.Services.CreateAsyncScope();

    if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test"))
        await DatabaseMigration.Migrate(scope.ServiceProvider, app.Lifetime.ApplicationStopping);

    await AdminSeeder.Seed(scope.ServiceProvider, app.Configuration, app.Lifetime.ApplicationStopping);
}

public partial class Program;
