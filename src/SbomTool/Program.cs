using Serilog;

namespace CppSbom;

internal static class Program
{
    public static int Main(string[] args)
    {
        var options = CommandLineOptions.Parse(args);
        ConfigureLogging(options);
        try
        {
            Log.Information("cppsbom starting in {Root}", options.RootDirectory);
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
}
