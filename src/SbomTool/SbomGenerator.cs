using Serilog;

namespace CppSbom;

internal sealed class SbomGenerator
{
    private readonly CommandLineOptions _options;
    private readonly ILogger _logger;
    private readonly SolutionScanner _solutionScanner;
    private readonly ProjectAnalyzer _projectAnalyzer;
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
        _solutionScanner = new SolutionScanner(logger);
        _projectAnalyzer = new ProjectAnalyzer(options, logger, sourceScanner, comResolver);
        _writer = new SbomWriter(logger);
    }

    public void Run()
    {
        var solutions = _solutionScanner.FindSolutions(_options.RootDirectory).ToList();
        if (solutions.Count == 0)
        {
            _logger.Warning("No Visual Studio solutions were found under {Root}", _options.RootDirectory);
        }

        var analysisCache = new Dictionary<string, ProjectAnalysis>(StringComparer.OrdinalIgnoreCase);
        var projectDependencyMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var dependencySummaries = new Dictionary<(string Id, DependencyType Type), DependencySummary>();

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

                foreach (var dependency in analysis.Dependencies)
                {
                    dependencySet.Add($"{dependency.Type}:{dependency.Identifier}");
                    var summaryKey = (dependency.Identifier, dependency.Type);
                    if (!dependencySummaries.TryGetValue(summaryKey, out var summary))
                    {
                        summary = new DependencySummary(dependency.Identifier, dependency.Type.ToString())
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
        }

        var projectReports = projectDependencyMap
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new ProjectReport(kvp.Key, kvp.Value.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();

        var dependencies = dependencySummaries.Values
            .OrderBy(d => d.Identifier, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Type)
            .ToList();

        var report = new SbomReport(DateTime.UtcNow, projectReports, dependencies);
        _writer.Write(report, _options.OutputPath);
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
}
