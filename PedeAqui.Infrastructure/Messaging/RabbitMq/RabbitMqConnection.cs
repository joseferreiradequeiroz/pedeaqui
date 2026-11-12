using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace PedeAqui.Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqConnection : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnection> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IConnection> Get(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true } openConnection)
            return openConnection;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true } connectionOpenedMeanwhile)
                return connectionOpenedMeanwhile;

            await DisposeCurrentConnection();
            _connection = await Connect(cancellationToken);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeCurrentConnection();
        _gate.Dispose();
    }

    private async Task<IConnection> Connect(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            ClientProvidedName = _options.ClientProvidedName,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(_options.ConnectionTimeoutSeconds),
            AutomaticRecoveryEnabled = false,
            TopologyRecoveryEnabled = false
        };

        var connection = await factory.CreateConnectionAsync(cancellationToken);

        try
        {
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await RabbitMqTopology.Declare(channel, _options, cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        _logger.LogInformation("Connected to RabbitMQ at {HostName}:{Port} and declared the topology", _options.HostName, _options.Port);
        return connection;
    }

    private async Task DisposeCurrentConnection()
    {
        if (_connection is null)
            return;

        var staleConnection = _connection;
        _connection = null;

        try
        {
            await staleConnection.DisposeAsync();
        }
        catch (System.Exception exception)
        {
            _logger.LogWarning(exception, "Failed to dispose a stale RabbitMQ connection");
        }
    }
}
