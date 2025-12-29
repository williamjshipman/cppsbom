using System.Text.Json.Serialization;

namespace CppSbom;

/// <summary>
/// Defines dependency categories for SBOM reporting.
/// </summary>
internal enum DependencyType
{
    /// <summary>
    /// Header include dependency.
    /// </summary>
    HeaderInclude,
    /// <summary>
    /// Static or linked library dependency.
    /// </summary>
    StaticLibrary,
    /// <summary>
    /// Import directive dependency.
    /// </summary>
    ImportDirective,
    /// <summary>
    /// COM registry dependency.
    /// </summary>
    Com
}

/// <summary>
/// Represents a resolved dependency entry.
/// </summary>
/// <param name="Identifier">Dependency identifier.</param>
/// <param name="Type">Dependency category.</param>
internal sealed record Dependency(string Identifier, DependencyType Type)
{
    /// <summary>
    /// Gets the source path where the dependency was discovered.
    /// </summary>
    public string? SourcePath { get; init; }
    /// <summary>
    /// Gets the resolved file path for the dependency.
    /// </summary>
    public string? ResolvedPath { get; init; }
    /// <summary>
    /// Gets the dependency description.
    /// </summary>
    public string? Description { get; init; }
    /// <summary>
    /// Gets additional metadata for the dependency.
    /// </summary>
    public string? Metadata { get; init; }
}

/// <summary>
/// Contains analysis results for a project.
/// </summary>
/// <param name="ProjectPath">Project file path.</param>
/// <param name="Dependencies">Resolved dependencies.</param>
/// <param name="InternalOutputs">Internal outputs produced by the project.</param>
internal sealed record ProjectAnalysis(string ProjectPath, IReadOnlyCollection<Dependency> Dependencies, IReadOnlyCollection<string> InternalOutputs);

/// <summary>
/// Represents a project entry in the SBOM report.
/// </summary>
/// <param name="ProjectPath">Project identifier or path.</param>
/// <param name="Dependencies">Dependency identifiers.</param>
internal sealed record ProjectReport(
    [property: JsonPropertyName("project")] string ProjectPath,
    [property: JsonPropertyName("dependencies")] IReadOnlyCollection<string> Dependencies);

/// <summary>
/// Summarizes a dependency across projects.
/// </summary>
/// <param name="Identifier">Dependency identifier.</param>
/// <param name="Type">Dependency category.</param>
internal sealed record DependencySummary(
    [property: JsonPropertyName("id")] string Identifier,
    [property: JsonPropertyName("type")] DependencyType Type)
{
    /// <summary>
    /// Gets or sets the dependency description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets the project identifiers that use the dependency.
    /// </summary>
    [JsonPropertyName("projects")]
    public HashSet<string> Projects { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the source paths where the dependency was found.
    /// </summary>
    [JsonPropertyName("sourcePaths")]
    public HashSet<string> SourcePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the resolved dependency paths.
    /// </summary>
    [JsonPropertyName("resolvedPaths")]
    public HashSet<string> ResolvedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets dependency metadata values.
    /// </summary>
    [JsonPropertyName("metadata")]
    public HashSet<string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Represents the top-level SBOM report payload.
/// </summary>
/// <param name="GeneratedAtUtc">Generation timestamp.</param>
/// <param name="Projects">Project entries.</param>
/// <param name="Dependencies">Dependency summaries.</param>
internal sealed record SbomReport(
    [property: JsonPropertyName("generatedAtUtc")] DateTime GeneratedAtUtc,
    [property: JsonPropertyName("projects")] IReadOnlyList<ProjectReport> Projects,
    [property: JsonPropertyName("dependencies")] IReadOnlyList<DependencySummary> Dependencies);

/// <summary>
/// Represents an SPDX document payload.
/// </summary>
/// <param name="SpdxVersion">SPDX specification version.</param>
/// <param name="DataLicense">Document data license.</param>
/// <param name="SpdxId">Document SPDX identifier.</param>
/// <param name="Name">Document name.</param>
/// <param name="DocumentNamespace">Document namespace URI.</param>
/// <param name="CreationInfo">Creation metadata.</param>
/// <param name="Packages">Packages in the document.</param>
/// <param name="Relationships">Relationships between packages.</param>
internal sealed record SpdxDocument(
    [property: JsonPropertyName("spdxVersion")] string SpdxVersion,
    [property: JsonPropertyName("dataLicense")] string DataLicense,
    [property: JsonPropertyName("SPDXID")] string SpdxId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("documentNamespace")] string DocumentNamespace,
    [property: JsonPropertyName("creationInfo")] SpdxCreationInfo CreationInfo,
    [property: JsonPropertyName("packages")] IReadOnlyList<SpdxPackage> Packages,
    [property: JsonPropertyName("relationships")] IReadOnlyList<SpdxRelationship> Relationships);

/// <summary>
/// Describes SPDX document creation metadata.
/// </summary>
/// <param name="Creators">Creators of the document.</param>
/// <param name="Created">Creation timestamp.</param>
internal sealed record SpdxCreationInfo(
    [property: JsonPropertyName("creators")] IReadOnlyList<string> Creators,
    [property: JsonPropertyName("created")] DateTime Created);

/// <summary>
/// Represents an SPDX package entry.
/// </summary>
/// <param name="Name">Package name.</param>
/// <param name="SpdxId">SPDX identifier.</param>
/// <param name="DownloadLocation">Download location value.</param>
/// <param name="FilesAnalyzed">Whether files were analyzed.</param>
/// <param name="LicenseConcluded">Concluded license expression.</param>
/// <param name="LicenseDeclared">Declared license expression.</param>
/// <param name="CopyrightText">Copyright text value.</param>
internal sealed record SpdxPackage(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("SPDXID")] string SpdxId,
    [property: JsonPropertyName("downloadLocation")] string DownloadLocation,
    [property: JsonPropertyName("filesAnalyzed")] bool FilesAnalyzed,
    [property: JsonPropertyName("licenseConcluded")] string LicenseConcluded,
    [property: JsonPropertyName("licenseDeclared")] string LicenseDeclared,
    [property: JsonPropertyName("copyrightText")] string CopyrightText)
{
    /// <summary>
    /// Gets or sets the package description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Represents an SPDX relationship entry.
/// </summary>
/// <param name="SpdxElementId">Source SPDX element ID.</param>
/// <param name="RelatedSpdxElement">Related SPDX element ID.</param>
/// <param name="RelationshipType">Relationship type.</param>
internal sealed record SpdxRelationship(
    [property: JsonPropertyName("spdxElementId")] string SpdxElementId,
    [property: JsonPropertyName("relatedSpdxElement")] string RelatedSpdxElement,
    [property: JsonPropertyName("relationshipType")] string RelationshipType);

/// <summary>
/// Represents a CycloneDX BOM payload.
/// </summary>
/// <param name="BomFormat">BOM format string.</param>
/// <param name="SpecVersion">CycloneDX specification version.</param>
/// <param name="Version">Document version.</param>
internal sealed record CycloneDxBom(
    [property: JsonPropertyName("bomFormat")] string BomFormat,
    [property: JsonPropertyName("specVersion")] string SpecVersion,
    [property: JsonPropertyName("version")] int Version)
{
    /// <summary>
    /// Gets or sets BOM metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public CycloneDxMetadata? Metadata { get; set; }

    /// <summary>
    /// Gets or sets component entries.
    /// </summary>
    [JsonPropertyName("components")]
    public IReadOnlyList<CycloneDxComponent>? Components { get; set; }

    /// <summary>
    /// Gets or sets dependency relationships.
    /// </summary>
    [JsonPropertyName("dependencies")]
    public IReadOnlyList<CycloneDxDependency>? Dependencies { get; set; }
}

/// <summary>
/// Represents CycloneDX metadata information.
/// </summary>
/// <param name="Timestamp">Document timestamp.</param>
internal sealed record CycloneDxMetadata(
    [property: JsonPropertyName("timestamp")] DateTime Timestamp)
{
    /// <summary>
    /// Gets or sets tool metadata.
    /// </summary>
    [JsonPropertyName("tools")]
    public CycloneDxTools? Tools { get; set; }
}

/// <summary>
/// Represents CycloneDX tool metadata.
/// </summary>
/// <param name="Components">Tool components.</param>
internal sealed record CycloneDxTools(
    [property: JsonPropertyName("components")] IReadOnlyList<CycloneDxComponent> Components);

/// <summary>
/// Represents a CycloneDX component entry.
/// </summary>
/// <param name="Type">Component type.</param>
/// <param name="Name">Component name.</param>
internal sealed record CycloneDxComponent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string Name)
{
    /// <summary>
    /// Gets or sets the component BOM reference.
    /// </summary>
    [JsonPropertyName("bom-ref")]
    public string? BomRef { get; set; }

    /// <summary>
    /// Gets or sets the component version.
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>
    /// Gets or sets the component description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets component properties.
    /// </summary>
    [JsonPropertyName("properties")]
    public IReadOnlyList<CycloneDxProperty>? Properties { get; set; }
}

/// <summary>
/// Represents a CycloneDX dependency relationship.
/// </summary>
/// <param name="Ref">Component reference.</param>
/// <param name="DependsOn">Dependent component references.</param>
internal sealed record CycloneDxDependency(
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("dependsOn")] IReadOnlyList<string> DependsOn);

/// <summary>
/// Represents a CycloneDX property entry.
/// </summary>
/// <param name="Name">Property name.</param>
/// <param name="Value">Property value.</param>
internal sealed record CycloneDxProperty(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string Value);
