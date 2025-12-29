using Serilog;

namespace CppSbom;

internal sealed class SbomGenerator
{
    private readonly CommandLineOptions _options;
    private readonly ILogger _logger;
    private readonly SolutionScanner _solutionScanner;
    private readonly ProjectAnalyzer _projectAnalyzer;
    private readonly IComResolver _comResolver;
    private readonly SbomWriter _writer;

    public SbomGenerator(CommandLineOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
        var sourceScanner = new SourceScanner();
        IComResolver comResolver;
        if (OperatingSystem.IsWindows())
        {
            comResolver = new ComRegistryResolver(logger);
        }
        else
        {
            _logger.Warning("COM registry interrogation skipped: unsupported on current OS");
            comResolver = new NullComResolver(logger);
        }
        _comResolver = comResolver;
        _solutionScanner = new SolutionScanner(logger);
        _projectAnalyzer = new ProjectAnalyzer(options, logger, sourceScanner, comResolver);
        _writer = new SbomWriter(logger);
    }

    public void Run()
    {
        var projectDependencyMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var dependencySummaries = new Dictionary<(string Id, DependencyType Type), DependencySummary>();

        switch (_options.Type)
        {
            case ScanType.CMake:
                RunCMakeScan(projectDependencyMap, dependencySummaries);
                break;
            case ScanType.VisualStudio:
            default:
                RunVisualStudioScan(projectDependencyMap, dependencySummaries);
                break;
        }

        var dependencyList = dependencySummaries.Values
            .OrderBy(d => d.Identifier, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Type)
            .ToList();

        var generatedAt = DateTime.UtcNow;
        switch (_options.Format)
        {
            case OutputFormat.CycloneDx:
                var cyclonedx = BuildCycloneDxBom(projectDependencyMap, dependencyList, generatedAt);
                _writer.Write(cyclonedx, _options.OutputPath);
                break;
            case OutputFormat.Spdx:
            default:
                var spdx = BuildSpdxDocument(projectDependencyMap, dependencyList, generatedAt);
                _writer.Write(spdx, _options.OutputPath);
                break;
        }
    }

    private void RunVisualStudioScan(
        Dictionary<string, HashSet<string>> projectDependencyMap,
        Dictionary<(string Id, DependencyType Type), DependencySummary> dependencySummaries)
    {
        var solutions = _solutionScanner.FindSolutions(_options.RootDirectory).ToList();
        if (solutions.Count == 0)
        {
            _logger.Warning("No Visual Studio solutions were found under {Root}", _options.RootDirectory);
        }

        var analysisCache = new Dictionary<string, ProjectAnalysis>(StringComparer.OrdinalIgnoreCase);

        foreach (var solutionPath in solutions)
        {
            var solutionDir = Path.GetDirectoryName(solutionPath)!;
            _logger.Information("Processing solution {Solution}", solutionPath);
            foreach (var projectPath in _solutionScanner.FindProjects(solutionPath))
            {
                if (!analysisCache.TryGetValue(projectPath, out var analysis))
                {
                    analysis = _projectAnalyzer.Analyze(projectPath, solutionDir);
                    analysisCache[projectPath] = analysis;
                }

                var projectKey = Relativize(projectPath);
                if (!projectDependencyMap.TryGetValue(projectKey, out var dependencySet))
                {
                    dependencySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    projectDependencyMap[projectKey] = dependencySet;
                }

                AddDependencies(projectKey, analysis.Dependencies, dependencySet, dependencySummaries);
            }
        }
    }

    private void RunCMakeScan(
        Dictionary<string, HashSet<string>> projectDependencyMap,
        Dictionary<(string Id, DependencyType Type), DependencySummary> dependencySummaries)
    {
        _logger.Information("Scanning CMakeLists.txt under {Root}", _options.RootDirectory);
        var cmakeScanner = new CMakeScanner(_logger);
        var graph = cmakeScanner.Scan(_options.RootDirectory);
        var analyzer = new CMakeTargetAnalyzer(_options, _logger, new SourceScanner(), _comResolver, graph);

        foreach (var target in graph.TargetsById.Values.OrderBy(t => t.Identifier, StringComparer.OrdinalIgnoreCase))
        {
            var dependencies = analyzer.Analyze(target);
            var projectKey = target.Identifier;
            if (!projectDependencyMap.TryGetValue(projectKey, out var dependencySet))
            {
                dependencySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                projectDependencyMap[projectKey] = dependencySet;
            }

            AddDependencies(projectKey, dependencies, dependencySet, dependencySummaries);
        }
    }

    private void AddDependencies(
        string projectKey,
        IEnumerable<Dependency> dependencies,
        HashSet<string> dependencySet,
        Dictionary<(string Id, DependencyType Type), DependencySummary> dependencySummaries)
    {
        foreach (var dependency in dependencies)
        {
            dependencySet.Add($"{dependency.Type}:{dependency.Identifier}");
            var summaryKey = (dependency.Identifier, dependency.Type);
            if (!dependencySummaries.TryGetValue(summaryKey, out var summary))
            {
                summary = new DependencySummary(dependency.Identifier, dependency.Type)
                {
                    Description = dependency.Description
                };
                dependencySummaries.Add(summaryKey, summary);
            }

            if (summary.Description is null && dependency.Description is not null)
            {
                summary.Description = dependency.Description;
            }

            summary.Projects.Add(projectKey);
            if (!string.IsNullOrWhiteSpace(dependency.SourcePath))
            {
                summary.SourcePaths.Add(Relativize(dependency.SourcePath));
            }

            if (!string.IsNullOrWhiteSpace(dependency.ResolvedPath))
            {
                summary.ResolvedPaths.Add(RelativizeOrAbsolute(dependency.ResolvedPath));
            }

            if (!string.IsNullOrWhiteSpace(dependency.Metadata))
            {
                summary.Metadata.Add(dependency.Metadata);
            }
        }
    }

    private string Relativize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(_options.RootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(_options.RootDirectory, fullPath);
        }

        return fullPath;
    }

    private string RelativizeOrAbsolute(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Relativize(path);
    }

    private SpdxDocument BuildSpdxDocument(
        Dictionary<string, HashSet<string>> projectDependencyMap,
        List<DependencySummary> dependencies,
        DateTime generatedAt)
    {
        var spdxIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packages = new List<SpdxPackage>();
        var relationships = new List<SpdxRelationship>();

        foreach (var project in projectDependencyMap.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var spdxId = EnsureSpdxId($"Project-{project}", spdxIdMap, usedIds);
            packages.Add(new SpdxPackage(project, spdxId, "NOASSERTION", false, "NOASSERTION", "NOASSERTION", "NOASSERTION"));
            relationships.Add(new SpdxRelationship("SPDXRef-DOCUMENT", spdxId, "DESCRIBES"));
        }

        foreach (var dependency in dependencies)
        {
            var key = $"{dependency.Type}:{dependency.Identifier}";
            var spdxId = EnsureSpdxId($"Dependency-{key}", spdxIdMap, usedIds);
            var package = new SpdxPackage(dependency.Identifier, spdxId, "NOASSERTION", false, "NOASSERTION", "NOASSERTION", "NOASSERTION")
            {
                Description = dependency.Description
            };
            packages.Add(package);
        }

        foreach (var (project, depKeys) in projectDependencyMap)
        {
            var projectId = spdxIdMap[$"Project-{project}"];
            foreach (var depKey in depKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                var dependencyId = spdxIdMap[$"Dependency-{depKey}"];
                relationships.Add(new SpdxRelationship(projectId, dependencyId, "DEPENDS_ON"));
            }
        }

        var namespaceId = Guid.NewGuid().ToString("D");
        var creationInfo = new SpdxCreationInfo(new[] { "Tool: cppsbom" }, generatedAt);
        return new SpdxDocument(
            "SPDX-2.3",
            "CC0-1.0",
            "SPDXRef-DOCUMENT",
            "cppsbom",
            $"https://spdx.org/spdxdocs/cppsbom-{namespaceId}",
            creationInfo,
            packages,
            relationships);
    }

    private CycloneDxBom BuildCycloneDxBom(
        Dictionary<string, HashSet<string>> projectDependencyMap,
        List<DependencySummary> dependencies,
        DateTime generatedAt)
    {
        var refMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var components = new List<CycloneDxComponent>();
        foreach (var project in projectDependencyMap.Keys)
        {
            var reference = EnsureCycloneDxRef($"project:{project}", refMap, usedRefs, "project", project);
            var component = new CycloneDxComponent("application", project)
            {
                BomRef = reference
            };
            components.Add(component);
        }

        foreach (var dependency in dependencies)
        {
            var reference = EnsureCycloneDxRef($"dependency:{dependency.Type}:{dependency.Identifier}", refMap, usedRefs, dependency.Type.ToString(), dependency.Identifier);
            var component = new CycloneDxComponent(MapCycloneDxType(dependency.Type), dependency.Identifier)
            {
                BomRef = reference,
                Description = dependency.Description,
                Properties = BuildCycloneDxProperties(dependency)
            };
            components.Add(component);
        }

        var dependencyGraph = new List<CycloneDxDependency>();
        foreach (var (project, depKeys) in projectDependencyMap)
        {
            var projectRef = refMap[$"project:{project}"];
            var dependsOn = depKeys
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .Select(k => refMap.TryGetValue($"dependency:{k}", out var depRef) ? depRef : null)
                .Where(depRef => !string.IsNullOrWhiteSpace(depRef))
                .Select(depRef => depRef!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            dependencyGraph.Add(new CycloneDxDependency(projectRef, dependsOn));
        }

        var metadata = new CycloneDxMetadata(generatedAt)
        {
            Tools = new CycloneDxTools(new[]
            {
                new CycloneDxComponent("application", "cppsbom")
            })
        };

        return new CycloneDxBom("CycloneDX", "1.5", 1)
        {
            Metadata = metadata,
            Components = components
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Dependencies = dependencyGraph
        };
    }

    private static string EnsureSpdxId(string key, Dictionary<string, string> existing, HashSet<string> used)
    {
        if (existing.TryGetValue(key, out var current))
        {
            return current;
        }

        var normalized = new string(key.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "Item";
        }

        var baseId = $"SPDXRef-{normalized}";
        var candidate = baseId;
        var index = 1;
        while (!used.Add(candidate))
        {
            candidate = $"{baseId}-{index}";
            index++;
        }

        existing[key] = candidate;
        return candidate;
    }

    private static string MapCycloneDxType(DependencyType type) =>
        type switch
        {
            DependencyType.HeaderInclude => "file",
            DependencyType.ImportDirective => "file",
            DependencyType.StaticLibrary => "library",
            DependencyType.Com => "library",
            _ => "library"
        };

    private static string BuildCycloneDxRef(string prefix, string value)
    {
        var normalized = new string(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "item";
        }
        return $"{prefix}-{normalized}";
    }

    private static string EnsureCycloneDxRef(
        string key,
        Dictionary<string, string> existing,
        HashSet<string> used,
        string prefix,
        string value)
    {
        if (existing.TryGetValue(key, out var current))
        {
            return current;
        }

        var baseRef = BuildCycloneDxRef(prefix, value);
        var candidate = baseRef;
        var index = 1;
        while (!used.Add(candidate))
        {
            candidate = $"{baseRef}-{index}";
            index++;
        }

        existing[key] = candidate;
        return candidate;
    }

    private static IReadOnlyList<CycloneDxProperty>? BuildCycloneDxProperties(DependencySummary dependency)
    {
        var properties = new List<CycloneDxProperty>
        {
            new("cppsbom:dependencyType", dependency.Type.ToString())
        };

        if (dependency.SourcePaths.Count > 0)
        {
            properties.Add(new CycloneDxProperty("cppsbom:sourcePaths", string.Join(";", dependency.SourcePaths)));
        }

        if (dependency.ResolvedPaths.Count > 0)
        {
            properties.Add(new CycloneDxProperty("cppsbom:resolvedPaths", string.Join(";", dependency.ResolvedPaths)));
        }

        if (dependency.Metadata.Count > 0)
        {
            properties.Add(new CycloneDxProperty("cppsbom:metadata", string.Join(";", dependency.Metadata)));
        }

        return properties.Count == 0 ? null : properties;
    }
}
