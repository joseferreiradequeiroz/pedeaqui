using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Npgsql;
using PedeAqui.IntegrationTests.Support;
using Testcontainers.PostgreSql;

namespace PedeAqui.IntegrationTests.Health;

[Collection(IntegrationCollection.Name)]
public class HealthTests
{
    private const string LIVE = "health/live";
    private const string READY = "health/ready";
    private const string UNREACHABLE_REDIS = "localhost:1," + PostgresContainerFixture.REDIS_TIMEOUT_SETTINGS;

    private readonly PostgresContainerFixture _infrastructure;

    public HealthTests(PostgresContainerFixture infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task Live_is_healthy()
    {
        using var factory = new PedeAquiWebApplicationFactory(_infrastructure);

        var response = await factory.CreateClient().GetAsync(LIVE);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", (await ReadReport(response)).Status);
    }

    [Fact]
    public async Task Ready_is_healthy_with_all_dependencies()
    {
        using var factory = new PedeAquiWebApplicationFactory(_infrastructure);

        var response = await factory.CreateClient().GetAsync(READY);

        var report = await ReadReport(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", report.Status);
        Assert.Equal(["postgres", "rabbitmq", "redis"], report.Checks.Keys.Order());
        Assert.All(report.Checks.Values, status => Assert.Equal("Healthy", status));
    }

    [Fact]
    public async Task Ready_is_degraded_not_unhealthy_when_redis_down()
    {
        using var factory = new PedeAquiWebApplicationFactory(_infrastructure) { RedisConnectionString = UNREACHABLE_REDIS };

        var response = await factory.CreateClient().GetAsync(READY);

        var report = await ReadReport(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Degraded", report.Status);
        Assert.Equal("Degraded", report.Checks["redis"]);
        Assert.Equal("Healthy", report.Checks["postgres"]);
        Assert.Equal("Healthy", report.Checks["rabbitmq"]);
    }

    [Fact]
    public async Task Ready_is_unhealthy_when_postgres_down()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        using var factory = new PedeAquiWebApplicationFactory(_infrastructure, postgres.GetConnectionString());
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(READY)).StatusCode);

        await postgres.StopAsync();
        NpgsqlConnection.ClearAllPools();
        var response = await client.GetAsync(READY);

        var report = await ReadReport(response);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", report.Status);
        Assert.Equal("Unhealthy", report.Checks["postgres"]);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(LIVE)).StatusCode);
    }

    [Fact]
    public async Task Ready_probes_reuse_a_single_rabbitmq_connection()
    {
        const int probes = 5;
        var clientProvidedName = $"pedeaqui-api-{Guid.NewGuid():N}";
        using var factory = new PedeAquiWebApplicationFactory(_infrastructure);
        var client = factory.WithWebHostBuilder(builder => builder.UseSetting("RabbitMq:ClientProvidedName", clientProvidedName)).CreateClient();

        for (var probe = 0; probe < probes; probe++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(READY)).StatusCode);

        Assert.Equal(1, await _infrastructure.RabbitMqConnectionCountOf(clientProvidedName));
    }

    private static async Task<HealthReportBody> ReadReport(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        return new HealthReportBody(
            root.GetProperty("status").GetString()!,
            root.GetProperty("checks").EnumerateArray().ToDictionary(
                check => check.GetProperty("name").GetString()!,
                check => check.GetProperty("status").GetString()!));
    }

    private sealed record HealthReportBody(string Status, Dictionary<string, string> Checks);
}
