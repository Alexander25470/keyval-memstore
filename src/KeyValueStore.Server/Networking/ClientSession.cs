using System.Net.Sockets;
using System.Threading.Channels;
using KeyValueStore.Server.Exceptions;
using KeyValueStore.Server.PubSub;
using KeyValueStore.Server.Resp;

namespace KeyValueStore.Server.Networking;

public class ClientSession
{
    private readonly TcpClient _client;
    private readonly CommandDispatcher _dispatcher;
    private readonly PubSubHub _hub;
    private readonly RespReader _reader = new();
    private readonly Channel<PubSubMessage> _inbox = Channel.CreateBounded<PubSubMessage>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Guid _sessionId = Guid.NewGuid();
    private RespWriter? _writer;
    private SubscriptionMode _pendingMode;

    public Guid SessionId => _sessionId;

    public ClientSession(TcpClient client, CommandDispatcher dispatcher, PubSubHub hub)
    {
        _client = client;
        _dispatcher = dispatcher;
        _hub = hub;
    }

    public bool TryPush(PubSubMessage msg)
        => _inbox.Writer.TryWrite(msg);

    public void EnterSubscriptionMode(SubscriptionMode mode)
        => _pendingMode = mode;

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var stream = _client.GetStream();
            _writer = new RespWriter(stream);

            while (!ct.IsCancellationRequested)
            {
                ReadOnlyMemory<byte>[]? args;
                try { args = await _reader.ReadCommand(stream); }
                catch (ProtocolException ex) { await _writer.WriteError(ex.Message); break; }
                if (args is null) break;

                try { await _dispatcher.ExecuteAsync(args, _writer, this); }
                catch (QuitException) { break; }

                // Subscription commands set _pendingMode via EnterSubscriptionMode.
                if (_pendingMode != SubscriptionMode.None)
                {
                    await ReceivePushLoop(stream);
                    if (_pendingMode == SubscriptionMode.None)
                        continue; // back to normal loop
                    break;
                }
            }
        }
        catch (IOException) { }
        finally
        {
            _hub.RemoveSession(this);
            _reader.Dispose();
            _client.Dispose();
        }
    }

    private async Task ReceivePushLoop(NetworkStream stream)
    {
        try
        {
            var socketTask = _reader.ReadCommand(stream).AsTask();

            while (true)
            {
                var inboxTask = _inbox.Reader.WaitToReadAsync().AsTask();
                var done = await Task.WhenAny(socketTask, inboxTask);

                if (done == socketTask)
                {
                    var args = await socketTask;
                    if (args is null) break;

                    _pendingMode = SubscriptionMode.None;
                    try { await _dispatcher.ExecuteAsync(args, _writer!, this); }
                    catch (QuitException) { break; }

                    if (_pendingMode == SubscriptionMode.None)
                        break;

                    // Only start a new read after consuming the previous result.
                    socketTask = _reader.ReadCommand(stream).AsTask();
                }
                else
                {
                    if (!await inboxTask) break;
                    while (_inbox.Reader.TryRead(out var msg))
                        await _writer!.WritePush(msg);
                }
            }
        }
        catch (IOException) { }
        catch (OperationCanceledException) { }
    }
}
