using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CUE4Parse.Mcp.Dtos;
using CUE4Parse.Mcp.Services;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Exports.Internationalization;
using CUE4Parse.UE4.Objects.UObject;
using ModelContextProtocol.Server;
using NewtonsoftJsonConvert = Newtonsoft.Json.JsonConvert;

namespace CUE4Parse.Mcp.Tools;

[McpServerToolType]
public static class TableTools
{
    private const int DefaultMaxDepth = 4;
    private const int DefaultMaxJsonBytes = 256 * 1024;

    [McpServerTool(Name = "get_data_table_rows"), Description(
        "Query rows from a UE DataTable asset with optional row name filtering, field filtering, and pagination. " +
        "Returns each row's property names/types and optionally their JSON values. " +
        "Much more efficient than get_object_json for DataTables with many rows.")]
    public static string GetDataTableRows(
        Cue4ParseSessionRegistry sessions,
        [Description("Object path to the DataTable (e.g. '/Game/Path/DT_Items.DT_Items').")] string objectPath,
        [Description("Comma-separated row names to filter. If omitted, returns all rows.")] string? rowNames = null,
        [Description("Comma-separated property names to include. If omitted, returns all properties.")] string? fields = null,
        [Description("Include full JSON value for each row. Default: true.")] bool includeJson = true,
        [Description("Maximum number of rows to return. Default: 100.")] int? limit = null,
        [Description("Number of rows to skip. Default: 0.")] int? cursor = null,
        [Description("Session ID from init_provider. If omitted, the most recent session.")] string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null) return Error("no_session", "No provider session found. Call init_provider first.");

        UObject? obj;
        try
        {
            obj = session.Provider.SafeLoadPackageObject(objectPath);
        }
        catch (Exception ex)
        {
            return Error("load_failed", ex.Message);
        }

        if (obj is not UDataTable dataTable)
        {
            return Error("not_a_data_table", $"Object at '{objectPath}' is of type '{obj?.ExportType ?? "null"}', not a DataTable.");
        }

        HashSet<string>? fieldFilter = string.IsNullOrWhiteSpace(fields)
            ? null
            : fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string>? rowFilter = string.IsNullOrWhiteSpace(rowNames)
            ? null
            : rowNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filteredRows = dataTable.RowMap
            .Where(kvp => rowFilter == null || rowFilter.Contains(kvp.Key.Text))
            .ToList();
        var maxLimit = Math.Clamp(limit ?? 100, 1, 1000);
        var skip = Math.Max(0, cursor ?? 0);
        var rows = new List<DataTableRowDto>();

        foreach (var kvp in filteredRows.Skip(skip).Take(maxLimit))
        {
            var rowDto = new DataTableRowDto { RowName = kvp.Key.Text };
            try
            {
                var filteredProps = fieldFilter == null
                    ? kvp.Value.Properties
                    : kvp.Value.Properties.Where(p => fieldFilter.Contains(p.Name.Text)).ToList();
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
                    var jsonStr = fieldFilter != null
                        ? NewtonsoftJsonConvert.SerializeObject(filteredProps.Select(p => new { name = p.Name.Text, value = p.Tag?.GetValue<object>() }).ToList(), settings)
                        : NewtonsoftJsonConvert.SerializeObject(kvp.Value, settings);
                    rowDto.TotalJsonLength = jsonStr.Length;
                    if (jsonStr.Length <= DefaultMaxJsonBytes)
                    {
                        rowDto.ReturnedJsonLength = jsonStr.Length;
                        rowDto.Json = JsonNode.Parse(jsonStr) ?? JsonValue.Create(jsonStr);
                    }
                    else
                    {
                        rowDto.Truncated = true;
                    }
                }
            }
            catch (Exception ex) { rowDto.Error = ex.Message; }
            rows.Add(rowDto);
        }

        return JsonSerializer.Serialize(new DataTableRowsResultDto
        {
            ObjectPath = objectPath,
            RowStructName = dataTable.RowStructName,
            TotalRows = dataTable.RowMap.Count,
            ReturnedRows = rows.Count,
            Rows = rows,
            NextCursor = skip + maxLimit < filteredRows.Count ? skip + maxLimit : null
        }, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "get_string_table_entries"), Description(
        "Query entries from a UE StringTable asset with optional key prefix filtering and pagination. " +
        "Returns key-value pairs from the string table without loading unrelated entries.")]
    public static string GetStringTableEntries(
        Cue4ParseSessionRegistry sessions,
        [Description("Object path to the StringTable (e.g. '/Game/Path/ST_Passives.ST_Passives').")] string objectPath,
        [Description("Filter keys by prefix. Case-insensitive.")] string? keyPrefix = null,
        [Description("Comma-separated exact key names to match. If omitted, returns all matching entries.")] string? keys = null,
        [Description("Maximum number of entries to return. Default: 200.")] int? limit = null,
        [Description("Number of entries to skip. Default: 0.")] int? cursor = null,
        [Description("Session ID from init_provider. If omitted, the most recent session.")] string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null) return Error("no_session", "No provider session found. Call init_provider first.");
        UObject? obj;
        try
        {
            obj = session.Provider.SafeLoadPackageObject(objectPath);
        }
        catch (Exception ex)
        {
            return Error("load_failed", ex.Message);
        }

        if (obj is not UStringTable stringTable)
        {
            return Error("not_a_string_table", $"Object at '{objectPath}' is of type '{obj?.ExportType ?? "null"}', not a StringTable.");
        }

        HashSet<string>? exactKeys = string.IsNullOrWhiteSpace(keys)
            ? null
            : keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filtered = stringTable.StringTable.KeysToEntries.AsEnumerable();
        if (exactKeys != null) filtered = filtered.Where(kvp => exactKeys.Contains(kvp.Key));
        else if (!string.IsNullOrWhiteSpace(keyPrefix)) filtered = filtered.Where(kvp => kvp.Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase));
        var filteredList = filtered.ToList();
        var maxLimit = Math.Clamp(limit ?? 200, 1, 2000);
        var skip = Math.Max(0, cursor ?? 0);
        var paged = filteredList.Skip(skip).Take(maxLimit).ToList();

        return JsonSerializer.Serialize(new StringTableResultDto
        {
            ObjectPath = objectPath,
            TableNamespace = stringTable.StringTable.TableNamespace,
            TotalEntries = stringTable.StringTable.KeysToEntries.Count,
            ReturnedEntries = paged.Count,
            Entries = paged.Select(kvp => new StringTableEntryDto { Key = kvp.Key, Value = kvp.Value }).ToList(),
            NextCursor = skip + maxLimit < filteredList.Count ? skip + maxLimit : null
        }, McpJsonOptions.Default);
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { ok = false, errorCode = code, message }, McpJsonOptions.Default);
}
