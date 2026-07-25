using MessagePack;

namespace HackyMessage.Core;

[MessagePackObject]
public record Envelope<T>()
{ 
    [Key(0)] public required string Id { get; init; }
    [Key(1)] public required string CorrelationId { get; init; }
    [Key(2)] public required DateTime CreatedAt { get; init; }
    [Key(3)] public required T Message { get; init; }
}
