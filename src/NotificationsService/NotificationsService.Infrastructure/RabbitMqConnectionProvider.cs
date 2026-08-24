using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace NotificationsService.Infrastructure;

// Same lazy-connect/auto-recovery shape as orders-service's RabbitMqConnectionProvider — kept
// as its own copy rather than a shared library (see CLAUDE.md, step 7: no shared Contracts
// assembly between services). Here, unlike orders-service, GetConnectionAsync's caller
// (OrderEventConsumer) *does* retry the initial connect in a loop, because for this service
// RabbitMQ is a hard dependency, not a best-effort side effect.
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

    // Read by RabbitMqHealthCheck — a cheap, non-blocking check of the last-known connection
    // state, not a fresh probe.
    public bool IsConnected => _connection is { IsOpen: true };

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
