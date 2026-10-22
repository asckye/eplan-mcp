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

    public bool MakeFiles { get; set; } = true;

    public bool HoldOpen { get; set; }

    public FileStream? Held { get; private set; }

    public (int Pages, string? ProjectGuid) Create(string linkFile, string basicProject)
    {
        Make(linkFile, basicProject);
        return (1, "GB_bas001@1792643412");
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

        if (Fail)
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not make the project.");
        }
    }
}
