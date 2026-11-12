using PedeAqui.Worker;
using PedeAqui.Worker.Health;
using Serilog;
using Serilog.Formatting.Compact;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services.AddWorker(builder.Configuration);
builder.Services.AddWorkerHealthLogging();

var host = builder.Build();

await host.RunAsync();
