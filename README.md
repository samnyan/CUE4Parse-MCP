# CUE4Parse MCP Server

A read-only MCP (Model Context Protocol) server for querying Unreal Engine assets using [CUE4Parse](https://github.com/FabianFG/CUE4Parse).

Built as a .NET 10 stdio-based MCP server. All logs go to stderr; stdout is reserved for MCP JSON-RPC.

## Features (MVP Phase 1)

- **`init_provider`** — Initialize a `DefaultFileProvider` to scan a game's archive directory
- **`submit_key`** — Submit an AES encryption key to decrypt mounted archives
- **`set_mappings`** — Set or update a `.usmap` mappings file for unversioned property parsing
- **`list_sessions`** — List all active provider sessions
- **`survey_provider`** — Get a comprehensive provider overview: file stats by extension, VFS archives, encryption status, top-level directories
- **`list_files`** — Browse the provider's file index with filtering and pagination
- **`search_assets`** — Search for assets by path substring
- **`get_package_summary`** — Load a UE package and return its summary (exports, imports, flags)
- **`get_exports`** — List all exports in a package with pagination
- **`get_object_summary`** — Load one or more UObjects (comma-separated batch) and return their type, name, and property list
- **`get_object_json`** — Serialize one or more UObjects (comma-separated batch) to JSON with depth and size limits

## Prerequisites

- .NET 10 SDK
- CUE4Parse source code (cloned at `../CUE4ParseProject`)

## Build

```bash
cd CUE4Parse-MCP
dotnet build -c Release
```

## Configure with an MCP Client

Add the following to your MCP client configuration (e.g. Claude Desktop, Cursor, etc.):

```json
{
  "mcpServers": {
    "cue4parse": {
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

Or use the built executable directly:

```json
{
  "mcpServers": {
    "cue4parse": {
      "command": "C:\\path\\to\\CUE4Parse-MCP\\bin\\Release\\net10.0\\CUE4Parse.Mcp.exe"
    }
  }
}
```

## Usage Workflow

1. **Initialize** — Call `init_provider` with the game's paks directory path and game version
2. **Survey** — Call `survey_provider` to get a comprehensive overview of the game's asset structure
3. **Decrypt** (if needed) — Call `submit_key` with the AES key if encrypted archives are detected
4. **Browse** — Use `list_files` or `search_assets` to find asset paths
5. **Inspect** — Use `get_package_summary` or `get_exports` to see what's in a package
6. **Drill down** — Use `get_object_summary` or `get_object_json` for detailed object data (supports batch queries)

### Example: init_provider

```
root: "D:\\Games\\MyGame\\Content\\Paks"
gameVersion: "GAME_UE5_3"
```

### Example: submit_key

```
aesKey: "0x1234567890ABCDEF..."
guid: "00000000000000000000000000000000"
```

## Architecture

```
CUE4Parse-MCP/
├── CUE4Parse.Mcp.csproj      # Project file referencing CUE4Parse
├── Program.cs                # MCP server setup with stdio transport
├── McpJsonOptions.cs         # Shared JSON serialization options
├── Dtos/
│   └── ResponseDtos.cs       # Explicit DTO classes for structured tool responses
├── Services/
│   └── Cue4ParseSession.cs   # Session management (Cue4ParseSession + SessionRegistry)
└── Tools/
    ├── ProviderTools.cs      # init_provider, submit_key, set_mappings, survey_provider, list_sessions
    ├── AssetTools.cs         # list_files, search_assets
    └── PackageTools.cs       # get_package_summary, get_exports, get_object_summary, get_object_json
```

## Notes

- All tool responses are JSON strings with `{ ok: true, ... }` or `{ ok: false, errorCode, message }` format
- `get_object_summary` and `get_object_json` accept comma-separated paths for batch queries
- JSON output from `get_object_json` is truncated at 256 KB per object by default (configurable)
- Session IDs are 12-character hex strings; the most recent session is used if `sessionId` is omitted
- CUE4Parse's Serilog output is redirected to stderr to avoid polluting the MCP protocol
