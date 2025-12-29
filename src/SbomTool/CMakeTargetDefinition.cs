namespace CppSbom;

internal sealed record CMakeTargetDefinition(
    string Name,
    string DirectoryPath,
    string Identifier,
    List<string> Sources,
    List<string> IncludeDirectories,
    List<string> LinkLibraries);
