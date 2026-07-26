using System.Text.Json.Serialization;

namespace CUE4Parse.Mcp.Dtos;

public class ErrorResponse
{
    [JsonPropertyName("ok")] public bool Ok => false;
    [JsonPropertyName("errorCode")] public string ErrorCode { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

public class SessionInfoDto
{
    [JsonPropertyName("sessionId")] public string SessionId { get; set; } = "";
    [JsonPropertyName("rootDirectory")] public string RootDirectory { get; set; } = "";
    [JsonPropertyName("gameVersion")] public string GameVersion { get; set; } = "";
    [JsonPropertyName("fileCount")] public int FileCount { get; set; }
    [JsonPropertyName("encryptedArchives")] public int EncryptedArchives { get; set; }
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = "";
}

public class FileEntryDto
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("extension")] public string Extension { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("isUePackage")] public bool IsUePackage { get; set; }
}

public class ExportSummaryDto
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public class PropertySummaryDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("arrayIndex")] public int ArrayIndex { get; set; }
}

public class ObjectSummaryDto
{
    [JsonPropertyName("objectPath")] public string ObjectPath { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("fullName")] public string FullName { get; set; } = "";
    [JsonPropertyName("pathName")] public string PathName { get; set; } = "";
    [JsonPropertyName("outer")] public string? Outer { get; set; }
    [JsonPropertyName("class")] public string? Class { get; set; }
    [JsonPropertyName("flags")] public string Flags { get; set; } = "";
    [JsonPropertyName("propertyCount")] public int PropertyCount { get; set; }
    [JsonPropertyName("properties")] public List<PropertySummaryDto> Properties { get; set; } = [];
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public class VfsArchiveDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("isEncrypted")] public bool IsEncrypted { get; set; }
    [JsonPropertyName("fileCount")] public int FileCount { get; set; }
    [JsonPropertyName("length")] public long Length { get; set; }
    [JsonPropertyName("mountPoint")] public string MountPoint { get; set; } = "";
}

public class ExtensionStatsDto
{
    [JsonPropertyName("extension")] public string Extension { get; set; } = "";
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("totalSizeBytes")] public long TotalSizeBytes { get; set; }
}

public class SurveyProviderDto
{
    [JsonPropertyName("ok")] public bool Ok => true;
    [JsonPropertyName("sessionId")] public string SessionId { get; set; } = "";
    [JsonPropertyName("projectName")] public string ProjectName { get; set; } = "";
    [JsonPropertyName("gameVersion")] public string GameVersion { get; set; } = "";
    [JsonPropertyName("rootDirectory")] public string RootDirectory { get; set; } = "";

    [JsonPropertyName("totalFiles")] public int TotalFiles { get; set; }
    [JsonPropertyName("looseFileCount")] public int LooseFileCount { get; set; }
    [JsonPropertyName("mountedVfsCount")] public int MountedVfsCount { get; set; }
    [JsonPropertyName("unloadedVfsCount")] public int UnloadedVfsCount { get; set; }
    [JsonPropertyName("encryptedArchiveCount")] public int EncryptedArchiveCount { get; set; }

    [JsonPropertyName("extensions")] public List<ExtensionStatsDto> Extensions { get; set; } = [];
    [JsonPropertyName("mountedArchives")] public List<VfsArchiveDto> MountedArchives { get; set; } = [];
    [JsonPropertyName("unloadedArchives")] public List<VfsArchiveDto> UnloadedArchives { get; set; } = [];

    [JsonPropertyName("hasMappings")] public bool HasMappings { get; set; }
    [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = [];

    [JsonPropertyName("topLevelDirectories")] public List<string> TopLevelDirectories { get; set; } = [];
}
