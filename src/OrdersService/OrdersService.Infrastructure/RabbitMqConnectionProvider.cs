using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace OrdersService.Infrastructure;

// Connects lazily on first publish, not at startup: orders-service must be able to start and
// serve orders even if RabbitMQ isn't up yet, since publishing is best-effort (see
// IEventPublisher). AutomaticRecoveryEnabled/TopologyRecoveryEnabled mean a RabbitMQ restart
// heals this connection on its own once it comes back, without redeploying this pod.
public sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnectionProvider(RabbitMqOptions options, ILogger<RabbitMqConnectionProvider> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
            return _connection;

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
                return _connection;

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _logger.LogInformation("Connected to RabbitMQ at {HostName}:{Port}.", _options.HostName, _options.Port);
            return _connection;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        _connectLock.Dispose();
    }
}
