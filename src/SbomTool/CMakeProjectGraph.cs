using System.Linq;

namespace CppSbom;

/// <summary>
/// Holds the resolved CMake target graph and alias mappings.
/// </summary>
internal sealed class CMakeProjectGraph
{
    /// <summary>
    /// Targets keyed by normalized identifier.
    /// </summary>
    private readonly Dictionary<string, CMakeTargetDefinition> _targetsById;
    /// <summary>
    /// Targets keyed by target name.
    /// </summary>
    private readonly Dictionary<string, List<CMakeTargetDefinition>> _targetsByName;
    /// <summary>
    /// Alias target mappings.
    /// </summary>
    private readonly Dictionary<string, string> _aliasTargets;

    /// <summary>
    /// Initializes a new project graph instance.
    /// </summary>
    /// <param name="rootDirectory">Root directory for the scan.</param>
    /// <param name="targetsById">Targets keyed by identifier.</param>
    /// <param name="targetsByName">Targets keyed by name.</param>
    /// <param name="aliasTargets">Alias target mappings.</param>
    public CMakeProjectGraph(
        string rootDirectory,
        Dictionary<string, CMakeTargetDefinition> targetsById,
        Dictionary<string, List<CMakeTargetDefinition>> targetsByName,
        Dictionary<string, string> aliasTargets)
    {
        RootDirectory = rootDirectory;
        _targetsById = targetsById;
        _targetsByName = targetsByName;
        _aliasTargets = aliasTargets;
    }

    /// <summary>
    /// Gets the root directory for the scan.
    /// </summary>
    public string RootDirectory { get; }

    /// <summary>
    /// Gets targets keyed by normalized identifier.
    /// </summary>
    public IReadOnlyDictionary<string, CMakeTargetDefinition> TargetsById => _targetsById;

    /// <summary>
    /// Gets targets keyed by target name.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<CMakeTargetDefinition>> TargetsByName =>
        _targetsByName.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<CMakeTargetDefinition>)pair.Value);

    /// <summary>
    /// Attempts to resolve a target identifier by name and directory.
    /// </summary>
    /// <param name="name">Target name.</param>
    /// <param name="directoryPath">Directory where the target is referenced.</param>
    /// <param name="identifier">Resolved identifier.</param>
    /// <returns>True when a target identifier is resolved.</returns>
    public bool TryResolveTargetIdentifier(string name, string directoryPath, out string identifier)
    {
        identifier = string.Empty;
        if (_aliasTargets.TryGetValue(name, out var aliasTarget))
        {
            name = aliasTarget;
        }

        if (_targetsByName.TryGetValue(name, out var candidates))
        {
            var match = candidates.FirstOrDefault(target =>
                string.Equals(target.DirectoryPath, directoryPath, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                identifier = match.Identifier;
                return true;
            }

            if (candidates.Count == 1)
            {
                identifier = candidates[0].Identifier;
                return true;
            }
        }

        return false;
    }
}
