using System.Security.Cryptography;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class PlacementImageCreateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Image_VerifiesSourceAndStoredPathBeforeOneCommit(bool wrongPath)
    {
        string folder = Path.Combine(Path.GetTempPath(), "EplanMcp-image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "image.png");
            File.WriteAllBytes(file, [1, 2, 3]);
            var store = new PlacementTextStore();
            PlacementTextCreateArgs args = PlacementGraphicsCreateTests.Request("image");
            args.Items![0].FilePath = file;
            args.Items[0].Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            if (wrongPath)
            { store.Readback = item => { item.FilePath += ".other"; return item; }; }
            if (wrongPath)
            {
                BridgeError error = Assert.Throws<BridgeErrorException>(() => store.Operation.Execute(args)).Error;
                Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
                Assert.Empty(store.Texts);
            }
            else
            {
                var result = Assert.IsType<PlacementTextCreateData>(store.Operation.Execute(args));
                Assert.Equal("image.png", result.Items[0].File);
                Assert.Empty(result.Items[0].FilePath);
                Assert.Equal(["Begin AI: eplan_placement_create", "Commit", "CloseUndo", "Dispose"], store.Transactions.Calls);
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedImage_RefusesBeforeWriteOrRollsBackIfChangedAfterPreflight(bool afterPreflight)
    {
        string folder = Path.Combine(Path.GetTempPath(), "EplanMcp-image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "image.png");
            File.WriteAllBytes(file, [1, 2, 3]);
            var store = new PlacementTextStore();
            PlacementTextCreateArgs args = PlacementGraphicsCreateTests.Request("image");
            args.Items![0].FilePath = file;
            args.Items[0].Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            if (afterPreflight)
            { store.Transactions.Snapshot = () => { File.WriteAllBytes(file, [4]); return () => { }; }; }
            else
            { File.WriteAllBytes(file, [4]); }
            BridgeError error = Assert.Throws<BridgeErrorException>(() => store.Operation.Execute(args)).Error;
            Assert.Equal((ErrorReason.PlanStale, afterPreflight ? ExecutionState.RolledBack : ExecutionState.NotStarted), (error.Reason, error.Execution));
            Assert.Empty(store.Texts);
            Assert.Equal(0, store.Writes);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
