using System.Text.RegularExpressions;
using Serilog;

namespace CppSbom;

/// <summary>
/// Scans Visual Studio solution files for project references.
/// </summary>
internal sealed class SolutionScanner
{
    /// <summary>
    /// Regex for parsing project lines in .sln files.
    /// </summary>
    private static readonly Regex ProjectLine = new(
        @"^Project\(""\{.*?\}""\) = "".*?"", ""(?<path>.*?\.vcxproj)"", ""\{.*?\}""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Logger used for scan diagnostics.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new solution scanner.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    public SolutionScanner(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Finds solution files under a root directory.
    /// </summary>
    /// <param name="root">Root directory to search.</param>
    /// <returns>Solution file paths.</returns>
    public IEnumerable<string> FindSolutions(string root)
    {
        _logger.Information("Scanning for solutions under {Root}", root);
        return Directory.EnumerateFiles(root, "*.sln", SearchOption.AllDirectories);
    }

    /// <summary>
    /// Finds project references declared by a solution.
    /// </summary>
    /// <param name="solutionPath">Path to the solution file.</param>
    /// <returns>Resolved project paths.</returns>
    public IEnumerable<string> FindProjects(string solutionPath)
    {
        var solutionDir = Path.GetDirectoryName(solutionPath)!;
        _logger.Debug("Parsing solution {Solution}", solutionPath);
        foreach (var line in File.ReadLines(solutionPath))
        {
            var match = ProjectLine.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var relative = match.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar);
            var projectPath = Path.GetFullPath(Path.Combine(solutionDir, relative));
            if (File.Exists(projectPath))
            {
                yield return projectPath;
            }
            else
            {
                _logger.Warning("Project {Project} referenced in {Solution} was not found", projectPath, solutionPath);
            }
        }
    }
}
