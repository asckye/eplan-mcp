namespace EplanMcp.Host.ClientConfig;

/// <summary>
/// Where the AI clients keep their configuration on this machine: the Windows profile folders, and the variables two
/// clients read to move their own files (<c>CODEX_HOME</c>, <c>KIMI_CODE_HOME</c>; M5 design 4.11). Production reads
/// them from Windows (<see cref="FromMachine"/>); tests build one over a temporary directory, so they never touch the
/// user's real client files. No setting or variable of EplanMcp can point it elsewhere.
/// </summary>
internal sealed class UserFolders
{
    private readonly Func<string, string?> _variable;

    /// <summary>For tests: <paramref name="variable"/> answers for the environment, usually with nothing.</summary>
    internal UserFolders(string userProfile, string appData, string localAppData, Func<string, string?> variable)
    {
        UserProfile = userProfile;
        AppData = appData;
        LocalAppData = localAppData;
        _variable = variable;
    }

    public string UserProfile { get; }

    public string AppData { get; }

    public string LocalAppData { get; }

    public static UserFolders FromMachine() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable);

    /// <summary>A client's own directory variable when it is set to something, else <paramref name="fallback"/>.</summary>
    public string ClientHome(string variable, string fallback) =>
        _variable(variable) is { } value && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}
