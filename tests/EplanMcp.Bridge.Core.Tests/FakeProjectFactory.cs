using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>
/// Makes the .elk and the .edb folder (with User.eod in it) as S27's CreateProject and restore did (A31), or fails after
/// making them; <see cref="HoldOpen"/> keeps User.eod open so that it cannot be deleted.
/// </summary>
internal sealed class FakeProjectFactory : IProjectFactory
{
    public List<(string LinkFile, string Source)> Calls { get; } = [];

    public bool Fail { get; set; }

    public BridgeErrorException? Failure { get; set; }

    public bool UpgradeChangesCopy { get; set; }

    public bool FailFirstCreateOnly { get; set; }

    public List<(string Source, string Folder)> Upgrades { get; } = [];

    public bool HoldUpgradeCopyOpen { get; set; }

    public FileStream? UpgradeHeld { get; private set; }

    public bool MakeFiles { get; set; } = true;

    public bool HoldOpen { get; set; }

    public FileStream? Held { get; private set; }

    public (int Pages, string? ProjectGuid) Create(string linkFile, string basicProject)
    {
        Make(linkFile, basicProject);
        return (1, "GB_bas001@1792643412");
    }

    public bool UpgradeBasicProject(string basicProject, string folder)
    {
        Upgrades.Add((basicProject, folder));
        string copy = Path.Combine(folder, Path.GetFileName(basicProject));
        File.Copy(basicProject, copy);
        if (UpgradeChangesCopy)
        {
            File.AppendAllText(copy, "upgraded");
        }
        if (HoldUpgradeCopyOpen)
        {
            UpgradeHeld = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        return UpgradeChangesCopy;
    }

    public void Restore(string archive, string linkFile) => Make(linkFile, archive);

    private void Make(string linkFile, string source)
    {
        Calls.Add((linkFile, source));
        if (MakeFiles)
        {
            File.WriteAllText(linkFile, "link");
            string data = Directory.CreateDirectory(Path.ChangeExtension(linkFile, ".edb")).FullName;
            File.WriteAllText(Path.Combine(data, "User.eod"), "session");
            if (HoldOpen)
            {
                Held = new FileStream(Path.Combine(data, "User.eod"), FileMode.Open, FileAccess.Read, FileShare.None);
            }
        }

        if ((Fail || Failure is not null) && (!FailFirstCreateOnly || Calls.Count == 1))
        {
            throw Failure ?? new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not make the project.");
        }
    }
}
