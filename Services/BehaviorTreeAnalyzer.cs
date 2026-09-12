using System.Text.Json.Nodes;
using CUE4Parse.Mcp.Dtos;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;

namespace CUE4Parse.Mcp.Services;

public static class BehaviorTreeAnalyzer
{
    public static BehaviorTreeAnalysisResultDto Analyze(UObject tree, int maxDepth, int maxNodes, bool includeNodeProperties, int maxPropertyBytes)
    {
        var result = new BehaviorTreeAnalysisResultDto { ObjectPath = tree.GetPathName() };
        var rootValue = GetProperty(tree, "RootNode");
        var root = LoadObject(rootValue);
        result.BlackboardPath = GetReferencePath(GetProperty(tree, "BlackboardAsset"));
        result.RootDecorators = GetObjectCollection(GetProperty(tree, "RootDecorators")).Select(GetId).ToList();
        result.RootDecoratorLogic = GetLogicCollection(GetProperty(tree, "RootDecoratorOps"));
        if (root == null)
        {
            result.Diagnostics.Add("RootNode was absent or could not be resolved.");
            return result;
        }

        result.RootNodeId = GetId(root);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodeById = new Dictionary<string, BehaviorTreeNodeDto>(StringComparer.OrdinalIgnoreCase);

        void AddNode(UObject node)
        {
            var id = GetId(node);
            if (nodeById.ContainsKey(id)) return;
            var dto = CreateNode(node, includeNodeProperties, maxPropertyBytes);
            nodeById[id] = dto;
            result.Nodes.Add(dto);
        }

        void Visit(UObject node, int depth)
        {
            var id = GetId(node);
            AddNode(node);
            if (!visited.Add(id)) return;
            if (result.Nodes.Count >= maxNodes)
            {
                result.Truncated = true;
                return;
            }
            if (depth >= maxDepth)
            {
                result.Truncated = true;
                return;
            }
            if (!GetKind(node.ExportType).Equals("composite", StringComparison.Ordinal)) return;

            var children = PropertyPathResolver.GetCollectionValues(GetProperty(node, "Children"));
            for (var childIndex = 0; childIndex < children.Count; childIndex++)
            {
                var childHolder = PropertyPathResolver.Unwrap(children[childIndex]) as IPropertyHolder;
                if (childHolder == null)
                {
                    result.Diagnostics.Add($"{id}.Children[{childIndex}] could not be interpreted as FBTCompositeChild.");
                    continue;
                }

                var child = LoadObject(GetProperty(childHolder, "ChildComposite")) ?? LoadObject(GetProperty(childHolder, "ChildTask"));
                if (child == null)
                {
                    result.UnresolvedReferences.Add($"{id}.Children[{childIndex}]");
                    continue;
                }

                AddNode(child);
                var decorators = GetObjectCollection(GetProperty(childHolder, "Decorators"));
                foreach (var decorator in decorators) AddNode(decorator);
                result.Edges.Add(new BehaviorTreeEdgeDto
                {
                    ParentId = id,
                    ChildId = GetId(child),
                    ChildIndex = childIndex,
                    ChildKind = GetKind(child.ExportType),
                    Decorators = decorators.Select(GetId).ToList(),
                    DecoratorLogic = GetLogicCollection(GetProperty(childHolder, "DecoratorOps"))
                });
                Visit(child, depth + 1);
            }
        }

        foreach (var decorator in GetObjectCollection(GetProperty(tree, "RootDecorators"))) AddNode(decorator);
        Visit(root, 0);
        result.TotalNodes = result.Nodes.Count;
        result.TotalEdges = result.Edges.Count;
        result.Nodes = result.Nodes.OrderBy(node => node.ExportIndex ?? int.MaxValue).ThenBy(node => node.Id, StringComparer.OrdinalIgnoreCase).ToList();
        result.Edges = result.Edges.OrderBy(edge => edge.ParentId, StringComparer.OrdinalIgnoreCase).ThenBy(edge => edge.ChildIndex).ToList();

        foreach (var node in result.Nodes)
        {
            if (node.ParentId == null || node.Id == result.RootNodeId || node.Kind is "decorator" or "service") continue;
            if (!result.Edges.Any(edge => edge.ParentId.Equals(node.ParentId, StringComparison.OrdinalIgnoreCase) && edge.ChildId.Equals(node.Id, StringComparison.OrdinalIgnoreCase)))
                result.Diagnostics.Add($"ParentNode cross-check did not find edge {node.ParentId} -> {node.Id}.");
        }
        return result;
    }

    private static BehaviorTreeNodeDto CreateNode(UObject node, bool includeProperties, int maxPropertyBytes)
    {
        var dto = new BehaviorTreeNodeDto
        {
            Id = GetId(node),
            ExportIndex = node.Owner == null ? null : PackageObjectResolver.FindExportIndex(node.Owner, node),
            Name = node.Name,
            Type = node.ExportType,
            Kind = GetKind(node.ExportType),
            ParentId = GetReferencePath(GetProperty(node, "ParentNode")),
            Services = GetObjectCollection(GetProperty(node, "Services")).Select(GetId).ToList(),
            NativePolicyUnknown = GetKind(node.ExportType) == "composite" && node.ExportType is not ("BTComposite_Selector" or "BTComposite_Sequence" or "BTComposite_SimpleParallel")
        };
        if (!includeProperties) return dto;

        var excluded = new HashSet<string>(["Children", "Services", "ParentNode", "TreeAsset"], StringComparer.OrdinalIgnoreCase);
        foreach (var property in node.Properties.Where(property => !excluded.Contains(property.Name.Text)))
        {
            var serialized = BoundedJsonSerializer.Serialize(PropertyPathResolver.Unwrap(property), 8, maxPropertyBytes);
            if (serialized.Truncated)
            {
                dto.TruncatedProperties.Add(property.Name.Text);
                continue;
            }
            if (serialized.Error == null) dto.Properties[property.Name.Text] = serialized.Json;
        }
        return dto;
    }

    private static object? GetProperty(IPropertyHolder holder, string name) => PropertyPathResolver.GetPropertyValue(holder, name);

    private static UObject? LoadObject(object? value)
    {
        value = PropertyPathResolver.Unwrap(value);
        return value switch
        {
            UObject obj => obj,
            FPackageIndex index => index.Load() as UObject,
            ResolvedObject resolved => resolved.Load() as UObject,
            _ => null
        };
    }

    private static List<UObject> GetObjectCollection(object? value)
    {
        var result = new List<UObject>();
        foreach (var item in PropertyPathResolver.GetCollectionValues(value))
        {
            var loaded = LoadObject(item);
            if (loaded != null) result.Add(loaded);
        }
        return result;
    }

    private static List<BehaviorTreeDecoratorLogicDto> GetLogicCollection(object? value)
    {
        var result = new List<BehaviorTreeDecoratorLogicDto>();
        foreach (var item in PropertyPathResolver.GetCollectionValues(value))
        {
            if (PropertyPathResolver.Unwrap(item) is not IPropertyHolder holder) continue;
            var operation = PropertyPathResolver.Unwrap(GetProperty(holder, "Operation"));
            var number = PropertyPathResolver.Unwrap(GetProperty(holder, "Number"));
            result.Add(new BehaviorTreeDecoratorLogicDto
            {
                Operation = GetDisplayValue(operation),
                Number = TryConvertInt(number)
            });
        }
        return result;
    }

    private static int? TryConvertInt(object? value)
    {
        try { return value == null ? null : Convert.ToInt32(value); }
        catch { return null; }
    }

    private static string GetDisplayValue(object? value) => value switch
    {
        null => "",
        FName name => name.Text.Contains("::", StringComparison.Ordinal) ? name.Text[(name.Text.LastIndexOf("::", StringComparison.Ordinal) + 2)..] : name.Text,
        _ => value.ToString() ?? ""
    };

    private static string? GetReferencePath(object? value)
    {
        value = PropertyPathResolver.Unwrap(value);
        return value switch
        {
            UObject obj => obj.GetPathName(),
            FPackageIndex index => index.ResolvedObject?.GetPathName(),
            ResolvedObject resolved => resolved.GetPathName(),
            _ => null
        };
    }

    private static string GetId(UObject obj) => obj.GetPathName();

    private static string GetKind(string type) =>
        type.Contains("Composite", StringComparison.OrdinalIgnoreCase) ? "composite" :
        type.Contains("Decorator", StringComparison.OrdinalIgnoreCase) ? "decorator" :
        type.Contains("Service", StringComparison.OrdinalIgnoreCase) ? "service" :
        type.Contains("Task", StringComparison.OrdinalIgnoreCase) ? "task" : "node";
}
