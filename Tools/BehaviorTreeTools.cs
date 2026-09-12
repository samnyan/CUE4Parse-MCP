using System.ComponentModel;
using System.Text.Json;
using CUE4Parse.Mcp.Services;
using ModelContextProtocol.Server;

namespace CUE4Parse.Mcp.Tools;

[McpServerToolType]
public static class BehaviorTreeTools
{
    [McpServerTool(Name = "analyze_behavior_tree"), Description(
        "Reconstruct the cooked runtime topology of a BehaviorTree, including ordered composite children, tasks, edge-bound decorators and decorator logic, services, Blackboard, and parent cross-check diagnostics.")]
    public static string AnalyzeBehaviorTree(
        Cue4ParseSessionRegistry sessions,
        [Description("BehaviorTree object path. Use this or packagePath/exportIndex.")] string? objectPath = null,
        [Description("Package path when selecting the BehaviorTree by export index.")] string? packagePath = null,
        [Description("Zero-based BehaviorTree export index inside packagePath.")] int? exportIndex = null,
        [Description("Maximum composite recursion depth. Default: 64, max: 256.")] int? maxDepth = null,
        [Description("Maximum nodes to return. Default: 1000, max: 10000.")] int? maxNodes = null,
        [Description("Include bounded non-structural node properties such as task commands and decorator parameters. Default: true.")] bool includeNodeProperties = true,
        [Description("Maximum JSON bytes per node property. Default: 16384, max: 262144.")] int? maxBytesPerProperty = null,
        [Description("Session ID from init_provider. If omitted, uses the most recent session.")] string? sessionId = null)
    {
        var session = sessions.GetSession(sessionId);
        if (session == null) return Error("no_session", "No provider session found. Call init_provider first.");
        if (!PackageObjectResolver.TryResolve(session, objectPath, packagePath, exportIndex, null, out var target, out var resolveError))
            return Error("behavior_tree_not_found", resolveError!);
        if (!target!.Object.ExportType.Contains("BehaviorTree", StringComparison.OrdinalIgnoreCase))
            return Error("not_a_behavior_tree", $"Object '{target.ObjectPath}' has type '{target.Object.ExportType}', not BehaviorTree.");

        try
        {
            var result = BehaviorTreeAnalyzer.Analyze(
                target.Object,
                Math.Clamp(maxDepth ?? 64, 1, 256),
                Math.Clamp(maxNodes ?? 1000, 1, 10000),
                includeNodeProperties,
                Math.Clamp(maxBytesPerProperty ?? 16 * 1024, 1024, 256 * 1024));
            return JsonSerializer.Serialize(result, McpJsonOptions.Default);
        }
        catch (Exception ex)
        {
            return Error("behavior_tree_analysis_failed", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { ok = false, errorCode = code, message }, McpJsonOptions.Default);
}
