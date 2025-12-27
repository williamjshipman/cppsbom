using System.Runtime.Versioning;
using Microsoft.Win32;
using Serilog;

namespace CppSbom;

internal interface IComResolver
{
    ComMetadata? ResolveFromProgId(string progId);
    ComMetadata? ResolveFromClsid(string clsid);
}

[SupportedOSPlatform("windows")]
internal sealed class ComRegistryResolver : IComResolver
{
    private static readonly RegistryView[] Views = { RegistryView.Registry64, RegistryView.Registry32 };
    private readonly ILogger _logger;

    public ComRegistryResolver(ILogger logger)
    {
        _logger = logger;
    }

    public ComMetadata? ResolveFromProgId(string progId)
    {
        foreach (var view in Views)
        {
            using var baseKey = OpenClassesRoot(view);
            using var progKey = baseKey?.OpenSubKey(progId);
            if (progKey is null)
            {
                continue;
            }

            var description = progKey.GetValue(null) as string;
            var clsid = progKey.GetValue("CLSID") as string;
            if (string.IsNullOrWhiteSpace(clsid))
            {
                return new ComMetadata
                {
                    ProgId = progId,
                    Description = description,
                    RegistryView = view.ToString()
                };
            }

            var metadata = ResolveFromClsid(clsid, view) ?? new ComMetadata();
            metadata.ProgId ??= progId;
            metadata.Description ??= description;
            metadata.RegistryView = view.ToString();
            return metadata;
        }

        _logger.Debug("ProgID {ProgId} not found in COM registry", progId);
        return null;
    }

    public ComMetadata? ResolveFromClsid(string clsid)
    {
        foreach (var view in Views)
        {
            var meta = ResolveFromClsid(clsid, view);
            if (meta is not null)
            {
                return meta;
            }
        }

        _logger.Debug("CLSID {Clsid} not found in COM registry", clsid);
        return null;
    }

    private ComMetadata? ResolveFromClsid(string clsid, RegistryView view)
    {
        var normalized = NormalizeClsid(clsid);
        using var baseKey = OpenClassesRoot(view);
        using var clsidKey = baseKey?.OpenSubKey($"CLSID\\{normalized}");
        if (clsidKey is null)
        {
            return null;
        }

        var description = clsidKey.GetValue(null) as string;
        using var inproc = clsidKey.OpenSubKey("InprocServer32");
        var serverPath = inproc?.GetValue(null) as string;
        var threading = inproc?.GetValue("ThreadingModel") as string;
        using var progIdKey = clsidKey.OpenSubKey("ProgID");
        var progId = progIdKey?.GetValue(null) as string;

        return new ComMetadata
        {
            Clsid = normalized,
            Description = description,
            InprocServer = serverPath,
            ThreadingModel = threading,
            ProgId = progId,
            RegistryView = view.ToString()
        };
    }

    private static string NormalizeClsid(string clsid)
    {
        var trimmed = clsid.Trim();
        if (!trimmed.StartsWith("{"))
        {
            trimmed = "{" + trimmed;
        }
        if (!trimmed.EndsWith("}"))
        {
            trimmed += "}";
        }
        return trimmed;
    }

    private static RegistryKey? OpenClassesRoot(RegistryView view)
    {
        try
        {
            return RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record ComMetadata
{
    public string? ProgId { get; set; }
    public string? Clsid { get; set; }
    public string? Description { get; set; }
    public string? InprocServer { get; set; }
    public string? ThreadingModel { get; set; }
    public string? RegistryView { get; set; }
}

internal sealed class NullComResolver : IComResolver
{
    private readonly ILogger _logger;

    public NullComResolver(ILogger logger)
    {
        _logger = logger;
    }

    public ComMetadata? ResolveFromProgId(string progId)
    {
        _logger.Debug("Skipped COM ProgID lookup for {ProgId} (non-Windows runtime)", progId);
        return null;
    }

    public ComMetadata? ResolveFromClsid(string clsid)
    {
        _logger.Debug("Skipped COM CLSID lookup for {Clsid} (non-Windows runtime)", clsid);
        return null;
    }
}
