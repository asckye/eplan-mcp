using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Tests;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlanGuardNameChecksTests
{
    [Theory]
    [InlineData("+a1-k1", "+A1-K1", true)]
    [InlineData("+a1-k1:1", "+A1-K1", true)]
    [InlineData("+A1-K10", "+A1-K1", false)]
    [InlineData("+A1-K1", "+A1-K1:1", false)]
    [InlineData("+A1-K?", "+A1-K?", true)]
    [InlineData("+A1-K2", "+A1-K?", false)]
    public void Collision_CaseColonAndLiteralQuestionMark_MatchesExistingSemantics(string occupied, string candidate, bool expected)
    {
        Assert.Equal(expected, PlanGuardNameChecks.Collides(occupied, candidate));
    }

    [Theory]
    [InlineData("+A1/1")]
    [InlineData("+A9/9")]
    [InlineData(null)]
    public void Absent_ExtraRepresentationOrUnplacedFunction_IsOccupied(string? page)
    {
        var checks = new PlanGuardNameChecks([new() { Name = "+a1-k1:1", Type = 17, Oid = 5, Page = page }]);
        Assert.True(checks.Occupied("+A1-K1"));
        Assert.False(checks.Occupied("+A1-K2"));
    }

    [Theory]
    [InlineData("same", true)]
    [InlineData("reordered", true)]
    [InlineData("added", false)]
    [InlineData("removed", false)]
    [InlineData("replaced", false)]
    [InlineData("moved", false)]
    [InlineData("unplaced", false)]
    [InlineData("case", false)]
    [InlineData("type", false)]
    [InlineData("missingHash", false)]
    [InlineData("duplicate", false)]
    [InlineData("shortTag", false)]
    public void Closure_CompleteMainAuxiliaryAndUnplacedIdentityPageSet_RequiresExactApprovedMembership(string change, bool expected)
    {
        const string name = "+A1-K1";
        PlanFunction[] functions =
        [
            new() { Name = name, Type = 17, Oid = 1, Page = "+A1/1", IsMain = true },
            new() { Name = name, Type = 17, Oid = 2, Page = "+A2/2", IsMain = false },
            new() { Name = name, Type = 17, Oid = 3, Page = null, IsMain = true },
        ];
        var guard = new PlanStepGuardArgs
        {
            Preconditions = [.. functions.Select(f => new PlanGuardCondition
            {
                Tag = "objectHash",
                Locator = new() { Project = PlanGuardReader.Project, Kind = f.Kind, Name = f.Name, Type = f.Type, Oid = f.Oid, Page = f.Page },
                Hash = new string('a', 64),
            })],
        };
        string deviceName = name;
        switch (change)
        {
            case "reordered":
                Array.Reverse(functions);
                break;
            case "added":
                functions = [.. functions, new() { Name = name, Type = 17, Oid = 4, Page = null }];
                break;
            case "removed":
                functions = [.. functions.Skip(1)];
                break;
            case "replaced":
                functions[0].Oid = 4;
                break;
            case "moved":
                functions[0].Page = "+A9/9";
                break;
            case "unplaced":
                functions[0].Page = null;
                break;
            case "case":
                functions[0].Name = name.ToLowerInvariant();
                break;
            case "type":
                functions[0].Type = 18;
                break;
            case "missingHash":
                guard.Preconditions.RemoveAt(0);
                break;
            case "duplicate":
                guard.Preconditions.Add(guard.Preconditions[0]);
                break;
            case "shortTag":
                deviceName = "-K1";
                break;
        }

        Assert.Equal(expected, new PlanGuardNameChecks(functions).ClosureMatches(guard, deviceName));
    }

    [Fact]
    public void Guard_FallbackWithoutCheckpointInterface_IsStillChargedBeforeAcceptingResult()
    {
        using var reader = new PlanGuardReader();
        var operation = new PlanStepGuardOperation(reader, reader, reader, new BlockingReader(reader), () => reader.Milliseconds);
        BridgeError failure = Assert.Throws<BridgeErrorException>(() => operation.Execute(reader.Args(all: true))).Error;
        Assert.Equal((ErrorReason.Timeout, ExecutionState.NotStarted), (failure.Reason, failure.Execution));
    }

    private sealed class BlockingReader(PlanGuardReader reader) : IPlanFunctionReader
    {
        public IReadOnlyList<PlanFunction> Read(string projectPath)
        {
            reader.Milliseconds += 10_000;
            return [];
        }
    }
}
