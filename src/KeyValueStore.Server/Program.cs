using KeyValueStore.Server;
using KeyValueStore.Server.Networking;
using KeyValueStore.Server.Store;
using KeyValueStore.Server.PubSub;

var host = Environment.GetEnvironmentVariable("KV_HOST") ?? "127.0.0.1";
var port = int.TryParse(Environment.GetEnvironmentVariable("KV_PORT"), out var envPort) ? envPort : 6379;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--host" && i + 1 < args.Length) host = args[++i];
    if (args[i] == "--port" && i + 1 < args.Length) port = int.Parse(args[++i]);
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var store = new InMemoryStore();
var pubSub = new PubSubHub();
var dispatcher = new CommandDispatcher(store, pubSub);
var server = new KvServer(dispatcher, pubSub, host, port);

_ = store.RunExpirationLoop(cts.Token);

try
{
    await server.RunAsync(cts.Token);
}
catch (OperationCanceledException) { }

Console.Error.WriteLine("Shutdown complete.");
