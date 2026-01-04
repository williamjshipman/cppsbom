using System.Xml.Linq;
using Serilog;

namespace CppSbom;

/// <summary>
/// Analyzes Visual Studio project files to resolve dependencies.
/// </summary>
internal sealed class ProjectAnalyzer
{
    /// <summary>
    /// Parsed command line options for the run.
    /// </summary>
    private readonly CommandLineOptions _options;
    /// <summary>
    /// Logger used for analysis diagnostics.
    /// </summary>
    private readonly ILogger _logger;
    /// <summary>
    /// Scanner used for source-level dependency discovery.
    /// </summary>
    private readonly SourceScanner _sourceScanner;
    /// <summary>
    /// COM metadata resolver for ProgIDs and CLSIDs.
    /// </summary>
    private readonly IComResolver _comResolver;
    /// <summary>
    /// Dependency comparer used for set equality.
    /// </summary>
    private readonly DependencyIdentityComparer _dependencyComparer = new();

    /// <summary>
    /// Initializes a new project analyzer.
    /// </summary>
    /// <param name="options">Parsed command line options.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="sourceScanner">Source scanner for headers and imports.</param>
    /// <param name="comResolver">COM resolver for registry references.</param>
    public ProjectAnalyzer(CommandLineOptions options, ILogger logger, SourceScanner sourceScanner, IComResolver comResolver)
    {
        _options = options;
        _logger = logger;
        _sourceScanner = sourceScanner;
        _comResolver = comResolver;
    }

    /// <summary>
    /// Analyzes a Visual Studio project and returns dependency information.
    /// </summary>
    /// <param name="projectPath">Path to the project file.</param>
    /// <param name="solutionDir">Solution directory for relative resolution.</param>
    /// <returns>Project analysis results.</returns>
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

    /// <summary>
    /// Adds header include dependencies discovered in source files.
    /// </summary>
    /// <param name="scanResult">Source scan results.</param>
    /// <param name="includeDirs">Include search paths.</param>
    /// <param name="dependencies">Dependency set to populate.</param>
    /// <param name="projectDir">Project directory.</param>
    private void ProcessIncludes(SourceScanResult scanResult, List<string> includeDirs, HashSet<Dependency> dependencies, string projectDir)
    {
        foreach (var include in scanResult.Includes)
        {
            var resolved = ResolveInclude(include, includeDirs, projectDir);
            if (resolved is null)
            {
                continue;
            }

            if (IsInternal(resolved) && !_options.IncludeInternal)
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
    /// <param name="projectDir">Project directory.</param>
    private void ProcessImports(SourceScanResult scanResult, HashSet<Dependency> dependencies, string projectDir)
    {
        foreach (var directive in scanResult.Imports)
        {
            var resolved = TryResolveRelative(directive.Value, Path.GetDirectoryName(directive.FilePath)!, projectDir);
            var identifier = resolved is not null ? BuildIdentifierFromPath(resolved) : directive.Value;
            if (resolved is not null && IsInternal(resolved) && !_options.IncludeInternal)
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
    /// Adds static library dependencies declared in project metadata.
    /// </summary>
    /// <param name="doc">Project XML document.</param>
    /// <param name="ns">XML namespace.</param>
    /// <param name="dependencies">Dependency set to populate.</param>
    /// <param name="projectDir">Project directory.</param>
    /// <param name="solutionDir">Solution directory.</param>
    /// <param name="libraryDirs">Library search paths.</param>
    /// <param name="internalOutputs">Internal outputs set to populate.</param>
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
                if (!_options.IncludeInternal)
                {
                    continue;
                }
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

    /// <summary>
    /// Adds static library dependencies from pragma comment directives.
    /// </summary>
    /// <param name="scanResult">Source scan results.</param>
    /// <param name="dependencies">Dependency set to populate.</param>
    /// <param name="projectDir">Project directory.</param>
    /// <param name="solutionDir">Solution directory.</param>
    /// <param name="libraryDirs">Library search paths.</param>
    /// <param name="internalOutputs">Internal outputs set to populate.</param>
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
                if (!_options.IncludeInternal)
                {
                    continue;
                }
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
    /// Resolves include paths using the include search paths.
    /// </summary>
    /// <param name="include">Include match to resolve.</param>
    /// <param name="includeDirs">Include search paths.</param>
    /// <param name="projectDir">Project directory.</param>
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

        // try relative to project root
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
    /// <param name="projectDir">Project directory.</param>
    /// <param name="solutionDir">Solution directory.</param>
    /// <param name="additionalRoots">Additional search roots.</param>
    /// <returns>Resolved library path or null.</returns>
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
    /// Collects include directories from project metadata.
    /// </summary>
    /// <param name="doc">Project XML document.</param>
    /// <param name="ns">XML namespace.</param>
    /// <param name="projectDir">Project directory.</param>
    /// <param name="solutionDir">Solution directory.</param>
    /// <returns>Include directory paths.</returns>
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

    /// <summary>
    /// Collects library directories from project metadata.
    /// </summary>
    /// <param name="doc">Project XML document.</param>
    /// <param name="ns">XML namespace.</param>
    /// <param name="projectDir">Project directory.</param>
    /// <param name="solutionDir">Solution directory.</param>
    /// <returns>Library directory paths.</returns>
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

    /// <summary>
    /// Collects source files listed in the project.
    /// </summary>
    /// <param name="doc">Project XML document.</param>
    /// <param name="ns">XML namespace.</param>
    /// <param name="projectDir">Project directory.</param>
    /// <returns>Source file paths.</returns>
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

    /// <summary>
    /// Collects header files listed in the project.
    /// </summary>
    /// <param name="doc">Project XML document.</param>
    /// <param name="ns">XML namespace.</param>
    /// <param name="projectDir">Project directory.</param>
    /// <returns>Header file paths.</returns>
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

    /// <summary>
    /// Splits a semicolon-delimited list value.
    /// </summary>
    /// <param name="value">Raw value string.</param>
    /// <returns>Individual entries.</returns>
    private static IEnumerable<string> SplitSemicolonList(string value) =>
        value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Expands common MSBuild macros in a path string.
    /// </summary>
    /// <param name="value">Raw value containing macros.</param>
    /// <param name="projectDir">Project directory.</param>
    /// <param name="solutionDir">Solution directory.</param>
    /// <returns>Expanded value.</returns>
    private static string ExpandMacros(string value, string projectDir, string solutionDir)
    {
        var expanded = value.Replace("$(ProjectDir)", EnsureTrailingSeparator(projectDir), StringComparison.OrdinalIgnoreCase)
            .Replace("$(SolutionDir)", EnsureTrailingSeparator(solutionDir), StringComparison.OrdinalIgnoreCase)
            .Replace("$(Configuration)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("$(Platform)", string.Empty, StringComparison.OrdinalIgnoreCase);

        return Environment.ExpandEnvironmentVariables(expanded);
    }

    /// <summary>
    /// Ensures a directory path ends with a separator.
    /// </summary>
    /// <param name="path">Path to normalize.</param>
    /// <returns>Path with trailing separator.</returns>
    private static string EnsureTrailingSeparator(string path)
    {
        if (!path.EndsWith(Path.DirectorySeparatorChar) && !path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path + Path.DirectorySeparatorChar;
        }

        return path;
    }
}

/// <summary>
/// Compares dependencies by identifier and type.
/// </summary>
internal sealed class DependencyIdentityComparer : IEqualityComparer<Dependency>
{
    /// <summary>
    /// Determines equality between dependencies.
    /// </summary>
    /// <param name="x">First dependency.</param>
    /// <param name="y">Second dependency.</param>
    /// <returns>True when dependencies match by type and identifier.</returns>
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

    /// <summary>
    /// Computes a hash code for a dependency.
    /// </summary>
    /// <param name="obj">Dependency to hash.</param>
    /// <returns>Hash code.</returns>
    public int GetHashCode(Dependency obj)
    {
        return HashCode.Combine(obj.Type, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Identifier));
    }
}
