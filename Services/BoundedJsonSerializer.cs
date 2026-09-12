using System.Text;
using System.Text.Json.Nodes;
using Newtonsoft.Json;

namespace CUE4Parse.Mcp.Services;

public sealed class BoundedJsonResult
{
    public JsonNode? Json { get; init; }
    public bool Truncated { get; init; }
    public int TotalBytes { get; init; }
    public int ReturnedBytes { get; init; }
    public string? Error { get; init; }
}

public static class BoundedJsonSerializer
{
    public static BoundedJsonResult Serialize(object? value, int maxDepth, int maxBytes)
    {
        try
        {
            var json = JsonConvert.SerializeObject(value, CreateSettings(maxDepth));
            var bytes = Encoding.UTF8.GetByteCount(json);
            if (bytes > maxBytes)
            {
                return new BoundedJsonResult
                {
                    Truncated = true,
                    TotalBytes = bytes
                };
            }

            return new BoundedJsonResult
            {
                Json = JsonNode.Parse(json) ?? JsonValue.Create(json),
                TotalBytes = bytes,
                ReturnedBytes = bytes
            };
        }
        catch (Exception ex)
        {
            return new BoundedJsonResult { Error = ex.Message };
        }
    }

    public static JsonSerializerSettings CreateSettings(int maxDepth) => new()
    {
        Formatting = Formatting.Indented,
        MaxDepth = maxDepth,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        Error = (_, args) => args.ErrorContext.Handled = true
    };
}
