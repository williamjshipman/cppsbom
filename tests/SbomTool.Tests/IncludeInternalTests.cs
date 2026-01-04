using System;
using System.Linq;
using CppSbom;
using Serilog;
using Xunit;

namespace CppSbom.Tests;

/// <summary>
/// Tests include-internal behavior for dependency collection.
/// </summary>
public sealed class IncludeInternalTests
{
    /// <summary>
    /// Creates a logger instance for tests.
    /// </summary>
    /// <returns>Logger instance.</returns>
    private static ILogger CreateLogger() => new LoggerConfiguration().MinimumLevel.Debug().CreateLogger();

    /// <summary>
    /// Verifies internal dependencies are excluded by default.
    /// </summary>
    [Fact]
    public void Parse_IncludeInternalDefaultsFalse()
    {
        var options = CommandLineOptions.Parse(Array.Empty<string>());
        Assert.False(options.IncludeInternal);
    }

    /// <summary>
    /// Verifies internal dependencies are included when the flag is set.
    /// </summary>
    [Fact]
    public void Parse_IncludeInternalSetsTrue()
    {
        var options = CommandLineOptions.Parse(new[] { "--include-internal" });
        Assert.True(options.IncludeInternal);
    }

    /// <summary>
    /// Verifies Visual Studio internal dependencies are excluded by default.
    /// </summary>
    [Fact]
    public void Analyze_InternalDependenciesExcludedByDefault()
    {
        using var workspace = new TempWorkspace();
        var projectPath = CreateVisualStudioProject(workspace);
        var options = CommandLineOptions.Parse(new[] { "--root", workspace.Root });
        var logger = CreateLogger();
        var analyzer = new ProjectAnalyzer(options, logger, new SourceScanner(), new NullComResolver(logger));
        var analysis = analyzer.Analyze(projectPath, workspace.Root);
        var dependencies = analysis.Dependencies.ToList();

        Assert.DoesNotContain(dependencies, dep => dep.Type == DependencyType.HeaderInclude);
        Assert.DoesNotContain(dependencies, dep => dep.Type == DependencyType.StaticLibrary);
    }

    /// <summary>
    /// Verifies Visual Studio internal dependencies are included when requested.
    /// </summary>
    [Fact]
    public void Analyze_InternalDependenciesIncludedWhenFlagSet()
    {
        using var workspace = new TempWorkspace();
        var projectPath = CreateVisualStudioProject(workspace);
        var options = CommandLineOptions.Parse(new[] { "--root", workspace.Root, "--include-internal" });
        var logger = CreateLogger();
        var analyzer = new ProjectAnalyzer(options, logger, new SourceScanner(), new NullComResolver(logger));
        var analysis = analyzer.Analyze(projectPath, workspace.Root);
        var dependencies = analysis.Dependencies.ToList();

        Assert.Contains(dependencies, dep => dep.Type == DependencyType.HeaderInclude
            && dep.ResolvedPath?.EndsWith("internal.h", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(dependencies, dep => dep.Type == DependencyType.StaticLibrary
            && dep.ResolvedPath?.EndsWith("mylib.lib", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// Verifies CMake internal libraries are excluded by default.
    /// </summary>
    [Fact]
    public void Analyze_CMakeInternalLibrariesExcludedByDefault()
    {
        using var workspace = new TempWorkspace();
        var target = CreateCMakeTarget(workspace, out var graph);
        var options = CommandLineOptions.Parse(new[] { "--root", workspace.Root, "--type", "cmake" });
        var analyzer = new CMakeTargetAnalyzer(options, CreateLogger(), new SourceScanner(), new NullComResolver(CreateLogger()), graph);
        var dependencies = analyzer.Analyze(target).ToList();

        Assert.DoesNotContain(dependencies, dep => dep.ResolvedPath?.EndsWith("libfoo.lib", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// Verifies CMake internal libraries are included when requested.
    /// </summary>
    [Fact]
    public void Analyze_CMakeInternalLibrariesIncludedWhenFlagSet()
    {
        using var workspace = new TempWorkspace();
        var target = CreateCMakeTarget(workspace, out var graph);
        var options = CommandLineOptions.Parse(new[] { "--root", workspace.Root, "--type", "cmake", "--include-internal" });
        var analyzer = new CMakeTargetAnalyzer(options, CreateLogger(), new SourceScanner(), new NullComResolver(CreateLogger()), graph);
        var dependencies = analyzer.Analyze(target).ToList();

        Assert.Contains(dependencies, dep => dep.ResolvedPath?.EndsWith("libfoo.lib", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// Creates a minimal Visual Studio project with internal include and library files.
    /// </summary>
    /// <param name="workspace">Workspace to populate.</param>
    /// <returns>Project file path.</returns>
    private static string CreateVisualStudioProject(TempWorkspace workspace)
    {
        workspace.WriteFile("main.cpp", "#include \"internal.h\"\n");
        workspace.WriteFile("internal.h", "// internal\n");
        workspace.WriteFile("mylib.lib", "dummy");

        var projectXml = """
<Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <ItemDefinitionGroup>
    <Link>
      <AdditionalDependencies>mylib.lib</AdditionalDependencies>
    </Link>
  </ItemDefinitionGroup>
  <ItemGroup>
    <ClCompile Include="main.cpp" />
    <ClInclude Include="internal.h" />
  </ItemGroup>
</Project>
""";
        return workspace.WriteFile("sample.vcxproj", projectXml);
    }

    /// <summary>
    /// Creates a CMake target with an internal linked library.
    /// </summary>
    /// <param name="workspace">Workspace to populate.</param>
    /// <param name="graph">Graph created from the workspace.</param>
    /// <returns>Discovered target definition.</returns>
    private static CMakeTargetDefinition CreateCMakeTarget(TempWorkspace workspace, out CMakeProjectGraph graph)
    {
        workspace.WriteFile("CMakeLists.txt", "add_library(foo foo.cpp)\ntarget_link_libraries(foo libs/libfoo.lib)");
        workspace.WriteFile("foo.cpp", "// foo");
        workspace.WriteFile("libs/libfoo.lib", "dummy");

        var scanner = new CMakeScanner(CreateLogger());
        graph = scanner.Scan(workspace.Root);
        return graph.TargetsById.Values.Single();
    }
}
