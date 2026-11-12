using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace PedeAqui.IntegrationTests.Support;

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "Integration";
}

public sealed class PostgresContainerFixture : IAsyncLifetime
{
    public const string REDIS_IMAGE = "redis:7-alpine";
    public const string REDIS_TIMEOUT_SETTINGS = "abortConnect=false,connectTimeout=1000,asyncTimeout=1000";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RedisContainer _redisContainer = new RedisBuilder(REDIS_IMAGE).Build();
    private readonly RabbitMqContainer _rabbitMqContainer = RabbitMqTestBroker.CreateContainer();

    public string RedisConnectionString => $"{_redisContainer.GetConnectionString()},{REDIS_TIMEOUT_SETTINGS}";

    public RabbitMqEndpoint RabbitMq => RabbitMqTestBroker.EndpointOf(_rabbitMqContainer);

    public Task InitializeAsync() => Task.WhenAll(_container.StartAsync(), _redisContainer.StartAsync(), _rabbitMqContainer.StartAsync());

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        await _redisContainer.DisposeAsync();
        await _rabbitMqContainer.DisposeAsync();
    }

    public async Task<int> RabbitMqConnectionCountOf(string clientProvidedName)
    {
        var result = await _rabbitMqContainer.ExecAsync(["rabbitmqctl", "-q", "list_connections", "--no-table-headers", "client_properties"]);

        return result.Stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Count(clientProperties => clientProperties.Contains($"{{\"connection_name\",\"{clientProvidedName}\"}}", StringComparison.Ordinal));
    }

    public string CreateDatabaseConnectionString()
    {
        var databaseName = $"pedeaqui_{Guid.NewGuid():N}";

        using var connection = new NpgsqlConnection(_container.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        command.ExecuteNonQuery();

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName
        }.ConnectionString;
    }
}
