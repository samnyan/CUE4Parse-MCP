using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CUE4Parse.Mcp.Dtos;
using CUE4Parse.Mcp.Services;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;
using ModelContextProtocol.Server;
using NewtonsoftJsonConvert = Newtonsoft.Json.JsonConvert;

namespace CUE4Parse.Mcp.Tools;

[McpServerToolType]
public static class ObjectTools
{
    private const int DefaultMaxDepth = 4;
    private const int DefaultPropertyBytes = 16 * 1024;
    private const int DefaultMaxProperties = 50;
    private const int DefaultMaxReferences = 200;

    [McpServerTool(Name = "get_object_preview"), Description(
        "Preview a UObject without serializing the entire object. Returns each top-level property independently; oversized properties are marked truncated with null JSON so other properties remain visible.")]
    public static string GetObjectPreview(
        Cue4ParseSessionRegistry sessions,
        [Description("Full object path or comma-separated object paths.")] string objectPath,
        [Description("Maximum top-level properties per object. Default: 50, max: 500.")] int? maxProperties = null,
        [Description("Maximum JSON bytes per property. Default: 16384, max: 262144.")] int? maxBytesPerProperty = null,
        [Description("Maximum serialization depth per property. Default: 4.")] int? maxDepth = null,
        [Description("Number of array items to sample per property. Default: 5, max: 50.")] int? sampleItems = null,
        [Description("Session ID from init_provider. If omitted, the most recent session.")] string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null) return Error("no_session", "No provider session found. Call init_provider first.");

        var propertyLimit = Math.Clamp(maxProperties ?? DefaultMaxProperties, 1, 500);
        var sampleLimit = Math.Clamp(sampleItems ?? 5, 1, 50);
        var bytesLimit = Math.Clamp(maxBytesPerProperty ?? DefaultPropertyBytes, 1024, 256 * 1024);
        var depth = Math.Clamp(maxDepth ?? DefaultMaxDepth, 1, 20);
        var results = new List<ObjectPreviewResultDto>();

        foreach (var path in SplitPaths(objectPath))
        {
            try
            {
                var obj = session.Provider.SafeLoadPackageObject(path);
                if (obj == null)
                {
                    results.Add(new ObjectPreviewResultDto { ObjectPath = path, Error = $"Could not load object at '{path}'." });
                    continue;
                }

                var properties = obj.Properties.Take(propertyLimit)
                    .Select(property => SerializeProperty(property, bytesLimit, depth, sampleLimit))
                    .ToList();
                results.Add(new ObjectPreviewResultDto
                {
                    ObjectPath = path,
                    Name = obj.Name,
                    Type = obj.ExportType,
                    PropertyCount = obj.Properties.Count,
                    ReturnedProperties = properties.Count,
                    PropertiesTruncated = properties.Count < obj.Properties.Count,
                    Properties = properties
                });
            }
            catch (Exception ex)
            {
                results.Add(new ObjectPreviewResultDto { ObjectPath = path, Error = ex.Message });
            }
        }

        return JsonSerializer.Serialize(new { ok = true, queried = results.Count, objects = results }, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "get_object_properties"), Description(
        "Read only selected UObject properties by name. This is the preferred precise alternative to get_object_json for large objects.")]
    public static string GetObjectProperties(
        Cue4ParseSessionRegistry sessions,
        [Description("Full object path.")] string objectPath,
        [Description("Comma-separated property names to return.")] string properties,
        [Description("Maximum JSON bytes per property. Default: 16384, max: 262144.")] int? maxBytesPerProperty = null,
        [Description("Maximum serialization depth per property. Default: 4.")] int? maxDepth = null,
        [Description("Number of array items to sample per property. Default: 5, max: 50.")] int? sampleItems = null,
        [Description("Session ID from init_provider. If omitted, the most recent session.")] string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null) return Error("no_session", "No provider session found. Call init_provider first.");
        var sampleLimit = Math.Clamp(sampleItems ?? 5, 1, 50);
        var names = SplitPaths(properties);
        if (names.Count == 0) return Error("invalid_properties", "At least one property name is required.");

        try
        {
            var obj = session.Provider.SafeLoadPackageObject(objectPath);
            if (obj == null) return Error("object_not_found", $"Could not load object at '{objectPath}'.");
            var requested = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            var matchedProperties = obj.Properties
                .Where(property => requested.Contains(property.Name.Text))
                .ToList();
            var values = matchedProperties
                .Select(property => SerializeProperty(property,
                    Math.Clamp(maxBytesPerProperty ?? DefaultPropertyBytes, 1024, 256 * 1024),
                    Math.Clamp(maxDepth ?? DefaultMaxDepth, 1, 20),
                    sampleLimit))
                .ToList();
            var matchedNames = matchedProperties.Select(property => property.Name.Text)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return JsonSerializer.Serialize(new ObjectPropertiesResultDto
            {
                ObjectPath = objectPath,
                Name = obj.Name,
                Type = obj.ExportType,
                RequestedProperties = names.Count,
                ReturnedProperties = values.Count,
                MissingProperties = names.Where(name => !matchedNames.Contains(name)).ToList(),
                Properties = values
            }, McpJsonOptions.Default);
        }
        catch (Exception ex)
        {
            return Error("load_failed", ex.Message);
        }
    }

    [McpServerTool(Name = "get_object_references"), Description(
        "Find hard and soft object references exposed by one UObject's metadata and property values. Results are bounded and deduplicated.")]
    public static string GetObjectReferences(
        Cue4ParseSessionRegistry sessions,
        [Description("Full object path.")] string objectPath,
        [Description("Maximum references to return. Default: 200, max: 1000.")] int? limit = null,
        [Description("Session ID from init_provider. If omitted, the most recent session.")] string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null) return Error("no_session", "No provider session found. Call init_provider first.");
        try
        {
            var obj = session.Provider.SafeLoadPackageObject(objectPath);
            if (obj == null) return Error("object_not_found", $"Could not load object at '{objectPath}'.");
            var referenceLimit = Math.Clamp(limit ?? DefaultMaxReferences, 1, 1000);
            var collected = CollectReferences(obj, referenceLimit);
            return JsonSerializer.Serialize(new ObjectReferencesResultDto
            {
                ObjectPath = objectPath,
                References = collected.References,
                Truncated = collected.HasMore
            }, McpJsonOptions.Default);
        }
        catch (Exception ex)
        {
            return Error("reference_scan_failed", ex.Message);
        }
    }

    [McpServerTool(Name = "find_references"), Description(
        "Scan mounted UE packages for objects referencing a target path. This can be expensive; always constrain packagePrefix, maxPackages, and limit for large games.")]
    public static string FindReferences(
        Cue4ParseSessionRegistry sessions,
        [Description("Target asset/object path or distinctive path substring.")] string targetPath,
        [Description("Optional virtual path prefix restricting packages to scan.")] string? packagePrefix = null,
        [Description("Maximum packages to scan. Default: 100, max: 1000.")] int? maxPackages = null,
        [Description("Maximum exports to inspect per package. Default: 500, max: 5000.")] int? maxExportsPerPackage = null,
        [Description("Maximum matches to return. Default: 100, max: 500.")] int? limit = null,
        [Description("Number of matching packages to skip. Default: 0.")] int? cursor = null,
        [Description("Maximum scan duration in milliseconds. Default: 30000, max: 300000.")] int? timeoutMs = null,
        CancellationToken cancellationToken = default,
        [Description("Session ID from init_provider. If omitted, the most recent session.")] string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null) return Error("no_session", "No provider session found. Call init_provider first.");
        if (string.IsNullOrWhiteSpace(targetPath)) return Error("invalid_target", "targetPath is required.");

        var packageLimit = Math.Clamp(maxPackages ?? 100, 1, 1000);
        var exportLimit = Math.Clamp(maxExportsPerPackage ?? 500, 1, 5000);
        var resultLimit = Math.Clamp(limit ?? 100, 1, 500);
        var skip = Math.Max(0, cursor ?? 0);
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs ?? 30000, 1000, 300000));
        var stopwatch = Stopwatch.StartNew();
        var failedPackages = 0;
        var scannedPackages = 0;
        var scanErrors = new List<string>();
        var cancelled = false;
        var packages = session.Provider.Files
            .Where(entry => entry.Value.IsUePackage)
            .Where(entry => string.IsNullOrEmpty(packagePrefix) || entry.Key.StartsWith(packagePrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Take(packageLimit)
            .ToList();
        var matches = new List<FindReferencesMatchDto>();

        foreach (var entry in packages)
        {
            if (cancellationToken.IsCancellationRequested || stopwatch.Elapsed >= timeout)
            {
                cancelled = true;
                break;
            }
            try
            {
                scannedPackages++;
                if (!session.Provider.TryLoadPackage(entry.Key, out var package))
                {
                    failedPackages++;
                    scanErrors.Add($"{entry.Key}: package_load_failed");
                    continue;
                }
                for (var i = 0; i < Math.Min(package.ExportsLazy.Length, exportLimit); i++)
                {
                    if (cancellationToken.IsCancellationRequested || stopwatch.Elapsed >= timeout)
                    {
                        cancelled = true;
                        break;
                    }
                    var obj = package.GetExport(i);
                    if (obj == null) continue;
                    if (IsTargetMatch(entry.Key, targetPath) || IsTargetMatch(obj.GetPathName(), targetPath))
                        continue;
                    var references = CollectReferences(obj, 1000).References
                        .Where(reference => IsTargetMatch(reference.Path, targetPath))
                        .Select(reference => reference.Path)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (references.Count > 0)
                    {
                        matches.Add(new FindReferencesMatchDto
                        {
                            PackagePath = entry.Key,
                            ObjectPath = obj.GetPathName(),
                            References = references
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                failedPackages++;
                if (scanErrors.Count < 100) scanErrors.Add($"{entry.Key}: {ex.Message}");
            }
        }

        var totalMatches = matches.Count;
        return JsonSerializer.Serialize(new FindReferencesResultDto
        {
            TargetPath = targetPath,
            ScannedPackages = scannedPackages,
            FailedPackages = failedPackages,
            ScanErrors = scanErrors,
            Cancelled = cancelled,
            TotalMatches = totalMatches,
            Matches = matches.Skip(skip).Take(resultLimit).ToList(),
            NextCursor = !cancelled && skip + resultLimit < totalMatches ? skip + resultLimit : null
        }, McpJsonOptions.Default);
    }

    private static List<string> SplitPaths(string value) => value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();

    private static ObjectPropertyValueDto SerializeProperty(FPropertyTag property, int maxBytes, int maxDepth, int sampleItems)
    {
        var result = new ObjectPropertyValueDto
        {
            Name = property.Name.Text,
            Type = property.PropertyType.Text,
            ArrayIndex = property.ArrayIndex
        };
        try
        {
            var value = property.Tag?.GetValue<object>();
            var enumerable = value switch
            {
                UScriptArray scriptArray => scriptArray.Properties,
                UScriptSet scriptSet => scriptSet.Properties,
                FPropertyTagType propertyType when propertyType.GenericValue is IEnumerable nested => nested,
                IEnumerable direct when value is not string => direct,
                _ => null
            };
            if (enumerable != null)
            {
                result.Json = BuildCollectionPreview(enumerable, sampleItems, maxDepth, maxBytes, out var totalItems, out var returnedItems, out var previewLength);
                result.TotalItems = totalItems;
                result.ReturnedItems = returnedItems;
                result.PreviewJsonLength = previewLength;
                result.Truncated = returnedItems < totalItems || previewLength > maxBytes;
                result.ReturnedJsonLength = result.Truncated && previewLength > maxBytes ? 0 : previewLength;
                if (previewLength > maxBytes) result.Json = null;
                return result;
            }

            var settings = CreateSerializerSettings(maxDepth);
            var json = NewtonsoftJsonConvert.SerializeObject(value, settings);
            result.TotalJsonLength = json.Length;
            if (json.Length > maxBytes)
            {
                result.Truncated = true;
                return result;
            }
            result.ReturnedJsonLength = json.Length;
            result.Json = JsonNode.Parse(json) ?? JsonValue.Create(json);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }
        return result;
    }

    private static JsonObject BuildCollectionPreview(
        IEnumerable enumerable,
        int sampleItems,
        int maxDepth,
        int maxBytes,
        out int totalItems,
        out int returnedItems,
        out int previewLength)
    {
        var values = new JsonArray();
        var knownCount = enumerable is ICollection collection ? collection.Count : (int?)null;
        totalItems = knownCount ?? 0;
        foreach (var item in enumerable)
        {
            if (knownCount == null) totalItems++;
            if (values.Count >= sampleItems)
            {
                if (knownCount != null) break;
                continue;
            }
            var itemJson = NewtonsoftJsonConvert.SerializeObject(item, CreateSerializerSettings(maxDepth));
            values.Add(JsonNode.Parse(itemJson) ?? JsonValue.Create(itemJson));
        }
        returnedItems = values.Count;
        var preview = new JsonObject
        {
            ["type"] = "array",
            ["totalItems"] = totalItems,
            ["returnedItems"] = returnedItems,
            ["omittedItems"] = Math.Max(0, totalItems - returnedItems),
            ["items"] = values
        };
        previewLength = preview.ToJsonString().Length;
        return preview;
    }

    private static Newtonsoft.Json.JsonSerializerSettings CreateSerializerSettings(int maxDepth) => new()
    {
        Formatting = Newtonsoft.Json.Formatting.Indented,
        MaxDepth = maxDepth,
        ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
        Error = (_, args) => args.ErrorContext.Handled = true
    };

    private static (List<ObjectReferenceDto> References, bool HasMore) CollectReferences(UObject obj, int limit)
    {
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddReference(references, obj.Outer?.GetPathName(), "outer");
        AddReference(references, obj.Class?.GetPathName(), "class");
        AddReference(references, obj.Super?.GetPathName(), "super");
        AddReference(references, obj.Template?.GetPathName(), "template");
        var package = obj.Owner;
        if (package != null)
        {
            foreach (var property in obj.Properties)
            {
                WalkValue(property.Tag?.GetValue<object>(), package, references, limit + 1, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
                if (references.Count > limit) break;
            }
        }
        var results = references.Take(limit).Select(pair => new ObjectReferenceDto { Path = pair.Key, Source = pair.Value }).ToList();
        return (results, references.Count > limit);
    }

    private static void WalkValue(object? value, IPackage package, Dictionary<string, string> references, int limit, int depth, HashSet<object> visited)
    {
        if (value == null || references.Count >= limit || depth > 4) return;
        if (value is string || value.GetType().IsPrimitive || value is decimal) return;
        if (!value.GetType().IsValueType && !visited.Add(value)) return;

        if (value is FPackageIndex packageIndex && !packageIndex.IsNull)
        {
            AddReference(references, package.ResolvePackageIndex(packageIndex)?.GetPathName(), "object_property");
            return;
        }
        if (value is FSoftObjectPath softPath)
        {
            AddReference(references, softPath.ToString(), "soft_object_property");
            return;
        }
        if (value is ResolvedObject resolved)
        {
            AddReference(references, resolved.GetPathName(), "resolved_property");
            return;
        }
        if (value is UObject nestedObject)
        {
            AddReference(references, nestedObject.GetPathName(), "nested_object");
            return;
        }
        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                WalkValue(item, package, references, limit, depth + 1, visited);
                if (references.Count >= limit) break;
            }
            return;
        }

        foreach (var member in value.GetType().GetMembers(BindingFlags.Instance | BindingFlags.Public))
        {
            object? child = member switch
            {
                FieldInfo field => field.GetValue(value),
                PropertyInfo property when property.CanRead && property.GetIndexParameters().Length == 0 => property.GetValue(value),
                _ => null
            };
            WalkValue(child, package, references, limit, depth + 1, visited);
            if (references.Count >= limit) break;
        }
    }

    private static void AddReference(Dictionary<string, string> references, string? path, string source)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "None") return;
        references.TryAdd(path, source);
    }

    private static bool IsTargetMatch(string path, string target) =>
        path.Contains(target, StringComparison.OrdinalIgnoreCase) ||
        target.Contains(path, StringComparison.OrdinalIgnoreCase);

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { ok = false, errorCode = code, message }, McpJsonOptions.Default);
}
