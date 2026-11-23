namespace SysSentinel.Data;

public sealed class DatabaseInitializationException : Exception
{
    public DatabaseInitializationException(string message, Exception inner) : base(message, inner) { }
}
