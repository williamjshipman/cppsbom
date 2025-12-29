namespace CppSbom;

/// <summary>
/// Represents a CMake target with resolved inputs.
/// </summary>
/// <param name="Name">Target name.</param>
/// <param name="DirectoryPath">Directory containing the target definition.</param>
/// <param name="Identifier">Normalized target identifier.</param>
/// <param name="Sources">Source files associated with the target.</param>
/// <param name="IncludeDirectories">Include directories associated with the target.</param>
/// <param name="LinkLibraries">Linked library entries.</param>
internal sealed record CMakeTargetDefinition(
    string Name,
    string DirectoryPath,
    string Identifier,
    List<string> Sources,
    List<string> IncludeDirectories,
    List<string> LinkLibraries);
