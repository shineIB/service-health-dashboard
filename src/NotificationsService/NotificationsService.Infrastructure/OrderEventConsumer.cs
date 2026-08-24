using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NotificationsService.Infrastructure;

// Owns all RabbitMQ plumbing (connect, declare topology, consume, ack/nack) and delegates
// actual message handling to OrderEventHandler. The retry loop below only covers the
// *initial* connect: once consuming has started, RabbitMqConnectionProvider's
// AutomaticRecoveryEnabled/TopologyRecoveryEnabled transparently recovers the connection,
// channel, and this consumer after a transient RabbitMQ restart — there's nothing this loop
// needs to do for that case.
public sealed class OrderEventConsumer : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly RabbitMqOptions _options;
    private readonly OrderEventHandler _handler;
    private readonly ILogger<OrderEventConsumer> _logger;

    public OrderEventConsumer(
        RabbitMqConnectionProvider connectionProvider,
        RabbitMqOptions options,
        OrderEventHandler handler,
        ILogger<OrderEventConsumer> logger)
    {
        _connectionProvider = connectionProvider;
        _options = options;
        _handler = handler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var connection = await _connectionProvider.GetConnectionAsync(stoppingToken);
                var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

                await DeclareTopologyAsync(channel, stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, ea) =>
                {
                    var acked = await _handler.HandleAsync(ea.Body, stoppingToken);
                    if (acked)
                        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, stoppingToken);
                    else
                        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, stoppingToken);
                };

                await channel.BasicConsumeAsync(_options.QueueName, autoAck: false, consumer, stoppingToken);
                _logger.LogInformation("Consuming order events from queue {QueueName}.", _options.QueueName);

                // Blocks here until shutdown is requested. A transient connection/channel drop
                // does not fall through to here — automatic recovery handles it in place.
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not connect to or consume from RabbitMQ; retrying in {Delay}.", RetryDelay);
                try
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: _options.DeadLetterExchangeName,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: _options.DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: _options.DeadLetterQueueName,
            exchange: _options.DeadLetterExchangeName,
            routingKey: string.Empty,
            cancellationToken: cancellationToken);

        // Messages nacked without requeue (see the ReceivedAsync handler above) land here
        // instead of being dropped or looping forever.
        var queueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = _options.DeadLetterExchangeName,
        };

        await channel.QueueDeclareAsync(
            queue: _options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArguments,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: _options.QueueName,
            exchange: _options.ExchangeName,
            routingKey: "order.*",
            cancellationToken: cancellationToken);
    }
}
