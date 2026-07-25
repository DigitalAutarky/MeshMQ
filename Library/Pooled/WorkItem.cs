namespace HackyMessage.Pooled;

public sealed record WorkItem<TInput, TResult>
{
    public TInput? Item { get; private set; }
    public  ValueTaskCompletionSource<TResult>? CompletionSource { get; private set; }

    public void Activate(TInput item, ValueTaskCompletionSource<TResult> cs)
    {
        Item = item;
        CompletionSource = cs;
    }
    
    public void Deactivate()
    {
        Item = default;
        CompletionSource = null;
    }
}