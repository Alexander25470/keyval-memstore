using System.Collections.Concurrent;
using KeyValueStore.Server.Networking;

namespace KeyValueStore.Server.PubSub;

/// <summary>
/// In-memory pub/sub hub. Maintains channel and pattern subscriptions.
/// PUBLISH delivers messages to subscriber inboxes.
/// Thread-safe: all operations use ConcurrentDictionary.
/// </summary>
public class PubSubHub
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, ClientSession>> _channels = new();
    private readonly ConcurrentDictionary<Guid, (string Pattern, ClientSession Session)> _patterns = new();

    // ---- exact channels ----

    public int Subscribe(string channel, ClientSession subscriber)
    {
        var subs = _channels.GetOrAdd(channel, _ => new ConcurrentDictionary<Guid, ClientSession>());
        subs[subscriber.SessionId] = subscriber;
        return subs.Count;
    }

    public int Unsubscribe(string channel, ClientSession subscriber)
    {
        if (!_channels.TryGetValue(channel, out var subs))
            return 0;
        subs.TryRemove(subscriber.SessionId, out _);
        if (subs.IsEmpty)
            _channels.TryRemove(channel, out _);
        return subs.Count;
    }

    public int UnsubscribeAll(ClientSession subscriber)
    {
        int count = 0;
        foreach (var kv in _channels)
        {
            if (kv.Value.TryRemove(subscriber.SessionId, out _))
                count++;
            if (kv.Value.IsEmpty)
                _channels.TryRemove(kv.Key, out _);
        }
        return count;
    }

    // ---- pattern subscriptions ----

    public void PSubscribe(string pattern, ClientSession subscriber)
    {
        _patterns[subscriber.SessionId] = (pattern, subscriber);
    }

    public int PUnsubscribe(string pattern, ClientSession subscriber)
    {
        if (_patterns.TryRemove(subscriber.SessionId, out var entry) && entry.Pattern == pattern)
            return 1;
        return 0;
    }

    public int PUnsubscribeAll(ClientSession subscriber)
    {
        return _patterns.TryRemove(subscriber.SessionId, out _) ? 1 : 0;
    }

    // ---- publish ----

    public int Publish(string channel, ReadOnlyMemory<byte> message)
    {
        int count = 0;
        var data = message.ToArray();

        // Deliver to exact-channel subscribers.
        if (_channels.TryGetValue(channel, out var subs))
        {
            var msg = new PubSubMessage("message", channel, null, data);
            foreach (var kv in subs)
            {
                if (kv.Value.TryPush(msg))
                    count++;
            }
        }

        // Deliver to pattern subscribers.
        foreach (var kv in _patterns)
        {
            if (Glob.Match(channel, kv.Value.Pattern))
            {
                var pm = new PubSubMessage("pmessage", channel, kv.Value.Pattern, data);
                if (kv.Value.Session.TryPush(pm))
                    count++;
            }
        }

        return count;
    }

    // ---- clean-up ----

    public void RemoveSession(ClientSession subscriber)
    {
        UnsubscribeAll(subscriber);
        PUnsubscribeAll(subscriber);
    }
}
