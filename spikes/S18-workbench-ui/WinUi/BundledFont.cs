using System.Buffers.Binary;
using System.Text;

namespace EplanMcp.Spikes.S18.WinUi;

/// <summary>
/// Font files found in the app's Fonts folder: the regular file, the family name read from its own name table, and
/// the SemiBold file of the same family if there is one.
/// </summary>
internal sealed record BundledFontFile(string Path, string Family, string? SemiBoldPath);

/// <summary>
/// The Workbench font (owner, 2026-10-20): Maple Mono CN for all text, shipped in the app folder under Fonts\ and
/// loaded only by this process, never installed. Whatever .ttf or .otf is in Fonts\ at start-up is used (a file with
/// "Regular" in its name first); without one the text falls back to Segoe UI Variable and Microsoft YaHei UI.
/// </summary>
internal static class BundledFont
{
    internal const string Fallback = "Segoe UI Variable Text, Microsoft YaHei UI";

    internal static string Folder => System.IO.Path.Combine(AppContext.BaseDirectory, "Fonts");

    internal static BundledFontFile? Find()
    {
        if (!Directory.Exists(Folder))
        {
            return null;
        }

        List<string> files = Directory.EnumerateFiles(Folder)
            .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => System.IO.Path.GetFileName(f).Contains("Regular", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(f => f, StringComparer.Ordinal)
            .ToList();
        foreach (string file in files)
        {
            string? family = ReadFamilyName(file);
            if (family is not null)
            {
                string? semiBold = files.FirstOrDefault(f => System.IO.Path.GetFileName(f).Contains("-SemiBold.", StringComparison.OrdinalIgnoreCase)
                    && ReadFamilyName(f) == family);
                return new BundledFontFile(file, family, semiBold);
            }
        }

        return null;
    }

    /// <summary>
    /// XAML FontFamily source: "ms-appx:///Fonts/&lt;file&gt;#&lt;family&gt;" followed by the fallback list. In an unpackaged
    /// app ms-appx:/// is the folder of the exe. One file per FontFamily: XAML does not pick the SemiBold file for
    /// SemiBold text by itself (it simulates bold from the regular file), so strong text gets its own FontFamily.
    /// </summary>
    internal static string Source(BundledFontFile? font, bool strong = false)
    {
        if (font is null)
        {
            return Fallback;
        }

        string file = strong && font.SemiBoldPath is not null ? font.SemiBoldPath : font.Path;
        return $"ms-appx:///Fonts/{System.IO.Path.GetFileName(file)}#{font.Family}, {Fallback}";
    }

    /// <summary>
    /// Typographic family (name ID 16), else family (name ID 1), Windows platform, US English. Reads only the table
    /// directory and the name table: a Maple Mono CN file is about 18 MB.
    /// </summary>
    internal static string? ReadFamilyName(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            byte[] header = Read(stream, 0, 12);
            int tables = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
            byte[] directory = Read(stream, 12, 16 * tables);
            for (int i = 0; i < tables; i++)
            {
                ReadOnlySpan<byte> record = directory.AsSpan(16 * i, 16);
                if (Encoding.ASCII.GetString(record[..4]) != "name")
                {
                    continue;
                }

                ReadOnlySpan<byte> name = Read(stream, BinaryPrimitives.ReadUInt32BigEndian(record[8..]), (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]));
                int count = BinaryPrimitives.ReadUInt16BigEndian(name[2..]);
                int strings = BinaryPrimitives.ReadUInt16BigEndian(name[4..]);
                string? family = null;
                for (int r = 0; r < count; r++)
                {
                    ReadOnlySpan<byte> entry = name.Slice(6 + (12 * r), 12);
                    ushort platform = BinaryPrimitives.ReadUInt16BigEndian(entry);
                    ushort language = BinaryPrimitives.ReadUInt16BigEndian(entry[4..]);
                    ushort id = BinaryPrimitives.ReadUInt16BigEndian(entry[6..]);
                    if (platform != 3 || language != 0x409 || id is not (1 or 16))
                    {
                        continue;
                    }

                    int length = BinaryPrimitives.ReadUInt16BigEndian(entry[8..]);
                    int offset = BinaryPrimitives.ReadUInt16BigEndian(entry[10..]);
                    string value = Encoding.BigEndianUnicode.GetString(name.Slice(strings + offset, length));
                    if (id == 16)
                    {
                        return value;
                    }

                    family = value;
                }

                return family;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            SpikeLog.Write("font-file-unreadable", System.IO.Path.GetFileName(path) + ": " + e.Message);
        }

        return null;
    }

    private static byte[] Read(FileStream stream, long offset, int count)
    {
        var buffer = new byte[count];
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }
}
