using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using CUE4Parse.Mcp.Dtos;
using CUE4Parse.Mcp.Services;
using ModelContextProtocol.Server;

namespace CUE4Parse.Mcp.Tools;

[McpServerToolType]
public static class AssetTools
{
    [McpServerTool(Name = "list_files"), Description(
        "List files from the provider's file index with substring/regex filtering, deterministic sorting, and pagination. " +
        "Returns file paths, extensions, and sizes. Does not load package data.")]
    public static string ListFiles(
        Cue4ParseSessionRegistry sessions,
        [Description("Filter: only return paths starting with this prefix (case-insensitive). Optional.")]
        string? prefix = null,
        [Description("Filter: only return files with this extension (e.g. uasset, umap, uexp). Optional.")]
        string? extension = null,
        [Description("Filter: 'package' for UE packages only (uasset/umap), 'all' for everything. Default: all.")]
        string? type = null,
        [Description("Optional path filter. Matches a case-insensitive substring, or a regular expression when regex=true.")]
        string? filter = null,
        [Description("Interpret filter/query as a regular expression. Invalid expressions return an error. Default: false.")]
        bool regex = false,
        [Description("Sort results by path, name, extension, or size. Default: path.")]
        string? sortBy = null,
        [Description("Sort descending instead of ascending. Default: false.")]
        bool descending = false,
        [Description("Maximum number of items to return. Default: 100, max: 500.")]
        int? limit = null,
        [Description("Number of items to skip for pagination. Default: 0.")]
        int? cursor = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")]
        string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null)
            return Error("no_session", "No provider session found. Call init_provider first.");

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

        if (!TryCreateMatcher(filter, regex, out var matcher, out var matcherError))
            return Error("invalid_filter", matcherError!);
        if (matcher != null)
            files = files.Where(f => matcher(f.Key));

        files = ApplySort(files, sortBy, descending, out var sortError);
        if (sortError != null)
            return Error("invalid_sort", sortError);

        var totalCount = files.Count();
        var page = files.Skip(skip).Take(maxLimit);

        var result = new
        {
            ok = true,
            filter,
            regex,
            sortBy = NormalizeSortBy(sortBy),
            descending,
            items = page.Select(ToFileEntry).ToArray(),
            nextCursor = skip + maxLimit < totalCount ? skip + maxLimit : (int?)null,
            totalEstimate = totalCount
        };

        return JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "search_assets"), Description(
        "Search asset paths using a case-insensitive substring or regular expression, with deterministic sorting and pagination. " +
        "Lightweight — only searches file paths and does not load packages. Use list_files for prefix-based browsing.")]
    public static string SearchAssets(
        Cue4ParseSessionRegistry sessions,
        [Description("Search query — matches if the file path contains this substring (case-insensitive).")]
        string query,
        [Description("Filter: only return files with this extension (e.g. uasset, umap). Optional.")]
        string? extension = null,
        [Description("Interpret query as a regular expression instead of a substring. Invalid expressions return an error. Default: false.")]
        bool regex = false,
        [Description("Sort results by path, name, extension, or size. Default: path.")]
        string? sortBy = null,
        [Description("Sort descending instead of ascending. Default: false.")]
        bool descending = false,
        [Description("Maximum number of results. Default: 50, max: 500.")]
        int? limit = null,
        [Description("Number of items to skip for pagination. Default: 0.")]
        int? cursor = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")]
        string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null)
            return Error("no_session", "No provider session found. Call init_provider first.");

        var maxLimit = Math.Clamp(limit ?? 50, 1, 500);
        var skip = Math.Max(0, cursor ?? 0);
        IEnumerable<KeyValuePair<string, CUE4Parse.FileProvider.Objects.GameFile>> files = session.Provider.Files;

        if (!string.IsNullOrEmpty(extension))
        {
            var extLower = extension.ToLowerInvariant().TrimStart('.');
            files = files.Where(f => f.Value.Extension.Equals(extLower, StringComparison.OrdinalIgnoreCase));
        }

        if (!TryCreateMatcher(query, regex, out var matcher, out var matcherError))
            return Error("invalid_query", matcherError!);

        var matches = files.Where(f => matcher!(f.Key));
        matches = ApplySort(matches, sortBy, descending, out var sortError);
        if (sortError != null)
            return Error("invalid_sort", sortError);

        var totalCount = matches.Count();
        var page = matches.Skip(skip).Take(maxLimit);

        var result = new
        {
            ok = true,
            query,
            regex,
            sortBy = NormalizeSortBy(sortBy),
            descending,
            items = page.Select(ToFileEntry).ToArray(),
            nextCursor = skip + maxLimit < totalCount ? skip + maxLimit : (int?)null,
            totalEstimate = totalCount
        };

        return JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    private static FileEntryDto ToFileEntry(KeyValuePair<string, CUE4Parse.FileProvider.Objects.GameFile> file) => new()
    {
        Path = file.Key,
        Name = file.Value.Name,
        Extension = file.Value.Extension,
        Size = file.Value.Size,
        IsUePackage = file.Value.IsUePackage
    };

    private static Func<string, bool>? CreateMatcher(string? pattern, bool regex)
    {
        if (string.IsNullOrEmpty(pattern))
            return _ => true;

        if (!regex)
            return value => value.Contains(pattern, StringComparison.OrdinalIgnoreCase);

        var expression = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250));
        return value =>
        {
            try
            {
                return expression.IsMatch(value);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        };
    }

    private static bool TryCreateMatcher(
        string? pattern,
        bool regex,
        out Func<string, bool>? matcher,
        out string? error)
    {
        try
        {
            matcher = CreateMatcher(pattern, regex);
            error = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            matcher = null;
            error = $"Invalid regular expression: {ex.Message}";
            return false;
        }
    }

    private static IEnumerable<KeyValuePair<string, CUE4Parse.FileProvider.Objects.GameFile>> ApplySort(
        IEnumerable<KeyValuePair<string, CUE4Parse.FileProvider.Objects.GameFile>> files,
        string? sortBy,
        bool descending,
        out string? error)
    {
        var key = NormalizeSortBy(sortBy);
        error = key is "path" or "name" or "extension" or "size"
            ? null
            : "sortBy must be one of: path, name, extension, size.";
        if (error != null)
            return files;

        return key switch
        {
            "name" => descending
                ? files.OrderByDescending(f => f.Value.Name, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(f => f.Value.Name, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Key, StringComparer.OrdinalIgnoreCase),
            "extension" => descending
                ? files.OrderByDescending(f => f.Value.Extension, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(f => f.Value.Extension, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Key, StringComparer.OrdinalIgnoreCase),
            "size" => descending
                ? files.OrderByDescending(f => f.Value.Size).ThenBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(f => f.Value.Size).ThenBy(f => f.Key, StringComparer.OrdinalIgnoreCase),
            _ => descending
                ? files.OrderByDescending(f => f.Key, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static string NormalizeSortBy(string? sortBy) =>
        string.IsNullOrWhiteSpace(sortBy) ? "path" : sortBy.Trim().ToLowerInvariant();

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { ok = false, errorCode = code, message }, McpJsonOptions.Default);
}
