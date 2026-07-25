using System.Threading.Tasks.Sources;
using HackyMessage.Extension;
using Serilog;

namespace HackyMessage.Pooled;

public sealed class ValueTaskCompletionSource<T> : IValueTaskSource<T>, IValueTaskSource
{
    private readonly ILogger _logger = Log.Logger.ForFriendlyContext<ValueTaskCompletionSource<T>>();
    private ManualResetValueTaskSourceCore<T> _core = default;
    
    public ValueTaskCompletionSource()
    {
        _core.RunContinuationsAsynchronously = true;
    }

    public ValueTask<T> ValueTask => new(this, _core.Version);
    

    // --- IValueTaskSource Implementation ---
    public T GetResult(short token) => _core.GetResult(token);
    
    public void Reset() => _core.Reset();

    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) 
        => _core.OnCompleted(continuation, state, token, flags);
    

    // --- Completion Methods ---
    public void SetResult(T result) => _core.SetResult(result);
    public void SetException(System.Exception ex) => _core.SetException(ex);
    
    // --- Safe Completion Methods ---
    public void TrySetResult(T result)
    {
        try
        {
            _core.SetResult(result);
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "An exception occurred while trying to set result on completion source");
            return;
        }
    }
    
    public void TrySetException(System.Exception exception)
    {
        try
        {
            _core.SetException(exception);
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "An exception occurred while trying to set exception on completion source");
            return;
        }
    }
    
    // IValueTaskSource (non-generic) for void support if needed
    void IValueTaskSource.GetResult(short token) => GetResult(token);
}