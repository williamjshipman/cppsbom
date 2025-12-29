using System.Runtime.Versioning;
using Microsoft.Win32;
using Serilog;

namespace CppSbom;

/// <summary>
/// Resolves COM metadata from ProgID or CLSID values.
/// </summary>
internal interface IComResolver
{
    /// <summary>
    /// Resolves COM metadata for a ProgID string.
    /// </summary>
    /// <param name="progId">ProgID to resolve.</param>
    /// <returns>Resolved COM metadata or null.</returns>
    ComMetadata? ResolveFromProgId(string progId);
    /// <summary>
    /// Resolves COM metadata for a CLSID string.
    /// </summary>
    /// <param name="clsid">CLSID to resolve.</param>
    /// <returns>Resolved COM metadata or null.</returns>
    ComMetadata? ResolveFromClsid(string clsid);
}

[SupportedOSPlatform("windows")]
/// <summary>
/// Resolves COM metadata from the Windows registry.
/// </summary>
internal sealed class ComRegistryResolver : IComResolver
{
    /// <summary>
    /// Registry views to query for COM metadata.
    /// </summary>
    private static readonly RegistryView[] Views = { RegistryView.Registry64, RegistryView.Registry32 };
    /// <summary>
    /// Logger used for diagnostics.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new resolver with the provided logger.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    public ComRegistryResolver(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Resolves COM metadata for a ProgID by querying registry views.
    /// </summary>
    /// <param name="progId">ProgID to resolve.</param>
    /// <returns>Resolved COM metadata or null.</returns>
    public ComMetadata? ResolveFromProgId(string progId)
    {
        foreach (var view in Views)
        {
            try
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
                if (metadata is not null)
                {
                    metadata.ProgId ??= progId;
                    metadata.Description ??= description;
                    metadata.RegistryView = view.ToString();
                }
                return metadata;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error resolving ProgID {ProgId} in {RegistryView}", progId, view);
            }
        }

        _logger.Debug("ProgID {ProgId} not found in COM registry", progId);
        return null;
    }

    /// <summary>
    /// Resolves COM metadata for a CLSID by querying registry views.
    /// </summary>
    /// <param name="clsid">CLSID to resolve.</param>
    /// <returns>Resolved COM metadata or null.</returns>
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

    /// <summary>
    /// Resolves COM metadata for a CLSID within a specific registry view.
    /// </summary>
    /// <param name="clsid">CLSID to resolve.</param>
    /// <param name="view">Registry view to query.</param>
    /// <returns>Resolved COM metadata or null.</returns>
    private ComMetadata? ResolveFromClsid(string clsid, RegistryView view)
    {
        try
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
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error resolving CLSID {Clsid} in {RegistryView}", clsid, view);
            return null;
        }
    }

    /// <summary>
    /// Normalizes a CLSID string to include braces.
    /// </summary>
    /// <param name="clsid">CLSID string.</param>
    /// <returns>Normalized CLSID.</returns>
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

    /// <summary>
    /// Opens the classes root hive for a registry view.
    /// </summary>
    /// <param name="view">Registry view to open.</param>
    /// <returns>Registry key or null if unavailable.</returns>
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

/// <summary>
/// Holds COM metadata resolved from the registry.
/// </summary>
internal sealed record ComMetadata
{
    /// <summary>
    /// Gets or sets the ProgID value.
    /// </summary>
    public string? ProgId { get; set; }
    /// <summary>
    /// Gets or sets the CLSID value.
    /// </summary>
    public string? Clsid { get; set; }
    /// <summary>
    /// Gets or sets the COM description.
    /// </summary>
    public string? Description { get; set; }
    /// <summary>
    /// Gets or sets the in-proc server path.
    /// </summary>
    public string? InprocServer { get; set; }
    /// <summary>
    /// Gets or sets the threading model.
    /// </summary>
    public string? ThreadingModel { get; set; }
    /// <summary>
    /// Gets or sets the registry view used.
    /// </summary>
    public string? RegistryView { get; set; }
}

/// <summary>
/// No-op COM resolver used on non-Windows platforms.
/// </summary>
internal sealed class NullComResolver : IComResolver
{
    /// <summary>
    /// Logger used for diagnostics.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new no-op resolver.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    public NullComResolver(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Logs and skips ProgID resolution.
    /// </summary>
    /// <param name="progId">ProgID to resolve.</param>
    /// <returns>Always null.</returns>
    public ComMetadata? ResolveFromProgId(string progId)
    {
        _logger.Debug("Skipped COM ProgID lookup for {ProgId} (non-Windows runtime)", progId);
        return null;
    }

    /// <summary>
    /// Logs and skips CLSID resolution.
    /// </summary>
    /// <param name="clsid">CLSID to resolve.</param>
    /// <returns>Always null.</returns>
    public ComMetadata? ResolveFromClsid(string clsid)
    {
        _logger.Debug("Skipped COM CLSID lookup for {Clsid} (non-Windows runtime)", clsid);
        return null;
    }
}
