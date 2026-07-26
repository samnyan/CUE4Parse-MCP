using System.ComponentModel;
using System.Text.Json;
using CUE4Parse.Mcp.Services;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;
using ModelContextProtocol.Server;
using NewtonsoftJsonSerializer = Newtonsoft.Json.JsonSerializer;
using NewtonsoftJsonConvert = Newtonsoft.Json.JsonConvert;

namespace CUE4Parse.Mcp.Tools;

[McpServerToolType]
public static class PackageTools
{
    private const int DefaultMaxDepth = 4;
    private const int DefaultMaxJsonBytes = 256 * 1024; // 256 KB

    [McpServerTool(Name = "get_package_summary"), Description(
        "Load a UE package and return its summary: export count, import count, export names, and package flags. " +
        "Does not fully deserialize exports — use get_object_summary or get_object_json for detailed object data.")]
    public static string GetPackageSummary(
        Cue4ParseSessionRegistry sessions,
        [Description("Package path (e.g. 'GameName/Content/Path/To/Asset.uasset' or '/Game/Path/To/Asset').")]
        string packagePath,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")]
        string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null)
            return Error("No session", "No provider session found. Call init_provider first.");

        string fixedPath;
        try
        {
            fixedPath = session.Provider.FixPath(packagePath);
        }
        catch
        {
            fixedPath = packagePath;
        }

        if (!session.Provider.TryLoadPackage(fixedPath, out var package))
        {
            // Try original path if FixPath didn't work
            if (!session.Provider.TryLoadPackage(packagePath, out package))
                return Error("Package not found", $"Could not load package at '{packagePath}'. Use search_assets to find valid paths.");
        }

        var exportSummaries = new List<object>();
        for (var i = 0; i < package.ExportsLazy.Length; i++)
        {
            try
            {
                var export = package.GetExport(i);
                if (export != null)
                {
                    exportSummaries.Add(new
                    {
                        index = i,
                        name = export.Name,
                        type = export.ExportType,
                        path = export.GetPathName()
                    });
                }
            }
            catch (Exception ex)
            {
                exportSummaries.Add(new
                {
                    index = i,
                    name = $"Export_{i}",
                    type = "Unknown",
                    error = ex.Message
                });
            }
        }

        var result = new
        {
            ok = true,
            packagePath = package.Name,
            exportCount = package.ExportMapLength,
            importCount = package.ImportMapLength,
            isFullyLoaded = package.IsFullyLoaded,
            packageFlags = package.Summary.PackageFlags.ToString(),
            isUnversioned = package.Summary.bUnversioned,
            exports = exportSummaries
        };

        return System.Text.Json.JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "get_exports"), Description(
        "List all exports in a package with their names, types, and indices. " +
        "Similar to get_package_summary but focused on the export list with pagination.")]
    public static string GetExports(
        Cue4ParseSessionRegistry sessions,
        [Description("Package path (e.g. 'GameName/Content/Path/To/Asset.uasset').")]
        string packagePath,
        [Description("Maximum number of exports to return. Default: 100.")]
        int? limit = null,
        [Description("Number of exports to skip. Default: 0.")]
        int? cursor = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")]
        string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null)
            return Error("No session", "No provider session found. Call init_provider first.");

        string fixedPath;
        try
        {
            fixedPath = session.Provider.FixPath(packagePath);
        }
        catch
        {
            fixedPath = packagePath;
        }

        if (!session.Provider.TryLoadPackage(fixedPath, out var package))
        {
            if (!session.Provider.TryLoadPackage(packagePath, out package))
                return Error("Package not found", $"Could not load package at '{packagePath}'.");
        }

        var maxLimit = Math.Clamp(limit ?? 100, 1, 500);
        var skip = Math.Max(0, cursor ?? 0);
        var totalExports = package.ExportsLazy.Length;

        var exports = new List<object>();
        for (var i = skip; i < Math.Min(skip + maxLimit, totalExports); i++)
        {
            try
            {
                var export = package.GetExport(i);
                exports.Add(new
                {
                    index = i,
                    name = export?.Name ?? $"Export_{i}",
                    type = export?.ExportType ?? "Unknown",
                    path = export?.GetPathName() ?? ""
                });
            }
            catch (Exception ex)
            {
                exports.Add(new
                {
                    index = i,
                    name = $"Export_{i}",
                    type = "Unknown",
                    error = ex.Message
                });
            }
        }

        var result = new
        {
            ok = true,
            packagePath = package.Name,
            exports = exports,
            nextCursor = skip + maxLimit < totalExports ? skip + maxLimit : (int?)null,
            totalExports
        };

        return System.Text.Json.JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "get_object_summary"), Description(
        "Load a specific UObject and return its type, name, outer, class, and a summary of its properties. " +
        "The objectPath should be the package path followed by '.' and the export name " +
        "(e.g. 'GameName/Content/Path/Asset.uasset.ExportName').")]
    public static string GetObjectSummary(
        Cue4ParseSessionRegistry sessions,
        [Description("Full object path: packagePath.exportName (e.g. 'GameName/Content/Path/Asset.uasset.MainObject').")]
        string objectPath,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")]
        string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null)
            return Error("No session", "No provider session found. Call init_provider first.");

        UObject? obj;
        try
        {
            obj = session.Provider.SafeLoadPackageObject(objectPath);
        }
        catch (Exception ex)
        {
            return Error("Load failed", ex.Message);
        }

        if (obj == null)
            return Error("Object not found", $"Could not load object at '{objectPath}'.");

        var properties = new List<object>();
        foreach (var prop in obj.Properties.Take(50))
        {
            try
            {
                properties.Add(new
                {
                    name = prop.Name.Text,
                    type = prop.PropertyType.Text,
                    arrayIndex = prop.ArrayIndex
                });
            }
            catch
            {
                properties.Add(new
                {
                    name = prop.Name.Text,
                    type = "Unknown",
                    arrayIndex = prop.ArrayIndex
                });
            }
        }

        var result = new
        {
            ok = true,
            objectPath = objectPath,
            name = obj.Name,
            type = obj.ExportType,
            fullName = obj.GetFullName(),
            pathName = obj.GetPathName(),
            outer = obj.Outer?.GetPathName(),
            @class = obj.Class?.Name.Text,
            flags = obj.Flags.ToString(),
            propertyCount = obj.Properties.Count,
            properties = properties
        };

        return System.Text.Json.JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "get_object_json"), Description(
        "Serialize a UObject to JSON with depth and size limits. " +
        "Returns the JSON representation of the object's properties. " +
        "The output is truncated if it exceeds maxBytes.")]
    public static string GetObjectJson(
        Cue4ParseSessionRegistry sessions,
        [Description("Full object path: packagePath.exportName.")]
        string objectPath,
        [Description("Maximum serialization depth. Default: 4.")]
        int? maxDepth = null,
        [Description("Maximum JSON output size in bytes. Default: 262144 (256 KB).")]
        int? maxBytes = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")]
        string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null)
            return Error("No session", "No provider session found. Call init_provider first.");

        UObject? obj;
        try
        {
            obj = session.Provider.SafeLoadPackageObject(objectPath);
        }
        catch (Exception ex)
        {
            return Error("Load failed", ex.Message);
        }

        if (obj == null)
            return Error("Object not found", $"Could not load object at '{objectPath}'.");

        var depth = Math.Clamp(maxDepth ?? DefaultMaxDepth, 1, 20);
        var maxOutputBytes = Math.Clamp(maxBytes ?? DefaultMaxJsonBytes, 1024, 4 * 1024 * 1024);

        string json;
        try
        {
            var settings = new Newtonsoft.Json.JsonSerializerSettings
            {
                Formatting = Newtonsoft.Json.Formatting.Indented,
                MaxDepth = depth,
                ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
                Error = (_, args) => args.ErrorContext.Handled = true
            };

            json = NewtonsoftJsonConvert.SerializeObject(obj, settings);
        }
        catch (Exception ex)
        {
            return Error("Serialization failed", ex.Message);
        }

        var truncated = false;
        if (json.Length > maxOutputBytes)
        {
            json = json[..maxOutputBytes] + "\n... [truncated]";
            truncated = true;
        }

        var result = new
        {
            ok = true,
            objectPath = objectPath,
            name = obj.Name,
            type = obj.ExportType,
            truncated,
            jsonLength = json.Length,
            json
        };

        return System.Text.Json.JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    private static string Error(string code, string message) =>
        System.Text.Json.JsonSerializer.Serialize(new { ok = false, errorCode = code, message }, McpJsonOptions.Default);
}
