using Xunit;

namespace NotificationsService.Api.Tests;

[CollectionDefinition(Name)]
public class NotificationsApiCollection : ICollectionFixture<RabbitMqContainerFixture>
{
    public const string Name = "Notifications API collection";
}
