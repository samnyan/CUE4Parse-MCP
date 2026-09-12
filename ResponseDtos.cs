using System.Text.Json.Nodes;
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
    [JsonPropertyName("readScriptData")] public bool ReadScriptData { get; set; }
    [JsonPropertyName("cachedPackageCount")] public int CachedPackageCount { get; set; }
    [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = [];

    [JsonPropertyName("topLevelDirectories")] public List<string> TopLevelDirectories { get; set; } = [];
}

public class DataTableRowDto
{
    [JsonPropertyName("rowName")] public string RowName { get; set; } = "";
    [JsonPropertyName("properties")] public List<PropertySummaryDto> Properties { get; set; } = [];
    [JsonPropertyName("truncated")] public bool Truncated { get; set; }
    [JsonPropertyName("totalJsonLength")] public int TotalJsonLength { get; set; }
    [JsonPropertyName("returnedJsonLength")] public int ReturnedJsonLength { get; set; }
    [JsonPropertyName("json")] public JsonNode? Json { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public class DataTableRowsResultDto
{
    [JsonPropertyName("ok")] public bool Ok => true;
    [JsonPropertyName("objectPath")] public string ObjectPath { get; set; } = "";
    [JsonPropertyName("rowStructName")] public string? RowStructName { get; set; }
    [JsonPropertyName("totalRows")] public int TotalRows { get; set; }
    [JsonPropertyName("returnedRows")] public int ReturnedRows { get; set; }
    [JsonPropertyName("rows")] public List<DataTableRowDto> Rows { get; set; } = [];
    [JsonPropertyName("nextCursor")] public int? NextCursor { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public class StringTableEntryDto
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}

public class StringTableResultDto
{
    [JsonPropertyName("ok")] public bool Ok => true;
    [JsonPropertyName("objectPath")] public string ObjectPath { get; set; } = "";
    [JsonPropertyName("tableNamespace")] public string TableNamespace { get; set; } = "";
    [JsonPropertyName("totalEntries")] public int TotalEntries { get; set; }
    [JsonPropertyName("returnedEntries")] public int ReturnedEntries { get; set; }
    [JsonPropertyName("entries")] public List<StringTableEntryDto> Entries { get; set; } = [];
    [JsonPropertyName("nextCursor")] public int? NextCursor { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public class ObjectJsonResultDto
{
    [JsonPropertyName("objectPath")] public string ObjectPath { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("truncated")] public bool Truncated { get; set; }
    [JsonPropertyName("totalJsonLength")] public int TotalJsonLength { get; set; }
    [JsonPropertyName("returnedJsonLength")] public int ReturnedJsonLength { get; set; }
    [JsonPropertyName("json")] public JsonNode? Json { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public class ObjectPropertyValueDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("arrayIndex")] public int ArrayIndex { get; set; }
    [JsonPropertyName("truncated")] public bool Truncated { get; set; }
    [JsonPropertyName("totalJsonLength")] public int TotalJsonLength { get; set; }
    [JsonPropertyName("returnedJsonLength")] public int ReturnedJsonLength { get; set; }
    [JsonPropertyName("previewJsonLength")] public int? PreviewJsonLength { get; set; }
    [JsonPropertyName("totalItems")] public int? TotalItems { get; set; }
    [JsonPropertyName("returnedItems")] public int? ReturnedItems { get; set; }
    [JsonPropertyName("json")] public JsonNode? Json { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public class ObjectPreviewResultDto
{
    [JsonPropertyName("objectPath")] public string ObjectPath { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("propertyCount")] public int PropertyCount { get; set; }
    [JsonPropertyName("returnedProperties")] public int ReturnedProperties { get; set; }
    [JsonPropertyName("propertiesTruncated")] public bool PropertiesTruncated { get; set; }
    [JsonPropertyName("properties")] public List<ObjectPropertyValueDto> Properties { get; set; } = [];
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public class ObjectPropertiesResultDto
{
    [JsonPropertyName("ok")] public bool Ok => true;
    [JsonPropertyName("objectPath")] public string ObjectPath { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("requestedProperties")] public int RequestedProperties { get; set; }
    [JsonPropertyName("returnedProperties")] public int ReturnedProperties { get; set; }
    [JsonPropertyName("missingProperties")] public List<string> MissingProperties { get; set; } = [];
    [JsonPropertyName("properties")] public List<ObjectPropertyValueDto> Properties { get; set; } = [];
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public class ObjectReferenceDto
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
}

public class ObjectReferencesResultDto
{
    [JsonPropertyName("ok")] public bool Ok => true;
    [JsonPropertyName("objectPath")] public string ObjectPath { get; set; } = "";
    [JsonPropertyName("references")] public List<ObjectReferenceDto> References { get; set; } = [];
    [JsonPropertyName("truncated")] public bool Truncated { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public class FindReferencesMatchDto
{
    [JsonPropertyName("packagePath")] public string PackagePath { get; set; } = "";
    [JsonPropertyName("objectPath")] public string ObjectPath { get; set; } = "";
    [JsonPropertyName("references")] public List<string> References { get; set; } = [];
}

public class FindReferencesResultDto
{
    [JsonPropertyName("ok")] public bool Ok => true;
    [JsonPropertyName("targetPath")] public string TargetPath { get; set; } = "";
    [JsonPropertyName("scannedPackages")] public int ScannedPackages { get; set; }
    [JsonPropertyName("failedPackages")] public int FailedPackages { get; set; }
    [JsonPropertyName("scanErrors")] public List<string> ScanErrors { get; set; } = [];
    [JsonPropertyName("cancelled")] public bool Cancelled { get; set; }
    [JsonPropertyName("totalMatches")] public int TotalMatches { get; set; }
    [JsonPropertyName("matches")] public List<FindReferencesMatchDto> Matches { get; set; } = [];
    [JsonPropertyName("nextCursor")] public int? NextCursor { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public class PackageExportResultDto
{
    [JsonPropertyName("ok")] public bool Ok => true;
    [JsonPropertyName("packagePath")] public string PackagePath { get; set; } = "";
    [JsonPropertyName("exportIndex")] public int ExportIndex { get; set; }
    [JsonPropertyName("objectPath")] public string ObjectPath { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("outer")] public string? Outer { get; set; }
    [JsonPropertyName("class")] public string? Class { get; set; }
    [JsonPropertyName("propertyCount")] public int PropertyCount { get; set; }
    [JsonPropertyName("properties")] public List<PropertySummaryDto> Properties { get; set; } = [];
    [JsonPropertyName("truncated")] public bool Truncated { get; set; }
    [JsonPropertyName("totalJsonBytes")] public int TotalJsonBytes { get; set; }
    [JsonPropertyName("returnedJsonBytes")] public int ReturnedJsonBytes { get; set; }
    [JsonPropertyName("json")] public JsonNode? Json { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public class PropertyPathValueDto
{
    [JsonPropertyName("resolvedPath")] public string ResolvedPath { get; set; } = "";
    [JsonPropertyName("truncated")] public bool Truncated { get; set; }
    [JsonPropertyName("totalJsonBytes")] public int TotalJsonBytes { get; set; }
    [JsonPropertyName("returnedJsonBytes")] public int ReturnedJsonBytes { get; set; }
    [JsonPropertyName("json")] public JsonNode? Json { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public class PropertyPathResultDto
{
    [JsonPropertyName("ok")] public bool Ok => true;
    [JsonPropertyName("objectPath")] public string ObjectPath { get; set; } = "";
    [JsonPropertyName("exportIndex")] public int? ExportIndex { get; set; }
    [JsonPropertyName("propertyPath")] public string PropertyPath { get; set; } = "";
    [JsonPropertyName("totalValues")] public int TotalValues { get; set; }
    [JsonPropertyName("values")] public List<PropertyPathValueDto> Values { get; set; } = [];
}

public class BehaviorTreeNodeDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("exportIndex")] public int? ExportIndex { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("parentId")] public string? ParentId { get; set; }
    [JsonPropertyName("services")] public List<string> Services { get; set; } = [];
    [JsonPropertyName("properties")] public Dictionary<string, JsonNode?> Properties { get; set; } = [];
    [JsonPropertyName("truncatedProperties")] public List<string> TruncatedProperties { get; set; } = [];
    [JsonPropertyName("nativePolicyUnknown")] public bool NativePolicyUnknown { get; set; }
}

public class BehaviorTreeDecoratorLogicDto
{
    [JsonPropertyName("operation")] public string Operation { get; set; } = "";
    [JsonPropertyName("number")] public int? Number { get; set; }
}

public class BehaviorTreeEdgeDto
{
    [JsonPropertyName("parentId")] public string ParentId { get; set; } = "";
    [JsonPropertyName("childId")] public string ChildId { get; set; } = "";
    [JsonPropertyName("childIndex")] public int ChildIndex { get; set; }
    [JsonPropertyName("childKind")] public string ChildKind { get; set; } = "";
    [JsonPropertyName("decorators")] public List<string> Decorators { get; set; } = [];
    [JsonPropertyName("decoratorLogic")] public List<BehaviorTreeDecoratorLogicDto> DecoratorLogic { get; set; } = [];
}

public class BehaviorTreeAnalysisResultDto
{
    [JsonPropertyName("ok")] public bool Ok => true;
    [JsonPropertyName("objectPath")] public string ObjectPath { get; set; } = "";
    [JsonPropertyName("rootNodeId")] public string? RootNodeId { get; set; }
    [JsonPropertyName("blackboardPath")] public string? BlackboardPath { get; set; }
    [JsonPropertyName("rootDecorators")] public List<string> RootDecorators { get; set; } = [];
    [JsonPropertyName("rootDecoratorLogic")] public List<BehaviorTreeDecoratorLogicDto> RootDecoratorLogic { get; set; } = [];
    [JsonPropertyName("nodes")] public List<BehaviorTreeNodeDto> Nodes { get; set; } = [];
    [JsonPropertyName("edges")] public List<BehaviorTreeEdgeDto> Edges { get; set; } = [];
    [JsonPropertyName("totalNodes")] public int TotalNodes { get; set; }
    [JsonPropertyName("totalEdges")] public int TotalEdges { get; set; }
    [JsonPropertyName("unresolvedReferences")] public List<string> UnresolvedReferences { get; set; } = [];
    [JsonPropertyName("diagnostics")] public List<string> Diagnostics { get; set; } = [];
    [JsonPropertyName("truncated")] public bool Truncated { get; set; }
}

public class BlueprintFunctionDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("objectPath")] public string? ObjectPath { get; set; }
    [JsonPropertyName("exportIndex")] public int? ExportIndex { get; set; }
    [JsonPropertyName("functionFlags")] public string FunctionFlags { get; set; } = "";
    [JsonPropertyName("isNative")] public bool IsNative { get; set; }
    [JsonPropertyName("isUberGraph")] public bool IsUberGraph { get; set; }
    [JsonPropertyName("statementCount")] public int StatementCount { get; set; }
    [JsonPropertyName("hasScriptBytecode")] public bool HasScriptBytecode { get; set; }
    [JsonPropertyName("scriptComplete")] public bool ScriptComplete { get; set; }
    [JsonPropertyName("eventGraphFunction")] public string? EventGraphFunction { get; set; }
    [JsonPropertyName("eventGraphCallOffset")] public int EventGraphCallOffset { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public class BlueprintFunctionListResultDto
{
    [JsonPropertyName("ok")] public bool Ok => true;
    [JsonPropertyName("classObjectPath")] public string ClassObjectPath { get; set; } = "";
    [JsonPropertyName("className")] public string ClassName { get; set; } = "";
    [JsonPropertyName("readScriptData")] public bool ReadScriptData { get; set; }
    [JsonPropertyName("totalFunctions")] public int TotalFunctions { get; set; }
    [JsonPropertyName("functions")] public List<BlueprintFunctionDto> Functions { get; set; } = [];
    [JsonPropertyName("nextCursor")] public int? NextCursor { get; set; }
    [JsonPropertyName("diagnostics")] public List<string> Diagnostics { get; set; } = [];
}

public class KismetStatementDto
{
    [JsonPropertyName("ordinal")] public int Ordinal { get; set; }
    [JsonPropertyName("statementIndex")] public int StatementIndex { get; set; }
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("targets")] public List<int> Targets { get; set; } = [];
    [JsonPropertyName("calls")] public List<string> Calls { get; set; } = [];
    [JsonPropertyName("variables")] public List<string> Variables { get; set; } = [];
    [JsonPropertyName("expressionDepth")] public int ExpressionDepth { get; set; }
    [JsonPropertyName("expressionCount")] public int ExpressionCount { get; set; }
    [JsonPropertyName("expression")] public JsonNode? Expression { get; set; }
    [JsonPropertyName("expressionTruncated")] public bool ExpressionTruncated { get; set; }
}

public class KismetDisassemblyResultDto
{
    [JsonPropertyName("ok")] public bool Ok => true;
    [JsonPropertyName("classObjectPath")] public string ClassObjectPath { get; set; } = "";
    [JsonPropertyName("functionName")] public string FunctionName { get; set; } = "";
    [JsonPropertyName("functionFlags")] public string FunctionFlags { get; set; } = "";
    [JsonPropertyName("totalStatements")] public int TotalStatements { get; set; }
    [JsonPropertyName("returnedStatements")] public int ReturnedStatements { get; set; }
    [JsonPropertyName("statements")] public List<KismetStatementDto> Statements { get; set; } = [];
    [JsonPropertyName("nextCursor")] public int? NextCursor { get; set; }
    [JsonPropertyName("scriptComplete")] public bool ScriptComplete { get; set; }
    [JsonPropertyName("diagnostics")] public List<string> Diagnostics { get; set; } = [];
}

public class KismetBasicBlockDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("startIndex")] public int StartIndex { get; set; }
    [JsonPropertyName("endIndex")] public int EndIndex { get; set; }
    [JsonPropertyName("statementIndices")] public List<int> StatementIndices { get; set; } = [];
    [JsonPropertyName("isEntry")] public bool IsEntry { get; set; }
    [JsonPropertyName("isExit")] public bool IsExit { get; set; }
}

public class KismetControlFlowEdgeDto
{
    [JsonPropertyName("fromBlock")] public int FromBlock { get; set; }
    [JsonPropertyName("toBlock")] public int? ToBlock { get; set; }
    [JsonPropertyName("targetStatementIndex")] public int? TargetStatementIndex { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("condition")] public string? Condition { get; set; }
    [JsonPropertyName("accuracy")] public string Accuracy { get; set; } = "exact";
}

public class KismetCfgResultDto
{
    [JsonPropertyName("ok")] public bool Ok => true;
    [JsonPropertyName("classObjectPath")] public string ClassObjectPath { get; set; } = "";
    [JsonPropertyName("functionName")] public string FunctionName { get; set; } = "";
    [JsonPropertyName("totalBlocks")] public int TotalBlocks { get; set; }
    [JsonPropertyName("totalEdges")] public int TotalEdges { get; set; }
    [JsonPropertyName("blocks")] public List<KismetBasicBlockDto> Blocks { get; set; } = [];
    [JsonPropertyName("edges")] public List<KismetControlFlowEdgeDto> Edges { get; set; } = [];
    [JsonPropertyName("nextCursor")] public int? NextCursor { get; set; }
    [JsonPropertyName("diagnostics")] public List<string> Diagnostics { get; set; } = [];
}

public class KismetCallEdgeDto
{
    [JsonPropertyName("caller")] public string Caller { get; set; } = "";
    [JsonPropertyName("callee")] public string Callee { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("statementIndex")] public int StatementIndex { get; set; }
    [JsonPropertyName("resolvedObjectPath")] public string? ResolvedObjectPath { get; set; }
    [JsonPropertyName("nativeBoundary")] public bool NativeBoundary { get; set; }
    [JsonPropertyName("accuracy")] public string Accuracy { get; set; } = "exact";
}

public class KismetCallGraphResultDto
{
    [JsonPropertyName("ok")] public bool Ok => true;
    [JsonPropertyName("classObjectPath")] public string ClassObjectPath { get; set; } = "";
    [JsonPropertyName("functionFilter")] public string? FunctionFilter { get; set; }
    [JsonPropertyName("totalEdges")] public int TotalEdges { get; set; }
    [JsonPropertyName("edges")] public List<KismetCallEdgeDto> Edges { get; set; } = [];
    [JsonPropertyName("nextCursor")] public int? NextCursor { get; set; }
    [JsonPropertyName("diagnostics")] public List<string> Diagnostics { get; set; } = [];
}

public class KismetDefUseDto
{
    [JsonPropertyName("variable")] public string Variable { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("definitions")] public List<int> Definitions { get; set; } = [];
    [JsonPropertyName("uses")] public List<int> Uses { get; set; } = [];
    [JsonPropertyName("accuracy")] public string Accuracy { get; set; } = "resolved";
}

public class KismetDefUseResultDto
{
    [JsonPropertyName("ok")] public bool Ok => true;
    [JsonPropertyName("classObjectPath")] public string ClassObjectPath { get; set; } = "";
    [JsonPropertyName("functionName")] public string FunctionName { get; set; } = "";
    [JsonPropertyName("experimental")] public bool Experimental => true;
    [JsonPropertyName("totalVariables")] public int TotalVariables { get; set; }
    [JsonPropertyName("variables")] public List<KismetDefUseDto> Variables { get; set; } = [];
    [JsonPropertyName("nextCursor")] public int? NextCursor { get; set; }
    [JsonPropertyName("diagnostics")] public List<string> Diagnostics { get; set; } = [];
}

public class BlueprintPseudoCodeResultDto
{
    [JsonPropertyName("ok")] public bool Ok => true;
    [JsonPropertyName("classObjectPath")] public string ClassObjectPath { get; set; } = "";
    [JsonPropertyName("partial")] public bool Partial { get; set; }
    [JsonPropertyName("totalCharacters")] public int TotalCharacters { get; set; }
    [JsonPropertyName("returnedCharacters")] public int ReturnedCharacters { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("diagnostics")] public List<string> Diagnostics { get; set; } = [];
}
