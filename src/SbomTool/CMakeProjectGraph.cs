using System.Linq;

namespace CppSbom;

internal sealed class CMakeProjectGraph
{
    private readonly Dictionary<string, CMakeTargetDefinition> _targetsById;
    private readonly Dictionary<string, List<CMakeTargetDefinition>> _targetsByName;
    private readonly Dictionary<string, string> _aliasTargets;

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

    public string RootDirectory { get; }

    public IReadOnlyDictionary<string, CMakeTargetDefinition> TargetsById => _targetsById;

    public IReadOnlyDictionary<string, IReadOnlyList<CMakeTargetDefinition>> TargetsByName =>
        _targetsByName.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<CMakeTargetDefinition>)pair.Value);

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
