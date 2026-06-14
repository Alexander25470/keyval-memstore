using KeyValueStore.Server.Networking;
using KeyValueStore.Server.PubSub;
using KeyValueStore.Server.Resp;

namespace KeyValueStore.Server.Commands;

public static class PubSubCommands
{
    public static async ValueTask Publish(string[] args, PubSubHub hub, RespWriter writer)
    {
        if (args.Length != 3) { await writer.WriteError("wrong number of arguments for 'PUBLISH' command"); return; }
        int count = hub.Publish(args[1], args[2]);
        await writer.WriteInteger(count);
    }

    public static async ValueTask Subscribe(string[] args, RespWriter writer, PubSubHub hub, ClientSession? subscriber)
    {
        if (args.Length < 2) { await writer.WriteError("wrong number of arguments for 'SUBSCRIBE' command"); return; }
        if (subscriber is null) { await writer.WriteError("SUBSCRIBE not available"); return; }

        int count = 0;
        for (int i = 1; i < args.Length; i++)
            count = hub.Subscribe(args[i], subscriber);

        subscriber.EnterSubscriptionMode(SubscriptionMode.Channel);

        for (int i = 1; i < args.Length; i++)
            await writer.WriteSubscribeAck("subscribe", args[i], count);
    }

    public static async ValueTask Unsubscribe(string[] args, RespWriter writer, PubSubHub hub, ClientSession? subscriber)
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
                count = hub.Unsubscribe(args[i], subscriber);
                await writer.WriteSubscribeAck("unsubscribe", args[i], count);
            }
        }
    }

    public static async ValueTask PSubscribe(string[] args, RespWriter writer, PubSubHub hub, ClientSession? subscriber)
    {
        if (args.Length < 2) { await writer.WriteError("wrong number of arguments for 'PSUBSCRIBE' command"); return; }
        if (subscriber is null) { await writer.WriteError("PSUBSCRIBE not available"); return; }

        for (int i = 1; i < args.Length; i++)
            hub.PSubscribe(args[i], subscriber);

        subscriber.EnterSubscriptionMode(SubscriptionMode.Pattern);

        for (int i = 1; i < args.Length; i++)
            await writer.WriteSubscribeAck("psubscribe", args[i], 1);
    }

    public static async ValueTask PUnsubscribe(string[] args, RespWriter writer, PubSubHub hub, ClientSession? subscriber)
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
                int remaining = hub.PUnsubscribe(args[i], subscriber);
                await writer.WriteSubscribeAck("punsubscribe", args[i], remaining);
            }
        }
    }
}

