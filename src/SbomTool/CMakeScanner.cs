using Serilog;

namespace CppSbom;

/// <summary>
/// Scans CMakeLists.txt files and builds a target graph.
/// </summary>
internal sealed class CMakeScanner
{
    /// <summary>
    /// Supported library extensions for link entries.
    /// </summary>
    private static readonly string[] LibraryExtensions = { ".lib", ".a", ".dll", ".so", ".dylib" };
    /// <summary>
    /// Keywords that denote link visibility.
    /// </summary>
    private static readonly HashSet<string> LinkKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "PUBLIC",
        "PRIVATE",
        "INTERFACE"
    };
    /// <summary>
    /// Keywords used in include directory commands.
    /// </summary>
    private static readonly HashSet<string> IncludeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM",
        "BEFORE",
        "PUBLIC",
        "PRIVATE",
        "INTERFACE"
    };
    /// <summary>
    /// Library type keywords in add_library commands.
    /// </summary>
    private static readonly HashSet<string> LibraryTypeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "STATIC",
        "SHARED",
        "MODULE",
        "OBJECT",
        "INTERFACE",
        "IMPORTED"
    };
    /// <summary>
    /// Executable keywords in add_executable commands.
    /// </summary>
    private static readonly HashSet<string> ExecutableKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "WIN32",
        "MACOSX_BUNDLE",
        "EXCLUDE_FROM_ALL",
        "IMPORTED"
    };

    /// <summary>
    /// Logger used for diagnostics.
    /// </summary>
    private readonly ILogger _logger;
    /// <summary>
    /// Parser for individual CMake files.
    /// </summary>
    private readonly CMakeFileParser _parser = new();
    /// <summary>
    /// Targets keyed by normalized identifier.
    /// </summary>
    private readonly Dictionary<string, CMakeTargetDefinition> _targetsById = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Targets keyed by target name.
    /// </summary>
    private readonly Dictionary<string, List<CMakeTargetDefinition>> _targetsByName = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Alias target mappings.
    /// </summary>
    private readonly Dictionary<string, string> _aliasTargets = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Visited CMake file paths.
    /// </summary>
    private readonly HashSet<string> _visited = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Root directory for the scan.
    /// </summary>
    private string _rootDirectory = string.Empty;

    /// <summary>
    /// Initializes a new CMake scanner.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    public CMakeScanner(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Scans the root directory and returns a target graph.
    /// </summary>
    /// <param name="rootDirectory">Root directory containing CMakeLists.txt.</param>
    /// <returns>CMake project graph.</returns>
    public CMakeProjectGraph Scan(string rootDirectory)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
        var rootCmake = Path.Combine(_rootDirectory, "CMakeLists.txt");
        if (!File.Exists(rootCmake))
        {
            throw new FileNotFoundException($"Root CMakeLists.txt not found at '{rootCmake}'");
        }

        var queue = new Queue<(string Path, bool IsRoot)>();
        queue.Enqueue((rootCmake, true));

        while (queue.Count > 0)
        {
            var (path, isRoot) = queue.Dequeue();
            var normalizedPath = NormalizeAbsolutePath(path);
            if (!_visited.Add(normalizedPath))
            {
                continue;
            }

            var parseResult = _parser.ParseFile(path);
            if (parseResult.HasError)
            {
                if (isRoot)
                {
                    throw new InvalidOperationException(parseResult.ErrorMessage ?? "Failed to parse root CMakeLists.txt");
                }

                _logger.Warning("{Message}", parseResult.ErrorMessage ?? "Failed to parse CMake file");
            }

            foreach (var command in parseResult.Commands)
            {
                ProcessCommand(command, queue);
            }
        }

        return new CMakeProjectGraph(_rootDirectory, _targetsById, _targetsByName, _aliasTargets);
    }

    /// <summary>
    /// Processes a parsed CMake command.
    /// </summary>
    /// <param name="command">Parsed command.</param>
    /// <param name="queue">Queue of files to scan.</param>
    private void ProcessCommand(CMakeCommand command, Queue<(string Path, bool IsRoot)> queue)
    {
        switch (command.Name.ToLowerInvariant())
        {
            case "add_subdirectory":
                ProcessAddSubdirectory(command, queue);
                break;
            case "add_library":
                ProcessAddLibrary(command);
                break;
            case "add_executable":
                ProcessAddExecutable(command);
                break;
            case "target_sources":
                ProcessTargetSources(command);
                break;
            case "target_include_directories":
                ProcessTargetIncludeDirectories(command);
                break;
            case "target_link_libraries":
                ProcessTargetLinkLibraries(command);
                break;
        }
    }

    /// <summary>
    /// Handles add_subdirectory commands and queues nested files.
    /// </summary>
    /// <param name="command">Parsed command.</param>
    /// <param name="queue">Queue of files to scan.</param>
    private void ProcessAddSubdirectory(CMakeCommand command, Queue<(string Path, bool IsRoot)> queue)
    {
        if (command.Arguments.Count == 0)
        {
            return;
        }

        var directoryArg = command.Arguments[0];
        if (!IsLiteral(directoryArg))
        {
            _logger.Warning("Ignoring non-literal add_subdirectory entry '{Entry}' in {Path}", directoryArg, command.DirectoryPath);
            return;
        }

        var subdirPath = Path.GetFullPath(Path.Combine(command.DirectoryPath, directoryArg));
        var cmakePath = Path.Combine(subdirPath, "CMakeLists.txt");
        if (!File.Exists(cmakePath))
        {
            _logger.Warning("CMakeLists.txt not found at {Path}", cmakePath);
            return;
        }

        queue.Enqueue((cmakePath, false));
    }

    /// <summary>
    /// Handles add_library commands.
    /// </summary>
    /// <param name="command">Parsed command.</param>
    private void ProcessAddLibrary(CMakeCommand command)
    {
        if (command.Arguments.Count < 1)
        {
            return;
        }

        var name = command.Arguments[0];
        if (!IsLiteral(name))
        {
            _logger.Warning("Ignoring non-literal target name '{Entry}' in {Path}", name, command.DirectoryPath);
            return;
        }

        if (command.Arguments.Count >= 3 && string.Equals(command.Arguments[1], "ALIAS", StringComparison.OrdinalIgnoreCase))
        {
            var aliasTarget = command.Arguments[2];
            if (!IsLiteral(aliasTarget))
            {
                _logger.Warning("Ignoring non-literal alias target '{Entry}' in {Path}", aliasTarget, command.DirectoryPath);
                return;
            }

            _aliasTargets[name] = aliasTarget;
            return;
        }

        var startIndex = 1;
        while (startIndex < command.Arguments.Count && LibraryTypeKeywords.Contains(command.Arguments[startIndex]))
        {
            startIndex++;
        }

        var target = CreateTarget(name, command.DirectoryPath);
        AddSources(target, command.Arguments.Skip(startIndex), command.DirectoryPath);
    }

    /// <summary>
    /// Handles add_executable commands.
    /// </summary>
    /// <param name="command">Parsed command.</param>
    private void ProcessAddExecutable(CMakeCommand command)
    {
        if (command.Arguments.Count < 1)
        {
            return;
        }

        var name = command.Arguments[0];
        if (!IsLiteral(name))
        {
            _logger.Warning("Ignoring non-literal target name '{Entry}' in {Path}", name, command.DirectoryPath);
            return;
        }

        var startIndex = 1;
        while (startIndex < command.Arguments.Count && ExecutableKeywords.Contains(command.Arguments[startIndex]))
        {
            startIndex++;
        }

        var target = CreateTarget(name, command.DirectoryPath);
        AddSources(target, command.Arguments.Skip(startIndex), command.DirectoryPath);
    }

    /// <summary>
    /// Handles target_sources commands.
    /// </summary>
    /// <param name="command">Parsed command.</param>
    private void ProcessTargetSources(CMakeCommand command)
    {
        if (command.Arguments.Count < 1)
        {
            return;
        }

        var targetName = command.Arguments[0];
        if (!TryGetTarget(targetName, command.DirectoryPath, out var target))
        {
            _logger.Warning("Unable to resolve target '{Target}' for target_sources in {Path}", targetName, command.DirectoryPath);
            return;
        }

        var entries = command.Arguments.Skip(1).Where(arg => !LinkKeywords.Contains(arg));
        AddSources(target, entries, command.DirectoryPath);
    }

    /// <summary>
    /// Handles target_include_directories commands.
    /// </summary>
    /// <param name="command">Parsed command.</param>
    private void ProcessTargetIncludeDirectories(CMakeCommand command)
    {
        if (command.Arguments.Count < 1)
        {
            return;
        }

        var targetName = command.Arguments[0];
        if (!TryGetTarget(targetName, command.DirectoryPath, out var target))
        {
            _logger.Warning("Unable to resolve target '{Target}' for target_include_directories in {Path}", targetName, command.DirectoryPath);
            return;
        }

        foreach (var entry in command.Arguments.Skip(1))
        {
            if (IncludeKeywords.Contains(entry))
            {
                continue;
            }

            if (!IsLiteral(entry))
            {
                _logger.Warning("Ignoring non-literal include entry '{Entry}' in {Path}", entry, command.DirectoryPath);
                continue;
            }

            var includePath = ResolvePath(entry, command.DirectoryPath);
            target.IncludeDirectories.Add(includePath);
        }
    }

    /// <summary>
    /// Handles target_link_libraries commands.
    /// </summary>
    /// <param name="command">Parsed command.</param>
    private void ProcessTargetLinkLibraries(CMakeCommand command)
    {
        if (command.Arguments.Count < 1)
        {
            return;
        }

        var targetName = command.Arguments[0];
        if (!TryGetTarget(targetName, command.DirectoryPath, out var target))
        {
            _logger.Warning("Unable to resolve target '{Target}' for target_link_libraries in {Path}", targetName, command.DirectoryPath);
            return;
        }

        foreach (var entry in command.Arguments.Skip(1))
        {
            if (LinkKeywords.Contains(entry))
            {
                continue;
            }

            if (!IsLiteral(entry))
            {
                _logger.Warning("Ignoring non-literal link entry '{Entry}' in {Path}", entry, command.DirectoryPath);
                continue;
            }

            target.LinkLibraries.Add(entry);
        }
    }

    /// <summary>
    /// Adds source file entries to a target definition.
    /// </summary>
    /// <param name="target">Target to update.</param>
    /// <param name="entries">Source entries to add.</param>
    /// <param name="directory">Directory used for path resolution.</param>
    private void AddSources(CMakeTargetDefinition target, IEnumerable<string> entries, string directory)
    {
        foreach (var entry in entries)
        {
            if (!IsLiteral(entry))
            {
                _logger.Warning("Ignoring non-literal source entry '{Entry}' in {Path}", entry, directory);
                continue;
            }

            var sourcePath = ResolvePath(entry, directory);
            target.Sources.Add(sourcePath);
        }
    }

    /// <summary>
    /// Creates a target definition and registers it in the graph.
    /// </summary>
    /// <param name="name">Target name.</param>
    /// <param name="directory">Directory containing the target.</param>
    /// <returns>Target definition.</returns>
    private CMakeTargetDefinition CreateTarget(string name, string directory)
    {
        var identifier = BuildTargetIdentifier(name, directory);
        if (_targetsById.ContainsKey(identifier))
        {
            throw new InvalidOperationException("Duplicate normalized CMake target identifier '{identifier}'");
        }

        var target = new CMakeTargetDefinition(
            name,
            directory,
            identifier,
            new List<string>(),
            new List<string>(),
            new List<string>());
        _targetsById.Add(identifier, target);

        if (!_targetsByName.TryGetValue(name, out var list))
        {
            list = new List<CMakeTargetDefinition>();
            _targetsByName[name] = list;
        }

        list.Add(target);
        return target;
    }

    /// <summary>
    /// Attempts to resolve a target definition.
    /// </summary>
    /// <param name="name">Target name.</param>
    /// <param name="directory">Directory containing the target.</param>
    /// <param name="target">Resolved target definition.</param>
    /// <returns>True when a target is found.</returns>
    private bool TryGetTarget(string name, string directory, out CMakeTargetDefinition target)
    {
        target = null!;
        if (!IsLiteral(name))
        {
            _logger.Warning("Ignoring non-literal target name '{Entry}' in {Path}", name, directory);
            return false;
        }

        if (_aliasTargets.TryGetValue(name, out var aliasTarget))
        {
            name = aliasTarget;
        }

        if (_targetsByName.TryGetValue(name, out var candidates))
        {
            var match = candidates.FirstOrDefault(item =>
                string.Equals(item.DirectoryPath, directory, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                target = match;
                return true;
            }

            if (candidates.Count == 1)
            {
                target = candidates[0];
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds a normalized target identifier for a target.
    /// </summary>
    /// <param name="name">Target name.</param>
    /// <param name="directory">Directory containing the target.</param>
    /// <returns>Normalized identifier.</returns>
    private string BuildTargetIdentifier(string name, string directory)
    {
        var relative = Path.GetRelativePath(_rootDirectory, directory);
        var normalizedDirectory = NormalizeRelativePath(relative);
        if (string.IsNullOrWhiteSpace(normalizedDirectory) || normalizedDirectory == ".")
        {
            normalizedDirectory = string.Empty;
        }

        var identifier = string.IsNullOrEmpty(normalizedDirectory)
            ? name
            : $"{normalizedDirectory}::{name}";
        identifier = NormalizeIdentifier(identifier);

        return identifier;
    }

    /// <summary>
    /// Determines whether a value is a literal (no variables).
    /// </summary>
    /// <param name="value">Value to inspect.</param>
    /// <returns>True when the value is literal.</returns>
    private static bool IsLiteral(string value) => !value.Contains('$');

    /// <summary>
    /// Resolves a path relative to a directory.
    /// </summary>
    /// <param name="value">Path value.</param>
    /// <param name="directory">Base directory.</param>
    /// <returns>Absolute path.</returns>
    private static string ResolvePath(string value, string directory)
    {
        if (Path.IsPathRooted(value))
        {
            return Path.GetFullPath(value);
        }

        return Path.GetFullPath(Path.Combine(directory, value));
    }

    /// <summary>
    /// Normalizes a target identifier for comparisons.
    /// </summary>
    /// <param name="value">Identifier value.</param>
    /// <returns>Normalized identifier.</returns>
    private static string NormalizeIdentifier(string value) =>
        value.Replace('\\', '/').ToLowerInvariant();

    /// <summary>
    /// Normalizes an absolute path for comparisons.
    /// </summary>
    /// <param name="value">Absolute path.</param>
    /// <returns>Normalized path.</returns>
    private static string NormalizeAbsolutePath(string value)
    {
        var normalized = Path.GetFullPath(value).Replace('\\', '/').ToLowerInvariant();
        return normalized.TrimEnd('/');
    }

    /// <summary>
    /// Normalizes a relative path for comparisons.
    /// </summary>
    /// <param name="value">Relative path.</param>
    /// <returns>Normalized path.</returns>
    private static string NormalizeRelativePath(string value)
    {
        var normalized = value.Replace('\\', '/').ToLowerInvariant();
        return normalized.TrimEnd('/');
    }

    /// <summary>
    /// Determines whether a link entry looks like a file path.
    /// </summary>
    /// <param name="value">Link entry value.</param>
    /// <returns>True when the value resembles a file path.</returns>
    public static bool LooksLikeFilePath(string value)
    {
        if (value.IndexOfAny(new[] { '/', '\\' }) >= 0)
        {
            return true;
        }

        return LibraryExtensions.Any(ext => value.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }
}
