using System.Threading.Channels;

namespace HackyMessage.Core.Policy.Buffer;

public record MyBoundedChannel<T>(
    Channel<T> Channel,
    BoundedChannelOptions Options
);