using System.Collections;
using System.Reflection;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;

namespace CUE4Parse.Mcp.Services;

public sealed class ResolvedPropertyPathValue
{
    public required string Path { get; init; }
    public object? Value { get; init; }
}

public static class PropertyPathResolver
{
    public static bool TryResolve(object root, string propertyPath, int sampleItems, out List<ResolvedPropertyPathValue> values, out string? error)
    {
        values = [new ResolvedPropertyPathValue { Path = "", Value = root }];
        error = null;
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            error = "propertyPath is required.";
            return false;
        }

        foreach (var segment in SplitSegments(propertyPath))
        {
            if (!TryParseSegment(segment, out var name, out var selectors, out error)) return false;
            var next = new List<ResolvedPropertyPathValue>();
            foreach (var current in values)
            {
                var value = current.Value;
                var resolvedPath = current.Path;
                if (!string.IsNullOrEmpty(name))
                {
                    if (!TryGetNamedValue(value, name, out value)) continue;
                    resolvedPath = string.IsNullOrEmpty(resolvedPath) ? name : $"{resolvedPath}.{name}";
                }

                var selected = new List<ResolvedPropertyPathValue> { new() { Path = resolvedPath, Value = value } };
                foreach (var selector in selectors)
                {
                    var selectedNext = new List<ResolvedPropertyPathValue>();
                    foreach (var item in selected)
                    {
                        var items = GetIndexedValues(item.Value);
                        if (items == null) continue;
                        if (selector == "*")
                        {
                            for (var i = 0; i < Math.Min(sampleItems, items.Count); i++)
                                selectedNext.Add(new ResolvedPropertyPathValue { Path = $"{item.Path}[{i}]", Value = Unwrap(items[i]) });
                        }
                        else if (int.TryParse(selector, out var index) && index >= 0 && index < items.Count)
                        {
                            selectedNext.Add(new ResolvedPropertyPathValue { Path = $"{item.Path}[{index}]", Value = Unwrap(items[index]) });
                        }
                    }
                    selected = selectedNext;
                }
                next.AddRange(selected);
            }

            if (next.Count == 0)
            {
                error = $"Property path segment '{segment}' could not be resolved.";
                values = [];
                return false;
            }
            values = next;
        }

        return true;
    }

    public static object? GetPropertyValue(IPropertyHolder holder, string name)
    {
        var property = holder.Properties.FirstOrDefault(p => p.Name.Text.Equals(name, StringComparison.OrdinalIgnoreCase));
        return Unwrap(property);
    }

    public static IReadOnlyList<object?> GetCollectionValues(object? value)
    {
        var values = GetIndexedValues(value);
        return values == null ? [] : values.Select(Unwrap).ToArray();
    }

    public static object? Unwrap(object? value)
    {
        for (var i = 0; i < 8; i++)
        {
            value = value switch
            {
                FPropertyTag tag => tag.Tag?.GenericValue,
                FPropertyTagType tagType => tagType.GenericValue,
                FScriptStruct scriptStruct => scriptStruct.StructType,
                _ => value
            };
            if (value is not FPropertyTag && value is not FPropertyTagType && value is not FScriptStruct) break;
        }
        return value;
    }

    private static bool TryGetNamedValue(object? source, string name, out object? value)
    {
        source = Unwrap(source);
        if (source is IPropertyHolder holder)
        {
            var property = holder.Properties.FirstOrDefault(p => p.Name.Text.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (property != null)
            {
                value = Unwrap(property);
                return true;
            }
        }

        if (source is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key?.ToString()?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
                {
                    value = Unwrap(entry.Value);
                    return true;
                }
            }
        }

        if (source != null)
        {
            var member = source.GetType().GetMembers(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            value = member switch
            {
                FieldInfo field => field.GetValue(source),
                PropertyInfo property when property.CanRead && property.GetIndexParameters().Length == 0 => property.GetValue(source),
                _ => null
            };
            if (member != null)
            {
                value = Unwrap(value);
                return true;
            }
        }

        value = null;
        return false;
    }

    private static IReadOnlyList<object?>? GetIndexedValues(object? source)
    {
        source = Unwrap(source);
        if (source is UScriptArray array) return array.Properties.Cast<object?>().ToArray();
        if (source is UScriptSet set) return set.Properties.Cast<object?>().ToArray();
        if (source is string) return null;
        if (source is IList list) return list.Cast<object?>().ToArray();
        if (source is IEnumerable enumerable) return enumerable.Cast<object?>().ToArray();
        return null;
    }

    private static IEnumerable<string> SplitSegments(string path)
    {
        var start = 0;
        var bracketDepth = 0;
        for (var i = 0; i < path.Length; i++)
        {
            if (path[i] == '[') bracketDepth++;
            else if (path[i] == ']') bracketDepth--;
            else if (path[i] == '.' && bracketDepth == 0)
            {
                yield return path[start..i];
                start = i + 1;
            }
        }
        yield return path[start..];
    }

    private static bool TryParseSegment(string segment, out string name, out List<string> selectors, out string? error)
    {
        selectors = [];
        error = null;
        var bracket = segment.IndexOf('[');
        name = bracket < 0 ? segment : segment[..bracket];
        var position = bracket;
        while (position >= 0 && position < segment.Length)
        {
            if (segment[position] != '[')
            {
                error = $"Invalid property path segment '{segment}'.";
                return false;
            }
            var end = segment.IndexOf(']', position + 1);
            if (end < 0)
            {
                error = $"Missing closing bracket in '{segment}'.";
                return false;
            }
            var selector = segment[(position + 1)..end];
            if (selector != "*" && !int.TryParse(selector, out _))
            {
                error = $"Invalid array selector '[{selector}]'.";
                return false;
            }
            selectors.Add(selector);
            position = end + 1;
            if (position == segment.Length) break;
        }
        return !string.IsNullOrEmpty(name) || selectors.Count > 0;
    }
}
