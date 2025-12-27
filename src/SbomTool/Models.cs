using System.Text.Json.Serialization;

namespace CppSbom;

internal enum DependencyType
{
    HeaderInclude,
    StaticLibrary,
    ImportDirective,
    Com
}

internal sealed record Dependency(string Identifier, DependencyType Type)
{
    public string? SourcePath { get; init; }
    public string? ResolvedPath { get; init; }
    public string? Description { get; init; }
    public string? Metadata { get; init; }
}

internal sealed record ProjectAnalysis(string ProjectPath, IReadOnlyCollection<Dependency> Dependencies, IReadOnlyCollection<string> InternalOutputs);

internal sealed record ProjectReport(
    [property: JsonPropertyName("project")] string ProjectPath,
    [property: JsonPropertyName("dependencies")] IReadOnlyCollection<string> Dependencies);

internal sealed record DependencySummary(
    [property: JsonPropertyName("id")] string Identifier,
    [property: JsonPropertyName("type")] string Type)
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("projects")]
    public HashSet<string> Projects { get; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("sourcePaths")]
    public HashSet<string> SourcePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("resolvedPaths")]
    public HashSet<string> ResolvedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("metadata")]
    public HashSet<string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed record SbomReport(
    [property: JsonPropertyName("generatedAtUtc")] DateTime GeneratedAtUtc,
    [property: JsonPropertyName("projects")] IReadOnlyList<ProjectReport> Projects,
    [property: JsonPropertyName("dependencies")] IReadOnlyList<DependencySummary> Dependencies);
