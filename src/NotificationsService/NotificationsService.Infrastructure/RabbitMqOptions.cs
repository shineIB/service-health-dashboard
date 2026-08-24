namespace NotificationsService.Infrastructure;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public required string HostName { get; init; }
    public int Port { get; init; } = 5672;
    public required string UserName { get; init; }
    public required string Password { get; init; }

    // Must match orders-service's RabbitMqOptions.ExchangeName — both sides declare the
    // same exchange idempotently rather than one owning it exclusively.
    public string ExchangeName { get; init; } = "orders";
    public string QueueName { get; init; } = "notifications.order-events";
    public string DeadLetterExchangeName { get; init; } = "orders.dlx";
    public string DeadLetterQueueName { get; init; } = "orders-notifications.dlq";
}
