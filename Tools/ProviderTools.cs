using System.ComponentModel;
using System.IO;
using System.Text.Json;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.Mcp.Dtos;
using CUE4Parse.Mcp.Services;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.VirtualFileSystem;
using CUE4Parse.Utils;
using ModelContextProtocol.Server;

namespace CUE4Parse.Mcp.Tools;

[McpServerToolType]
public static class ProviderTools
{
    [McpServerTool(Name = "init_provider"), Description(
        "Initialize a CUE4Parse DefaultFileProvider to scan a game's archive directory. " +
        "Returns a session ID that must be passed to subsequent tool calls. " +
        "The provider scans for .pak, .utoc, .ucas, .uasset, .umap files. " +
        "If archives are encrypted, call submit_key after initialization.")]
    public static string InitProvider(
        Cue4ParseSessionRegistry sessions,
        [Description("Absolute path to the game's content/paks directory (e.g. D:\\Games\\MyGame\\Content\\Paks)")]
        string root,
        [Description("Game version enum name (e.g. GAME_UE5_3, GAME_UE4_27, GAME_FortniteGame, etc.). See EGame enum for all values.")]
        string gameVersion,
        [Description("Search option: 'TopDirectoryOnly' or 'AllDirectories'. Default: TopDirectoryOnly.")]
        string? searchOption = null,
        [Description("Whether paths should be case-insensitive. Default: false.")]
        bool? caseInsensitive = null,
        [Description("Path to a .usmap mappings file for unversioned property parsing. Optional.")]
        string? mappingsFile = null,
        [Description("Whether to read script data. Default: false.")]
        bool? readScriptData = null,
        [Description("Whether to read shader maps. Default: false.")]
        bool? readShaderMaps = null,
        [Description("Whether to read Nanite data. Default: false.")]
        bool? readNaniteData = null,
        [Description("Whether to skip loading referenced textures in materials. Default: false.")]
        bool? skipReferencedTextures = null)
    {
        if (!Directory.Exists(root))
            return Error("Directory not found", $"The directory '{root}' does not exist.");

        if (!Enum.TryParse<EGame>(gameVersion, out var eGame))
            return Error("Unsupported game version", $"'{gameVersion}' is not a valid EGame enum value. Examples: GAME_UE5_3, GAME_UE4_27, GAME_FortniteGame.");

        var searchOpt = searchOption?.Equals("AllDirectories", StringComparison.OrdinalIgnoreCase) == true
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        var pathComparer = caseInsensitive == true
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var versions = new VersionContainer(eGame);
        var provider = new DefaultFileProvider(root, searchOpt, versions, pathComparer);

        if (readScriptData == true) provider.ReadScriptData = true;
        if (readShaderMaps == true) provider.ReadShaderMaps = true;
        if (readNaniteData == true) provider.ReadNaniteData = true;
        if (skipReferencedTextures == true) provider.SkipReferencedTextures = true;

        if (!string.IsNullOrEmpty(mappingsFile) && File.Exists(mappingsFile))
        {
            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(mappingsFile, pathComparer);
        }

        var warnings = new List<string>();

        try
        {
            provider.Initialize();
        }
        catch (Exception ex)
        {
            return Error("Initialization failed", ex.Message);
        }

        // Try to mount unencrypted archives
        try
        {
            var mounted = provider.Mount();
        }
        catch (Exception ex)
        {
            warnings.Add($"Mount warning: {ex.Message}");
        }

        // Try PostMount to load ini configs
        try
        {
            provider.PostMount();
        }
        catch (Exception ex)
        {
            warnings.Add($"PostMount warning: {ex.Message}");
        }

        var encryptedCount = provider.UnloadedVfs.Count(v => v.IsEncrypted);
        var fileCount = provider.Files.Count;

        var session = sessions.CreateSession(provider, root, eGame);
        session.IsInitialized = true;
        session.EncryptedArchiveCount = encryptedCount;
        session.Warnings.AddRange(warnings);

        if (!string.IsNullOrEmpty(mappingsFile) && !File.Exists(mappingsFile))
            session.Warnings.Add($"Mappings file '{mappingsFile}' not found, loaded without mappings.");

        var result = new
        {
            ok = true,
            sessionId = session.SessionId,
            projectName = provider.ProjectName,
            gameVersion = eGame.ToString(),
            fileCount,
            encryptedArchiveCount = encryptedCount,
            looseFileCount = provider.LooseFileCount,
            warnings = session.Warnings
        };

        return JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "submit_key"), Description(
        "Submit an AES encryption key to decrypt mounted archives. " +
        "Use after init_provider when encrypted archives are detected. " +
        "The GUID can be empty (all zeros) if the game uses a single key.")]
    public static string SubmitKey(
        Cue4ParseSessionRegistry sessions,
        [Description("AES key in hex format starting with 0x (e.g. 0xABCD...). 32 bytes = 64 hex chars.")]
        string aesKey,
        [Description("Encryption key GUID as hex string (e.g. 00000000000000000000000000000000). Use all zeros if unsure.")]
        string? guid = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")]
        string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null)
            return Error("No session", "No provider session found. Call init_provider first.");

        if (!aesKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Error("Invalid key format", "AES key must start with '0x' followed by hex characters.");

        FGuid fguid;
        if (string.IsNullOrEmpty(guid))
        {
            fguid = new FGuid();
        }
        else
        {
            var guidStr = guid.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? guid[2..] : guid;
            guidStr = guidStr.Replace("-", "");
            try
            {
                fguid = new FGuid(guidStr);
            }
            catch
            {
                return Error("Invalid GUID", $"Could not parse '{guid}' as a GUID. Expected 32 hex characters.");
            }
        }

        FAesKey key;
        try
        {
            key = new FAesKey(aesKey);
        }
        catch (Exception ex)
        {
            return Error("Invalid AES key", ex.Message);
        }

        int mountedCount;
        try
        {
            mountedCount = session.Provider.SubmitKey(fguid, key);
        }
        catch (Exception ex)
        {
            return Error("Submit key failed", ex.Message);
        }

        var result = new
        {
            ok = true,
            sessionId = session.SessionId,
            newlyMountedArchives = mountedCount,
            remainingEncryptedArchives = session.Provider.UnloadedVfs.Count(v => v.IsEncrypted),
            totalFiles = session.Provider.Files.Count
        };

        return JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "set_mappings"), Description(
        "Set or update the .usmap mappings file for unversioned property parsing. " +
        "Call this after init_provider if you didn't provide mappingsFile initially.")]
    public static string SetMappings(
        Cue4ParseSessionRegistry sessions,
        [Description("Absolute path to the .usmap file.")]
        string mappingsFile,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")]
        string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null)
            return Error("No session", "No provider session found. Call init_provider first.");

        if (!File.Exists(mappingsFile))
            return Error("File not found", $"Mappings file '{mappingsFile}' does not exist.");

        try
        {
            session.Provider.MappingsContainer = new FileUsmapTypeMappingsProvider(mappingsFile, session.Provider.PathComparer);
        }
        catch (Exception ex)
        {
            return Error("Failed to load mappings", ex.Message);
        }

        var result = new
        {
            ok = true,
            sessionId = session.SessionId,
            mappingsFile = Path.GetFileName(mappingsFile)
        };

        return JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "survey_provider"), Description(
        "Get a comprehensive overview of the provider in one call: project name, file statistics by extension, " +
        "mounted/unmounted VFS archives, encryption status, top-level directories, and mappings status. " +
        "Call this after init_provider (and optionally submit_key) to understand the game's asset structure.")]
    public static string SurveyProvider(
        Cue4ParseSessionRegistry sessions,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")]
        string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null)
            return Error("No session", "No provider session found. Call init_provider first.");

        var provider = session.Provider;

        // File extension statistics
        var extStats = provider.Files
            .GroupBy(f => f.Value.Extension)
            .Select(g => new ExtensionStatsDto
            {
                Extension = g.Key,
                Count = g.Count(),
                TotalSizeBytes = g.Sum(f => f.Value.Size)
            })
            .OrderByDescending(s => s.Count)
            .Take(20)
            .ToList();

        // Mounted VFS archives
        var mountedArchives = provider.MountedVfs.Select(r => new VfsArchiveDto
        {
            Name = r.Name,
            Path = r.Path,
            IsEncrypted = r.IsEncrypted,
            FileCount = r.FileCount,
            Length = r is IAesVfsReader aes ? aes.Length : 0,
            MountPoint = r.MountPoint
        }).ToList();

        // Unloaded (encrypted/not yet mounted) VFS archives
        var unloadedArchives = provider.UnloadedVfs.Select(r => new VfsArchiveDto
        {
            Name = r.Name,
            Path = r.Path,
            IsEncrypted = r.IsEncrypted,
            FileCount = r.FileCount,
            Length = r.Length,
            MountPoint = r.MountPoint
        }).ToList();

        // Top-level directories (first path segment)
        var topDirs = provider.Files.Keys
            .Select(p => p.SubstringBefore('/'))
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .OrderBy(s => s)
            .Take(30)
            .ToList();

        var survey = new SurveyProviderDto
        {
            SessionId = session.SessionId,
            ProjectName = provider.ProjectName,
            GameVersion = session.GameVersion.ToString(),
            RootDirectory = session.RootDirectory,
            TotalFiles = provider.Files.Count,
            LooseFileCount = provider.LooseFileCount,
            MountedVfsCount = mountedArchives.Count,
            UnloadedVfsCount = unloadedArchives.Count,
            EncryptedArchiveCount = unloadedArchives.Count(a => a.IsEncrypted),
            Extensions = extStats,
            MountedArchives = mountedArchives,
            UnloadedArchives = unloadedArchives,
            HasMappings = provider.MappingsContainer?.MappingsForGame != null,
            Warnings = session.Warnings,
            TopLevelDirectories = topDirs
        };

        return JsonSerializer.Serialize(survey, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "list_sessions"), Description(
        "List all active provider sessions with their metadata.")]
    public static string ListSessions(
        Cue4ParseSessionRegistry sessions)
    {
        var result = new
        {
            ok = true,
            sessions = sessions.GetAllSessions().Select(s => new SessionInfoDto
            {
                SessionId = s.SessionId,
                RootDirectory = s.RootDirectory,
                GameVersion = s.GameVersion.ToString(),
                FileCount = s.Provider.Files.Count,
                EncryptedArchives = s.EncryptedArchiveCount,
                CreatedAt = s.CreatedAt.ToString("o")
            }).ToArray()
        };

        return JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { ok = false, errorCode = code, message }, McpJsonOptions.Default);
}
