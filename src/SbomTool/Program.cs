using Serilog;

namespace CppSbom;

/// <summary>
/// Entry point for the cppsbom CLI.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Executes the CLI workflow and returns an exit code.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Zero on success, non-zero on failure.</returns>
    public static int Main(string[] args)
    {
        var options = CommandLineOptions.Parse(args);
        ConfigureLogging(options);
        try
        {
            Log.Information("cppsbom starting in {Root}", options.RootDirectory);
            RegisterCodePagesEncodingProvider();
            var generator = new SbomGenerator(options, Log.Logger);
            generator.Run();
            Log.Information("cppsbom completed successfully");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "SBOM generation failed");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Configures the logger for console and rolling file output.
    /// </summary>
    /// <param name="options">Parsed command line options.</param>
    private static void ConfigureLogging(CommandLineOptions options)
    {
        var logPath = options.LogPath;
        var logDir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();
    }

    /// <summary>
    /// Registers the code pages encoding provider to support non-standard XML
    /// encodings like Windows-1252 used in some vcxproj files.
    /// </summary>
    private static void RegisterCodePagesEncodingProvider()
    {
        Log.Information("Registering code pages encoding provider for non-standard XML encodings.");
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to register code pages encoding provider.");
        }
    }
}
