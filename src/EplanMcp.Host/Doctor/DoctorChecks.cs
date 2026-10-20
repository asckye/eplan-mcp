using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using EplanMcp.Host.Bridge;
using Microsoft.Win32;

namespace EplanMcp.Host.Doctor;

/// <summary>
/// The checks behind <c>eplanmcp doctor</c> (design 4.5). Each turns a pitfall met during the spikes into an
/// automatic check. Doctor changes nothing on the machine: the one probe file it writes is removed again.
/// </summary>
internal static class DoctorChecks
{
    /// <summary>Above this, Windows stopped loading the user's environment at logon on the dev machine (api-notes A1).</summary>
    public const int UserPathWarningLength = 2047;

    public static DoctorCheck HostVersion() =>
        new(DoctorStatus.Info, $"eplanmcp {HostInfo.Version}", $"eplanmcp {HostInfo.Version}");

    public static DoctorCheck DataDirectory()
    {
        string root = Environment.GetEnvironmentVariable("EPLANMCP_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp");
        return Directory.Exists(root)
            ? new(DoctorStatus.Ok, $"数据目录：{root}", $"Data directory: {root}")
            : new(DoctorStatus.Info, $"数据目录尚未创建：{root}（首次需要时创建）", $"Data directory not created yet: {root} (created when first needed)");
    }

    /// <summary>
    /// Whether this process runs in an MSIX package's environment: a Host started by a packaged client such as the
    /// Claude desktop app has its writes to %LOCALAPPDATA% redirected into the package's LocalCache (api-notes A6),
    /// even without package identity (spike S5). So doctor looks at where a probe file lands. EplanMcp itself only
    /// uses the data directory, which is never redirected.
    /// </summary>
    public static DoctorCheck MsixEnvironment(string localAppData) =>
        MsixEnvironment(CurrentPackageFullName(), RedirectingPackage(localAppData));

    internal static DoctorCheck MsixEnvironment(string? identity, string? redirectingPackage)
    {
        if (redirectingPackage is null && identity is null)
        {
            return new(DoctorStatus.Ok, "不在 MSIX 包环境中：写入 %LOCALAPPDATA% 没有被重定向", "Not in an MSIX package environment: writes to %LOCALAPPDATA% are not redirected");
        }

        string package = redirectingPackage ?? identity!;
        string chinese = identity is null ? "当前进程没有包身份" : $"当前进程的包身份是 {identity}";
        string english = identity is null ? "this process has no package identity" : $"this process has package identity {identity}";
        return new(
            DoctorStatus.Info,
            $"在 MSIX 包 {package} 的环境中（例如由 Claude 桌面版启动），{chinese}：写入 %LOCALAPPDATA% 会被重定向到包的 LocalCache。EplanMcp 只用数据目录，不受影响",
            $"In the environment of MSIX package {package} (started by the Claude desktop app, for example); {english}: writes to %LOCALAPPDATA% are redirected into the package's LocalCache. EplanMcp only uses the data directory and is not affected");
    }

    /// <summary>
    /// The package whose LocalCache a probe file written to <paramref name="localAppData"/> lands in, or null when it
    /// is not redirected or cannot be written. The probe is removed again either way.
    /// </summary>
    internal static string? RedirectingPackage(string localAppData)
    {
        string name = $"eplanmcp-doctor-{Guid.NewGuid():N}.tmp";
        string probe = Path.Combine(localAppData, name);
        try
        {
            File.WriteAllBytes(probe, []);
            return FindRedirected(Path.Combine(localAppData, "Packages"), name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try
            {
                File.Delete(probe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // a zero-byte file named for doctor; nothing depends on it
            }
        }
    }

    /// <summary>The package under <paramref name="packages"/> whose <c>LocalCache\Local</c> holds <paramref name="name"/>.</summary>
    internal static string? FindRedirected(string packages, string name) =>
        Directory.Exists(packages)
            ? Directory.EnumerateDirectories(packages).FirstOrDefault(p => File.Exists(Path.Combine(p, "LocalCache", "Local", name))) is { } package ? Path.GetFileName(package) : null
            : null;

    public static IEnumerable<DoctorCheck> EplanInstallations(string programFiles)
    {
        string platform = Path.Combine(programFiles, "EPLAN", "Platform");
        string[] executables = Directory.Exists(platform)
            ? [.. Directory.EnumerateDirectories(platform).Select(d => Path.Combine(d, "Bin", "EPLAN.exe")).Where(File.Exists).Order(StringComparer.OrdinalIgnoreCase)]
            : [];
        if (executables.Length == 0)
        {
            yield return new(DoctorStatus.Failure, $"在 {platform} 下没有找到 EPLAN", $"No EPLAN found under {platform}");
            yield break;
        }

        foreach (string exe in executables)
        {
            string version = FileVersionInfo.GetVersionInfo(exe).FileVersion ?? "?";
            bool supported = version.StartsWith("2026.", StringComparison.Ordinal);
            yield return supported
                ? new(DoctorStatus.Ok, $"EPLAN {version}：{exe}", $"EPLAN {version}: {exe}")
                : new(DoctorStatus.Warning, $"EPLAN {version}：{exe}（尚未验证支持，目前只支持 2026）", $"EPLAN {version}: {exe} (not verified yet; only 2026 is supported)");
        }
    }

    public static IEnumerable<DoctorCheck> RunningEplan()
    {
        Process[] processes = Process.GetProcessesByName("EPLAN");
        try
        {
            if (processes.Length == 0)
            {
                yield return new(DoctorStatus.Warning, "EPLAN 没有在运行", "EPLAN is not running");
                yield break;
            }

            foreach (Process process in processes)
            {
                string pid = process.Id.ToString(CultureInfo.InvariantCulture);
                yield return new(DoctorStatus.Ok, $"EPLAN 正在运行，进程 {pid}", $"EPLAN is running, process {pid}");
            }
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    public static async Task<IReadOnlyList<DoctorCheck>> BridgeAsync(IBridgeClient bridge, CancellationToken cancellationToken)
    {
        BridgeStatus status = await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var checks = new List<DoctorCheck>();
        if (status.Instances.Count == 0)
        {
            checks.Add(new(DoctorStatus.Failure,
                "没有发现 EplanMcp 插件：请确认 EPLAN 正在以所用的变体运行，并且插件已为该变体注册（每个变体要分别注册）",
                "No EplanMcp add-in found: make sure EPLAN is running in the variant you use and the add-in is registered for that variant (each variant is registered separately)"));
            return checks;
        }

        foreach (BridgeEndpoint endpoint in status.Instances)
        {
            string pid = endpoint.ProcessId.ToString(CultureInfo.InvariantCulture);
            checks.Add(new(DoctorStatus.Ok, $"发现插件管道：进程 {pid}", $"Add-in pipe found: process {pid}"));
        }

        if (status.Handshake is { } handshake)
        {
            string licence = handshake.Eplan.ApiExtensionLicence switch { true => "可用", false => "不可用", null => "未知" };
            string licenceEn = handshake.Eplan.ApiExtensionLicence switch { true => "available", false => "missing", null => "unknown" };
            checks.Add(new(DoctorStatus.Ok,
                $"握手成功：插件 {handshake.BridgeVersion}，协议 {handshake.ProtocolVersion}，EPLAN {handshake.Eplan.Version}（{handshake.Eplan.Variant ?? "变体未知"}）",
                $"Handshake OK: add-in {handshake.BridgeVersion}, protocol {handshake.ProtocolVersion}, EPLAN {handshake.Eplan.Version} ({handshake.Eplan.Variant ?? "variant unknown"})"));
            checks.Add(new(handshake.Eplan.ApiExtensionLicence == false ? DoctorStatus.Failure : DoctorStatus.Ok,
                $"API 扩展授权（APIExtension）：{licence}", $"API extension licence (APIExtension): {licenceEn}"));
        }
        else if (status.Problem is { } problem)
        {
            checks.Add(new(DoctorStatus.Failure, $"无法使用插件：{problem.Reason}", $"Cannot use the add-in: {problem.Reason} — {problem.Message}"));
        }

        return checks;
    }

    public static DoctorCheck UserPathLength(int length) => length > UserPathWarningLength
        ? new(DoctorStatus.Warning,
            $"用户 PATH 有 {length} 个字符：过长时 Windows 登录不会加载用户环境变量，EPLAN 的 TEMP 会变成 C:\\WINDOWS\\TEMP，脚本编译失败。请手工缩短（EplanMcp 不会修改 PATH）",
            $"User PATH is {length} characters: when it is too long Windows skips the user environment at logon, EPLAN's TEMP becomes C:\\WINDOWS\\TEMP and script compilation fails. Shorten it by hand (EplanMcp never changes PATH)")
        : new(DoctorStatus.Ok, $"用户 PATH 长度 {length}", $"User PATH length {length}");

    public static int ReadUserPathLength()
    {
        using RegistryKey? environment = Registry.CurrentUser.OpenSubKey("Environment");
        return (environment?.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames) as string)?.Length ?? 0;
    }

    private static string? CurrentPackageFullName()
    {
        int length = 0;
        if (GetCurrentPackageFullName(ref length, null) == AppModelErrorNoPackage)
        {
            return null;
        }

        char[] buffer = new char[length];
        return GetCurrentPackageFullName(ref length, buffer) == 0 ? new string(buffer, 0, Math.Max(0, length - 1)) : "?";
    }

    private const int AppModelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, [Out] char[]? packageFullName);
}
