using System.Text.RegularExpressions;
using System.Xml.Linq;
using Serilog;

namespace CppSbom;

internal sealed class ProjectAnalyzer
{
    private readonly CommandLineOptions _options;
    private readonly ILogger _logger;
    private readonly SourceScanner _sourceScanner;
    private readonly IComResolver _comResolver;
    private readonly DependencyIdentityComparer _dependencyComparer = new();

    public ProjectAnalyzer(CommandLineOptions options, ILogger logger, SourceScanner sourceScanner, IComResolver comResolver)
    {
        _options = options;
        _logger = logger;
        _sourceScanner = sourceScanner;
        _comResolver = comResolver;
    }

    public ProjectAnalysis Analyze(string projectPath, string? solutionDir)
    {
        _logger.Information("Analyzing project {Project}", projectPath);
        var doc = XDocument.Load(projectPath);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var projectDir = Path.GetDirectoryName(projectPath)!;
        solutionDir ??= projectDir;

        var includeDirs = GatherIncludeDirectories(doc, ns, projectDir, solutionDir).ToList();
        var libraryDirs = GatherLibraryDirectories(doc, ns, projectDir, solutionDir).ToList();
        var sourceFiles = GatherSourceFiles(doc, ns, projectDir).ToList();
        var scanResult = _sourceScanner.Scan(sourceFiles.Concat(GatherHeaderFiles(doc, ns, projectDir)));

        var dependencies = new HashSet<Dependency>(_dependencyComparer);
        var internalOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ProcessIncludes(scanResult, includeDirs, dependencies, projectDir);
        ProcessImports(scanResult, dependencies, projectDir);
        ProcessStaticLibraries(doc, ns, dependencies, projectDir, solutionDir, libraryDirs, internalOutputs);
        ProcessPragmaLibs(scanResult, dependencies, projectDir, solutionDir, libraryDirs, internalOutputs);
        ProcessComReferences(scanResult, dependencies);

        return new ProjectAnalysis(projectPath, dependencies.ToList(), internalOutputs.ToList());
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

    private void ProcessStaticLibraries(XDocument doc, XNamespace ns, HashSet<Dependency> dependencies, string projectDir, string solutionDir, List<string> libraryDirs, HashSet<string> internalOutputs)
    {
        var libs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in doc.Descendants(ns + "AdditionalDependencies"))
        {
            foreach (var entry in SplitSemicolonList(element.Value))
            {
                if (entry.Contains("%("))
                {
                    continue;
                }
                if (entry.EndsWith(".lib", StringComparison.OrdinalIgnoreCase))
                {
                    libs.Add(entry.Trim());
                }
            }
        }

        foreach (var entry in libs)
        {
            var resolved = TryResolveLibrary(entry, projectDir, solutionDir, libraryDirs);
            if (resolved is not null && IsInternal(resolved))
            {
                internalOutputs.Add(Path.GetFileName(resolved));
                continue;
            }

            var identifier = resolved is not null ? BuildIdentifierFromPath(resolved) : Path.GetFileNameWithoutExtension(entry);
            var dependency = new Dependency(identifier, DependencyType.StaticLibrary)
            {
                SourcePath = projectDir,
                ResolvedPath = resolved,
                Description = resolved is null ? entry : null
            };
            dependencies.Add(dependency);
        }
    }

    private void ProcessPragmaLibs(SourceScanResult scanResult, HashSet<Dependency> dependencies, string projectDir, string solutionDir, List<string> libraryDirs, HashSet<string> internalOutputs)
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
                solutionDir
            };
            var resolved = TryResolveLibrary(value, projectDir, solutionDir, searchRoots);
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

        // try relative to project root
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
        var expanded = ExpandMacros(value, projectDir, solutionDir);
        if (Path.IsPathRooted(expanded))
        {
            return File.Exists(expanded) ? expanded : null;
        }

        var candidates = new List<string>
        {
            Path.Combine(projectDir, expanded),
            Path.Combine(solutionDir, expanded)
        };

        var fileName = Path.GetFileName(expanded);

        foreach (var root in additionalRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var normalized = Path.GetFullPath(root);
            candidates.Add(Path.Combine(normalized, expanded));
            if (!string.Equals(fileName, expanded, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(Path.Combine(normalized, fileName));
            }
        }

        foreach (var dir in _options.ThirdPartyDirectories)
        {
            candidates.Add(Path.Combine(dir, expanded));
            if (!string.Equals(fileName, expanded, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(Path.Combine(dir, fileName));
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

    private IEnumerable<string> GatherIncludeDirectories(XDocument doc, XNamespace ns, string projectDir, string solutionDir)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            projectDir
        };

        foreach (var element in doc.Descendants(ns + "AdditionalIncludeDirectories"))
        {
            foreach (var entry in SplitSemicolonList(element.Value))
            {
                if (entry.Contains("%("))
                {
                    continue;
                }

                var expanded = ExpandMacros(entry, projectDir, solutionDir);
                if (!string.IsNullOrWhiteSpace(expanded) && Directory.Exists(expanded))
                {
                    dirs.Add(expanded);
                }
            }
        }

        foreach (var third in _options.ThirdPartyDirectories)
        {
            dirs.Add(third);
            var include = Path.Combine(third, "include");
            if (Directory.Exists(include))
            {
                dirs.Add(include);
            }
        }

        return dirs;
    }

    private IEnumerable<string> GatherLibraryDirectories(XDocument doc, XNamespace ns, string projectDir, string solutionDir)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            projectDir
        };

        foreach (var element in doc.Descendants(ns + "AdditionalLibraryDirectories"))
        {
            foreach (var entry in SplitSemicolonList(element.Value))
            {
                if (entry.Contains("%("))
                {
                    continue;
                }

                var expanded = ExpandMacros(entry, projectDir, solutionDir);
                if (!string.IsNullOrWhiteSpace(expanded) && Directory.Exists(expanded))
                {
                    dirs.Add(expanded);
                }
            }
        }

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

        return dirs;
    }

    private IEnumerable<string> GatherSourceFiles(XDocument doc, XNamespace ns, string projectDir)
    {
        foreach (var item in doc.Descendants(ns + "ClCompile"))
        {
            var include = item.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(projectDir, include));
            yield return path;
        }
    }

    private IEnumerable<string> GatherHeaderFiles(XDocument doc, XNamespace ns, string projectDir)
    {
        foreach (var item in doc.Descendants(ns + "ClInclude"))
        {
            var include = item.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            yield return Path.GetFullPath(Path.Combine(projectDir, include));
        }
    }

    private static IEnumerable<string> SplitSemicolonList(string value) =>
        value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ExpandMacros(string value, string projectDir, string solutionDir)
    {
        var expanded = value.Replace("$(ProjectDir)", EnsureTrailingSeparator(projectDir), StringComparison.OrdinalIgnoreCase)
            .Replace("$(SolutionDir)", EnsureTrailingSeparator(solutionDir), StringComparison.OrdinalIgnoreCase)
            .Replace("$(Configuration)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("$(Platform)", string.Empty, StringComparison.OrdinalIgnoreCase);

        return Environment.ExpandEnvironmentVariables(expanded);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (!path.EndsWith(Path.DirectorySeparatorChar) && !path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path + Path.DirectorySeparatorChar;
        }

        return path;
    }
}

internal sealed class DependencyIdentityComparer : IEqualityComparer<Dependency>
{
    public bool Equals(Dependency? x, Dependency? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }
        if (x is null || y is null)
        {
            return false;
        }

        return string.Equals(x.Identifier, y.Identifier, StringComparison.OrdinalIgnoreCase) && x.Type == y.Type;
    }

    public int GetHashCode(Dependency obj)
    {
        return HashCode.Combine(obj.Type, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Identifier));
    }
}
