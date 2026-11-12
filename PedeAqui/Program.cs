using System.Text.Json.Serialization;
using PedeAqui.Application;
using PedeAqui.Domain.Tokens;
using PedeAqui.ExceptionHandlers;
using PedeAqui.Extensions;
using PedeAqui.Infrastructure;
using PedeAqui.Infrastructure.Database;
using PedeAqui.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddExceptionHandler<PedeAquiExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerWithJwt();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITokenProvider, HttpContextTokenProvider>();

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await PrepareDatabase();

app.Run();

async Task PrepareDatabase()
{
    await using var scope = app.Services.CreateAsyncScope();

    if (app.Environment.IsDevelopment())
        await DatabaseMigration.Migrate(scope.ServiceProvider);

    await AdminSeeder.Seed(scope.ServiceProvider, app.Configuration);
}

public partial class Program;
