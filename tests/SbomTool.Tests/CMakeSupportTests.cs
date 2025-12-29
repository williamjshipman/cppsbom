using System;
using System.IO;
using System.Linq;
using CppSbom;
using Serilog;
using Xunit;

namespace CppSbom.Tests;

/// <summary>
/// Tests CMake scanning and analysis behavior.
/// </summary>
public sealed class CMakeSupportTests
{
    /// <summary>
    /// Creates a logger instance for tests.
    /// </summary>
    /// <returns>Logger instance.</returns>
    private static ILogger CreateLogger() => new LoggerConfiguration().MinimumLevel.Debug().CreateLogger();

    /// <summary>
    /// Verifies the default scan type is Visual Studio.
    /// </summary>
    [Fact]
    public void Parse_DefaultsToVisualStudio()
    {
        var options = CommandLineOptions.Parse(Array.Empty<string>());
        Assert.Equal(ScanType.VisualStudio, options.Type);
    }

    /// <summary>
    /// Verifies that parsing cmake scan type selects CMake.
    /// </summary>
    [Fact]
    public void Parse_CMakeType()
    {
        var options = CommandLineOptions.Parse(new[] { "--type", "cmake" });
        Assert.Equal(ScanType.CMake, options.Type);
    }

    /// <summary>
    /// Verifies that unsupported scan types fail parsing.
    /// </summary>
    [Fact]
    public void Parse_InvalidTypeThrows()
    {
        var ex = Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(new[] { "--type", "nope" }));
        Assert.Contains("Unknown scan type", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that root CMakeLists.txt is required for scanning.
    /// </summary>
    [Fact]
    public void Scan_CMakeListsRequiresRootFile()
    {
        using var workspace = new TempWorkspace();
        var scanner = new CMakeScanner(CreateLogger());

        var ex = Assert.Throws<FileNotFoundException>(() => scanner.Scan(workspace.Root));
        Assert.Contains("CMakeLists.txt", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies root parse errors are fatal.
    /// </summary>
    [Fact]
    public void Scan_RootParseErrorIsFatal()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("CMakeLists.txt", "add_library(foo");

        var scanner = new CMakeScanner(CreateLogger());
        Assert.Throws<InvalidOperationException>(() => scanner.Scan(workspace.Root));
    }

    /// <summary>
    /// Verifies non-root parse errors are logged but non-fatal.
    /// </summary>
    [Fact]
    public void Scan_NonRootParseErrorIsNonFatal()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("CMakeLists.txt", "add_subdirectory(sub)");
        workspace.WriteFile("sub/CMakeLists.txt", "add_library(foo");

        var scanner = new CMakeScanner(CreateLogger());
        var graph = scanner.Scan(workspace.Root);

        Assert.Empty(graph.TargetsById);
    }

    /// <summary>
    /// Verifies add_subdirectory can traverse outside root.
    /// </summary>
    [Fact]
    public void Scan_AddSubdirectoryIncludesOutsideRoot()
    {
        using var workspace = new TempWorkspace();
        var root = Path.Combine(workspace.BaseDirectory, "root");
        var external = Path.Combine(workspace.BaseDirectory, "external");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(external);

        File.WriteAllText(Path.Combine(root, "CMakeLists.txt"), "add_subdirectory(../external)\nadd_library(rootlib root.cpp)");
        File.WriteAllText(Path.Combine(root, "root.cpp"), "// root");
        File.WriteAllText(Path.Combine(external, "CMakeLists.txt"), "add_library(extlib ext.cpp)");
        File.WriteAllText(Path.Combine(external, "ext.cpp"), "// ext");

        var scanner = new CMakeScanner(CreateLogger());
        var graph = scanner.Scan(root);

        Assert.Contains(graph.TargetsById.Keys, key => key.EndsWith("rootlib", StringComparison.Ordinal));
        Assert.Contains(graph.TargetsById.Keys, key => key.Contains("external", StringComparison.Ordinal) && key.EndsWith("extlib", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies nested add_subdirectory discovery.
    /// </summary>
    [Fact]
    public void Scan_NestedAddSubdirectoryTargetsDiscovered()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("CMakeLists.txt", "add_subdirectory(sub)");
        workspace.WriteFile("sub/CMakeLists.txt", "add_subdirectory(nested)");
        workspace.WriteFile("sub/nested/CMakeLists.txt", "add_executable(App main.cpp)");
        workspace.WriteFile("sub/nested/main.cpp", "// main");

        var scanner = new CMakeScanner(CreateLogger());
        var graph = scanner.Scan(workspace.Root);

        Assert.Contains(graph.TargetsById.Keys, key => key == "sub/nested::app");
    }

    /// <summary>
    /// Verifies non-literal add_subdirectory entries are ignored.
    /// </summary>
    [Fact]
    public void Scan_IgnoresNonLiteralAddSubdirectory()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("CMakeLists.txt", "add_subdirectory(${FOO})\nadd_subdirectory(sub)");
        workspace.WriteFile("sub/CMakeLists.txt", "add_library(bar bar.cpp)");
        workspace.WriteFile("sub/bar.cpp", "// bar");

        var scanner = new CMakeScanner(CreateLogger());
        var graph = scanner.Scan(workspace.Root);

        Assert.Single(graph.TargetsById);
    }

    /// <summary>
    /// Verifies normalized target identifiers are produced.
    /// </summary>
    [Fact]
    public void Scan_TargetIdentifiersAreNormalized()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("CMakeLists.txt", "add_subdirectory(SubDir)");
        workspace.WriteFile("SubDir/CMakeLists.txt", "add_library(Foo foo.cpp)");
        workspace.WriteFile("SubDir/foo.cpp", "// foo");

        var scanner = new CMakeScanner(CreateLogger());
        var graph = scanner.Scan(workspace.Root);

        Assert.Contains(graph.TargetsById.Keys, key => key == "subdir::foo");
    }

    /// <summary>
    /// Verifies target identifier collisions are fatal.
    /// </summary>
    [Fact]
    public void Scan_TargetIdentifierCollisionsAreFatal()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("CMakeLists.txt", "add_library(foo foo.cpp)\nadd_library(foo bar.cpp)");
        workspace.WriteFile("foo.cpp", "// foo");
        workspace.WriteFile("bar.cpp", "// bar");

        var scanner = new CMakeScanner(CreateLogger());
        Assert.Throws<InvalidOperationException>(() => scanner.Scan(workspace.Root));
    }

    /// <summary>
    /// Verifies non-literal target arguments are ignored.
    /// </summary>
    [Fact]
    public void Scan_NonLiteralTargetArgumentsAreIgnored()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("CMakeLists.txt", "add_library(foo foo.cpp)\ntarget_sources(foo ${SRC} extra.cpp)");
        workspace.WriteFile("foo.cpp", "// foo");
        workspace.WriteFile("extra.cpp", "// extra");

        var scanner = new CMakeScanner(CreateLogger());
        var graph = scanner.Scan(workspace.Root);
        var target = graph.TargetsById.Values.Single();

        Assert.Contains(target.Sources, path => path.EndsWith("foo.cpp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(target.Sources, path => path.EndsWith("extra.cpp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(target.Sources, path => path.Contains("${SRC}", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies link libraries appear as dependencies and includes do not.
    /// </summary>
    [Fact]
    public void Analyze_LinkLibrariesAndIncludeDirectories()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("CMakeLists.txt", "add_library(foo foo.cpp)\ntarget_include_directories(foo include)\ntarget_link_libraries(foo mylib)");
        workspace.WriteFile("foo.cpp", "// foo\n#include \"header.h\"");
        workspace.WriteFile("include/header.h", "// header");

        var scanner = new CMakeScanner(CreateLogger());
        var graph = scanner.Scan(workspace.Root);
        var target = graph.TargetsById.Values.Single();

        var options = CommandLineOptions.Parse(new[] { "--root", workspace.Root, "--type", "cmake" });
        var analyzer = new CMakeTargetAnalyzer(options, CreateLogger(), new SourceScanner(), new NullComResolver(CreateLogger()), graph);
        var dependencies = analyzer.Analyze(target).ToList();

        Assert.Contains(dependencies, dep => dep.Identifier == "mylib" && dep.Type == DependencyType.StaticLibrary);
        Assert.DoesNotContain(dependencies, dep => dep.Type == DependencyType.HeaderInclude && dep.ResolvedPath?.Contains("include") == true);
    }

    /// <summary>
    /// Verifies link file paths are recorded as file dependencies.
    /// </summary>
    [Fact]
    public void Analyze_LinkFilePathsAreRecordedAsFiles()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("CMakeLists.txt", "add_library(foo foo.cpp)\ntarget_link_libraries(foo libs/libfoo.lib libbar.lib)");
        workspace.WriteFile("foo.cpp", "// foo");
        workspace.WriteFile("libs/libfoo.lib", "dummy");
        workspace.WriteFile("libbar.lib", "dummy");

        var scanner = new CMakeScanner(new LoggerConfiguration().CreateLogger());
        var graph = scanner.Scan(workspace.Root);
        var target = graph.TargetsById.Values.Single();

        var options = CommandLineOptions.Parse(new[] { "--root", workspace.Root, "--type", "cmake" });
        var analyzer = new CMakeTargetAnalyzer(options, CreateLogger(), new SourceScanner(), new NullComResolver(CreateLogger()), graph);
        var dependencies = analyzer.Analyze(target).ToList();

        Assert.Contains(dependencies, dep => dep.ResolvedPath is not null && dep.ResolvedPath.EndsWith("libfoo.lib", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dependencies, dep => dep.ResolvedPath is not null && dep.ResolvedPath.EndsWith("libbar.lib", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies include directories are deduped and must exist.
    /// </summary>
    [Fact]
    public void Analyze_IncludeDirectoriesAreDedupedAndExist()
    {
        using var workspace = new TempWorkspace();
        var includeDir = workspace.CreateDirectory("Include");
        workspace.WriteFile("Include/header.h", "// header");
        workspace.WriteFile("CMakeLists.txt", "add_library(foo foo.cpp)\ntarget_include_directories(foo Include include Missing)");
        workspace.WriteFile("foo.cpp", "// foo");

        var scanner = new CMakeScanner(new LoggerConfiguration().CreateLogger());
        var graph = scanner.Scan(workspace.Root);
        var target = graph.TargetsById.Values.Single();

        var options = CommandLineOptions.Parse(new[] { "--root", workspace.Root, "--type", "cmake" });
        var analyzer = new CMakeTargetAnalyzer(options, CreateLogger(), new SourceScanner(), new NullComResolver(CreateLogger()), graph);
        var includeDirs = analyzer.GetIncludeDirectoriesForTarget(target);

        Assert.Single(includeDirs, path => path.EndsWith("Include", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(includeDirs, path => path.EndsWith("Missing", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies third-party roots apply when scanning outside root.
    /// </summary>
    [Fact]
    public void Analyze_ThirdPartyRootsApplyOutsideRoot()
    {
        using var workspace = new TempWorkspace();
        var thirdParty = workspace.CreateSiblingDirectory("thirdparty");
        var thirdPartyInclude = Path.Combine(thirdParty, "include");
        Directory.CreateDirectory(thirdPartyInclude);
        File.WriteAllText(Path.Combine(thirdPartyInclude, "third.h"), "// third");

        workspace.WriteFile("CMakeLists.txt", "add_library(foo foo.cpp)\ntarget_include_directories(foo " + thirdPartyInclude.Replace("\\", "/") + ")");
        workspace.WriteFile("foo.cpp", "// foo\n#include \"third.h\"");

        var options = CommandLineOptions.Parse(new[] { "--root", workspace.Root, "--type", "cmake", "--third-party", thirdParty });
        var scanner = new CMakeScanner(new LoggerConfiguration().CreateLogger());
        var graph = scanner.Scan(workspace.Root);
        var target = graph.TargetsById.Values.Single();
        var analyzer = new CMakeTargetAnalyzer(options, CreateLogger(), new SourceScanner(), new NullComResolver(CreateLogger()), graph);
        var dependencies = analyzer.Analyze(target).ToList();

        Assert.Contains(dependencies, dep => dep.Identifier.StartsWith(Path.GetFileName(thirdParty), StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Creates a temporary workspace for filesystem-based tests.
/// </summary>
internal sealed class TempWorkspace : IDisposable
{
    /// <summary>
    /// Initializes a new temporary workspace.
    /// </summary>
    public TempWorkspace()
    {
        BaseDirectory = Path.Combine(Path.GetTempPath(), "cppsbom-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(BaseDirectory);
        Root = Path.Combine(BaseDirectory, "root");
        Directory.CreateDirectory(Root);
    }

    /// <summary>
    /// Gets the base directory for this workspace.
    /// </summary>
    public string BaseDirectory { get; }

    /// <summary>
    /// Gets the root directory used for scans.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Creates a directory under the workspace root.
    /// </summary>
    /// <param name="parts">Path segments under the root.</param>
    /// <returns>Created directory path.</returns>
    public string CreateDirectory(params string[] parts)
    {
        var path = Path.Combine(new[] { Root }.Concat(parts).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Creates a sibling directory next to the root folder.
    /// </summary>
    /// <param name="name">Directory name.</param>
    /// <returns>Created directory path.</returns>
    public string CreateSiblingDirectory(string name)
    {
        var path = Path.Combine(BaseDirectory, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Writes a file under the workspace root.
    /// </summary>
    /// <param name="relativePath">Path relative to the root.</param>
    /// <param name="content">File content.</param>
    /// <returns>Written file path.</returns>
    public string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// Removes the workspace directories.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(BaseDirectory))
        {
            Directory.Delete(BaseDirectory, true);
        }
    }
}
