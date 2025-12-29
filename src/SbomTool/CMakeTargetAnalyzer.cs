using Serilog;

namespace CppSbom;

/// <summary>
/// Analyzes CMake targets to resolve dependencies.
/// </summary>
internal sealed class CMakeTargetAnalyzer
{
    /// <summary>
    /// Parsed command line options.
    /// </summary>
    private readonly CommandLineOptions _options;
    /// <summary>
    /// Logger used for diagnostics.
    /// </summary>
    private readonly ILogger _logger;
    /// <summary>
    /// Source scanner for include/import/pragma analysis.
    /// </summary>
    private readonly SourceScanner _sourceScanner;
    /// <summary>
    /// COM metadata resolver.
    /// </summary>
    private readonly IComResolver _comResolver;
    /// <summary>
    /// CMake target graph for resolution.
    /// </summary>
    private readonly CMakeProjectGraph _graph;
    /// <summary>
    /// Dependency comparer for set equality.
    /// </summary>
    private readonly DependencyIdentityComparer _dependencyComparer = new();

    /// <summary>
    /// Initializes a new CMake target analyzer.
    /// </summary>
    /// <param name="options">Parsed command line options.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="sourceScanner">Source scanner for includes and imports.</param>
    /// <param name="comResolver">COM resolver for registry lookups.</param>
    /// <param name="graph">CMake project graph.</param>
    public CMakeTargetAnalyzer(
        CommandLineOptions options,
        ILogger logger,
        SourceScanner sourceScanner,
        IComResolver comResolver,
        CMakeProjectGraph graph)
    {
        _options = options;
        _logger = logger;
        _sourceScanner = sourceScanner;
        _comResolver = comResolver;
        _graph = graph;
    }

    /// <summary>
    /// Analyzes a CMake target and returns resolved dependencies.
    /// </summary>
    /// <param name="target">Target to analyze.</param>
    /// <returns>Dependencies for the target.</returns>
    public IReadOnlyCollection<Dependency> Analyze(CMakeTargetDefinition target)
    {
        var includeDirs = BuildIncludeDirectories(target);
        var scanResult = _sourceScanner.Scan(target.Sources);
        var dependencies = new HashSet<Dependency>(_dependencyComparer);
        var internalOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ProcessIncludes(scanResult, includeDirs, dependencies, target.DirectoryPath);
        ProcessImports(scanResult, dependencies, target.DirectoryPath);
        ProcessLinkedLibraries(target, dependencies, target.DirectoryPath, internalOutputs);
        ProcessPragmaLibs(scanResult, dependencies, target.DirectoryPath, BuildLibraryDirectories(target.DirectoryPath), internalOutputs);
        ProcessComReferences(scanResult, dependencies);

        return dependencies.ToList();
    }

    /// <summary>
    /// Returns the include directories used for a target analysis.
    /// </summary>
    /// <param name="target">Target to inspect.</param>
    /// <returns>Resolved include directories.</returns>
    internal IReadOnlyList<string> GetIncludeDirectoriesForTarget(CMakeTargetDefinition target)
    {
        return BuildIncludeDirectories(target);
    }

    /// <summary>
    /// Builds include search paths for a target.
    /// </summary>
    /// <param name="target">Target definition.</param>
    /// <returns>Include directory list.</returns>
    private List<string> BuildIncludeDirectories(CMakeTargetDefinition target)
    {
        var dirs = new List<string>
        {
            target.DirectoryPath
        };

        foreach (var third in _options.ThirdPartyDirectories)
        {
            dirs.Add(third);
            var include = Path.Combine(third, "include");
            if (Directory.Exists(include))
            {
                dirs.Add(include);
            }
        }

        foreach (var include in target.IncludeDirectories)
        {
            if (!Directory.Exists(include))
            {
                continue;
            }

            dirs.Add(include);
        }

        return DeduplicatePaths(dirs);
    }

    /// <summary>
    /// Builds library search paths for a target.
    /// </summary>
    /// <param name="targetDirectory">Target directory.</param>
    /// <returns>Library directory list.</returns>
    private List<string> BuildLibraryDirectories(string targetDirectory)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            targetDirectory
        };

        foreach (var third in _options.ThirdPartyDirectories)
        {
            dirs.Add(third);
            foreach (var suffix in new[] { "lib", "libs", "Lib", "Libs" })
            {
                var candidate = Path.Combine(third, suffix);
                if (Directory.Exists(candidate))
                {
                    dirs.Add(candidate);
                }
            }
        }

        return dirs.ToList();
    }

    /// <summary>
    /// Deduplicates paths after normalization.
    /// </summary>
    /// <param name="paths">Paths to deduplicate.</param>
    /// <returns>Deduplicated path list.</returns>
    private static List<string> DeduplicatePaths(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<string>();
        foreach (var path in paths)
        {
            var normalized = NormalizePath(path);
            if (seen.Add(normalized))
            {
                deduped.Add(path);
            }
        }

        return deduped;
    }

    /// <summary>
    /// Adds header include dependencies discovered in source files.
    /// </summary>
    /// <param name="scanResult">Source scan results.</param>
    /// <param name="includeDirs">Include search paths.</param>
    /// <param name="dependencies">Dependency set to populate.</param>
    /// <param name="projectDir">Target directory.</param>
    private void ProcessIncludes(SourceScanResult scanResult, List<string> includeDirs, HashSet<Dependency> dependencies, string projectDir)
    {
        foreach (var include in scanResult.Includes)
        {
            var resolved = ResolveInclude(include, includeDirs, projectDir);
            if (resolved is null)
            {
                continue;
            }

            if (IsInternal(resolved))
            {
                continue;
            }

            var identifier = BuildIdentifierFromPath(resolved);
            var dependency = new Dependency(identifier, DependencyType.HeaderInclude)
            {
                SourcePath = include.FilePath,
                ResolvedPath = resolved
            };
            dependencies.Add(dependency);
        }
    }

    /// <summary>
    /// Adds import directive dependencies discovered in source files.
    /// </summary>
    /// <param name="scanResult">Source scan results.</param>
    /// <param name="dependencies">Dependency set to populate.</param>
    /// <param name="projectDir">Target directory.</param>
    private void ProcessImports(SourceScanResult scanResult, HashSet<Dependency> dependencies, string projectDir)
    {
        foreach (var directive in scanResult.Imports)
        {
            var resolved = TryResolveRelative(directive.Value, Path.GetDirectoryName(directive.FilePath)!, projectDir);
            var identifier = resolved is not null ? BuildIdentifierFromPath(resolved) : directive.Value;
            if (resolved is not null && IsInternal(resolved))
            {
                continue;
            }

            var dependency = new Dependency(identifier, DependencyType.ImportDirective)
            {
                SourcePath = directive.FilePath,
                ResolvedPath = resolved
            };
            dependencies.Add(dependency);
        }
    }

    /// <summary>
    /// Adds dependencies from linked library entries.
    /// </summary>
    /// <param name="target">Target definition.</param>
    /// <param name="dependencies">Dependency set to populate.</param>
    /// <param name="projectDir">Target directory.</param>
    /// <param name="internalOutputs">Internal outputs set to populate.</param>
    private void ProcessLinkedLibraries(
        CMakeTargetDefinition target,
        HashSet<Dependency> dependencies,
        string projectDir,
        HashSet<string> internalOutputs)
    {
        var libraryDirs = BuildLibraryDirectories(projectDir);
        foreach (var entry in target.LinkLibraries)
        {
            if (CMakeScanner.LooksLikeFilePath(entry))
            {
                var resolved = TryResolveLibrary(entry, projectDir, _options.RootDirectory, libraryDirs);
                if (resolved is not null && IsInternal(resolved))
                {
                    internalOutputs.Add(Path.GetFileName(resolved));
                }

                var identifier = resolved is not null ? BuildIdentifierFromPath(resolved) : Path.GetFileNameWithoutExtension(entry);
                var dependency = new Dependency(identifier, DependencyType.StaticLibrary)
                {
                    SourcePath = projectDir,
                    ResolvedPath = resolved,
                    Description = resolved is null ? entry : null
                };
                dependencies.Add(dependency);
                continue;
            }

            if (_graph.TryResolveTargetIdentifier(entry, target.DirectoryPath, out var targetId))
            {
                dependencies.Add(new Dependency(targetId, DependencyType.StaticLibrary)
                {
                    SourcePath = projectDir
                });
                continue;
            }

            dependencies.Add(new Dependency(entry, DependencyType.StaticLibrary)
            {
                SourcePath = projectDir
            });
        }
    }

    /// <summary>
    /// Adds dependencies from pragma comment lib directives.
    /// </summary>
    /// <param name="scanResult">Source scan results.</param>
    /// <param name="dependencies">Dependency set to populate.</param>
    /// <param name="projectDir">Target directory.</param>
    /// <param name="libraryDirs">Library search paths.</param>
    /// <param name="internalOutputs">Internal outputs set to populate.</param>
    private void ProcessPragmaLibs(
        SourceScanResult scanResult,
        HashSet<Dependency> dependencies,
        string projectDir,
        List<string> libraryDirs,
        HashSet<string> internalOutputs)
    {
        foreach (var directive in scanResult.PragmaLibs)
        {
            var value = directive.Value;
            if (!value.EndsWith(".lib", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var searchRoots = new List<string>(libraryDirs)
            {
                Path.GetDirectoryName(directive.FilePath)!,
                projectDir,
                _options.RootDirectory
            };
            var resolved = TryResolveLibrary(value, projectDir, _options.RootDirectory, searchRoots);
            if (resolved is not null && IsInternal(resolved))
            {
                internalOutputs.Add(Path.GetFileName(resolved));
                continue;
            }

            var identifier = resolved is not null ? BuildIdentifierFromPath(resolved) : Path.GetFileNameWithoutExtension(value);
            var dependency = new Dependency(identifier, DependencyType.StaticLibrary)
            {
                SourcePath = directive.FilePath,
                ResolvedPath = resolved
            };
            dependencies.Add(dependency);
        }
    }

    /// <summary>
    /// Adds COM dependencies discovered in source files.
    /// </summary>
    /// <param name="scanResult">Source scan results.</param>
    /// <param name="dependencies">Dependency set to populate.</param>
    private void ProcessComReferences(SourceScanResult scanResult, HashSet<Dependency> dependencies)
    {
        foreach (var progId in scanResult.ProgIds)
        {
            var metadata = _comResolver.ResolveFromProgId(progId.Value);
            if (metadata is null)
            {
                continue;
            }

            AddComDependency(dependencies, metadata, progId.FilePath);
        }

        foreach (var clsid in scanResult.Clsids)
        {
            var metadata = _comResolver.ResolveFromClsid(clsid.Value);
            if (metadata is null)
            {
                continue;
            }

            AddComDependency(dependencies, metadata, clsid.FilePath);
        }
    }

    /// <summary>
    /// Adds a COM dependency to the dependency set.
    /// </summary>
    /// <param name="dependencies">Dependency set to populate.</param>
    /// <param name="metadata">Resolved COM metadata.</param>
    /// <param name="sourcePath">Source file path.</param>
    private void AddComDependency(HashSet<Dependency> dependencies, ComMetadata metadata, string sourcePath)
    {
        var identifier = metadata.ProgId ?? metadata.Clsid ?? "UnknownCOM";
        var description = metadata.Description ?? metadata.InprocServer;
        var resolved = metadata.InprocServer;
        var metadataFlags = new List<string>();
        if (!string.IsNullOrWhiteSpace(metadata.RegistryView))
        {
            metadataFlags.Add($"RegistryView={metadata.RegistryView}");
        }
        if (!string.IsNullOrWhiteSpace(metadata.ThreadingModel))
        {
            metadataFlags.Add($"ThreadingModel={metadata.ThreadingModel}");
        }

        dependencies.Add(new Dependency(identifier, DependencyType.Com)
        {
            SourcePath = sourcePath,
            ResolvedPath = resolved,
            Description = description,
            Metadata = metadataFlags.Count > 0 ? string.Join(";", metadataFlags) : null
        });
    }

    /// <summary>
    /// Resolves include paths using include search paths.
    /// </summary>
    /// <param name="include">Include match to resolve.</param>
    /// <param name="includeDirs">Include search paths.</param>
    /// <param name="projectDir">Target directory.</param>
    /// <returns>Resolved include path or null.</returns>
    private string? ResolveInclude(IncludeMatch include, List<string> includeDirs, string projectDir)
    {
        var sourceDir = Path.GetDirectoryName(include.FilePath)!;
        if (!include.IsSystem)
        {
            var local = Path.GetFullPath(Path.Combine(sourceDir, include.Value));
            if (File.Exists(local))
            {
                return local;
            }
        }

        foreach (var dir in includeDirs)
        {
            var candidate = Path.GetFullPath(Path.Combine(dir, include.Value));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var projectCandidate = Path.GetFullPath(Path.Combine(projectDir, include.Value));
        return File.Exists(projectCandidate) ? projectCandidate : null;
    }

    /// <summary>
    /// Resolves a relative path from a base directory or project directory.
    /// </summary>
    /// <param name="value">Relative value to resolve.</param>
    /// <param name="baseDir">Base directory to check first.</param>
    /// <param name="projectDir">Fallback project directory.</param>
    /// <returns>Resolved file path or null.</returns>
    private string? TryResolveRelative(string value, string baseDir, string projectDir)
    {
        var full = Path.GetFullPath(Path.Combine(baseDir, value));
        if (File.Exists(full))
        {
            return full;
        }

        full = Path.GetFullPath(Path.Combine(projectDir, value));
        return File.Exists(full) ? full : null;
    }

    /// <summary>
    /// Resolves library references from known search paths.
    /// </summary>
    /// <param name="value">Library value to resolve.</param>
    /// <param name="projectDir">Target directory.</param>
    /// <param name="solutionDir">Root directory.</param>
    /// <param name="additionalRoots">Additional search roots.</param>
    /// <returns>Resolved library path or null.</returns>
    private string? TryResolveLibrary(string value, string projectDir, string solutionDir, IEnumerable<string> additionalRoots)
    {
        if (Path.IsPathRooted(value))
        {
            return File.Exists(value) ? value : null;
        }

        var candidates = new List<string>
        {
            Path.Combine(projectDir, value),
            Path.Combine(solutionDir, value)
        };

        var fileName = Path.GetFileName(value);

        foreach (var root in additionalRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var normalized = Path.GetFullPath(root);
            candidates.Add(Path.Combine(normalized, value));
            if (!string.Equals(fileName, value, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(Path.Combine(normalized, fileName));
            }
        }

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full))
            {
                return full;
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether a path is internal to the root directory.
    /// </summary>
    /// <param name="path">Path to evaluate.</param>
    /// <returns>True when the path is internal.</returns>
    private bool IsInternal(string path)
    {
        var normalized = Path.GetFullPath(path);
        if (normalized.StartsWith(_options.RootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var third in _options.ThirdPartyDirectories)
            {
                if (normalized.StartsWith(third, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Builds a dependency identifier from a file path.
    /// </summary>
    /// <param name="path">Resolved file path.</param>
    /// <returns>Dependency identifier.</returns>
    private string BuildIdentifierFromPath(string path)
    {
        foreach (var third in _options.ThirdPartyDirectories)
        {
            if (path.StartsWith(third, StringComparison.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(third, path);
                var packageRoot = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .FirstOrDefault(part => !string.IsNullOrWhiteSpace(part));
                return packageRoot is null
                    ? Path.GetFileName(path)
                    : $"{Path.GetFileName(third)}::{packageRoot}";
            }
        }

        if (path.StartsWith(_options.RootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(_options.RootDirectory, path);
        }

        return Path.GetFileName(path);
    }

    /// <summary>
    /// Normalizes a path for deduplication.
    /// </summary>
    /// <param name="path">Path to normalize.</param>
    /// <returns>Normalized path.</returns>
    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).Replace('\\', '/').ToLowerInvariant();
}
