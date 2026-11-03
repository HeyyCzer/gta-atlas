namespace GtaAtlas.Indexer;

/// <summary>One definition of an asset found in one file. Several may exist per asset (DLC overrides).</summary>
public sealed record RawAsset
{
    public required string Kind { get; init; }
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required uint Hash { get; init; }
    public required string Path { get; init; }
    public required string Dlc { get; init; }
    public string? DisplayName { get; init; }
    public object? Data { get; init; }
    public IReadOnlyCollection<string> Tags { get; init; } = [];
    public (string Kind, string Key)? Parent { get; init; }
}
