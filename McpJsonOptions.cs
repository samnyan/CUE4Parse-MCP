using System.Text.Json;

namespace CUE4Parse.Mcp;

public static class McpJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
