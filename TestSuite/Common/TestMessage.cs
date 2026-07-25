using MessagePack;

namespace TestSuite.Common;

[MessagePackObject]
public record TestMessage()
{
    [Key(0)] public required string String { get; init; }
    [Key(1)] public required int Int { get; init; }
    [Key(2)] public required short Short { get; init; }
    [Key(3)] public required long Long { get; init; }
    [Key(4)] public required float Float { get; init; }
    [Key(5)] public required double Double { get; init; }
    [Key(6)] public required byte[] Bytes { get; init; }
    [Key(7)] public required List<int> List { get; init; }
    [Key(8)] public required HashSet<int> Set { get; init; }
    [Key(9)] public required Dictionary<int, int> Dictionary { get; init; }
};