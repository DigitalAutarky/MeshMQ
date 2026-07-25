using System.Runtime.CompilerServices;

namespace HackyMessage.Persistence.Provider.Disk;

public record Success();
public record RetryLater();
public record SerializationFailure();
public record PersistenceCapacityReached();
public record PersistenceFailure();
public record Cancelled();

//TODO: use proper union declaration once supported by rider..
[Union]
public readonly struct PersistenceResult : IUnion
{
    public PersistenceResult(Success success) => Value = success;
    public PersistenceResult(RetryLater retryLater) => Value = retryLater;
    public PersistenceResult(SerializationFailure serializationFailure) => Value = serializationFailure;
    public PersistenceResult(PersistenceCapacityReached persistenceCapacityReached) => Value = persistenceCapacityReached;
    public PersistenceResult(PersistenceFailure persistenceFailure) => Value = persistenceFailure;
    public PersistenceResult(Cancelled cancelled) => Value = cancelled;
    public object? Value { get; }
}

public static class CachedPersistenceResult
{
    public static readonly PersistenceResult Success = new Success();
    public static readonly PersistenceResult RetryLater = new RetryLater();
    public static readonly PersistenceResult SerializationFailure = new SerializationFailure();
    public static readonly PersistenceResult PersistenceCapacityReached = new PersistenceCapacityReached();
    public static readonly PersistenceResult PersistenceFailure = new PersistenceFailure();
    public static readonly PersistenceResult Cancelled = new Cancelled();
}