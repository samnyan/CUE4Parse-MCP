using System.ComponentModel;
using System.Text.Json;
using CUE4Parse.Mcp.Dtos;
using CUE4Parse.Mcp.Services;
using CUE4Parse.UE4.Kismet;
using CUE4Parse.UE4.Objects.UObject;
using ModelContextProtocol.Server;

namespace CUE4Parse.Mcp.Tools;

[McpServerToolType]
public static class KismetTools
{
    [McpServerTool(Name = "list_blueprint_functions"), Description(
        "List a cooked BlueprintGeneratedClass's UFunctions with export indices, flags, Kismet script availability, completeness, and event-to-ubergraph entry offsets.")]
    public static string ListBlueprintFunctions(
        Cue4ParseSessionRegistry sessions,
        [Description("Object path to a BlueprintGeneratedClass, such as /Game/Path/BP_Name.BP_Name_C.")] string classObjectPath,
        [Description("Maximum functions to return. Default: 100, max: 1000.")] int? limit = null,
        [Description("Number of functions to skip. Default: 0.")] int? cursor = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")] string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null) return Error("no_session", "No provider session found. Call init_provider first.");
        if (!KismetAnalyzer.TryResolveClass(session, classObjectPath, out var blueprintClass, out var resolveError))
            return Error("class_not_found", resolveError!);

        var diagnostics = new List<string>();
        if (!session.Provider.ReadScriptData) diagnostics.Add("Script parsing is disabled. Reinitialize with readScriptData=true before loading Blueprint packages.");
        var functions = new List<BlueprintFunctionDto>();
        foreach (var pair in blueprintClass!.FuncMap.OrderBy(pair => pair.Key.Text, StringComparer.OrdinalIgnoreCase))
        {
            if (pair.Value.TryLoad<UFunction>(out var function))
                functions.Add(KismetAnalyzer.DescribeFunction(function, pair.Key.Text.StartsWith("ExecuteUbergraph_", StringComparison.OrdinalIgnoreCase)));
            else
                functions.Add(new BlueprintFunctionDto { Name = pair.Key.Text, Error = "Function export could not be loaded." });
        }

        var skip = Math.Max(0, cursor ?? 0);
        var max = Math.Clamp(limit ?? 100, 1, 1000);
        return JsonSerializer.Serialize(new BlueprintFunctionListResultDto
        {
            ClassObjectPath = blueprintClass.GetPathName(),
            ClassName = blueprintClass.Name,
            ReadScriptData = session.Provider.ReadScriptData,
            TotalFunctions = functions.Count,
            Functions = functions.Skip(skip).Take(max).ToList(),
            NextCursor = skip + max < functions.Count ? skip + max : null,
            Diagnostics = diagnostics
        }, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "get_kismet_disassembly"), Description(
        "Return bounded structured Kismet bytecode for one cooked Blueprint function. Includes byte offsets, tokens, jump targets, nested calls, variables, and optional expression JSON.")]
    public static string GetKismetDisassembly(
        Cue4ParseSessionRegistry sessions,
        [Description("Object path to the BlueprintGeneratedClass.")] string classObjectPath,
        [Description("Function name from list_blueprint_functions.")] string functionName,
        [Description("Skip the first N matching top-level statements. Default: 0.")] int? cursor = null,
        [Description("Maximum statements. Default: 100, max: 1000.")] int? limit = null,
        [Description("Comma-separated EExprToken names to include. Optional.")] string? tokenFilter = null,
        [Description("Only include control-flow, call, return, and script-end statements. Default: false.")] bool significantOnly = false,
        [Description("Include nested expression JSON up to this depth. Default: 0, max: 12.")] int? maxExpressionDepth = null,
        [Description("Maximum JSON bytes for each expanded expression. Default: 65536, max: 1048576.")] int? maxExpressionBytes = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")] string? sessionId = null)
    {
        if (!TryGetFunction(sessions, sessionId, classObjectPath, functionName, out var session, out _, out var function, out var error)) return error!;
        HashSet<string>? filters = null;
        if (!string.IsNullOrWhiteSpace(tokenFilter))
        {
            filters = tokenFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var invalid = filters.Where(token => !Enum.TryParse<EExprToken>(token, true, out _)).ToList();
            if (invalid.Count > 0) return Error("invalid_token_filter", $"Unknown EExprToken values: {string.Join(", ", invalid)}");
        }

        var result = KismetAnalyzer.Disassemble(
            classObjectPath,
            function!,
            Math.Max(0, cursor ?? 0),
            Math.Clamp(limit ?? 100, 1, 1000),
            filters,
            significantOnly,
            Math.Clamp(maxExpressionDepth ?? 0, 0, 12),
            Math.Clamp(maxExpressionBytes ?? 64 * 1024, 1024, 1024 * 1024));
        if (!session!.Provider.ReadScriptData) result.Diagnostics.Add("Script parsing was disabled for this session.");
        return JsonSerializer.Serialize(result, McpJsonOptions.Default);
    }

    [McpServerTool(Name = "get_kismet_cfg"), Description(
        "Build a bounded control-flow graph for one cooked Blueprint function using direct jumps, conditional jumps, switch offsets, computed jumps, and execution-flow stack tokens.")]
    public static string GetKismetCfg(
        Cue4ParseSessionRegistry sessions,
        [Description("Object path to the BlueprintGeneratedClass.")] string classObjectPath,
        [Description("Function name from list_blueprint_functions.")] string functionName,
        [Description("Skip the first N basic blocks. Default: 0.")] int? cursor = null,
        [Description("Maximum basic blocks. Default: 100, max: 1000.")] int? limit = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")] string? sessionId = null)
    {
        if (!TryGetFunction(sessions, sessionId, classObjectPath, functionName, out _, out _, out var function, out var error)) return error!;
        return JsonSerializer.Serialize(KismetAnalyzer.BuildCfg(classObjectPath, function!, Math.Max(0, cursor ?? 0), Math.Clamp(limit ?? 100, 1, 1000)), McpJsonOptions.Default);
    }

    [McpServerTool(Name = "get_kismet_call_graph"), Description(
        "Extract Blueprint call edges from nested Kismet expressions, including final, virtual, math, delegate, multicast, native-boundary, and event-to-ubergraph fast-call edges.")]
    public static string GetKismetCallGraph(
        Cue4ParseSessionRegistry sessions,
        [Description("Object path to the BlueprintGeneratedClass.")] string classObjectPath,
        [Description("Optional exact function name. If omitted, scans every function in the class.")] string? functionName = null,
        [Description("Skip the first N call edges. Default: 0.")] int? cursor = null,
        [Description("Maximum call edges. Default: 200, max: 2000.")] int? limit = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")] string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null) return Error("no_session", "No provider session found. Call init_provider first.");
        if (!session.Provider.ReadScriptData) return Error("script_data_disabled", "Reinitialize the provider with readScriptData=true before Kismet analysis.");
        if (!KismetAnalyzer.TryResolveClass(session, classObjectPath, out var blueprintClass, out var resolveError))
            return Error("class_not_found", resolveError!);
        return JsonSerializer.Serialize(KismetAnalyzer.BuildCallGraph(classObjectPath, blueprintClass!, functionName, Math.Max(0, cursor ?? 0), Math.Clamp(limit ?? 200, 1, 2000)), McpJsonOptions.Default);
    }

    [McpServerTool(Name = "get_kismet_def_use"), Description(
        "Return experimental approximate variable definition/use information for one cooked Blueprint function. Does not model aliases, all out/ref parameters, native side effects, or original Blueprint pins.")]
    public static string GetKismetDefUse(
        Cue4ParseSessionRegistry sessions,
        [Description("Object path to the BlueprintGeneratedClass.")] string classObjectPath,
        [Description("Function name from list_blueprint_functions.")] string functionName,
        [Description("Skip the first N variables. Default: 0.")] int? cursor = null,
        [Description("Maximum variables. Default: 100, max: 1000.")] int? limit = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")] string? sessionId = null)
    {
        if (!TryGetFunction(sessions, sessionId, classObjectPath, functionName, out _, out _, out var function, out var error)) return error!;
        return JsonSerializer.Serialize(KismetAnalyzer.BuildDefUse(classObjectPath, function!, Math.Max(0, cursor ?? 0), Math.Clamp(limit ?? 100, 1, 1000)), McpJsonOptions.Default);
    }

    [McpServerTool(Name = "decompile_blueprint_pseudo"), Description(
        "Run CUE4Parse's best-effort Blueprint pseudo-decompiler. This is a presentation view, may be partial, and is not the source of truth for CFG or data-flow analysis.")]
    public static string DecompileBlueprintPseudo(
        Cue4ParseSessionRegistry sessions,
        [Description("Object path to the BlueprintGeneratedClass.")] string classObjectPath,
        [Description("Maximum pseudo-code characters. Default: 262144, max: 2097152.")] int? maxCharacters = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")] string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null) return Error("no_session", "No provider session found. Call init_provider first.");
        if (!session.Provider.ReadScriptData) return Error("script_data_disabled", "Reinitialize the provider with readScriptData=true before Kismet analysis.");
        if (!KismetAnalyzer.TryResolveClass(session, classObjectPath, out var blueprintClass, out var resolveError))
            return Error("class_not_found", resolveError!);
        return JsonSerializer.Serialize(KismetAnalyzer.DecompilePseudo(classObjectPath, blueprintClass!, Math.Clamp(maxCharacters ?? 256 * 1024, 1024, 2 * 1024 * 1024)), McpJsonOptions.Default);
    }

    private static bool TryGetFunction(
        Cue4ParseSessionRegistry sessions,
        string? sessionId,
        string classObjectPath,
        string functionName,
        out Cue4ParseSession? session,
        out UClass? blueprintClass,
        out UFunction? function,
        out string? error)
    {
        session = sessions.GetSession(sessionId);
        blueprintClass = null;
        function = null;
        if (session == null)
        {
            error = Error("no_session", "No provider session found. Call init_provider first.");
            return false;
        }
        if (!session.Provider.ReadScriptData)
        {
            error = Error("script_data_disabled", "Reinitialize the provider with readScriptData=true before Kismet analysis.");
            return false;
        }
        if (!KismetAnalyzer.TryResolveClass(session, classObjectPath, out blueprintClass, out var classError))
        {
            error = Error("class_not_found", classError!);
            return false;
        }
        if (!KismetAnalyzer.TryResolveFunction(blueprintClass!, functionName, out function, out var functionError))
        {
            error = Error("function_not_found", functionError!);
            return false;
        }
        error = null;
        return true;
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { ok = false, errorCode = code, message }, McpJsonOptions.Default);
}
