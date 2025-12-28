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
    [property: JsonPropertyName("type")] DependencyType Type)
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

internal sealed record SpdxDocument(
    [property: JsonPropertyName("spdxVersion")] string SpdxVersion,
    [property: JsonPropertyName("dataLicense")] string DataLicense,
    [property: JsonPropertyName("SPDXID")] string SpdxId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("documentNamespace")] string DocumentNamespace,
    [property: JsonPropertyName("creationInfo")] SpdxCreationInfo CreationInfo,
    [property: JsonPropertyName("packages")] IReadOnlyList<SpdxPackage> Packages,
    [property: JsonPropertyName("relationships")] IReadOnlyList<SpdxRelationship> Relationships);

internal sealed record SpdxCreationInfo(
    [property: JsonPropertyName("creators")] IReadOnlyList<string> Creators,
    [property: JsonPropertyName("created")] DateTime Created);

internal sealed record SpdxPackage(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("SPDXID")] string SpdxId,
    [property: JsonPropertyName("downloadLocation")] string DownloadLocation,
    [property: JsonPropertyName("filesAnalyzed")] bool FilesAnalyzed,
    [property: JsonPropertyName("licenseConcluded")] string LicenseConcluded,
    [property: JsonPropertyName("licenseDeclared")] string LicenseDeclared,
    [property: JsonPropertyName("copyrightText")] string CopyrightText)
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal sealed record SpdxRelationship(
    [property: JsonPropertyName("spdxElementId")] string SpdxElementId,
    [property: JsonPropertyName("relatedSpdxElement")] string RelatedSpdxElement,
    [property: JsonPropertyName("relationshipType")] string RelationshipType);

internal sealed record CycloneDxBom(
    [property: JsonPropertyName("bomFormat")] string BomFormat,
    [property: JsonPropertyName("specVersion")] string SpecVersion,
    [property: JsonPropertyName("version")] int Version)
{
    [JsonPropertyName("metadata")]
    public CycloneDxMetadata? Metadata { get; set; }

    [JsonPropertyName("components")]
    public IReadOnlyList<CycloneDxComponent>? Components { get; set; }

    [JsonPropertyName("dependencies")]
    public IReadOnlyList<CycloneDxDependency>? Dependencies { get; set; }
}

internal sealed record CycloneDxMetadata(
    [property: JsonPropertyName("timestamp")] DateTime Timestamp)
{
    [JsonPropertyName("tools")]
    public CycloneDxTools? Tools { get; set; }
}

internal sealed record CycloneDxTools(
    [property: JsonPropertyName("components")] IReadOnlyList<CycloneDxComponent> Components);

internal sealed record CycloneDxComponent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string Name)
{
    [JsonPropertyName("bom-ref")]
    public string? BomRef { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("properties")]
    public IReadOnlyList<CycloneDxProperty>? Properties { get; set; }
}

internal sealed record CycloneDxDependency(
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("dependsOn")] IReadOnlyList<string> DependsOn);

internal sealed record CycloneDxProperty(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string Value);
