namespace KeyValueStore.Server.PubSub;

/// <summary>Message delivered through the pub/sub system.</summary>
/// <param name="Data">Message payload as owned byte array (binary-safe).</param>
public record PubSubMessage(string Type, string Channel, string? Pattern, byte[] Data);

/// <summary>Subscription state of a client session.</summary>
public enum SubscriptionMode
{
    None,
    Channel,
    Pattern,
}
