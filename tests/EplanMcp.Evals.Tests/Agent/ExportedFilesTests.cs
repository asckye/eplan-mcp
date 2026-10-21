using EplanMcp.Evals.Agent;

namespace EplanMcp.Evals.Tests.Agent;

/// <summary>An export root in a temporary directory, laid out as eplanmcp's (M4 design 4.4); no EPLAN.</summary>
[Trait("Size", "Medium")]
public sealed class ExportedFilesTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("eplanmcp-exports-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public void ListAndSince_GiveTheFilesThatAppearedBetweenTwoListings()
    {
        string root = Path.Combine(_directory.FullName, "exports");
        IReadOnlyList<ExportedFile> none = ExportedFiles.List(root);
        Write(@"EPLAN_Sample_Project\20261021T083000Z-aaaaaa\EPLAN_Sample_Project.pdf", 7);
        IReadOnlyList<ExportedFile> before = ExportedFiles.List(root);
        Write(@"Eval_r1_01\20261021T083100Z-bbbbbb\Eval_r1_01\=GAA\+A1\&EFS1\=GAA+A1&EFS1_1.dxf", 3);
        Write(@"Eval_r1_01\20261021T083100Z-bbbbbb\Eval_r1_01\=GAA\+A1\&EFS1\=GAA+A1&EFS1_2.dxf", 4);
        Directory.CreateDirectory(Path.Combine(root, "Eval_r1_01", "20261021T083200Z-cccccc"));

        IReadOnlyList<ExportedFile> exported = ExportedFiles.Since(before, ExportedFiles.List(root));

        Assert.Empty(none);
        Assert.Equal([new ExportedFile(@"EPLAN_Sample_Project\20261021T083000Z-aaaaaa\EPLAN_Sample_Project.pdf", 7)], before);
        Assert.Equal(
            [
                new ExportedFile(@"Eval_r1_01\20261021T083100Z-bbbbbb\Eval_r1_01\=GAA\+A1\&EFS1\=GAA+A1&EFS1_1.dxf", 3),
                new ExportedFile(@"Eval_r1_01\20261021T083100Z-bbbbbb\Eval_r1_01\=GAA\+A1\&EFS1\=GAA+A1&EFS1_2.dxf", 4),
            ],
            exported);
    }

    private void Write(string path, int bytes)
    {
        string file = Path.Combine(_directory.FullName, "exports", path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, new byte[bytes]);
    }
}
