using System.Text.Json;
using Serilog;

namespace CppSbom;

internal sealed class SbomWriter
{
    private readonly ILogger _logger;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public SbomWriter(ILogger logger)
    {
        _logger = logger;
    }

    public void Write(SbomReport report, string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(report, Options);
        File.WriteAllText(outputPath, json);
        _logger.Information("SBOM written to {Output}", outputPath);
    }
}
