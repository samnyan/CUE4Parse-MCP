using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CUE4Parse.Mcp.Dtos;
using CUE4Parse.Mcp.Services;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;
using ModelContextProtocol.Server;
using NewtonsoftJsonConvert = Newtonsoft.Json.JsonConvert;

namespace CUE4Parse.Mcp.Tools;

[McpServerToolType]
public static class PackageTools
{
    private const int DefaultMaxDepth = 4;
    private const int DefaultMaxJsonBytes = 256 * 1024;

    [McpServerTool(Name = "get_package_summary"), Description(
        "Load a UE package and return bounded export summaries, import count, package flags, and version metadata. " +
        "Use get_object_summary or get_object_json for detailed object data.")]
    public static string GetPackageSummary(
        Cue4ParseSessionRegistry sessions,
        [Description("Package path, such as 'GameName/Content/Path/Asset.uasset' or '/Game/Path/Asset'.")] string packagePath,
        [Description("Maximum export summaries. Default: 100, max: 500.")] int? exportLimit = null,
        [Description("Session ID from init_provider. If omitted, the most recent session.")] string? sessionId = null)
    {
        if (!TryGetPackage(sessions, packagePath, sessionId, out var package, out var error)) return error!;
        var maxExports = Math.Clamp(exportLimit ?? 100, 1, 500);
        var exports = new List<ExportSummaryDto>();
        for (var i = 0; i < Math.Min(package!.ExportsLazy.Length, maxExports); i++)
            exports.Add(ReadExport(package, i));

        return JsonSerializer.Serialize(new
        {
            ok = true,
            packagePath = package.Name,
            exportCount = package.ExportMapLength,
            returnedExports = exports.Count,
            exportsTruncated = exports.Count < package.ExportMapLength,
            importCount = package.ImportMapLength,
            isFullyLoaded = package.IsFullyLoaded,
            packageFlags = package.Summary.PackageFlags.ToString(),
            isUnversioned = package.Summary.bUnversioned,
            exports
        }, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "get_exports"), Description(
        "List package exports with names, types, paths, and indices using bounded pagination.")]
    public static string GetExports(
        Cue4ParseSessionRegistry sessions,
        [Description("Package path, such as 'GameName/Content/Path/Asset.uasset'.")] string packagePath,
        [Description("Maximum exports to return. Default: 100, max: 500.")] int? limit = null,
        [Description("Number of exports to skip. Default: 0.")] int? cursor = null,
        [Description("Session ID from init_provider. If omitted, the most recent session.")] string? sessionId = null)
    {
        if (!TryGetPackage(sessions, packagePath, sessionId, out var package, out var error)) return error!;
        var maxLimit = Math.Clamp(limit ?? 100, 1, 500);
        var skip = Math.Max(0, cursor ?? 0);
        var total = package!.ExportsLazy.Length;
        var exports = Enumerable.Range(skip, Math.Max(0, Math.Min(maxLimit, total - skip)))
            .Select(index => ReadExport(package, index)).ToList();
        return JsonSerializer.Serialize(new
        {
            ok = true,
            packagePath = package.Name,
            exports,
            nextCursor = skip + maxLimit < total ? skip + maxLimit : (int?)null,
            totalExports = total
        }, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "get_object_summary"), Description(
        "Load one or more UObjects and return metadata plus a bounded property summary. Supports comma-separated batch paths.")]
    public static string GetObjectSummary(
        Cue4ParseSessionRegistry sessions,
        [Description("Full object path or comma-separated object paths.")] string objectPath,
        [Description("Maximum properties per object. Default: 50, max: 500.")] int? maxProperties = null,
        [Description("Session ID from init_provider. If omitted, the most recent session.")] string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null) return Error("no_session", "No provider session found. Call init_provider first.");
        var limit = Math.Clamp(maxProperties ?? 50, 1, 500);
        var results = new List<ObjectSummaryDto>();
        foreach (var path in SplitPaths(objectPath))
        {
            try
            {
                if (!PackageObjectResolver.TryResolve(session, path, null, null, null, out var target, out var resolveError))
                {
                    results.Add(new ObjectSummaryDto { ObjectPath = path, Error = resolveError });
                    continue;
                }
                var obj = target!.Object;
                results.Add(new ObjectSummaryDto
                {
                    ObjectPath = path,
                    Name = obj.Name,
                    Type = obj.ExportType,
                    FullName = obj.GetFullName(),
                    PathName = obj.GetPathName(),
                    Outer = obj.Outer?.GetPathName(),
                    Class = obj.Class?.Name.Text,
                    Flags = obj.Flags.ToString(),
                    PropertyCount = obj.Properties.Count,
                    Properties = obj.Properties.Take(limit).Select(p => new PropertySummaryDto { Name = p.Name.Text, Type = p.PropertyType.Text, ArrayIndex = p.ArrayIndex }).ToList()
                });
            }
            catch (Exception ex) { results.Add(new ObjectSummaryDto { ObjectPath = path, Error = ex.Message }); }
        }
        return JsonSerializer.Serialize(new { ok = true, queried = results.Count, objects = results }, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "get_object_json"), Description(
        "Serialize one or more UObjects to JSON with depth and size limits. Oversized objects return truncated: true with null JSON, never partial invalid JSON.")]
    public static string GetObjectJson(
        Cue4ParseSessionRegistry sessions,
        [Description("Full object path or comma-separated object paths.")] string objectPath,
        [Description("Maximum serialization depth. Default: 4.")] int? maxDepth = null,
        [Description("Maximum JSON bytes per object. Default: 262144, max: 4194304.")] int? maxBytes = null,
        [Description("Session ID from init_provider. If omitted, the most recent session.")] string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null) return Error("no_session", "No provider session found. Call init_provider first.");
        var depth = Math.Clamp(maxDepth ?? DefaultMaxDepth, 1, 20);
        var outputLimit = Math.Clamp(maxBytes ?? DefaultMaxJsonBytes, 1024, 4 * 1024 * 1024);
        var results = new List<ObjectJsonResultDto>();
        foreach (var path in SplitPaths(objectPath))
        {
            try
            {
                if (!PackageObjectResolver.TryResolve(session, path, null, null, null, out var target, out var resolveError))
                {
                    results.Add(new ObjectJsonResultDto { ObjectPath = path, Error = resolveError });
                    continue;
                }
                var obj = target!.Object;
                var json = NewtonsoftJsonConvert.SerializeObject(obj, CreateSettings(depth));
                var parsed = JsonNode.Parse(json);
                results.Add(new ObjectJsonResultDto
                {
                    ObjectPath = path,
                    Name = obj.Name,
                    Type = obj.ExportType,
                    Truncated = json.Length > outputLimit,
                    TotalJsonLength = json.Length,
                    ReturnedJsonLength = json.Length > outputLimit ? 0 : json.Length,
                    Json = json.Length > outputLimit ? null : parsed
                });
            }
            catch (Exception ex) { results.Add(new ObjectJsonResultDto { ObjectPath = path, Error = ex.Message }); }
        }
        return JsonSerializer.Serialize(new { ok = true, queried = results.Count, objects = results }, McpJsonOptions.Default);
    }

    private static ExportSummaryDto ReadExport(CUE4Parse.UE4.Assets.IPackage package, int index)
    {
        try
        {
            var export = package.GetExport(index);
            return new ExportSummaryDto { Index = index, Name = export?.Name ?? $"Export_{index}", Type = export?.ExportType ?? "Unknown", Path = export?.GetPathName() ?? "" };
        }
        catch (Exception ex) { return new ExportSummaryDto { Index = index, Name = $"Export_{index}", Type = "Unknown", Error = ex.Message }; }
    }

    private static bool TryGetPackage(Cue4ParseSessionRegistry sessions, string packagePath, string? sessionId, out CUE4Parse.UE4.Assets.IPackage? package, out string? error)
    {
        package = null;
        var session = sessions.GetSession(sessionId);
        if (session == null) { error = Error("no_session", "No provider session found. Call init_provider first."); return false; }
        if (session.TryLoadPackage(packagePath, out package, out _)) { error = null; return true; }
        error = Error("package_not_found", $"Could not load package at '{packagePath}'. Use search_assets to find valid paths.");
        return false;
    }

    private static List<string> SplitPaths(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static Newtonsoft.Json.JsonSerializerSettings CreateSettings(int depth) => new()
    {
        Formatting = Newtonsoft.Json.Formatting.Indented,
        MaxDepth = depth,
        ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
        Error = (_, args) => args.ErrorContext.Handled = true
    };

    private static string Error(string code, string message) => JsonSerializer.Serialize(new { ok = false, errorCode = code, message }, McpJsonOptions.Default);
}
