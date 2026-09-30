using CUE4Parse.Mcp.Configuration;
using Xunit;

namespace CUE4Parse.Mcp.Tests;

public class ServerOptionsTests
{
    [Fact]
    public void DefaultsToStdioAndLoopbackHttpDefaults()
    {
        Assert.True(ServerOptions.TryParse([], out var options, out var error), error);
        Assert.False(options.Http);
        Assert.Equal("127.0.0.1", options.Host);
        Assert.Equal(3001, options.Port);
        Assert.Empty(options.WhitelistDirectories);
    }

    [Fact]
    public void ParsesHttpHostPortAndRepeatedWhitelistDirectories()
    {
        var args = new[]
        {
            "--http",
            "--host", "0.0.0.0",
            "--port", "13337",
            "--whitelist-dir", @"D:\Games",
            "--whitelist-dir", @"D:\Mappings"
        };

        Assert.True(ServerOptions.TryParse(args, out var options, out var error), error);
        Assert.True(options.Http);
        Assert.Equal("0.0.0.0", options.Host);
        Assert.Equal(13337, options.Port);
        Assert.Equal([@"D:\Games", @"D:\Mappings"], options.WhitelistDirectories);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("not-a-port")]
    public void RejectsInvalidPort(string value)
    {
        Assert.False(ServerOptions.TryParse(["--port", value], out _, out var error));
        Assert.Contains("--port", error);
    }
}
