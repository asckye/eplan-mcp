using EplanMcp.Coverage.Mapping;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Medium")]
public sealed class ApiNotesTests
{
    [Fact]
    public void Anchors_ReadsTopicsAndNewEntries_ButNotIndexRows()
    {
        using var repository = new TempRepository();
        repository.Write(ApiNotes.IndexPath, "| A1 | Archived |\n\n## New entries\n\n### A67. Pending (2026-11-01)\n");
        repository.Write("docs/eplan/api-notes/reads.md", "### A1. Read `Page` (2026-10-12)\n");
        repository.Write("docs/eplan/api-notes/writes.md", "### B12. Write\n");

        Dictionary<string, string> anchors = ApiNotes.Anchors(repository.Root);

        Assert.Equal(3, anchors.Count);
        Assert.Equal("docs/eplan/api-notes/reads.md#a1-read-page-2026-10-12", anchors["A1"]);
        Assert.Equal("docs/eplan/api-notes/writes.md#b12-write", anchors["B12"]);
        Assert.Equal("docs/eplan/api-notes.md#a67-pending-2026-11-01", anchors["A67"]);
    }

    [Fact]
    public void Anchors_DuplicateIdAcrossIndexAndTopic_Fails()
    {
        using var repository = new TempRepository();
        repository.Write(ApiNotes.IndexPath, "### A1. Pending\n");
        repository.Write("docs/eplan/api-notes/topic.md", "### A1. Archived\n");

        Assert.Throws<ArgumentException>(() => ApiNotes.Anchors(repository.Root));
    }

    [Fact]
    public void Mapping_EvidenceIsUnchangedWhenHeadingMovesToTopic()
    {
        using TempRepository repository = Mapping.FakeRepository.Create();
        string before = CoverageJson.Serialize(ToolMapping.Build(repository.Root));
        string notes = repository.Read(ApiNotes.IndexPath);
        repository.Write(ApiNotes.IndexPath, "| A12 | See topic |\n");
        repository.Write("docs/eplan/api-notes/reads.md", notes);

        Assert.Equal(before, CoverageJson.Serialize(ToolMapping.Build(repository.Root)));
    }
}
