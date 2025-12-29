using Serilog;

namespace CppSbom;

internal sealed class CMakeScanner
{
    private static readonly string[] LibraryExtensions = { ".lib", ".a", ".dll", ".so", ".dylib" };
    private static readonly HashSet<string> LinkKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "PUBLIC",
        "PRIVATE",
        "INTERFACE"
    };
    private static readonly HashSet<string> IncludeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM",
        "BEFORE",
        "PUBLIC",
        "PRIVATE",
        "INTERFACE"
    };
    private static readonly HashSet<string> LibraryTypeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "STATIC",
        "SHARED",
        "MODULE",
        "OBJECT",
        "INTERFACE",
        "IMPORTED"
    };
    private static readonly HashSet<string> ExecutableKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "WIN32",
        "MACOSX_BUNDLE",
        "EXCLUDE_FROM_ALL",
        "IMPORTED"
    };

    private readonly ILogger _logger;
    private readonly CMakeFileParser _parser = new();
    private readonly Dictionary<string, CMakeTargetDefinition> _targetsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<CMakeTargetDefinition>> _targetsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _aliasTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _visited = new(StringComparer.OrdinalIgnoreCase);
    private string _rootDirectory = string.Empty;

    public CMakeScanner(ILogger logger)
    {
        _logger = logger;
    }

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

    private static bool IsLiteral(string value) => !value.Contains('$');

    private static string ResolvePath(string value, string directory)
    {
        if (Path.IsPathRooted(value))
        {
            return Path.GetFullPath(value);
        }

        return Path.GetFullPath(Path.Combine(directory, value));
    }

    private static string NormalizeIdentifier(string value) =>
        value.Replace('\\', '/').ToLowerInvariant();

    private static string NormalizeAbsolutePath(string value)
    {
        var normalized = Path.GetFullPath(value).Replace('\\', '/').ToLowerInvariant();
        return normalized.TrimEnd('/');
    }

    private static string NormalizeRelativePath(string value)
    {
        var normalized = value.Replace('\\', '/').ToLowerInvariant();
        return normalized.TrimEnd('/');
    }

    public static bool LooksLikeFilePath(string value)
    {
        if (value.IndexOfAny(new[] { '/', '\\' }) >= 0)
        {
            return true;
        }

        return LibraryExtensions.Any(ext => value.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }
}
