using System.Runtime.CompilerServices;
using CUE4Parse.Mcp.Services;
using CUE4Parse.UE4.Kismet;
using CUE4Parse.UE4.Objects.UObject;
using Xunit;

namespace CUE4Parse.Mcp.Tests;

public class LogicAnalysisTests
{
    [Fact]
    public void PropertyPathResolverHandlesIndexedAndWildcardPaths()
    {
        var root = new TestRoot
        {
            Children =
            [
                new TestChild { Name = "First", Values = [1, 2] },
                new TestChild { Name = "Second", Values = [3, 4] }
            ]
        };

        Assert.True(PropertyPathResolver.TryResolve(root, "Children[1].Values[0]", 10, out var indexed, out var indexedError), indexedError);
        Assert.Equal(3, indexed.Single().Value);
        Assert.True(PropertyPathResolver.TryResolve(root, "Children[*].Name", 10, out var wildcard, out var wildcardError), wildcardError);
        Assert.Equal(["First", "Second"], wildcard.Select(item => item.Value));
    }

    [Fact]
    public void BoundedJsonSerializerNeverReturnsPartialJson()
    {
        var result = BoundedJsonSerializer.Serialize(new { value = new string('x', 2048) }, 4, 128);
        Assert.True(result.Truncated);
        Assert.Null(result.Json);
        Assert.Equal(0, result.ReturnedBytes);
        Assert.True(result.TotalBytes > 128);
    }

    [Fact]
    public void CfgBuilderResolvesDirectJumpToBasicBlock()
    {
        var first = new EX_Nothing { StatementIndex = 0 };
        var jump = Create<EX_Jump>();
        jump.StatementIndex = 1;
        jump.CodeOffset = 10;
        var unreachable = new EX_Nothing { StatementIndex = 5 };
        var returned = Create<EX_Return>();
        returned.StatementIndex = 10;
        returned.ReturnExpression = new EX_Nothing();
        var end = new EX_EndOfScript { StatementIndex = 12 };
        var function = new UFunction
        {
            Name = "TestFunction",
            ScriptBytecode = [first, jump, unreachable, returned, end]
        };

        var cfg = KismetAnalyzer.BuildCfg("/Game/Test.Test_C", function, 0, 100);
        var edge = Assert.Single(cfg.Edges, item => item.Kind == "jump");
        Assert.Equal(10, edge.TargetStatementIndex);
        Assert.NotNull(edge.ToBlock);
        Assert.Equal(10, cfg.Blocks.Single(block => block.Id == edge.ToBlock).StartIndex);
    }

    [Fact]
    public void ExpressionWalkerFindsNestedExpressions()
    {
        var assignment = Create<EX_Let>();
        assignment.StatementIndex = 0;
        assignment.Variable = new EX_Nothing { StatementIndex = 1 };
        var returned = Create<EX_Return>();
        returned.StatementIndex = 2;
        returned.ReturnExpression = new EX_Nothing { StatementIndex = 3 };
        assignment.Assignment = returned;

        var visits = KismetAnalyzer.Walk(assignment).ToList();
        Assert.Equal(4, visits.Count);
        Assert.Equal(2, visits.Max(visit => visit.Depth));
    }

    private static T Create<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private sealed class TestRoot
    {
        public List<TestChild> Children { get; init; } = [];
    }

    private sealed class TestChild
    {
        public string Name { get; init; } = "";
        public List<int> Values { get; init; } = [];
    }
}
