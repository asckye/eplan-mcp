namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class MethodSizeTests
{
    private static SortedDictionary<string, int> Sizes(params (string Key, int Lines)[] entries)
    {
        var sizes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach ((string key, int lines) in entries)
        {
            sizes[key] = lines;
        }
        return sizes;
    }

    [Fact]
    public void Bodies_CountTheLinesBetweenTheBraces_AndNameNestedTypes()
    {
        const string source = """
            class Outer
            {
                void One(int a)
                {
                    int x = a;
                    x++;
                }
                int Two => 1;
                Outer() { }
                class Inner
                {
                    int Value
                    {
                        get
                        {
                            return 1;
                        }
                    }
                }
            }
            """;
        Assert.Equal([("Outer.One(1)", 2, 1), ("Outer..ctor(0)", 0, 1), ("Outer.Inner.Value.get", 1, 1)], MethodSize.Bodies(source));
    }

    [Fact]
    public void Complexity_IsOnePlusEveryBranchAndShortCircuit_LambdasAndLocalFunctionsIncluded()
    {
        const string source = """
            class C
            {
                int M(int a, int? b)
                {
                    if (a > 0 && a < 9 || a == 20) { return 1; }
                    foreach (int x in new[] { 1 }) { a += x; }
                    int Local(int v) => v > 0 ? v : -v;
                    return a switch { 1 => b ?? 0, _ => Local(a) };
                }
            }
            """;
        // 1 + if + && + || + foreach + ?: + two switch arms + ??
        Assert.Equal([("C.M(2)", 4, 9)], MethodSize.Bodies(source));
    }

    [Fact]
    public void ANewLongMethod_AndAListedOneThatGrew_AreProblems()
    {
        List<string> problems = MethodSize.Problems(Sizes(("P/T.New(0)", 101), ("P/T.Old(1)", 151)), Sizes(("P/T.Old(1)", 150)), MethodSize.Lines);
        Assert.Equal(2, problems.Count);
        Assert.Contains("P/T.New(0) has 101 lines", problems[0], StringComparison.Ordinal);
        Assert.Contains("P/T.Old(1) grew from 150 to 151", problems[1], StringComparison.Ordinal);
        Assert.Contains("P/T.New(0) has 41 as its cyclomatic complexity; the limit is 40", Assert.Single(MethodSize.Problems(Sizes(("P/T.New(0)", 41)), Sizes(), MethodSize.Complexity)), StringComparison.Ordinal);
    }

    [Fact]
    public void AListedMethodThatShrankOrWent_MakesTheBaselineOutOfDate()
    {
        Assert.Empty(MethodSize.Problems(Sizes(("P/T.Old(1)", 150)), Sizes(("P/T.Old(1)", 150)), MethodSize.Lines));
        Assert.Contains("out of date for P/T.Old(1) (listed 150, now 120)", Assert.Single(MethodSize.Problems(Sizes(("P/T.Old(1)", 120)), Sizes(("P/T.Old(1)", 150)), MethodSize.Lines)), StringComparison.Ordinal);
        Assert.Contains("out of date for P/T.Old(1) (listed 150, now 0)", Assert.Single(MethodSize.Problems(Sizes(), Sizes(("P/T.Old(1)", 150)), MethodSize.Lines)), StringComparison.Ordinal);
    }

    /// <summary>The committed baseline matches the sources: the gate step would pass on this checkout.</summary>
    [Fact]
    public void TheRepository_HasNoMethodSizeProblems()
    {
        string root = Program.RepositoryRoot(AppContext.BaseDirectory)!;
        Assert.Empty(MethodSize.Problems(root));
    }
}
