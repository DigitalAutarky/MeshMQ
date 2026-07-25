namespace HackyMessage.Core.Policy.Buffer;

public interface IChannelPolicy
{
    int BufferSize { get; }
    TimeSpan MaxDelayInterval { get; }
    MyBoundedChannel<T> Create<T>(bool singleWriter = false, bool singleReader = false);
}