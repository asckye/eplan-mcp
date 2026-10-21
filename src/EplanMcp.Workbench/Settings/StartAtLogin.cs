using System.IO;
using System.Security;
using Microsoft.Win32;

namespace EplanMcp.Workbench.Settings;

/// <summary>
/// "Start at login" (M5 design 4.1, Q3, ADR-0016): the value <see cref="ValueName"/> in the current user's Run key, the
/// one registry entry the Workbench writes, and only in HKCU. The installer writes it when its box is ticked (the
/// default) and removes it on uninstall; the settings drawer turns it off or on again. The registry is the only record
/// of it, so the switch shows what Windows will do.
/// </summary>
/// <param name="runKey">The Run key below HKCU; tests give one of their own.</param>
/// <param name="executable">What the entry starts: this Workbench.</param>
internal sealed class StartAtLogin(string runKey, string executable)
{
    /// <summary>The name of the value, which the installer uses too (M5 design 4.3).</summary>
    public const string ValueName = "EplanMcp Workbench";

    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The entry for this Workbench in the current user's own Run key.</summary>
    public static StartAtLogin ForCurrentUser() => new(RunKey, Environment.ProcessPath ?? "EplanMcp.Workbench.exe");

    /// <summary>Whether the Workbench starts when the engineer logs in; false when the key cannot be read.</summary>
    public bool IsOn
    {
        get
        {
            try
            {
                using RegistryKey? run = Registry.CurrentUser.OpenSubKey(runKey);
                return run?.GetValue(ValueName) is string { Length: > 0 };
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                return false;
            }
        }
    }

    /// <summary>Writes the entry, quoted, or removes it; false, with nothing changed, when Windows refuses.</summary>
    public bool Set(bool on)
    {
        try
        {
            using RegistryKey run = Registry.CurrentUser.CreateSubKey(runKey);
            if (on)
            {
                run.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
            }
            else
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
