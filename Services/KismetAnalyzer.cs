using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
using CUE4Parse.Mcp.Dtos;
using CUE4Parse.UE4.Kismet;
using CUE4Parse.UE4.Objects.UObject;

namespace CUE4Parse.Mcp.Services;

public sealed class KismetExpressionVisit
{
    public required KismetExpression Expression { get; init; }
    public required string Path { get; init; }
    public int Depth { get; init; }
}

public static class KismetAnalyzer
{
    private static readonly object PseudoCodeLock = new();

    public static bool TryResolveClass(Cue4ParseSession session, string classObjectPath, out UClass? blueprintClass, out string? error)
    {
        blueprintClass = null;
        if (!PackageObjectResolver.TryResolve(session, classObjectPath, null, null, null, out var target, out error)) return false;
        blueprintClass = target!.Object as UClass;
        if (blueprintClass != null) return true;
        error = $"Object '{target.ObjectPath}' has type '{target.Object.ExportType}', not UClass or BlueprintGeneratedClass.";
        return false;
    }

    public static bool TryResolveFunction(UClass blueprintClass, string functionName, out UFunction? function, out string? error)
    {
        function = null;
        var pair = blueprintClass.FuncMap.FirstOrDefault(entry => entry.Key.Text.Equals(functionName, StringComparison.OrdinalIgnoreCase));
        if (pair.Value == null)
        {
            error = $"Function '{functionName}' was not found in class '{blueprintClass.Name}'.";
            return false;
        }
        if (!pair.Value.TryLoad<UFunction>(out function))
        {
            error = $"Function '{functionName}' could not be loaded from class '{blueprintClass.Name}'.";
            return false;
        }
        error = null;
        return true;
    }

    public static BlueprintFunctionDto DescribeFunction(UFunction function, bool isUberGraph)
    {
        var script = function.ScriptBytecode ?? [];
        return new BlueprintFunctionDto
        {
            Name = function.Name,
            ObjectPath = function.GetPathName(),
            ExportIndex = function.Owner == null ? null : PackageObjectResolver.FindExportIndex(function.Owner, function),
            FunctionFlags = function.FunctionFlags.ToString(),
            IsNative = function.FunctionFlags.HasFlag(EFunctionFlags.FUNC_Native),
            IsUberGraph = isUberGraph || function.Name.StartsWith("ExecuteUbergraph_", StringComparison.OrdinalIgnoreCase),
            StatementCount = script.Length,
            HasScriptBytecode = script.Length > 0,
            ScriptComplete = IsScriptComplete(script),
            EventGraphFunction = function.EventGraphFunction is { IsNull: false } eventGraph ? eventGraph.ResolvedObject?.GetPathName() ?? eventGraph.ToString() : null,
            EventGraphCallOffset = function.EventGraphCallOffset
        };
    }

    public static KismetDisassemblyResultDto Disassemble(
        string classObjectPath,
        UFunction function,
        int cursor,
        int limit,
        HashSet<string>? tokenFilter,
        bool significantOnly,
        int expressionDepth,
        int expressionBytes)
    {
        var script = function.ScriptBytecode ?? [];
        var indexed = script.Select((expression, ordinal) => (expression, ordinal));
        if (tokenFilter is { Count: > 0 }) indexed = indexed.Where(item => tokenFilter.Contains(item.expression.Token.ToString()));
        if (significantOnly) indexed = indexed.Where(item => IsSignificant(item.expression));
        var filtered = indexed.ToList();
        var page = filtered.Skip(cursor).Take(limit).ToList();
        var result = new KismetDisassemblyResultDto
        {
            ClassObjectPath = classObjectPath,
            FunctionName = function.Name,
            FunctionFlags = function.FunctionFlags.ToString(),
            TotalStatements = filtered.Count,
            ReturnedStatements = page.Count,
            NextCursor = cursor + limit < filtered.Count ? cursor + limit : null,
            ScriptComplete = IsScriptComplete(script)
        };

        if (script.Length == 0) result.Diagnostics.Add("Function has no parsed script bytecode.");
        if (script.Length > 0 && !result.ScriptComplete) result.Diagnostics.Add("Parsed script does not end with EX_EndOfScript and may be partial.");
        foreach (var (expression, ordinal) in page)
        {
            var visits = Walk(expression).ToList();
            var dto = new KismetStatementDto
            {
                Ordinal = ordinal,
                StatementIndex = expression.StatementIndex,
                Token = expression.Token.ToString(),
                Summary = Summarize(expression),
                Targets = GetTargets(expression).Distinct().ToList(),
                Calls = visits.Select(visit => TryGetCall(visit.Expression)).Where(call => call != null).Select(call => call!.Value.Callee).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Variables = visits.Select(visit => TryGetVariable(visit.Expression)).Where(variable => variable != null).Select(variable => variable!.Value.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                ExpressionDepth = visits.Count == 0 ? 0 : visits.Max(visit => visit.Depth),
                ExpressionCount = visits.Count
            };
            if (expressionDepth > 0)
            {
                var serialized = BoundedJsonSerializer.Serialize(expression, expressionDepth, expressionBytes);
                dto.Expression = serialized.Json;
                dto.ExpressionTruncated = serialized.Truncated || serialized.Error != null;
                if (serialized.Error != null) result.Diagnostics.Add($"Statement {expression.StatementIndex}: {serialized.Error}");
            }
            result.Statements.Add(dto);
        }
        return result;
    }

    public static KismetCfgResultDto BuildCfg(string classObjectPath, UFunction function, int cursor, int limit)
    {
        var result = new KismetCfgResultDto { ClassObjectPath = classObjectPath, FunctionName = function.Name };
        var script = function.ScriptBytecode ?? [];
        if (script.Length == 0)
        {
            result.Diagnostics.Add("Function has no parsed script bytecode.");
            return result;
        }

        var offsetToOrdinal = script.Select((expression, ordinal) => (expression.StatementIndex, ordinal))
            .GroupBy(item => item.StatementIndex).ToDictionary(group => group.Key, group => group.First().ordinal);
        var leaders = new HashSet<int> { 0 };
        for (var i = 0; i < script.Length; i++)
        {
            foreach (var target in GetTargets(script[i]))
            {
                if (offsetToOrdinal.TryGetValue(target, out var targetOrdinal)) leaders.Add(targetOrdinal);
                else result.Diagnostics.Add($"Target offset {target} from statement {script[i].StatementIndex} is not a top-level statement boundary.");
            }
            if (IsBlockTerminator(script[i]) && i + 1 < script.Length) leaders.Add(i + 1);
        }

        var leaderOrdinals = leaders.OrderBy(value => value).ToArray();
        var blocks = new List<KismetBasicBlockDto>();
        var blockByOffset = new Dictionary<int, int>();
        for (var i = 0; i < leaderOrdinals.Length; i++)
        {
            var start = leaderOrdinals[i];
            var endExclusive = i + 1 < leaderOrdinals.Length ? leaderOrdinals[i + 1] : script.Length;
            var statements = script[start..endExclusive].Select(expression => expression.StatementIndex).ToList();
            var block = new KismetBasicBlockDto
            {
                Id = i,
                StartIndex = statements[0],
                EndIndex = statements[^1],
                StatementIndices = statements,
                IsEntry = i == 0,
                IsExit = script[endExclusive - 1] is EX_Return or EX_EndOfScript
            };
            blocks.Add(block);
            foreach (var statement in statements) blockByOffset[statement] = block.Id;
        }

        var edges = new List<KismetControlFlowEdgeDto>();
        foreach (var block in blocks)
        {
            var lastOrdinal = offsetToOrdinal[block.EndIndex];
            var expression = script[lastOrdinal];
            var nextBlock = block.Id + 1 < blocks.Count ? blocks[block.Id + 1].Id : (int?)null;
            switch (expression)
            {
                case EX_JumpIfNot conditional:
                    AddTargetEdge(edges, block.Id, conditional.CodeOffset, "branch_false", "exact", Summarize(conditional.BooleanExpression), blockByOffset);
                    AddFallthrough(edges, block.Id, nextBlock, blocks, "branch_true", Summarize(conditional.BooleanExpression));
                    break;
                case EX_Jump jump:
                    AddTargetEdge(edges, block.Id, jump.CodeOffset, "jump", "exact", null, blockByOffset);
                    break;
                case EX_ComputedJump computed:
                    if (computed.CodeOffsetExpression is EX_SkipOffsetConst constant)
                        AddTargetEdge(edges, block.Id, Convert.ToInt32(constant.Value), "computed_jump", "resolved", null, blockByOffset);
                    else
                        edges.Add(new KismetControlFlowEdgeDto { FromBlock = block.Id, Kind = "computed_jump", Accuracy = "unknown", Condition = Summarize(computed.CodeOffsetExpression) });
                    break;
                case EX_PushExecutionFlow push:
                    AddFallthrough(edges, block.Id, nextBlock, blocks, "fallthrough", null);
                    AddTargetEdge(edges, block.Id, push.PushingAddress, "deferred_flow_target", "exact", null, blockByOffset);
                    break;
                case EX_PopExecutionFlow:
                    edges.Add(new KismetControlFlowEdgeDto { FromBlock = block.Id, Kind = "pop_execution_flow", Accuracy = "unknown" });
                    break;
                case EX_PopExecutionFlowIfNot popIfNot:
                    AddFallthrough(edges, block.Id, nextBlock, blocks, "branch_true", Summarize(popIfNot.BooleanExpression));
                    edges.Add(new KismetControlFlowEdgeDto { FromBlock = block.Id, Kind = "pop_flow_if_false", Accuracy = "unknown", Condition = Summarize(popIfNot.BooleanExpression) });
                    break;
                case EX_SwitchValue switchValue:
                    foreach (var switchCase in switchValue.Cases)
                        AddTargetEdge(edges, block.Id, switchCase.NextOffset, "switch_case_next", "exact", Summarize(switchCase.CaseIndexValueTerm), blockByOffset);
                    AddTargetEdge(edges, block.Id, switchValue.EndGotoOffset, "switch_end", "exact", null, blockByOffset);
                    AddFallthrough(edges, block.Id, nextBlock, blocks, "fallthrough", null);
                    break;
                case EX_Return or EX_EndOfScript:
                    break;
                default:
                    AddFallthrough(edges, block.Id, nextBlock, blocks, "fallthrough", null);
                    break;
            }
        }

        result.TotalBlocks = blocks.Count;
        result.TotalEdges = edges.Count;
        var pageBlocks = blocks.Skip(cursor).Take(limit).ToList();
        var ids = pageBlocks.Select(block => block.Id).ToHashSet();
        result.Blocks = pageBlocks;
        result.Edges = edges.Where(edge => ids.Contains(edge.FromBlock)).ToList();
        result.NextCursor = cursor + limit < blocks.Count ? cursor + limit : null;
        return result;
    }

    public static KismetCallGraphResultDto BuildCallGraph(string classObjectPath, UClass blueprintClass, string? functionFilter, int cursor, int limit)
    {
        var result = new KismetCallGraphResultDto { ClassObjectPath = classObjectPath, FunctionFilter = functionFilter };
        var functions = LoadFunctions(blueprintClass, result.Diagnostics)
            .Where(function => string.IsNullOrWhiteSpace(functionFilter) || function.Name.Equals(functionFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!string.IsNullOrWhiteSpace(functionFilter) && functions.Count == 0)
        {
            result.Diagnostics.Add($"Function '{functionFilter}' was not found or could not be loaded.");
            return result;
        }

        var edges = new List<KismetCallEdgeDto>();
        foreach (var function in functions)
        {
            if (function.EventGraphFunction is { IsNull: false } eventGraph)
            {
                edges.Add(new KismetCallEdgeDto
                {
                    Caller = function.Name,
                    Callee = eventGraph.ResolvedObject?.Name.Text ?? eventGraph.ToString(),
                    Kind = "event_graph_fast_call",
                    StatementIndex = function.EventGraphCallOffset,
                    ResolvedObjectPath = eventGraph.ResolvedObject?.GetPathName(),
                    Accuracy = "exact"
                });
            }
            foreach (var root in function.ScriptBytecode ?? [])
            {
                foreach (var visit in Walk(root))
                {
                    var call = TryGetCall(visit.Expression);
                    if (call == null) continue;
                    edges.Add(new KismetCallEdgeDto
                    {
                        Caller = function.Name,
                        Callee = call.Value.Callee,
                        Kind = call.Value.Kind,
                        StatementIndex = visit.Expression.StatementIndex,
                        ResolvedObjectPath = call.Value.ResolvedPath,
                        NativeBoundary = call.Value.NativeBoundary,
                        Accuracy = call.Value.Accuracy
                    });
                }
            }
        }

        edges = edges.GroupBy(edge => (edge.Caller, edge.Callee, edge.Kind, edge.StatementIndex))
            .Select(group => group.First())
            .OrderBy(edge => edge.Caller, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.StatementIndex)
            .ToList();
        result.TotalEdges = edges.Count;
        result.Edges = edges.Skip(cursor).Take(limit).ToList();
        result.NextCursor = cursor + limit < edges.Count ? cursor + limit : null;
        return result;
    }

    public static KismetDefUseResultDto BuildDefUse(string classObjectPath, UFunction function, int cursor, int limit)
    {
        var result = new KismetDefUseResultDto { ClassObjectPath = classObjectPath, FunctionName = function.Name };
        var data = new Dictionary<(string Name, string Kind), (HashSet<int> Definitions, HashSet<int> Uses)>();
        foreach (var root in function.ScriptBytecode ?? [])
        {
            var definitions = new HashSet<KismetExpression>(ReferenceEqualityComparer.Instance);
            foreach (var visit in Walk(root))
            {
                switch (visit.Expression)
                {
                    case EX_Let let:
                        AddDefinitionExpression(let.Variable, root.StatementIndex, data, definitions);
                        break;
                    case EX_LetBase letBase:
                        AddDefinitionExpression(letBase.Variable, root.StatementIndex, data, definitions);
                        break;
                    case EX_LetValueOnPersistentFrame persistent:
                        AddDataPoint(persistent.DestinationProperty.ToString(), "persistent_frame", root.StatementIndex, true, data);
                        break;
                }
            }
            foreach (var visit in Walk(root))
            {
                if (visit.Expression is not EX_VariableBase variable || definitions.Contains(variable)) continue;
                AddDataPoint(variable.Variable.ToString(), GetVariableKind(variable), root.StatementIndex, false, data);
            }
        }

        var variables = data.Select(entry => new KismetDefUseDto
        {
            Variable = entry.Key.Name,
            Kind = entry.Key.Kind,
            Definitions = entry.Value.Definitions.OrderBy(value => value).ToList(),
            Uses = entry.Value.Uses.OrderBy(value => value).ToList(),
            Accuracy = "resolved"
        }).OrderBy(variable => variable.Variable, StringComparer.OrdinalIgnoreCase).ThenBy(variable => variable.Kind).ToList();
        result.TotalVariables = variables.Count;
        result.Variables = variables.Skip(cursor).Take(limit).ToList();
        result.NextCursor = cursor + limit < variables.Count ? cursor + limit : null;
        result.Diagnostics.Add("Def-use is approximate: aliases, native side effects, dynamic dispatch, and out/ref call parameters are not fully modeled.");
        return result;
    }

    public static BlueprintPseudoCodeResultDto DecompilePseudo(string classObjectPath, UClass blueprintClass, int maxCharacters)
    {
        var result = new BlueprintPseudoCodeResultDto { ClassObjectPath = classObjectPath };
        try
        {
            string code;
            lock (PseudoCodeLock) code = blueprintClass.DecompileBlueprintToPseudo();
            result.TotalCharacters = code.Length;
            result.Partial = code.Length > maxCharacters;
            if (!result.Partial)
            {
                result.Code = code;
                result.ReturnedCharacters = code.Length;
            }
            else
            {
                result.Diagnostics.Add("Pseudo code exceeded maxCharacters; use disassembly and graph tools for bounded queries.");
            }
        }
        catch (Exception ex)
        {
            result.Partial = true;
            result.Diagnostics.Add(ex.Message);
        }
        return result;
    }

    public static IEnumerable<KismetExpressionVisit> Walk(KismetExpression root)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var visit in WalkExpression(root, "$", 0, visited)) yield return visit;
    }

    private static IEnumerable<KismetExpressionVisit> WalkExpression(KismetExpression expression, string path, int depth, HashSet<object> visited)
    {
        if (depth > 64 || !visited.Add(expression)) yield break;
        yield return new KismetExpressionVisit { Expression = expression, Path = path, Depth = depth };
        foreach (var (child, childPath) in GetChildExpressions(expression, path, visited, 0))
        {
            foreach (var visit in WalkExpression(child, childPath, depth + 1, visited)) yield return visit;
        }
    }

    private static IEnumerable<(KismetExpression Expression, string Path)> GetChildExpressions(object value, string path, HashSet<object> visited, int containerDepth)
    {
        if (containerDepth > 8) yield break;
        foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            var childValue = field.GetValue(value);
            var childPath = $"{path}.{field.Name}";
            if (childValue is KismetExpression expression)
            {
                yield return (expression, childPath);
                continue;
            }
            if (childValue is string or null) continue;
            if (childValue is IEnumerable enumerable)
            {
                var index = 0;
                foreach (var item in enumerable)
                {
                    if (item is KismetExpression itemExpression) yield return (itemExpression, $"{childPath}[{index}]");
                    else if (item != null && IsKismetContainer(item.GetType()))
                    {
                        foreach (var nested in GetChildExpressions(item, $"{childPath}[{index}]", visited, containerDepth + 1)) yield return nested;
                    }
                    index++;
                }
            }
            else if (IsKismetContainer(childValue.GetType()))
            {
                foreach (var nested in GetChildExpressions(childValue, childPath, visited, containerDepth + 1)) yield return nested;
            }
        }
    }

    private static bool IsKismetContainer(Type type) => type.Namespace?.StartsWith("CUE4Parse.UE4.Kismet", StringComparison.Ordinal) == true;

    private static List<UFunction> LoadFunctions(UClass blueprintClass, List<string> diagnostics)
    {
        var functions = new List<UFunction>();
        foreach (var pair in blueprintClass.FuncMap)
        {
            if (pair.Value.TryLoad<UFunction>(out var function)) functions.Add(function);
            else diagnostics.Add($"Function '{pair.Key.Text}' could not be loaded.");
        }
        return functions;
    }

    private static bool IsScriptComplete(IReadOnlyList<KismetExpression> script) => script.Count > 0 && script[^1] is EX_EndOfScript;

    private static bool IsSignificant(KismetExpression expression) => expression is EX_Jump or EX_ComputedJump or EX_PushExecutionFlow or EX_PopExecutionFlow or EX_PopExecutionFlowIfNot or EX_SwitchValue or EX_Return or EX_EndOfScript || Walk(expression).Any(visit => TryGetCall(visit.Expression) != null);

    private static bool IsBlockTerminator(KismetExpression expression) => expression is EX_Jump or EX_ComputedJump or EX_PopExecutionFlow or EX_PopExecutionFlowIfNot or EX_SwitchValue or EX_Return or EX_EndOfScript;

    private static IEnumerable<int> GetTargets(KismetExpression expression)
    {
        switch (expression)
        {
            case EX_Jump jump:
                yield return Convert.ToInt32(jump.CodeOffset);
                break;
            case EX_ComputedJump { CodeOffsetExpression: EX_SkipOffsetConst constant }:
                yield return Convert.ToInt32(constant.Value);
                break;
            case EX_PushExecutionFlow push:
                yield return Convert.ToInt32(push.PushingAddress);
                break;
            case EX_SwitchValue switchValue:
                yield return Convert.ToInt32(switchValue.EndGotoOffset);
                foreach (var switchCase in switchValue.Cases) yield return Convert.ToInt32(switchCase.NextOffset);
                break;
        }
    }

    private static string Summarize(KismetExpression expression) => expression switch
    {
        EX_JumpIfNot jump => $"if not ({Summarize(jump.BooleanExpression)}) goto {jump.CodeOffset}",
        EX_Jump jump => $"goto {jump.CodeOffset}",
        EX_ComputedJump computed => $"computed goto {Summarize(computed.CodeOffsetExpression)}",
        EX_PushExecutionFlow push => $"push flow {push.PushingAddress}",
        EX_PopExecutionFlowIfNot conditional => $"pop flow if not ({Summarize(conditional.BooleanExpression)})",
        EX_PopExecutionFlow => "pop execution flow",
        EX_CallMath call => $"call math {GetFinalCallName(call)}",
        EX_LocalFinalFunction call => $"call local final {GetFinalCallName(call)}",
        EX_FinalFunction call => $"call final {GetFinalCallName(call)}",
        EX_LocalVirtualFunction call => $"call local virtual {call.VirtualFunctionName.Text}",
        EX_VirtualFunction call => $"call virtual {call.VirtualFunctionName.Text}",
        EX_Let let => $"{Summarize(let.Variable)} = {Summarize(let.Assignment)}",
        EX_LetBase let => $"{Summarize(let.Variable)} = {Summarize(let.Assignment)}",
        EX_LetValueOnPersistentFrame let => $"{let.DestinationProperty} = {Summarize(let.AssignmentExpression)}",
        EX_VariableBase variable => variable.Variable.ToString(),
        EX_Return returned => $"return {Summarize(returned.ReturnExpression)}",
        EX_SwitchValue => "switch value",
        EX_EndOfScript => "end of script",
        _ => expression.Token.ToString()
    };

    private static string GetFinalCallName(EX_FinalFunction call) => call.StackNode.ResolvedObject?.GetPathName() ?? call.StackNode.ToString();

    private static (string Callee, string Kind, string? ResolvedPath, bool NativeBoundary, string Accuracy)? TryGetCall(KismetExpression expression)
    {
        switch (expression)
        {
            case EX_CallMath call:
                return CreateFinalCall(call, "math");
            case EX_LocalFinalFunction call:
                return CreateFinalCall(call, "local_final");
            case EX_FinalFunction call:
                return CreateFinalCall(call, "final");
            case EX_LocalVirtualFunction call:
                return (call.VirtualFunctionName.Text, "local_virtual", null, false, "resolved");
            case EX_VirtualFunction call:
                return (call.VirtualFunctionName.Text, "virtual", null, false, "resolved");
            case EX_CallMulticastDelegate call:
                return (call.StackNode.ResolvedObject?.Name.Text ?? call.StackNode.ToString(), "multicast", call.StackNode.ResolvedObject?.GetPathName(), false, "heuristic");
            case EX_BindDelegate bind:
                return (bind.FunctionName.Text, "bind_delegate", null, false, "resolved");
            case EX_InstanceDelegate instance:
                return (instance.FunctionName.Text, "instance_delegate", null, false, "resolved");
            default:
                return null;
        }
    }

    private static (string Callee, string Kind, string? ResolvedPath, bool NativeBoundary, string Accuracy) CreateFinalCall(EX_FinalFunction call, string kind)
    {
        var resolved = call.StackNode.ResolvedObject;
        var loaded = call.StackNode.Load<UFunction>();
        var path = resolved?.GetPathName();
        var callee = resolved?.Name.Text ?? call.StackNode.ToString();
        var native = loaded?.FunctionFlags.HasFlag(EFunctionFlags.FUNC_Native) == true || path?.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase) == true;
        return (callee, kind, path, native, resolved == null ? "heuristic" : "resolved");
    }

    private static (string Name, string Kind)? TryGetVariable(KismetExpression expression) => expression switch
    {
        EX_VariableBase variable => (variable.Variable.ToString(), GetVariableKind(variable)),
        EX_LetValueOnPersistentFrame persistent => (persistent.DestinationProperty.ToString(), "persistent_frame"),
        _ => null
    };

    private static string GetVariableKind(EX_VariableBase variable) => variable switch
    {
        EX_LocalOutVariable => "local_out",
        EX_LocalVariable => "local",
        EX_InstanceVariable => "instance",
        EX_DefaultVariable => "default",
        EX_ClassSparseDataVariable => "class_sparse",
        _ => "variable"
    };

    private static void AddDefinitionExpression(KismetExpression expression, int statementIndex, Dictionary<(string Name, string Kind), (HashSet<int> Definitions, HashSet<int> Uses)> data, HashSet<KismetExpression> definitions)
    {
        foreach (var visit in Walk(expression))
        {
            if (visit.Expression is not EX_VariableBase variable) continue;
            definitions.Add(variable);
            AddDataPoint(variable.Variable.ToString(), GetVariableKind(variable), statementIndex, true, data);
        }
    }

    private static void AddDataPoint(string name, string kind, int statementIndex, bool definition, Dictionary<(string Name, string Kind), (HashSet<int> Definitions, HashSet<int> Uses)> data)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Equals("None", StringComparison.OrdinalIgnoreCase)) return;
        var key = (name, kind);
        if (!data.TryGetValue(key, out var entry)) entry = ([], []);
        if (definition) entry.Definitions.Add(statementIndex); else entry.Uses.Add(statementIndex);
        data[key] = entry;
    }

    private static void AddTargetEdge(List<KismetControlFlowEdgeDto> edges, int fromBlock, long target, string kind, string accuracy, string? condition, IReadOnlyDictionary<int, int> blockByOffset)
    {
        var targetInt = Convert.ToInt32(target);
        edges.Add(new KismetControlFlowEdgeDto
        {
            FromBlock = fromBlock,
            ToBlock = blockByOffset.TryGetValue(targetInt, out var block) ? block : null,
            TargetStatementIndex = targetInt,
            Kind = kind,
            Condition = condition,
            Accuracy = blockByOffset.ContainsKey(targetInt) ? accuracy : "partial"
        });
    }

    private static void AddFallthrough(List<KismetControlFlowEdgeDto> edges, int fromBlock, int? nextBlock, IReadOnlyList<KismetBasicBlockDto> blocks, string kind, string? condition)
    {
        if (nextBlock == null) return;
        edges.Add(new KismetControlFlowEdgeDto
        {
            FromBlock = fromBlock,
            ToBlock = nextBlock,
            TargetStatementIndex = blocks[nextBlock.Value].StartIndex,
            Kind = kind,
            Condition = condition,
            Accuracy = "exact"
        });
    }
}
