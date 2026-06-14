using KeyValueStore.Server;

var host = "127.0.0.1";
var port = 6379;

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
var dispatcher = new CommandDispatcher(store);
var server = new KvServer(dispatcher, host, port);

_ = store.RunExpirationLoop(cts.Token);

try
{
    await server.RunAsync(cts.Token);
}
catch (OperationCanceledException) { }

Console.Error.WriteLine("Shutdown complete.");
