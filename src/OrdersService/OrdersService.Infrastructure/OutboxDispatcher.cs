using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrdersService.Infrastructure.Telemetry;

namespace OrdersService.Infrastructure;

// Reads committed, unpublished outbox rows and actually delivers them to RabbitMQ — the only
// thing in this service that calls IOutboxSender. Runs independently of any HTTP
// request: a row that fails to publish just stays unpublished and gets retried on the next
// poll — RabbitMQ being down between polls never loses the row, because the row was already
// committed in the same transaction as the order (see EfOrderEventOutbox) — up to
// OutboxOptions.MaxAttempts, after which it's marked FailedAtUtc and stops being retried (see
// below and CLAUDE.md, step 7.6, for why one poison row must not be retried forever). Same
// scoped-DbContext-per-tick shape as InventoryService's ReservationExpiryService.
//
// A single poison row does not block the rows after it within a batch: the foreach below tries
// every row in the batch regardless of earlier failures in the same pass, and SaveChangesAsync
// is only called once, at the end. What *would* block healthy rows is a pile of poison rows
// larger than BatchSize all older than a healthy one — OrderBy(CreatedAtUtc) always picks the
// oldest pending rows first, so with no cutoff, that pile would occupy every batch slot forever
// and a healthy row behind it would never even be attempted. Excluding FailedAtUtc rows from
// the query is what closes that gap, not just the per-row try/catch.
public sealed class OutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOutboxSender _sender;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        IOutboxSender sender,
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
            .Where(m => m.PublishedAtUtc == null && m.FailedAtUtc == null)
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
                message.Attempts++;
                message.LastError = ex.Message;
                activity?.SetTag("error.type", ex.GetType().FullName);
                MessagingTelemetry.EventsPublishFailed.Add(1, new KeyValuePair<string, object?>("event_type", message.EventType));

                if (message.Attempts >= _options.MaxAttempts)
                {
                    // Excluded from the next poll's query (see the Where clause above) — this
                    // is what stops a message that can never be published from occupying a
                    // batch slot forever. The row and its LastError stay in the table; resetting
                    // FailedAtUtc to null manually re-queues it.
                    message.FailedAtUtc = DateTimeOffset.UtcNow;
                    activity?.SetTag("outbox.abandoned", true);
                    MessagingTelemetry.EventsAbandoned.Add(1, new KeyValuePair<string, object?>("event_type", message.EventType));
                    _logger.LogError(
                        ex,
                        "Giving up on outbox message {MessageId} ({EventType}) after {Attempts} failed attempts. " +
                        "It will NOT be retried automatically — see OutboxMessage.FailedAtUtc/LastError.",
                        message.Id,
                        message.EventType,
                        message.Attempts);
                }
                else
                {
                    // Left unpublished on purpose — the next poll retries it.
                    _logger.LogWarning(
                        ex,
                        "Failed to publish outbox message {MessageId} ({EventType}), attempt {Attempts} of {MaxAttempts}. Will retry.",
                        message.Id,
                        message.EventType,
                        message.Attempts,
                        _options.MaxAttempts);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
