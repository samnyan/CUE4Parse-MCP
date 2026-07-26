using System.ComponentModel;
using System.Text.Json;
using CUE4Parse.Mcp.Dtos;
using CUE4Parse.Mcp.Services;
using ModelContextProtocol.Server;

namespace CUE4Parse.Mcp.Tools;

[McpServerToolType]
public static class AssetTools
{
    [McpServerTool(Name = "list_files"), Description(
        "List files from the provider's file index with optional filtering and pagination. " +
        "Returns file paths, extensions, and sizes. Does not load package data.")]
    public static string ListFiles(
        Cue4ParseSessionRegistry sessions,
        [Description("Filter: only return paths starting with this prefix (case-insensitive). Optional.")]
        string? prefix = null,
        [Description("Filter: only return files with this extension (e.g. uasset, umap, uexp). Optional.")]
        string? extension = null,
        [Description("Filter: 'package' for UE packages only (uasset/umap), 'all' for everything. Default: all.")]
        string? type = null,
        [Description("Maximum number of items to return. Default: 100, max: 500.")]
        int? limit = null,
        [Description("Number of items to skip for pagination. Default: 0.")]
        int? cursor = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")]
        string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null)
            return Error("No session", "No provider session found. Call init_provider first.");

        var maxLimit = Math.Clamp(limit ?? 100, 1, 500);
        var skip = Math.Max(0, cursor ?? 0);

        IEnumerable<KeyValuePair<string, CUE4Parse.FileProvider.Objects.GameFile>> files = session.Provider.Files;

        if (!string.IsNullOrEmpty(prefix))
        {
            var prefixLower = prefix.ToLowerInvariant();
            files = files.Where(f => f.Key.StartsWith(prefixLower, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(extension))
        {
            var extLower = extension.ToLowerInvariant().TrimStart('.');
            files = files.Where(f => f.Value.Extension.Equals(extLower, StringComparison.OrdinalIgnoreCase));
        }

        if (type?.Equals("package", StringComparison.OrdinalIgnoreCase) == true)
        {
            files = files.Where(f => f.Value.IsUePackage);
        }

        var totalCount = files.Count();
        var page = files.Skip(skip).Take(maxLimit);

        var result = new
        {
            ok = true,
            items = page.Select(f => new FileEntryDto
            {
                Path = f.Key,
                Name = f.Value.Name,
                Extension = f.Value.Extension,
                Size = f.Value.Size,
                IsUePackage = f.Value.IsUePackage
            }).ToArray(),
            nextCursor = skip + maxLimit < totalCount ? skip + maxLimit : (int?)null,
            totalEstimate = totalCount
        };

        return JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "search_assets"), Description(
        "Search for assets by path substring. Lightweight — only searches file paths, does not load packages. " +
        "Use list_files for prefix-based browsing.")]
    public static string SearchAssets(
        Cue4ParseSessionRegistry sessions,
        [Description("Search query — matches if the file path contains this substring (case-insensitive).")]
        string query,
        [Description("Filter: only return files with this extension (e.g. uasset, umap). Optional.")]
        string? extension = null,
        [Description("Maximum number of results. Default: 50, max: 500.")]
        int? limit = null,
        [Description("Number of items to skip for pagination. Default: 0.")]
        int? cursor = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")]
        string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null)
            return Error("No session", "No provider session found. Call init_provider first.");

        var maxLimit = Math.Clamp(limit ?? 50, 1, 500);
        var skip = Math.Max(0, cursor ?? 0);
        var queryLower = query.ToLowerInvariant();

        IEnumerable<KeyValuePair<string, CUE4Parse.FileProvider.Objects.GameFile>> files = session.Provider.Files;

        if (!string.IsNullOrEmpty(extension))
        {
            var extLower = extension.ToLowerInvariant().TrimStart('.');
            files = files.Where(f => f.Value.Extension.Equals(extLower, StringComparison.OrdinalIgnoreCase));
        }

        // Case-insensitive substring search
        var matches = files
            .Where(f => f.Key.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var totalCount = matches.Count;
        var page = matches.Skip(skip).Take(maxLimit);

        var result = new
        {
            ok = true,
            query = query,
            items = page.Select(f => new FileEntryDto
            {
                Path = f.Key,
                Name = f.Value.Name,
                Extension = f.Value.Extension,
                Size = f.Value.Size,
                IsUePackage = f.Value.IsUePackage
            }).ToArray(),
            nextCursor = skip + maxLimit < totalCount ? skip + maxLimit : (int?)null,
            totalEstimate = totalCount
        };

        return JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { ok = false, errorCode = code, message }, McpJsonOptions.Default);
}
