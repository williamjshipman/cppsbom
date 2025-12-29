using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace CppSbom;

/// <summary>
/// Writes SBOM reports to disk as JSON.
/// </summary>
internal sealed class SbomWriter
{
    /// <summary>
    /// Logger instance used for reporting output status.
    /// </summary>
    private readonly ILogger _logger;
    /// <summary>
    /// JSON serializer options for emitting reports.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Initializes a new writer with the provided logger.
    /// </summary>
    /// <param name="logger">Logger used for output status.</param>
    public SbomWriter(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Serializes and writes a report to the output path.
    /// </summary>
    /// <param name="report">Report payload to serialize.</param>
    /// <param name="outputPath">Destination file path.</param>
    public void Write(object report, string outputPath)
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
