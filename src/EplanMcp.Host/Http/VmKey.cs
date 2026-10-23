using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace EplanMcp.Host.Http;

/// <summary>Current-user DPAPI only; replacement is atomic so each new request sees the current key (SEC-27).</summary>
internal static class VmKey
{
    public static string Generate() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool IsValid(string value) => value.Length == 43 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        && "AEIMQUYcgkosw048".Contains(value[^1], StringComparison.Ordinal);

    private static string FileName(HostDataLocations locations) => Path.Combine(locations.Root, "vm-key.dpapi");

    public static string Read(HostDataLocations locations) => System.Text.Encoding.ASCII.GetString(Transform(File.ReadAllBytes(FileName(locations)), protect: false));

    public static async Task<int> RunAsync(string action, HostDataLocations locations, TextWriter output, CancellationToken cancellationToken)
    {
        try
        {
            if (action == "delete")
            {
                File.Delete(FileName(locations));
                return 0;
            }

            if (action == "create")
            {
                Directory.CreateDirectory(locations.Root);
                string temporary = FileName(locations) + "." + Guid.NewGuid().ToString("N");
                try
                {
                    await File.WriteAllBytesAsync(temporary, Transform(System.Text.Encoding.ASCII.GetBytes(Generate()), protect: true), cancellationToken).ConfigureAwait(false);
                    File.Move(temporary, FileName(locations), overwrite: true);
                }
                finally
                {
                    File.Delete(temporary);
                }
            }

            // Only the explicit show command reveals the key, never serve, logs or audit.
            await output.WriteLineAsync(action == "show" ? Read(locations) : "VM key created.").ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            await output.WriteLineAsync("VM key unavailable.").ConfigureAwait(false);
            return 1;
        }
    }

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        var result = new Blob();
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            // No LOCAL_MACHINE flag: DPAPI binds the ciphertext to the current Windows user. UI is forbidden.
            bool ok = protect ? CryptProtectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out result)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out result);
            if (!ok)
            {
                throw new CryptographicException();
            }

            byte[] value = new byte[result.Size];
            Marshal.Copy(result.Data, value, 0, value.Length);
            return value;
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            LocalFree(result.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
