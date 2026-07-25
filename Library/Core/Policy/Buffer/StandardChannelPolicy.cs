using System.Threading.Channels;

namespace HackyMessage.Core.Policy.Buffer;

public sealed class StandardChannelPolicy(int bufferSize, TimeSpan maxDelayInterval): IChannelPolicy
{
    public int BufferSize { get; } = bufferSize;
    public TimeSpan MaxDelayInterval { get; } = maxDelayInterval;

    public MyBoundedChannel<T> Create<T>(bool singleWriter = false, bool singleReader = false)
    {
        var channelOptions = GetChannelOptions(BufferSize, singleWriter, singleReader);
        var channel = Channel.CreateBounded<T>(channelOptions);
        return new MyBoundedChannel<T>(channel, channelOptions);
    }

    private static BoundedChannelOptions GetChannelOptions(int bufferSize, bool singleWriter, bool singleReader)
    {
        return new BoundedChannelOptions(bufferSize)
        {
            Capacity = bufferSize,
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = singleWriter,
            SingleReader = singleReader,
            AllowSynchronousContinuations = false
        };
    }
}