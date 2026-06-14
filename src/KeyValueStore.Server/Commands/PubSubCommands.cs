using System.Text;
using KeyValueStore.Server.Networking;
using KeyValueStore.Server.PubSub;
using KeyValueStore.Server.Resp;

namespace KeyValueStore.Server.Commands;

public static class PubSubCommands
{
    private static string Str(ReadOnlyMemory<byte> b) => Encoding.ASCII.GetString(b.Span);

    public static async ValueTask Publish(ReadOnlyMemory<byte>[] args, PubSubHub hub, RespWriter writer)
    {
        if (args.Length != 3) { await writer.WriteError("wrong number of arguments for 'PUBLISH' command"); return; }
        int count = hub.Publish(Str(args[1]), args[2]);
        await writer.WriteInteger(count);
    }

    public static async ValueTask Subscribe(ReadOnlyMemory<byte>[] args, RespWriter writer, PubSubHub hub, ClientSession? subscriber)
    {
        if (args.Length < 2) { await writer.WriteError("wrong number of arguments for 'SUBSCRIBE' command"); return; }
        if (subscriber is null) { await writer.WriteError("SUBSCRIBE not available"); return; }

        int count = 0;
        for (int i = 1; i < args.Length; i++)
            count = hub.Subscribe(Str(args[i]), subscriber);

        subscriber.EnterSubscriptionMode(SubscriptionMode.Channel);

        for (int i = 1; i < args.Length; i++)
            await writer.WriteSubscribeAck("subscribe", Str(args[i]), count);
    }

    public static async ValueTask Unsubscribe(ReadOnlyMemory<byte>[] args, RespWriter writer, PubSubHub hub, ClientSession? subscriber)
    {
        if (subscriber is null) { await writer.WriteError("UNSUBSCRIBE not available"); return; }

        if (args.Length == 1)
        {
            hub.UnsubscribeAll(subscriber);
            subscriber.EnterSubscriptionMode(SubscriptionMode.None);
        }
        else
        {
            int count = 0;
            for (int i = 1; i < args.Length; i++)
            {
                count = hub.Unsubscribe(Str(args[i]), subscriber);
                await writer.WriteSubscribeAck("unsubscribe", Str(args[i]), count);
            }
        }
    }

    public static async ValueTask PSubscribe(ReadOnlyMemory<byte>[] args, RespWriter writer, PubSubHub hub, ClientSession? subscriber)
    {
        if (args.Length < 2) { await writer.WriteError("wrong number of arguments for 'PSUBSCRIBE' command"); return; }
        if (subscriber is null) { await writer.WriteError("PSUBSCRIBE not available"); return; }

        for (int i = 1; i < args.Length; i++)
            hub.PSubscribe(Str(args[i]), subscriber);

        subscriber.EnterSubscriptionMode(SubscriptionMode.Pattern);

        for (int i = 1; i < args.Length; i++)
            await writer.WriteSubscribeAck("psubscribe", Str(args[i]), 1);
    }

    public static async ValueTask PUnsubscribe(ReadOnlyMemory<byte>[] args, RespWriter writer, PubSubHub hub, ClientSession? subscriber)
    {
        if (subscriber is null) { await writer.WriteError("PUNSUBSCRIBE not available"); return; }

        if (args.Length == 1)
        {
            hub.PUnsubscribeAll(subscriber);
            subscriber.EnterSubscriptionMode(SubscriptionMode.None);
        }
        else
        {
            for (int i = 1; i < args.Length; i++)
            {
                int remaining = hub.PUnsubscribe(Str(args[i]), subscriber);
                await writer.WriteSubscribeAck("punsubscribe", Str(args[i]), remaining);
            }
        }
    }
}

