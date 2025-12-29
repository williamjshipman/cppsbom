using Serilog;

namespace CppSbom;

internal sealed class CMakeTargetAnalyzer
{
    private readonly CommandLineOptions _options;
    private readonly ILogger _logger;
    private readonly SourceScanner _sourceScanner;
    private readonly IComResolver _comResolver;
    private readonly CMakeProjectGraph _graph;
    private readonly DependencyIdentityComparer _dependencyComparer = new();

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

    internal IReadOnlyList<string> GetIncludeDirectoriesForTarget(CMakeTargetDefinition target)
    {
        return BuildIncludeDirectories(target);
    }

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

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).Replace('\\', '/').ToLowerInvariant();
}
