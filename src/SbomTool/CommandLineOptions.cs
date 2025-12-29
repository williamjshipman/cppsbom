namespace CppSbom;

/// <summary>
/// Defines supported SBOM output formats.
/// </summary>
internal enum OutputFormat
{
    /// <summary>
    /// Emit SPDX JSON output.
    /// </summary>
    Spdx,
    /// <summary>
    /// Emit CycloneDX JSON output.
    /// </summary>
    CycloneDx
}

/// <summary>
/// Defines supported scan modes for dependency discovery.
/// </summary>
internal enum ScanType
{
    /// <summary>
    /// Scan Visual Studio solution and project files.
    /// </summary>
    VisualStudio,
    /// <summary>
    /// Scan CMakeLists.txt files.
    /// </summary>
    CMake
}

/// <summary>
/// Captures parsed command line options for a scan run.
/// </summary>
internal sealed record CommandLineOptions
{
    /// <summary>
    /// Gets the root directory for scanning.
    /// </summary>
    public string RootDirectory { get; init; } = Directory.GetCurrentDirectory();
    /// <summary>
    /// Gets the third-party roots used to classify dependencies.
    /// </summary>
    public IReadOnlyList<string> ThirdPartyDirectories { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Gets the output SBOM path.
    /// </summary>
    public string OutputPath { get; init; } = Path.Combine(Directory.GetCurrentDirectory(), "sbom-report.json");
    /// <summary>
    /// Gets the log file path.
    /// </summary>
    public string LogPath { get; init; } = Path.Combine(Directory.GetCurrentDirectory(), "cppsbom.log");
    /// <summary>
    /// Gets the selected output format.
    /// </summary>
    public OutputFormat Format { get; init; } = OutputFormat.Spdx;
    /// <summary>
    /// Gets the selected scan type.
    /// </summary>
    public ScanType Type { get; init; } = ScanType.VisualStudio;

    /// <summary>
    /// Parses command line arguments into options.
    /// </summary>
    /// <param name="args">Arguments passed to the CLI.</param>
    /// <returns>The parsed options.</returns>
    public static CommandLineOptions Parse(string[] args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h"))
        {
            PrintUsage();
            Environment.Exit(0);
        }

        var root = Directory.GetCurrentDirectory();
        var thirdParty = new List<string>();
        var output = (string?)null;
        var log = (string?)null;
        var format = OutputFormat.Spdx;
        var scanType = ScanType.VisualStudio;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root":
                case "-r":
                    root = RequireValue(args, ref i);
                    break;
                case "--third-party":
                case "-t":
                    thirdParty.Add(RequireValue(args, ref i));
                    break;
                case "--output":
                case "-o":
                    output = RequireValue(args, ref i);
                    break;
                case "--format":
                    format = ParseFormat(RequireValue(args, ref i));
                    break;
                case "--type":
                    scanType = ParseScanType(RequireValue(args, ref i));
                    break;
                case "--log":
                    log = RequireValue(args, ref i);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'");
            }
        }

        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Root directory '{root}' does not exist");
        }

        var normalizedThirdParty = thirdParty
            .Select(Path.GetFullPath)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var outputPath = Path.GetFullPath(output ?? Path.Combine(root, "sbom-report.json"));
        var logPath = Path.GetFullPath(log ?? Path.Combine(root, "cppsbom.log"));

        return new CommandLineOptions
        {
            RootDirectory = root,
            ThirdPartyDirectories = normalizedThirdParty,
            OutputPath = outputPath,
            LogPath = logPath,
            Format = format,
            Type = scanType
        };
    }

    /// <summary>
    /// Reads a required value following a flag argument.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="index">The current argument index.</param>
    /// <returns>The next argument value.</returns>
    private static string RequireValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for '{args[index]}'");
        }

        index++;
        return args[index];
    }

    /// <summary>
    /// Writes usage information to the console.
    /// </summary>
    private static void PrintUsage()
    {
        const string text = """
Usage: cppsbom [--root <path>] [--third-party <path>]... [--output <file>] [--log <file>] [--format spdx|cyclonedx] [--type cmake|vs|visualstudio]

  --format spdx|cyclonedx   Output format (default: spdx)
  --type cmake|vs|visualstudio  Scan mode (default: visualstudio)
""";
        Console.WriteLine(text);
    }

    /// <summary>
    /// Converts a format value into an output format.
    /// </summary>
    /// <param name="value">The format argument.</param>
    /// <returns>The parsed output format.</returns>
    private static OutputFormat ParseFormat(string value)
    {
        if (string.Equals(value, "spdx", StringComparison.OrdinalIgnoreCase))
        {
            return OutputFormat.Spdx;
        }

        if (string.Equals(value, "cyclonedx", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "cdx", StringComparison.OrdinalIgnoreCase))
        {
            return OutputFormat.CycloneDx;
        }

        throw new ArgumentException($"Unknown format '{value}'. Expected 'spdx' or 'cyclonedx'.");
    }

    /// <summary>
    /// Converts a scan type value into a scan mode.
    /// </summary>
    /// <param name="value">The scan type argument.</param>
    /// <returns>The parsed scan type.</returns>
    private static ScanType ParseScanType(string value)
    {
        if (string.Equals(value, "vs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "visualstudio", StringComparison.OrdinalIgnoreCase))
        {
            return ScanType.VisualStudio;
        }

        if (string.Equals(value, "cmake", StringComparison.OrdinalIgnoreCase))
        {
            return ScanType.CMake;
        }

        throw new ArgumentException($"Unknown scan type '{value}'. Expected 'cmake', 'vs', or 'visualstudio'.");
    }
}
