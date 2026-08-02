using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CUE4Parse.Mcp.Dtos;
using CUE4Parse.Mcp.Services;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Exports.Internationalization;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;
using ModelContextProtocol.Server;
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
            if (!session.Provider.TryLoadPackage(packagePath, out package))
                return Error("Package not found", $"Could not load package at '{packagePath}'. Use search_assets to find valid paths.");
        }

        var exportSummaries = new List<ExportSummaryDto>();
        for (var i = 0; i < package.ExportsLazy.Length; i++)
        {
            try
            {
                var export = package.GetExport(i);
                if (export != null)
                {
                    exportSummaries.Add(new ExportSummaryDto
                    {
                        Index = i,
                        Name = export.Name,
                        Type = export.ExportType,
                        Path = export.GetPathName()
                    });
                }
            }
            catch (Exception ex)
            {
                exportSummaries.Add(new ExportSummaryDto
                {
                    Index = i,
                    Name = $"Export_{i}",
                    Type = "Unknown",
                    Error = ex.Message
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

        var exports = new List<ExportSummaryDto>();
        for (var i = skip; i < Math.Min(skip + maxLimit, totalExports); i++)
        {
            try
            {
                var export = package.GetExport(i);
                exports.Add(new ExportSummaryDto
                {
                    Index = i,
                    Name = export?.Name ?? $"Export_{i}",
                    Type = export?.ExportType ?? "Unknown",
                    Path = export?.GetPathName() ?? ""
                });
            }
            catch (Exception ex)
            {
                exports.Add(new ExportSummaryDto
                {
                    Index = i,
                    Name = $"Export_{i}",
                    Type = "Unknown",
                    Error = ex.Message
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
        "Load one or more UObjects and return their type, name, outer, class, and a summary of their properties. " +
        "Accepts a single objectPath or multiple comma-separated paths for batch queries. " +
        "Each objectPath should be the package path followed by '.' and the export name " +
        "(e.g. 'GameName/Content/Path/Asset.uasset.ExportName').")]
    public static string GetObjectSummary(
        Cue4ParseSessionRegistry sessions,
        [Description("Full object path or comma-separated paths (e.g. 'GameName/Content/Path/Asset.uasset.MainObject' or 'path1,obj2,path3').")]
        string objectPath,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")]
        string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null)
            return Error("No session", "No provider session found. Call init_provider first.");

        var paths = objectPath.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var results = new List<ObjectSummaryDto>();

        foreach (var path in paths)
        {
            try
            {
                var obj = session.Provider.SafeLoadPackageObject(path);
                if (obj == null)
                {
                    results.Add(new ObjectSummaryDto
                    {
                        ObjectPath = path,
                        Error = $"Could not load object at '{path}'."
                    });
                    continue;
                }

                var properties = new List<PropertySummaryDto>();
                foreach (var prop in obj.Properties.Take(50))
                {
                    try
                    {
                        properties.Add(new PropertySummaryDto
                        {
                            Name = prop.Name.Text,
                            Type = prop.PropertyType.Text,
                            ArrayIndex = prop.ArrayIndex
                        });
                    }
                    catch
                    {
                        properties.Add(new PropertySummaryDto
                        {
                            Name = prop.Name.Text,
                            Type = "Unknown",
                            ArrayIndex = prop.ArrayIndex
                        });
                    }
                }

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
                    Properties = properties
                });
            }
            catch (Exception ex)
            {
                results.Add(new ObjectSummaryDto
                {
                    ObjectPath = path,
                    Error = ex.Message
                });
            }
        }

        var result = new
        {
            ok = true,
            queried = paths.Length,
            objects = results
        };

        return System.Text.Json.JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "get_object_json"), Description(
        "Serialize one or more UObjects to JSON with depth and size limits. " +
        "Accepts a single objectPath or multiple comma-separated paths for batch queries. " +
        "Returns the JSON representation of each object's properties as structured JSON (not escaped string). " +
        "Each object's output is truncated if it exceeds maxBytes.")]
    public static string GetObjectJson(
        Cue4ParseSessionRegistry sessions,
        [Description("Full object path or comma-separated paths (e.g. 'path1.uasset.Obj1' or 'path1.Obj1,path2.Obj2').")]
        string objectPath,
        [Description("Maximum serialization depth. Default: 4.")]
        int? maxDepth = null,
        [Description("Maximum JSON output size in bytes per object. Default: 262144 (256 KB).")]
        int? maxBytes = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")]
        string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null)
            return Error("No session", "No provider session found. Call init_provider first.");

        var paths = objectPath.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var depth = Math.Clamp(maxDepth ?? DefaultMaxDepth, 1, 20);
        var maxOutputBytes = Math.Clamp(maxBytes ?? DefaultMaxJsonBytes, 1024, 4 * 1024 * 1024);

        var results = new List<ObjectJsonResultDto>();

        foreach (var path in paths)
        {
            UObject? obj;
            try
            {
                obj = session.Provider.SafeLoadPackageObject(path);
            }
            catch (Exception ex)
            {
                results.Add(new ObjectJsonResultDto { ObjectPath = path, Error = ex.Message });
                continue;
            }

            if (obj == null)
            {
                results.Add(new ObjectJsonResultDto { ObjectPath = path, Error = $"Could not load object at '{path}'." });
                continue;
            }

            string jsonStr;
            try
            {
                var settings = new Newtonsoft.Json.JsonSerializerSettings
                {
                    Formatting = Newtonsoft.Json.Formatting.Indented,
                    MaxDepth = depth,
                    ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
                    Error = (_, args) => args.ErrorContext.Handled = true
                };

                jsonStr = NewtonsoftJsonConvert.SerializeObject(obj, settings);
            }
            catch (Exception ex)
            {
                results.Add(new ObjectJsonResultDto { ObjectPath = path, Error = $"Serialization failed: {ex.Message}" });
                continue;
            }

            var totalLength = jsonStr.Length;
            var truncated = false;

            if (jsonStr.Length > maxOutputBytes)
            {
                // Try to truncate at a valid JSON boundary
                var cutPoint = maxOutputBytes;
                // Find a safe cut point - look for a newline before the limit
                var lastNewline = jsonStr.LastIndexOf('\n', maxOutputBytes);
                if (lastNewline > maxOutputBytes / 2)
                    cutPoint = lastNewline;

                // Try to parse the truncated JSON; if it fails, return a text snippet
                try
                {
                    var truncatedJson = jsonStr[..cutPoint] + "\n  // ... [truncated]";
                    // Attempt to close the JSON by finding the opening brace and closing it
                    // This is a best-effort approach; if parsing fails we fall back to raw text
                    var node = JsonNode.Parse(truncatedJson);
                    results.Add(new ObjectJsonResultDto
                    {
                        ObjectPath = path,
                        Name = obj.Name,
                        Type = obj.ExportType,
                        Truncated = true,
                        TotalJsonLength = totalLength,
                        ReturnedJsonLength = cutPoint,
                        Json = node
                    });
                    continue;
                }
                catch
                {
                    // Can't parse truncated JSON, return as text in a simple wrapper
                    truncated = true;
                    results.Add(new ObjectJsonResultDto
                    {
                        ObjectPath = path,
                        Name = obj.Name,
                        Type = obj.ExportType,
                        Truncated = true,
                        TotalJsonLength = totalLength,
                        ReturnedJsonLength = cutPoint,
                        Json = JsonValue.Create(jsonStr[..cutPoint] + "\n... [truncated]")
                    });
                    continue;
                }
            }

            JsonNode? jsonNode;
            try
            {
                jsonNode = JsonNode.Parse(jsonStr);
            }
            catch
            {
                // If Newtonsoft JSON can't be parsed by System.Text.Json, fall back to raw string
                jsonNode = JsonValue.Create(jsonStr);
            }

            results.Add(new ObjectJsonResultDto
            {
                ObjectPath = path,
                Name = obj.Name,
                Type = obj.ExportType,
                Truncated = truncated,
                TotalJsonLength = totalLength,
                ReturnedJsonLength = jsonStr.Length,
                Json = jsonNode
            });
        }

        var result = new
        {
            ok = true,
            queried = paths.Length,
            objects = results
        };

        return System.Text.Json.JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "get_data_table_rows"), Description(
        "Query rows from a UE DataTable asset with optional row name filtering, field filtering, and pagination. " +
        "Returns each row's property names/types and optionally their JSON values. " +
        "Much more efficient than get_object_json for DataTables with many rows.")]
    public static string GetDataTableRows(
        Cue4ParseSessionRegistry sessions,
        [Description("Object path to the DataTable (e.g. '/Game/Path/DT_Items.DT_Items').")]
        string objectPath,
        [Description("Comma-separated row names to filter (e.g. 'i_passive_maxhpup_LV1,i_passive_maxhpup_LV2'). If omitted, returns all rows.")]
        string? rowNames = null,
        [Description("Comma-separated property names to include (e.g. 'SlotCount,bAvailableInGame'). If omitted, returns all properties.")]
        string? fields = null,
        [Description("Include full JSON value for each row. Default: true. Set to false for a lightweight row list.")]
        bool includeJson = true,
        [Description("Maximum number of rows to return. Default: 100.")]
        int? limit = null,
        [Description("Number of rows to skip. Default: 0.")]
        int? cursor = null,
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

        if (obj is not UDataTable dataTable)
        {
            return Error("Not a DataTable", $"Object at '{objectPath}' is of type '{obj?.ExportType ?? "null"}', not a DataTable.");
        }

        var rowMap = dataTable.RowMap;
        var totalRows = rowMap.Count;

        // Parse field filter
        HashSet<string>? fieldFilter = null;
        if (!string.IsNullOrWhiteSpace(fields))
        {
            fieldFilter = fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(f => f.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // Parse row name filter
        HashSet<string>? rowFilter = null;
        if (!string.IsNullOrWhiteSpace(rowNames))
        {
            rowFilter = rowNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(r => r.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // Filter and paginate rows
        var filteredRows = rowMap
            .Where(kvp => rowFilter == null || rowFilter.Contains(kvp.Key.Text))
            .ToList();

        var maxLimit = Math.Clamp(limit ?? 100, 1, 1000);
        var skip = Math.Max(0, cursor ?? 0);
        var pagedRows = filteredRows.Skip(skip).Take(maxLimit).ToList();

        var rows = new List<DataTableRowDto>();
        foreach (var kvp in pagedRows)
        {
            var rowDto = new DataTableRowDto { RowName = kvp.Key.Text };

            try
            {
                var props = kvp.Value.Properties;
                var filteredProps = fieldFilter != null
                    ? props.Where(p => fieldFilter.Contains(p.Name.Text)).ToList()
                    : props;

                rowDto.Properties = filteredProps.Select(p => new PropertySummaryDto
                {
                    Name = p.Name.Text,
                    Type = p.PropertyType.Text,
                    ArrayIndex = p.ArrayIndex
                }).ToList();

                if (includeJson)
                {
                    var settings = new Newtonsoft.Json.JsonSerializerSettings
                    {
                        Formatting = Newtonsoft.Json.Formatting.Indented,
                        MaxDepth = DefaultMaxDepth,
                        ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
                        Error = (_, args) => args.ErrorContext.Handled = true
                    };

                    if (fieldFilter != null)
                    {
 // Serialize only filtered properties
                        var filteredDict = filteredProps.Select(p => new { name = p.Name.Text, value = p.Tag?.GetValue<object>() }).ToList();
                        var jsonStr = NewtonsoftJsonConvert.SerializeObject(filteredDict, settings);
                        rowDto.Json = JsonNode.Parse(jsonStr) ?? JsonValue.Create(jsonStr);
                    }
                    else
                    {
                        var jsonStr = NewtonsoftJsonConvert.SerializeObject(kvp.Value, settings);
                        // Truncate if too large
                        if (jsonStr.Length > DefaultMaxJsonBytes)
                        {
                            jsonStr = jsonStr[..(DefaultMaxJsonBytes / 2)] + "\n  // ... [truncated]";
                            try { rowDto.Json = JsonNode.Parse(jsonStr); }
                            catch { rowDto.Json = JsonValue.Create(jsonStr); }
                        }
                        else
                        {
                            rowDto.Json = JsonNode.Parse(jsonStr) ?? JsonValue.Create(jsonStr);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                rowDto.Error = ex.Message;
            }

            rows.Add(rowDto);
        }

        var result = new DataTableRowsResultDto
        {
            ObjectPath = objectPath,
            RowStructName = dataTable.RowStructName,
            TotalRows = totalRows,
            ReturnedRows = rows.Count,
            Rows = rows,
            NextCursor = skip + maxLimit < filteredRows.Count ? skip + maxLimit : null
        };

        return System.Text.Json.JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "get_string_table_entries"), Description(
        "Query entries from a UE StringTable asset with optional key prefix filtering and pagination. " +
        "Returns key-value pairs from the string table. Much more efficient than get_object_json for StringTables.")]
    public static string GetStringTableEntries(
        Cue4ParseSessionRegistry sessions,
        [Description("Object path to the StringTable (e.g. '/Game/Path/ST_Passives.ST_Passives').")]
        string objectPath,
        [Description("Filter keys by prefix (e.g. 'PASSIVE_MAXHP'). Case-insensitive.")]
        string? keyPrefix = null,
        [Description("Comma-separated exact key names to match. If omitted, returns all matching entries.")]
        string? keys = null,
        [Description("Maximum number of entries to return. Default: 200.")]
        int? limit = null,
        [Description("Number of entries to skip. Default: 0.")]
        int? cursor = null,
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

        if (obj is not UStringTable stringTable)
        {
            return Error("Not a StringTable", $"Object at '{objectPath}' is of type '{obj?.ExportType ?? "null"}', not a StringTable.");
        }

        var entries = stringTable.StringTable.KeysToEntries;
        var totalEntries = entries.Count;

        // Parse exact key filter
        HashSet<string>? exactKeys = null;
        if (!string.IsNullOrWhiteSpace(keys))
        {
            exactKeys = keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // Filter entries
        var filtered = entries.AsEnumerable();
        if (exactKeys != null)
        {
            filtered = filtered.Where(kvp => exactKeys.Contains(kvp.Key));
        }
        else if (!string.IsNullOrWhiteSpace(keyPrefix))
        {
            filtered = filtered.Where(kvp => kvp.Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase));
        }

        var filteredList = filtered.ToList();
        var maxLimit = Math.Clamp(limit ?? 200, 1, 2000);
        var skip = Math.Max(0, cursor ?? 0);
        var paged = filteredList.Skip(skip).Take(maxLimit).ToList();

        var result = new StringTableResultDto
        {
            ObjectPath = objectPath,
            TableNamespace = stringTable.StringTable.TableNamespace,
            TotalEntries = totalEntries,
            ReturnedEntries = paged.Count,
            Entries = paged.Select(kvp => new StringTableEntryDto
            {
                Key = kvp.Key,
                Value = kvp.Value
            }).ToList(),
            NextCursor = skip + maxLimit < filteredList.Count ? skip + maxLimit : null
        };

        return System.Text.Json.JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    private static string Error(string code, string message) =>
        System.Text.Json.JsonSerializer.Serialize(new { ok = false, errorCode = code, message }, McpJsonOptions.Default);
}
