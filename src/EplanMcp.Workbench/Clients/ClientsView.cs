using System.ComponentModel;
using System.IO;
using System.Text;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Clients;

/// <summary>Where the client page is: not looked yet, running client-config, showing its result, or without one.</summary>
internal enum ClientsState
{
    Idle,
    Running,
    Ready,
    Failed,
}

/// <summary>
/// One client as a card (CTL-07): whether this computer has it, the entry the Host found or would write, and what the last
/// write or removal did. Every text from the Host or a file is made plain (CTL-12).
/// </summary>
internal sealed class ClientCard(string name) : IAccessibleItem
{
    public string Name { get; } = name;

    public string Title => Loc.Current["Clients.Name." + ClientsView.Key(Name)];

    public bool IsSelected { get; set; }

    /// <summary>The client's configuration file, or the folder that holds it, is on this computer.</summary>
    public bool Detected { get; internal set; }

    /// <summary>The last dry run's part for this client: its file, the entry before and after, notes and problems.</summary>
    public ClientOutcome? Preview { get; internal set; }

    /// <summary>The last write's or removal's part for this client.</summary>
    public ClientOutcome? Result { get; internal set; }

    /// <summary>The Claude Code plugin already gives the server: no entry is written for Claude Code (design 4.7).</summary>
    public bool PluginInstalled => Preview?.Problem?.Code == "plugin_installed";

    /// <summary>
    /// An entry can be written for it: with the plugin, Claude Code gets none (design 4.7), but an old entry can still be
    /// removed, so the card can be chosen all the same.
    /// </summary>
    public bool CanWrite => !PluginInstalled;

    public string Subtitle => Loc.Current[Detected ? "Clients.Detected" : "Clients.NotDetected"]
        + (Preview?.Before is not null ? Loc.Current["Clients.Configured"] : string.Empty);

    /// <summary>Why the client cannot be configured now, from the last dry run.</summary>
    public string? Problem => Preview?.Problem is { } problem ? ClientsView.Text(problem) : null;

    /// <summary>
    /// What the write or removal did, with the backup the Host made, then any problem as a note: the file may be written
    /// although recording it failed (<c>record_failed</c>).
    /// </summary>
    public string? ResultText => Result switch
    {
        null => null,
        { Result: ClientResultKind.Written, Backup: { } backup } => Loc.Current.T("Clients.WrittenBackup", ProjectText.Line(backup)),
        { Result: ClientResultKind.Removed, Backup: { } backup } => Loc.Current.T("Clients.RemovedBackup", ProjectText.Line(backup)),
        { Result: var kind } => Loc.Current["Clients.Result." + kind],
    } + (Result?.Problem is { } problem ? Loc.Current.T("Clients.Note", ClientsView.Text(problem)) : string.Empty);

    public string AccessibleName => Loc.Current.T("Clients.CardName", Title, Subtitle);
}

/// <summary>
/// The client configuration wizard without its window (M5 design 4.4 item 3, 4.11, Q7, DLG-12): the eleven clients as
/// cards, found by a dry run of all of them; the chosen ones previewed as a plain diff, then written or removed by the
/// Host, which backs each file up first. The Workbench never writes a client's file itself.
/// </summary>
internal sealed class ClientsView : INotifyPropertyChanged
{
    /// <summary>The clients of Q7, in its order, by their names on client-config's command line.</summary>
    public static readonly string[] Names = ["claude-code", "claude-desktop", "codex", "gemini-cli", "qwen-code", "kimi-code", "codebuddy", "opencode", "qwen-agent", "cursor", "vscode-copilot"];

    /// <summary>For Claude Code the plugin is the recommended way (design 4.7): these two commands install it.</summary>
    public static readonly string[] PluginCommands = ["claude plugin marketplace add ~/EplanMcp/app/claude-plugin", "claude plugin install eplan-mcp@eplanmcp-local"];

    private readonly HostCommands _host;
    private readonly string _userProfile;
    private HostCommandException? _error;
    private HostText? _refused;
    private ClientConfigMode _mode;

    public ClientsView(HostCommands host, string userProfile)
    {
        (_host, _userProfile) = (host, userProfile);
        Cards = [.. Names.Select(n => new ClientCard(n))];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ClientCard> Cards { get; }

    public ClientsState State { get; private set; }

    public bool CanRun => _host.IsAvailable;

    /// <summary>Why nothing could be done: client-config refused as a whole (such as in an MSIX client's process), or gave no result.</summary>
    public string? Error => _refused is { } refused ? Text(refused)
        : _error is { } error ? HostErrors.Text(error) + (error.Failure == HostFailure.TimedOut && _mode != ClientConfigMode.DryRun ? " " + Loc.Current["Clients.CheckAfterTimeout"] : string.Empty)
        : null;

    public string? ErrorDetails => _error is { } error ? HostErrors.Details(error) : null;

    public IReadOnlyList<ClientCard> Selected => [.. Cards.Where(c => c.IsSelected)];

    /// <summary>The chosen clients an entry can be written for.</summary>
    public IReadOnlyList<ClientCard> ToWrite => [.. Selected.Where(c => c.CanWrite)];

    /// <summary>The Host's text in the current language, made plain.</summary>
    public static string Text(HostText text) => ProjectText.Line(Loc.Current.Language == AppLanguage.Chinese ? text.Zh : text.En);

    /// <summary>The catalogue key part of a client's name: <c>vscode-copilot</c> is <c>VscodeCopilot</c>.</summary>
    public static string Key(string name) => string.Concat(name.Split('-').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    /// <summary>A dry run of every client: which this computer has, and which already have the entry (refresh, F5).</summary>
    public Task DetectAsync(CancellationToken cancellationToken) => RunAsync(Cards, ClientConfigMode.DryRun, cancellationToken);

    /// <summary>A dry run of the chosen clients, for the diff the engineer confirms (DLG-12).</summary>
    public Task PreviewAsync(CancellationToken cancellationToken) => RunAsync(ToWrite, ClientConfigMode.DryRun, cancellationToken);

    /// <summary>Writes or removes the chosen clients' entry, after the engineer confirmed (PRI-01).</summary>
    public Task ApplyAsync(ClientConfigMode mode, CancellationToken cancellationToken) =>
        RunAsync(mode == ClientConfigMode.Remove ? Selected : ToWrite, mode, cancellationToken);

    /// <summary>The chosen clients' preview as plain text: the file, then the entry's lines, "−" before and "+" after.</summary>
    public string Diff()
    {
        var text = new StringBuilder();
        foreach (ClientCard card in ToWrite.Where(c => c.Preview is not null))
        {
            ClientOutcome preview = card.Preview!;
            text.AppendLine(Loc.Current.T("Clients.DiffHead", card.Title, ProjectText.Line(preview.File ?? Loc.Current["Clients.NoFile"])));
            foreach (string line in Lines(preview.Before))
            {
                text.Append("− ").AppendLine(line);
            }

            foreach (string line in Lines(preview.After))
            {
                text.Append("+ ").AppendLine(line);
            }

            foreach (HostText note in preview.Notes.Concat(preview.Problem is { } problem ? [problem] : []))
            {
                text.Append("! ").AppendLine(Text(note));
            }

            text.AppendLine();
        }

        return text.ToString().TrimEnd();
    }

    private static IEnumerable<string> Lines(string? entry) => entry is null ? [] : entry.Split('\n').Select(l => ProjectText.Line(l.TrimEnd('\r')));

    private async Task RunAsync(IReadOnlyList<ClientCard> cards, ClientConfigMode mode, CancellationToken cancellationToken)
    {
        if (cards.Count == 0)
        {
            return;
        }

        (State, _mode) = (ClientsState.Running, mode);
        Changed();
        try
        {
            ClientConfigReport report = await _host.ConfigureClientsAsync([.. cards.Select(c => c.Name)], mode, cancellationToken).ConfigureAwait(true);
            (_error, _refused, State) = (null, report.Problem, report.Problem is null ? ClientsState.Ready : ClientsState.Failed);
            foreach (ClientOutcome outcome in report.Clients)
            {
                if (cards.FirstOrDefault(c => c.Name == outcome.Client) is not { } card)
                {
                    continue;
                }

                if (mode == ClientConfigMode.DryRun)
                {
                    (card.Preview, card.Detected) = (outcome, Present(outcome.File));
                }
                else
                {
                    card.Result = outcome;
                }
            }
        }
        catch (HostCommandException ex)
        {
            (_error, _refused, State) = (ex, null, ClientsState.Failed);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            (_error, _refused, State) = (new HostCommandException(HostFailure.Unreadable, ex.Message, ex), null, ClientsState.Failed);
        }
        finally
        {
            // However it ends, the page leaves "Running": its buttons are never off for good.
            if (State == ClientsState.Running)
            {
                State = ClientsState.Failed;
            }

            Changed();
        }
    }

    /// <summary>The file is there, or its own folder is; the user profile itself, which every user has, does not count.</summary>
    private bool Present(string? file) =>
        file is not null && (File.Exists(file) || (Path.GetDirectoryName(file) is { } folder && Directory.Exists(folder)
            && !string.Equals(Path.TrimEndingDirectorySeparator(folder), Path.TrimEndingDirectorySeparator(_userProfile), StringComparison.OrdinalIgnoreCase)));

    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
