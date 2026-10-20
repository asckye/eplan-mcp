using EplanMcp.Evals.Run;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Tests.Run;

/// <summary>Copies of a fake sample project in a temporary directory; no EPLAN.</summary>
[Trait("Size", "Medium")]
public sealed class ProjectCopiesTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("eplanmcp-copies-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public void Create_CopiesTheProjectOncePerAttemptUnderItsOwnName()
    {
        string template = Template();
        string runRoot = Path.Combine(_directory.FullName, "evals", "20261020-183005");

        Dictionary<(string Task, int Attempt), AttemptCopy> copies = ProjectCopies.Create([Task("read", "shared"), Task("write", "copy"), Task("refuse", "copy_outside_scratch")], 2, template, runRoot);

        Assert.Equal(
            [
                (("write", 1), "Eval_20261020_183005_01", Path.Combine(runRoot, "copies", "01", "Eval_20261020_183005_01")),
                (("write", 2), "Eval_20261020_183005_02", Path.Combine(runRoot, "copies", "02", "Eval_20261020_183005_02")),
                (("refuse", 1), "Eval_20261020_183005_03", Path.Combine(runRoot, "outside", "03", "Eval_20261020_183005_03")),
                (("refuse", 2), "Eval_20261020_183005_04", Path.Combine(runRoot, "outside", "04", "Eval_20261020_183005_04")),
            ],
            copies.Select(c => (c.Key, c.Value.Name, c.Value.Path)));
        Assert.All(copies.Values, c => Assert.Null(c.Problem));
        string copy = copies[("refuse", 2)].Path;
        Assert.Equal("elk", File.ReadAllText(copy + ".elk"));
        Assert.Equal("data", File.ReadAllText(Path.Combine(copy + ".edb", "PRJ", "Project.db")));
        Assert.True(Directory.Exists(Path.Combine(copy + ".edb", "Empty")));
        Assert.Equal(Path.Combine(runRoot, "copies"), ProjectCopies.Writable(runRoot));
    }

    [Fact]
    public void Create_ACopyThatFails_GetsTheProblem()
    {
        string template = Template();
        File.Delete(template);

        AttemptCopy copy = Assert.Single(ProjectCopies.Create([Task("write", "copy")], 1, template, Path.Combine(_directory.FullName, "r1"))).Value;

        Assert.StartsWith("copying the sample project failed: ", copy.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void FindTemplate_LooksThreeFoldersDeep()
    {
        Assert.Null(ProjectCopies.FindTemplate(Path.Combine(_directory.FullName, "missing")));
        Assert.Null(ProjectCopies.FindTemplate(_directory.FullName));

        string template = Template();

        Assert.Equal(template, ProjectCopies.FindTemplate(_directory.FullName));
    }

    /// <summary>A sample project two folders down, as under C:\Users\Public\EPLAN\Data: an .elk file and an .edb folder.</summary>
    private string Template()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_directory.FullName, "项目", "Company name")).FullName;
        string project = Path.Combine(folder, "EPLAN_Sample_Project");
        File.WriteAllText(project + ".elk", "elk");
        Directory.CreateDirectory(Path.Combine(project + ".edb", "PRJ"));
        Directory.CreateDirectory(Path.Combine(project + ".edb", "Empty"));
        File.WriteAllText(Path.Combine(project + ".edb", "PRJ", "Project.db"), "data");
        return project + ".elk";
    }

    private static EvalTask Task(string id, string fixture) => TaskLoader.Load(id, $"""
        id: {id}
        set: dev
        fixture: {fixture}
        prompt: {(fixture == "shared" ? "p" : "改 {project}")}
        answer: {"{"} type: object {"}"}
        checks: [{"{"} answer: $.done, equals: true {"}"}]
        """).Task!;
}
