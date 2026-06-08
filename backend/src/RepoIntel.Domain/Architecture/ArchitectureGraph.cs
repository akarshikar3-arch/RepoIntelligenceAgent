namespace RepoIntel.Domain.Architecture;

public enum NodeKind { Module, Component, Service, Controller, Repository, Entity, Pipe, Guard, Interceptor, File, Folder, External }

public sealed class GraphNode
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required NodeKind Kind { get; init; }
    public IReadOnlyDictionary<string, double>? Metrics { get; init; }
}

public sealed class GraphEdge
{
    public required string From { get; init; }
    public required string To { get; init; }
    public string Kind { get; init; } = "depends-on";
}

public sealed class ArchitectureGraph
{
    public required IReadOnlyList<GraphNode> Nodes { get; init; }
    public required IReadOnlyList<GraphEdge> Edges { get; init; }
}
