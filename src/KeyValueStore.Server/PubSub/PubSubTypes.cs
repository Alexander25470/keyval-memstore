namespace KeyValueStore.Server.PubSub;

/// <summary>Message delivered through the pub/sub system.</summary>
public record PubSubMessage(string Type, string Channel, string? Pattern, string Data);

/// <summary>Subscription state of a client session.</summary>
public enum SubscriptionMode
{
    None,
    Channel,
    Pattern,
}
