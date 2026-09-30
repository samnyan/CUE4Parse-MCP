# CUE4Parse MCP Server

A read-only MCP (Model Context Protocol) server for querying Unreal Engine assets using [CUE4Parse](https://github.com/FabianFG/CUE4Parse).

Built as a .NET 10 MCP server with stdio and Streamable HTTP transports. Current package version is `0.2.0`. All logs go to stderr; stdout is reserved for MCP JSON-RPC when stdio is used.

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
- **`get_package_export`** — Load any package export by zero-based index, including nested BehaviorTree nodes and UFunctions
- **`get_property_path`** — Resolve bounded dotted/indexed paths such as `Children[0].ChildTask`
- **`analyze_behavior_tree`** — Reconstruct cooked runtime BehaviorTree topology, ordered children, edge decorators, services, and node parameters
- **`list_blueprint_functions`** — Enumerate cooked Blueprint UFunctions and Kismet script status
- **`get_kismet_disassembly`** — Return bounded structured Kismet statements, nested calls, variables, and jump targets
- **`get_kismet_cfg`** — Build a Kismet control-flow graph with basic blocks and confidence-labelled edges
- **`get_kismet_call_graph`** — Extract direct, virtual, delegate, native-boundary, and event-to-ubergraph calls
- **`get_kismet_def_use`** — Return experimental approximate Kismet variable definition/use data
- **`decompile_blueprint_pseudo`** — Expose CUE4Parse's best-effort pseudo-decompiler as a bounded presentation view

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

Run the logic-analysis unit tests from the repository directory:

```powershell
dotnet test .\Tests\CUE4Parse.Mcp.Tests.csproj -c Release
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

## Transport Modes

The default transport remains stdio for compatibility:

```powershell
CUE4Parse.Mcp.exe
```

Run the server with the MCP Streamable HTTP transport:

```powershell
CUE4Parse.Mcp.exe --http
```

The default HTTP endpoint is:

```text
http://127.0.0.1:3001/mcp
```

Choose a bind address and port explicitly:

```powershell
CUE4Parse.Mcp.exe --http --host 0.0.0.0 --port 13337
```

Restrict filesystem access with one or more whitelist roots:

```powershell
CUE4Parse.Mcp.exe --http `
  --host 127.0.0.1 `
  --port 13337 `
  --whitelist-dir "D:\\Games" `
  --whitelist-dir "D:\\Mappings"
```

Available command-line options:

- `--http` — use Streamable HTTP instead of stdio
- `--host <host>` — HTTP bind host, default `127.0.0.1`
- `--port <port>` — HTTP port, default `3001`
- `--whitelist-dir <directory>` — allow local filesystem access only within this directory; repeat to allow multiple roots
- `--help` / `-h` — show command-line help

When at least one whitelist root is configured, `init_provider.root`, `init_provider.mappingsFile`, and `set_mappings.mappingsFile` are restricted to the configured roots. Paths are normalized before comparison and symlink/junction targets are checked to prevent escaping the whitelist. Recursive provider scans also reject reparse points that resolve outside all allowed roots.

If no `--whitelist-dir` is supplied, filesystem access remains unrestricted for backward compatibility. HTTP mode prints a warning in this case.

The HTTP transport itself does not add application authentication. Keep the default loopback bind for local use. If the server is exposed to a LAN, VPN, container network, or reverse proxy, use an appropriate firewall/authentication layer and configure a filesystem whitelist.

## Usage Workflow

1. **Initialize** — Call `init_provider` with the game's paks directory path and game version
2. **Survey** — Call `survey_provider` to get a comprehensive overview of the game's asset structure
3. **Decrypt** (if needed) — Call `submit_key` with the AES key if encrypted archives are detected
4. **Browse** — Use `list_files` or `search_assets` to find asset paths; pass `regex: true` for regular expressions and `sortBy` (`path`, `name`, `extension`, or `size`) for deterministic ordering
5. **Inspect** — Use `get_package_summary` or `get_exports` to see what's in a package
6. **Drill down** — Use `get_object_summary`, `get_object_preview`, or `get_object_properties` before falling back to `get_object_json`
7. **Nested exports** — Use `get_package_export` when a `Package.Asset:Subobject` path cannot be loaded directly
8. **Property paths** — Use `get_property_path` for precise traversal of nested arrays and structs
9. **BehaviorTree analysis** — Use `analyze_behavior_tree` for the cooked runtime tree, ordered child edges, decorators, services, and parameters
10. **Blueprint analysis** — Initialize with `readScriptData: true`, then use `list_blueprint_functions`, Kismet disassembly, CFG, call graph, and def-use tools
11. **References** — Use `get_object_references` for one object or bounded `find_references` for reverse lookup
12. **DataTable queries** — Use `get_data_table_rows` to filter specific rows and fields without loading the entire table
13. **StringTable queries** — Use `get_string_table_entries` to filter by key prefix

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

### Example: cooked logic analysis

Initialize with script parsing enabled:

```
root: "D:\\Games\\MyGame\\Content\\Paks"
gameVersion: "GAME_UE5_4"
mappingsFile: "D:\\Games\\MyGame\\Binaries\\Win64\\MyGame.usmap"
readScriptData: true
```

Inspect an otherwise unaddressable nested export:

```
get_package_export(
  packagePath: "MyGame/Content/AI/BT_Enemy.uasset",
  exportIndex: 3,
  includeJson: true
)
```

Analyze a cooked BehaviorTree:

```
analyze_behavior_tree(
  objectPath: "/Game/AI/BT_Enemy.BT_Enemy",
  includeNodeProperties: true
)
```

Analyze compiled Blueprint logic:

```
list_blueprint_functions(
  classObjectPath: "/Game/AI/BTTask_Custom.BTTask_Custom_C"
)

get_kismet_disassembly(
  classObjectPath: "/Game/AI/BTTask_Custom.BTTask_Custom_C",
  functionName: "ReceiveExecuteAI",
  significantOnly: true
)
```

## Cooked Logic Boundaries

BehaviorTree runtime topology is retained in cooked packages because the game needs `RootNode`, ordered composite `Children`, edge decorators, decorator operations, and services at runtime. `analyze_behavior_tree` reconstructs that static runtime structure; custom native node selection policies and live Blackboard values remain outside the asset.

Shipping Blueprint editor graphs, node GUIDs, pin GUIDs, macro boundaries, and layout are normally removed. The retained `UBlueprintGeneratedClass` and `UFunction.ScriptBytecode` support compiled semantic analysis, not original graph reconstruction. Direct Kismet jumps are usually exact; virtual dispatch, delegates, computed jumps, latent continuations, aliasing, native side effects, and def-use results carry lower confidence or diagnostics. Native function bodies are in the executable or modules and are reported as native boundaries.

For IoStore, `.utoc/.ucas` and any required global container must be present. Encrypted containers require the correct AES key. Unversioned packages normally require a matching `.usmap`. Kismet tools require `readScriptData: true` at provider initialization.

## Architecture

```
CUE4Parse-MCP/
├── CUE4Parse.Mcp.csproj      # Project file and NuGet dependencies
├── Program.cs                # MCP server setup for stdio and Streamable HTTP
├── McpJsonOptions.cs         # Shared JSON serialization options
├── ResponseDtos.cs           # Explicit DTO classes for structured tool responses
├── Cue4ParseSession.cs       # Session management and bounded package cache
├── Configuration/
│   └── ServerOptions.cs      # Command-line transport/listen/whitelist options
├── Services/
│   ├── PathAccessPolicy.cs   # Filesystem whitelist and link-escape protection
│   ├── PackageObjectResolver.cs
│   ├── PropertyPathResolver.cs
│   ├── BoundedJsonSerializer.cs
│   ├── BehaviorTreeAnalyzer.cs
│   └── KismetAnalyzer.cs
├── Tools/
│   ├── ProviderTools.cs      # init_provider, submit_key, set_mappings, survey_provider, list_sessions
│   ├── AssetTools.cs         # list_files, search_assets
│   ├── PackageTools.cs       # package summaries and export lists
│   ├── ObjectTools.cs        # object preview, properties, and references
│   ├── LogicObjectTools.cs   # package export and nested property-path access
│   ├── BehaviorTreeTools.cs  # cooked runtime BehaviorTree topology
│   ├── KismetTools.cs        # functions, disassembly, CFG, calls, def-use, pseudo-code
│   └── TableTools.cs         # DataTable and StringTable queries
└── Tests/
    ├── CUE4Parse.Mcp.Tests.csproj
    └── LogicAnalysisTests.cs
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
- `get_package_export` is the reliable nested-export entry point for cooked packages
- `get_property_path` supports dotted paths and numeric/wildcard array selectors
- `analyze_behavior_tree` returns static runtime topology; decorators are attached to parent-child edges
- Kismet analysis requires `readScriptData: true` during `init_provider`
- `get_kismet_def_use` is explicitly experimental and approximate
- All logic-analysis tools are read-only and do not write game assets
- JSON output from `get_object_json` is limited to 256 KB per object by default (configurable)
- Oversized object and row values are marked with `truncated: true`; no invalid partial JSON is returned
- Truncation reports `totalJsonLength` and `returnedJsonLength` for accurate status
- Session IDs are 12-character hex strings; the most recent session is used if `sessionId` is omitted
- CUE4Parse's Serilog output is redirected to stderr to avoid polluting the MCP protocol
