namespace KeyValueStore.Server.Exceptions;

public class ProtocolException : Exception
{
    public ProtocolException(string message) : base(message) { }
}
