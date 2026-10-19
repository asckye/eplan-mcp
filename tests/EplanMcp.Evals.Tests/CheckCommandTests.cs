namespace EplanMcp.Evals.Tests;

[Trait("Size", "Medium")]
public sealed class CheckCommandTests : IDisposable
{
    private const string Valid = """
        id: <id>
        set: <set>
        prompt: 这个项目有多少页？
        answer: { type: object, properties: { pages: { type: integer } } }
        oracle: { pages: { tool: eplan_project_get, pick: $.pageCount } }
        checks: [{ answer: $.pages, equals: oracle.pages }]
        """;

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("eplanmcp-evals-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public void Run_ValidTasks_CountsThemPerSetAndSucceeds()
    {
        Write("page-count", Task("page-count", "dev"));
        Write("page-count-holdout", Task("page-count-holdout", "holdout"));
        File.WriteAllText(Path.Combine(_directory.FullName, "notes.md"), "not a task");

        (int exit, string output) = Run(_directory.FullName);

        Assert.Equal(0, exit);
        Assert.Equal($"2 valid tasks (dev 1, holdout 1), 0 invalid files in {_directory.FullName}.\n", output);
    }

    [Fact]
    public void Run_AnInvalidFile_ListsItsProblemsAndFails()
    {
        Write("a-valid", Task("a-valid", "dev"));
        Write("b-broken", Task("b-other", "nightly"));

        (int exit, string output) = Run(_directory.FullName);

        Assert.Equal(1, exit);
        Assert.Equal(
            "b-broken.yaml line 1: id 'b-other' differs from the file name 'b-broken'\n"
            + "b-broken.yaml line 2: set 'nightly' is neither dev nor holdout\n"
            + $"1 valid tasks (dev 1, holdout 0), 1 invalid files in {_directory.FullName}.\n",
            output);
    }

    [Fact]
    public void Run_AYmlFile_IsReportedAsInvalid()
    {
        Write("page-count", Task("page-count", "dev"));
        File.WriteAllText(Path.Combine(_directory.FullName, "part-count.yml"), Task("part-count", "dev"));

        (int exit, string output) = Run(_directory.FullName);

        Assert.Equal(1, exit);
        Assert.Equal(
            "part-count.yml: task files end in .yaml; rename it\n"
            + $"1 valid tasks (dev 1, holdout 0), 1 invalid files in {_directory.FullName}.\n",
            output);
    }

    [Fact]
    public void Run_AMissingDirectory_Fails()
    {
        string missing = Path.Combine(_directory.FullName, "tasks");

        (int exit, string output) = Run(missing);

        Assert.Equal(1, exit);
        Assert.Equal($"There is no task directory {missing}.\n", output);
    }

    private static string Task(string id, string set) => Valid.Replace("<id>", id, StringComparison.Ordinal).Replace("<set>", set, StringComparison.Ordinal);

    private void Write(string name, string yaml) => File.WriteAllText(Path.Combine(_directory.FullName, name + ".yaml"), yaml);

    private static (int Exit, string Output) Run(string directory)
    {
        using var output = new StringWriter { NewLine = "\n" };
        int exit = CheckCommand.Run(directory, output);
        return (exit, output.ToString());
    }
}
