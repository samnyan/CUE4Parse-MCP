using CUE4Parse.Mcp.Services;
using Xunit;

namespace CUE4Parse.Mcp.Tests;

public class PathAccessPolicyTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _allowed;
    private readonly string _outside;

    public PathAccessPolicyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cue4parse-mcp-tests-" + Guid.NewGuid().ToString("N"));
        _allowed = Directory.CreateDirectory(Path.Combine(_tempRoot, "Allowed")).FullName;
        _outside = Directory.CreateDirectory(Path.Combine(_tempRoot, "Outside")).FullName;
    }

    [Fact]
    public void AllowsRootAndDescendantsButRejectsSibling()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_allowed, "Game", "Content")).FullName;
        var policy = new PathAccessPolicy([_allowed]);

        Assert.True(policy.TryValidateDirectory(nested, out var normalized, out _, out var message), message);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(nested)), normalized);

        Assert.False(policy.TryValidateDirectory(_outside, out _, out var code, out _));
        Assert.Equal("path_not_allowed", code);
    }

    [Fact]
    public void DotDotCannotEscapeWhitelist()
    {
        var policy = new PathAccessPolicy([_allowed]);
        var escaped = Path.Combine(_allowed, "..", "Outside");

        Assert.False(policy.TryValidateDirectory(escaped, out _, out var code, out _));
        Assert.Equal("path_not_allowed", code);
    }

    [Fact]
    public void FileInsideWhitelistIsAllowed()
    {
        var file = Path.Combine(_allowed, "Mappings.usmap");
        File.WriteAllText(file, "test");
        var policy = new PathAccessPolicy([_allowed]);

        Assert.True(policy.TryValidateFile(file, out var normalized, out _, out var message), message);
        Assert.Equal(Path.GetFullPath(file), normalized);
    }

    [Fact]
    public void SymlinkEscapeIsRejectedWhenSupported()
    {
        var policy = new PathAccessPolicy([_allowed]);
        var outsideFile = Path.Combine(_outside, "secret.usmap");
        File.WriteAllText(outsideFile, "secret");
        var link = Path.Combine(_allowed, "OutsideLink");

        try
        {
            Directory.CreateSymbolicLink(link, _outside);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.False(policy.TryValidateFile(Path.Combine(link, "secret.usmap"), out _, out var code, out _));
        Assert.Equal("path_not_allowed", code);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for test temp data.
        }
    }
}
