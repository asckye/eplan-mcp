using System.Diagnostics;
using System.Globalization;
using Eplan.EplApi.ApplicationFramework;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Contracts;
using Microsoft.Win32;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Static facts about this EPLAN process, captured once on the main thread when the Bridge starts.</summary>
internal static class EplanEnvironment
{
    private const string VariantSwitch = "/Variant:";

    /// <summary>The add-in: EPLAN.exe with its user interface.</summary>
    public static EplanInstanceInfo Capture(IBridgeLog log)
    {
        MainThread.Assert();
        using var process = Process.GetCurrentProcess();
        return new EplanInstanceInfo
        {
            ProcessId = process.Id,
            Version = process.MainModule?.FileVersionInfo.FileVersion ?? string.Empty,
            Variant = VariantFromCommandLine() ?? VariantFromRemoteServerRegistry(process.Id, log),
            ApiExtensionLicence = ApiExtension(log),
        };
    }

    /// <summary>
    /// The offline host (M5 design 4.9): EPLAN runs without a user interface in this process, which has neither a
    /// <c>/Variant</c> switch nor EPLAN.exe as its image. So the version is that of EPLAN.exe in the Platform's Bin, and
    /// the variant is the folder above the variant's Bin version folder (<c>…\Electric P8\2026.0.3\Bin</c>, api-notes A22).
    /// </summary>
    public static EplanInstanceInfo CaptureOffline(IBridgeLog log, string variantBin, string platformBin)
    {
        MainThread.Assert();
        using var process = Process.GetCurrentProcess();
        string eplan = Path.Combine(platformBin, "EPLAN.exe");
        return new EplanInstanceInfo
        {
            ProcessId = process.Id,
            Version = File.Exists(eplan) ? FileVersionInfo.GetVersionInfo(eplan).FileVersion ?? string.Empty : string.Empty,
            Variant = Directory.GetParent(variantBin.TrimEnd('\\'))?.Parent?.Name,
            ApiExtensionLicence = ApiExtension(log),
            Offline = true,
        };
    }

    /// <summary><c>EPLAN.exe /Variant:"Pro Panel"</c> arrives as the argument <c>/Variant:Pro Panel</c>.</summary>
    private static string? VariantFromCommandLine() =>
        Environment.GetCommandLineArgs()
            .Where(arg => arg.StartsWith(VariantSwitch, StringComparison.OrdinalIgnoreCase))
            .Select(arg => arg.Substring(VariantSwitch.Length).Trim('"', ' '))
            .FirstOrDefault(v => v.Length > 0);

    /// <summary>EPLAN registers each running instance under <c>HKCU\Software\EPLAN\RemoteServer\&lt;port&gt;</c> (api-notes A1).</summary>
    private static string? VariantFromRemoteServerRegistry(int processId, IBridgeLog log)
    {
        try
        {
            using RegistryKey? servers = Registry.CurrentUser.OpenSubKey(@"Software\EPLAN\RemoteServer");
            foreach (string port in servers?.GetSubKeyNames() ?? [])
            {
                using RegistryKey? server = servers!.OpenSubKey(port);
                if (Convert.ToString(server?.GetValue("ProcessID"), CultureInfo.InvariantCulture) == processId.ToString(CultureInfo.InvariantCulture))
                {
                    return server!.GetValue("EplanProductVariant") as string;
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            log.Write(BridgeLogLevel.Warning, "Could not read the EPLAN remote server registry", ex);
        }

        return null;
    }

    /// <summary><c>HasOption</c> only checks; <c>LockOption</c> would hold a licence until EPLAN exits (api-notes A2).</summary>
    public static bool? ApiExtension(IBridgeLog log)
    {
        MainThread.Assert();
        try
        {
            using var license = new License();
            return license.HasOption(LicenseOptions.APIExtension);
        }
        catch (Exception ex)
        {
            log.Write(BridgeLogLevel.Warning, "Could not check the API extension licence", ex);
            return null;
        }
    }
}
