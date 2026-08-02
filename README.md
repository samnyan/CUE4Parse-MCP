# CUE4Parse MCP Server

A read-only MCP (Model Context Protocol) server for querying Unreal Engine assets using [CUE4Parse](https://github.com/FabianFG/CUE4Parse).

Built as a .NET 10 stdio-based MCP server. All logs go to stderr; stdout is reserved for MCP JSON-RPC.

## Features

- **`init_provider`** — Initialize a `DefaultFileProvider` to scan a game's archive directory
- **`submit_key`** — Submit an AES encryption key to decrypt mounted archives
- **`set_mappings`** — Set or update a `.usmap` mappings file for unversioned property parsing
- **`list_sessions`** — List all active provider sessions
- **`survey_provider`** — Get a comprehensive provider overview: file stats by extension, VFS archives, encryption status, top-level directories
- **`list_files`** — Browse the provider's file index with substring/regex filtering, deterministic sorting, and pagination
- **`search_assets`** — Search for assets by path substring or regular expression, with deterministic sorting and pagination
- **`get_package_summary`** — Load a UE package and return bounded export summaries, imports, and flags (`exportLimit`, default 100)
- **`get_exports`** — List all exports in a package with pagination
- **`get_object_summary`** — Load one or more UObjects (comma-separated batch) and return their type, name, and property list
- **`get_object_preview`** — Preview top-level UObject properties independently without returning one oversized object
- **`get_object_properties`** — Return only selected UObject properties by name
- **`get_object_json`** — Serialize one or more UObjects (comma-separated batch) to structured JSON with depth and size limits
- **`get_object_references`** — Return bounded hard/soft references discovered from one UObject
- **`find_references`** — Scan a bounded set of packages for objects referencing a target path
- **`get_data_table_rows`** — Query DataTable rows with row name filtering, field filtering, and pagination
- **`get_string_table_entries`** — Query StringTable entries with key prefix filtering and pagination

## Prerequisites

- .NET 10 SDK for building
- .NET 10 Runtime for framework-dependent deployment
- CUE4Parse restored from the NuGet package declared in `CUE4Parse.Mcp.csproj`

## Build

Run the documented build from the repository directory:

```bash
cd CUE4Parse-MCP
dotnet build -c Release
```

The Release output is also the executable used by the MCP client. If that executable is currently running, stop the MCP server before rebuilding because Windows locks both `CUE4Parse.Mcp.exe` and its loaded `CUE4Parse.Mcp.dll`:

```powershell
Get-Process CUE4Parse.Mcp -ErrorAction SilentlyContinue | Stop-Process
dotnet build -c Release
```

For a compile-only check while the normal Release server remains running, use a separate output directory:

```powershell
dotnet build -c Release -p:OutputPath="bin\Release\net10.0-test\" -p:AppendTargetFrameworkToOutputPath=false
```

`UseAppHost=false` only prevents the apphost executable from being generated; it does not prevent the running process from locking `CUE4Parse.Mcp.dll`, so it is not a workaround for a running MCP server.

## Publish for Distribution

Publish to a clean directory and distribute the **entire directory**, not only the `.exe`. The framework-dependent Windows x64 package is smaller, but the target machine must have the .NET 10 runtime installed:

```powershell
dotnet publish CUE4Parse.Mcp.csproj -c Release -r win-x64 --self-contained false -o .\publish\win-x64
```

If the target machine should not require a separate .NET installation, publish a self-contained package instead:

```powershell
dotnet publish CUE4Parse.Mcp.csproj -c Release -r win-x64 --self-contained true -o .\publish\win-x64-self-contained
```

The package directory contains `CUE4Parse.Mcp.exe`, `CUE4Parse.Mcp.dll`, `CUE4Parse.Mcp.deps.json`, `CUE4Parse.Mcp.runtimeconfig.json`, and all dependent assemblies. Keep all of them together. The `.pdb` file is optional for diagnostics and can be omitted from a release archive if debugging symbols are not needed. Do not distribute `bin`, `obj`, the source tree, or NuGet cache files.

For a release archive, verify the package on a clean Windows x64 machine and test the stdio workflow with `init_provider`, `survey_provider`, `search_assets`, and one package query before shipping it.

## Configure with an MCP Client

For a released MCP package, point the client at the executable in the publish directory:

```json
{
  "mcpServers": {
    "cue4parse": {
      "command": "C:\\path\\to\\publish\\win-x64\\CUE4Parse.Mcp.exe"
    }
  }
}
```

For local development only, the client can launch the project through the .NET SDK:

```json
{
  "mcpServers": {
    "cue4parse-dev": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\path\\to\\CUE4Parse-MCP\\CUE4Parse.Mcp.csproj",
        "-c",
        "Release"
      ]
    }
  }
}
```

Do not use `bin\\Release\\net10.0` as a release package; use `dotnet publish` and distribute the complete publish directory.

## Usage Workflow

1. **Initialize** — Call `init_provider` with the game's paks directory path and game version
2. **Survey** — Call `survey_provider` to get a comprehensive overview of the game's asset structure
3. **Decrypt** (if needed) — Call `submit_key` with the AES key if encrypted archives are detected
4. **Browse** — Use `list_files` or `search_assets` to find asset paths; pass `regex: true` for regular expressions and `sortBy` (`path`, `name`, `extension`, or `size`) for deterministic ordering
5. **Inspect** — Use `get_package_summary` or `get_exports` to see what's in a package
6. **Drill down** — Use `get_object_summary`, `get_object_preview`, or `get_object_properties` before falling back to `get_object_json`
7. **References** — Use `get_object_references` for one object or bounded `find_references` for reverse lookup
8. **DataTable queries** — Use `get_data_table_rows` to filter specific rows and fields without loading the entire table
9. **StringTable queries** — Use `get_string_table_entries` to filter by key prefix

### Example: init_provider

```
root: "D:\\Games\\MyGame\\Content\\Paks"
gameVersion: "GAME_UE5_3"
```

### Example: filtered and sorted search

```
query: "^EnderLilies/Content/.+\\.(uasset|umap)$"
regex: true
sortBy: "size"
descending: true
limit: 20
```

`list_files` also accepts `filter` for path filtering. Regex matching is case-insensitive and has a timeout; invalid expressions return a structured error instead of failing the MCP session.

### Example: submit_key

```
aesKey: "0x1234567890ABCDEF..."
guid: "00000000000000000000000000000000"
```

## Architecture

```
CUE4Parse-MCP/
├── CUE4Parse.Mcp.csproj      # Project file and NuGet dependencies
├── Program.cs                # MCP server setup with stdio transport
├── McpJsonOptions.cs         # Shared JSON serialization options
├── ResponseDtos.cs           # Explicit DTO classes for structured tool responses
├── Cue4ParseSession.cs       # Session management (Cue4ParseSession + SessionRegistry)
└── Tools/
    ├── ProviderTools.cs      # init_provider, submit_key, set_mappings, survey_provider, list_sessions
    ├── AssetTools.cs         # list_files, search_assets
    ├── PackageTools.cs       # package summaries and export lists
    ├── ObjectTools.cs        # object preview, properties, and references
    └── TableTools.cs         # DataTable and StringTable queries
```

## Notes

- All tool responses are JSON strings with `{ ok: true, ... }` or `{ ok: false, errorCode, message }` format
- `get_object_summary` and `get_object_json` accept comma-separated paths for batch queries
- `get_object_preview` serializes top-level properties independently and samples array/set properties with `sampleItems`
- `get_object_properties` returns only selected properties and reports `missingProperties`; it is preferred for large objects
- `get_object_references` returns bounded hard/soft references with an accurate `truncated` flag
- `find_references` supports `packagePrefix`, `maxPackages`, `maxExportsPerPackage`, `timeoutMs`, cancellation, and reports scan failures
- `get_object_json` returns structured JSON when not truncated; oversized objects return `truncated: true` with a safe null JSON payload
- `get_data_table_rows` supports `rowNames`, `fields`, `includeJson`, and pagination for efficient DataTable queries
- `get_string_table_entries` supports `keyPrefix`, `keys`, and pagination for efficient StringTable queries
- JSON output from `get_object_json` is limited to 256 KB per object by default (configurable)
- Oversized object and row values are marked with `truncated: true`; no invalid partial JSON is returned
- Truncation reports `totalJsonLength` and `returnedJsonLength` for accurate status
- Session IDs are 12-character hex strings; the most recent session is used if `sessionId` is omitted
- CUE4Parse's Serilog output is redirected to stderr to avoid polluting the MCP protocol
