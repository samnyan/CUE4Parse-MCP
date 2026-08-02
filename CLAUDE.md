# CLAUDE.md

Guidance for working in this repository.

## What this project is

CUE4Parse MCP Server: exposes CUE4Parse's read-only asset querying capabilities to MCP clients (Claude, Cursor, etc.).

Main pieces:
- `Program.cs`: MCP server entrypoint with stdio transport
- `Cue4ParseSession.cs`: Session management (Cue4ParseSession + Cue4ParseSessionRegistry)
- `ResponseDtos.cs`: Explicit DTO classes for structured tool responses
- `Tools/ProviderTools.cs`: init_provider, submit_key, set_mappings, survey_provider, list_sessions
- `Tools/AssetTools.cs`: list_files, search_assets
- `Tools/PackageTools.cs`: package summaries and export lists
- `Tools/ObjectTools.cs`: object preview, property selection, and reference queries
- `Tools/TableTools.cs`: DataTable and StringTable queries

## Core implementation rules

### MCP tool conventions
- All tools are static methods in static classes decorated with `[McpServerToolType]`
- Each tool method is decorated with `[McpServerTool(Name = "snake_case_name")]`
- Use `[Description("...")]` on the method for the tool description and on each parameter
- All tools return `string` (JSON serialized)
- The first parameter is always `Cue4ParseSessionRegistry sessions` (injected by DI)
- Session ID is an optional last parameter; if omitted, the most recent session is used

### Response format
- Success: `{ ok: true, ... }`
- Error: `{ ok: false, errorCode: "snake_case_code", message: "..." }`
- Error codes are stable machine-readable identifiers; human text belongs in `message`
- Use the private `Error(string code, string message)` helper in each tool class

### DTOs
- Define response types in `ResponseDtos.cs`
- Use `[JsonPropertyName("camelCase")]` for JSON property names
- DTOs provide stable response shapes and consistent JSON serialization; tool methods currently return serialized JSON strings

### Batch operations
- Tools that accept a single path should also accept comma-separated paths
- Split with `Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)`
- Return `{ ok: true, queried: N, objects: [...] }` with per-item results and individual errors

### Pagination and query ordering
- Use `cursor` (skip offset) and `limit` parameters
- Return `nextCursor` (null when no more pages) and `totalEstimate`/`totalExports`
- Clamp limit to reasonable bounds (e.g. 1-500)
- List/search tools must apply filtering and deterministic sorting before pagination
- Regex filters are case-insensitive, use a timeout, and return structured errors for invalid expressions
- Supported asset list ordering is explicit and stable: `path`, `name`, `extension`, or `size`; use path as the default tie-breaker

### Error handling
- Catch exceptions per-item in batch operations; don't fail the whole batch
- Use try-catch around CUE4Parse API calls (they can throw on corrupt/unsupported assets)
- Never write to stdout (reserved for MCP JSON-RPC)
- All logging goes to stderr via Microsoft.Extensions.Logging

### JSON serialization
- Use `System.Text.Json` with `McpJsonOptions.Default` for tool responses
- Use `Newtonsoft.Json.JsonConvert.SerializeObject` for UObject serialization (CUE4Parse uses Newtonsoft converters)
- Never return invalid partial JSON; oversized values must use `truncated: true` and a safe structured response

## Build and run

```bash
dotnet build -c Release
dotnet run -c Release
```

The project references CUE4Parse through the NuGet package declared in `CUE4Parse.Mcp.csproj`.

## Adding a new tool

1. Add the tool method to the appropriate `Tools/*.cs` file
2. Decorate with `[McpServerTool(Name = "snake_case")]` and `[Description("...")]`
3. Define any new DTOs in `ResponseDtos.cs`
4. Use `Cue4ParseSessionRegistry sessions` as the first parameter
5. Handle errors gracefully with try-catch and the `Error()` helper
6. Build and verify: `dotnet build -c Release`

## CUE4Parse API notes

- `DefaultFileProvider` scans directories for .pak, .utoc, .ucas, .uasset, .umap files
- `provider.Files` is a dictionary of all indexed files (path → GameFile)
- `provider.MountedVfs` / `provider.UnloadedVfs` track VFS archive state
- `provider.TryLoadPackage(path, out var package)` loads a UE package
- `provider.SafeLoadPackageObject(objectPath)` loads a UObject by path
- `provider.FixPath(path)` normalizes UE virtual paths
- `provider.SubmitKey(guid, key)` mounts encrypted archives with an AES key
- `provider.MappingsContainer` holds .usmap type mappings for unversioned properties
- `EGame` enum defines game versions (e.g. GAME_UE5_3, GAME_UE4_27, GAME_FortniteGame)

## Scope

This is a read-only query server. It intentionally does not modify or export assets. Future query-focused work may add:
- Precise nested UObject property-path queries
- More complete dependency/reference indexing
- MCP Resources for browsable provider state
- Automated tests against representative packages
