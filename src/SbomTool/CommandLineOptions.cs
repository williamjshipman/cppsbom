namespace CppSbom;

internal sealed record CommandLineOptions
{
    public string RootDirectory { get; init; } = Directory.GetCurrentDirectory();
    public IReadOnlyList<string> ThirdPartyDirectories { get; init; } = Array.Empty<string>();
    public string OutputPath { get; init; } = Path.Combine(Directory.GetCurrentDirectory(), "sbom-report.json");
    public string LogPath { get; init; } = Path.Combine(Directory.GetCurrentDirectory(), "cppsbom.log");

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
            LogPath = logPath
        };
    }

    private static string RequireValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for '{args[index]}'");
        }

        index++;
        return args[index];
    }

    private static void PrintUsage()
    {
        const string text = "Usage: cppsbom [--root <path>] [--third-party <path>]... [--output <file>] [--log <file>]";
        Console.WriteLine(text);
    }
}
