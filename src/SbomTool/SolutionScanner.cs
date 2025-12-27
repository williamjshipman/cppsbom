using System.Text.RegularExpressions;
using Serilog;

namespace CppSbom;

internal sealed class SolutionScanner
{
    private static readonly Regex ProjectLine = new(
        @"^Project\(""\{.*?\}""\) = "".*?"", ""(?<path>.*?\.vcxproj)"", ""\{.*?\}""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ILogger _logger;

    public SolutionScanner(ILogger logger)
    {
        _logger = logger;
    }

    public IEnumerable<string> FindSolutions(string root)
    {
        _logger.Information("Scanning for solutions under {Root}", root);
        return Directory.EnumerateFiles(root, "*.sln", SearchOption.AllDirectories);
    }

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
