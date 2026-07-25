using System.Runtime.CompilerServices;
using HackyMessage.Persistence;

namespace HackyMessage.Core.Partition;

public record Success();
public record Retry();
public record Cancelled();
public record Failed();
public record Unavailable();

//TODO: use proper union declaration once supported by rider..
[Union]
public readonly struct AcceptResult : IUnion
{
    public AcceptResult(Success success) => Value = success;
    public AcceptResult(Retry retry) => Value = retry;
    public AcceptResult(Cancelled cancelled) => Value = cancelled;
    public AcceptResult(Failed failed) => Value = failed;
    public AcceptResult(Unavailable unavailable) => Value = unavailable;
    public object? Value { get; }
}

public static class CachedAcceptResult
{
    public static readonly AcceptResult Success = new Success();
    public static readonly AcceptResult RetryLater = new Retry();
    public static readonly AcceptResult Cancelled = new Cancelled();
    public static readonly AcceptResult Failed = new Failed();
    public static readonly AcceptResult Unavailable = new Unavailable();
}