using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;

namespace PedeAqui.Infrastructure.Redis;

public sealed class RedisConnection : IAsyncDisposable
{
    private readonly string? _connectionString;
    private readonly string _keyPrefix;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<RedisConnection> _logger;
    private readonly object _connectionLock = new();
    private Task<ConnectionMultiplexer>? _connecting;

    public RedisConnection(
        IConfiguration configuration,
        IOptions<RedisOptions> options,
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<RedisConnection> logger)
    {
        _connectionString = configuration.GetConnectionString("Redis");
        _keyPrefix = options.Value.InstanceName;
        _pipeline = pipelineProvider.GetPipeline(RedisResilience.PIPELINE_NAME);
        _logger = logger;
    }

    public async Task<bool> TryExecute(Func<IDatabase, CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        var outcome = await TryExecute<bool>(async (database, token) =>
        {
            await operation(database, token);
            return true;
        }, cancellationToken);

        return outcome.Succeeded;
    }

    public async Task<RedisOutcome<T>> TryExecute<T>(Func<IDatabase, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        try
        {
            var value = await _pipeline.ExecuteAsync(async token =>
            {
                var multiplexer = await GetMultiplexer().WaitAsync(token);

                return await operation(DatabaseOf(multiplexer), token).WaitAsync(token);
            }, cancellationToken);

            return RedisOutcome<T>.Success(value);
        }
        catch (System.Exception exception)
        {
            _logger.LogWarning(exception, "Chamada ao Redis falhou. Seguindo sem Redis.");

            return RedisOutcome<T>.Failure();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connecting is { IsCompletedSuccessfully: true })
            await _connecting.Result.DisposeAsync();
    }

    private Task<ConnectionMultiplexer> GetMultiplexer()
    {
        lock (_connectionLock)
        {
            if (_connecting is null || _connecting.IsFaulted)
                _connecting = Connect();

            return _connecting;
        }
    }

    private Task<ConnectionMultiplexer> Connect()
    {
        var configuration = ConfigurationOptions.Parse(_connectionString ?? string.Empty);
        configuration.AbortOnConnectFail = false;

        return ConnectionMultiplexer.ConnectAsync(configuration);
    }

    private IDatabase DatabaseOf(ConnectionMultiplexer multiplexer)
    {
        var database = multiplexer.GetDatabase();

        return string.IsNullOrEmpty(_keyPrefix) ? database : database.WithKeyPrefix(_keyPrefix);
    }
}
