using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrdersService.Infrastructure.Telemetry;

namespace OrdersService.Infrastructure;

// Reads committed, unpublished outbox rows and actually delivers them to RabbitMQ — the only
// thing in this service that calls RabbitMqOutboxSender. Runs independently of any HTTP
// request: a row that fails to publish just stays unpublished and gets retried on the next
// poll, indefinitely — RabbitMQ being down between polls never loses the row, because the row
// was already committed in the same transaction as the order (see EfOrderEventOutbox). Same
// scoped-DbContext-per-tick shape as InventoryService's ReservationExpiryService.
public sealed class OutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOutboxSender _sender;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        RabbitMqOutboxSender sender,
        IOptions<OutboxOptions> options,
        ILogger<OutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _sender = sender;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await DispatchPendingAsync(stoppingToken);
        }
    }

    private async Task DispatchPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var pending = await dbContext.OutboxMessages
            .Where(m => m.PublishedAtUtc == null)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return;

        foreach (var message in pending)
        {
            using var activity = MessagingTelemetry.ActivitySource.StartActivity("order.publish-event");
            activity?.SetTag("messaging.routing_key", message.EventType);
            activity?.SetTag("outbox.message_id", message.Id);

            try
            {
                await _sender.PublishAsync(message.EventType, Encoding.UTF8.GetBytes(message.PayloadJson), cancellationToken);

                message.PublishedAtUtc = DateTimeOffset.UtcNow;
                message.LastError = null;
                MessagingTelemetry.EventsPublished.Add(1, new KeyValuePair<string, object?>("event_type", message.EventType));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Left unpublished on purpose — the next poll retries it. No max-attempts
                // cutoff: an outbox row that can never be published (not just transiently, but
                // structurally) would need alerting/manual intervention either way, and this
                // system has no consumer of that signal yet — see CLAUDE.md, step 7.5.
                message.Attempts++;
                message.LastError = ex.Message;
                activity?.SetTag("error.type", ex.GetType().FullName);
                MessagingTelemetry.EventsPublishFailed.Add(1, new KeyValuePair<string, object?>("event_type", message.EventType));
                _logger.LogWarning(
                    ex,
                    "Failed to publish outbox message {MessageId} ({EventType}), attempt {Attempts}. Will retry.",
                    message.Id,
                    message.EventType,
                    message.Attempts);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
