using System.ComponentModel;
using System.Text.Json;
using CUE4Parse.Mcp.Dtos;
using CUE4Parse.Mcp.Services;
using ModelContextProtocol.Server;

namespace CUE4Parse.Mcp.Tools;

[McpServerToolType]
public static class LogicObjectTools
{
    [McpServerTool(Name = "get_package_export"), Description(
        "Load one package export by zero-based index, including nested exports that cannot be addressed through normal object paths. Returns metadata, bounded property summaries, and optional bounded JSON.")]
    public static string GetPackageExport(
        Cue4ParseSessionRegistry sessions,
        [Description("Package path such as EnderMagnolia/Content/Path/Asset.uasset or /Game/Path/Asset.")] string packagePath,
        [Description("Zero-based export index returned by get_exports.")] int exportIndex,
        [Description("Include bounded full export JSON. Default: false.")] bool includeJson = false,
        [Description("Maximum properties to summarize. Default: 100, max: 500.")] int? maxProperties = null,
        [Description("Maximum JSON serialization depth. Default: 6, max: 20.")] int? maxDepth = null,
        [Description("Maximum JSON bytes. Default: 262144, max: 4194304.")] int? maxBytes = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")] string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null) return Error("no_session", "No provider session found. Call init_provider first.");
        if (exportIndex < 0) return Error("invalid_export_index", "exportIndex must be zero or greater.");
        if (!PackageObjectResolver.TryResolve(session, null, packagePath, exportIndex, null, out var target, out var resolveError))
            return Error("export_not_found", resolveError!);

        var obj = target!.Object;
        var result = new PackageExportResultDto
        {
            PackagePath = target.Package.Name,
            ExportIndex = target.ExportIndex ?? exportIndex,
            ObjectPath = target.ObjectPath,
            Name = obj.Name,
            Type = obj.ExportType,
            Outer = obj.Outer?.GetPathName(),
            Class = obj.Class?.Name.Text,
            PropertyCount = obj.Properties.Count,
            Properties = obj.Properties.Take(Math.Clamp(maxProperties ?? 100, 1, 500))
                .Select(property => new PropertySummaryDto
                {
                    Name = property.Name.Text,
                    Type = property.PropertyType.Text,
                    ArrayIndex = property.ArrayIndex
                }).ToList()
        };

        if (includeJson)
        {
            var serialized = BoundedJsonSerializer.Serialize(obj,
                Math.Clamp(maxDepth ?? 6, 1, 20),
                Math.Clamp(maxBytes ?? 256 * 1024, 1024, 4 * 1024 * 1024));
            result.Json = serialized.Json;
            result.Truncated = serialized.Truncated;
            result.TotalJsonBytes = serialized.TotalBytes;
            result.ReturnedJsonBytes = serialized.ReturnedBytes;
            result.Error = serialized.Error;
        }

        return JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "get_property_path"), Description(
        "Resolve a dotted/indexed property path on a top-level object or package export. Supports paths such as Children[0].ChildTask and Children[*].Decorators[0].")]
    public static string GetPropertyPath(
        Cue4ParseSessionRegistry sessions,
        [Description("Dotted property path with optional numeric or wildcard array selectors.")] string propertyPath,
        [Description("Top-level or nested object path. Use this or packagePath/exportIndex.")] string? objectPath = null,
        [Description("Package path when selecting by export index. Use with exportIndex instead of objectPath.")] string? packagePath = null,
        [Description("Zero-based export index inside packagePath.")] int? exportIndex = null,
        [Description("Maximum wildcard array items. Default: 10, max: 100.")] int? sampleItems = null,
        [Description("Maximum JSON serialization depth per value. Default: 6, max: 20.")] int? maxDepth = null,
        [Description("Maximum JSON bytes per value. Default: 65536, max: 1048576.")] int? maxBytesPerValue = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")] string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null) return Error("no_session", "No provider session found. Call init_provider first.");
        if (!PackageObjectResolver.TryResolve(session, objectPath, packagePath, exportIndex, null, out var target, out var resolveError))
            return Error("object_not_found", resolveError!);
        if (!PropertyPathResolver.TryResolve(target!.Object, propertyPath, Math.Clamp(sampleItems ?? 10, 1, 100), out var values, out var pathError))
            return Error("property_path_not_found", pathError!);

        var depth = Math.Clamp(maxDepth ?? 6, 1, 20);
        var bytes = Math.Clamp(maxBytesPerValue ?? 64 * 1024, 1024, 1024 * 1024);
        var dtoValues = values.Select(value =>
        {
            var serialized = BoundedJsonSerializer.Serialize(value.Value, depth, bytes);
            return new PropertyPathValueDto
            {
                ResolvedPath = value.Path,
                Json = serialized.Json,
                Truncated = serialized.Truncated,
                TotalJsonBytes = serialized.TotalBytes,
                ReturnedJsonBytes = serialized.ReturnedBytes,
                Error = serialized.Error
            };
        }).ToList();

        return JsonSerializer.Serialize(new PropertyPathResultDto
        {
            ObjectPath = target.ObjectPath,
            ExportIndex = target.ExportIndex,
            PropertyPath = propertyPath,
            TotalValues = dtoValues.Count,
            Values = dtoValues
        }, McpJsonOptions.Default);
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { ok = false, errorCode = code, message }, McpJsonOptions.Default);
}
