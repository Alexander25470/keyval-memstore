using KeyValueStore.Server;

var host = "127.0.0.1";
var port = 6379;
var healthPort = 0; // 0 = disabled

// Parse CLI args: --host 0.0.0.0 --port 6380 --health-port 8080
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--host" && i + 1 < args.Length) host = args[++i];
    if (args[i] == "--port" && i + 1 < args.Length) port = int.Parse(args[++i]);
    if (args[i] == "--health-port" && i + 1 < args.Length) healthPort = int.Parse(args[++i]);
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var store = new InMemoryStore();
var dispatcher = new CommandDispatcher(store);
var server = new KvServer(store, dispatcher, host, port);

// Start active expiration in background.
_ = store.RunExpirationLoop(cts.Token);

// Start health endpoint if configured.
if (healthPort > 0)
{
    var health = new HttpHealthEndpoint(healthPort);
    _ = health.RunAsync(cts.Token);
    Console.Error.WriteLine($"Health endpoint: http://127.0.0.1:{healthPort}/health");
}

try
{
    await server.RunAsync(cts.Token);
}
catch (OperationCanceledException) { }

Console.Error.WriteLine("Shutdown complete.");
