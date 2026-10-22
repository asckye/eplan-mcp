using System.IO.Compression;
using System.Text;

namespace EplanMcp.Packs.Tests;

/// <summary>ZIP archives built in memory, including the unsafe ones a hostile .eplanpack could hold.</summary>
internal static class Zips
{
    public sealed record Entry(string Name, string Content = "x", int Attributes = 0, CompressionLevel Level = CompressionLevel.Optimal);

    public static byte[] Bytes(params Entry[] entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            foreach (Entry entry in entries)
            {
                ZipArchiveEntry created = zip.CreateEntry(entry.Name, entry.Level);
                created.ExternalAttributes = entry.Attributes;
                if (!entry.Name.EndsWith('/'))
                {
                    using var writer = new StreamWriter(created.Open(), new UTF8Encoding(false));
                    writer.Write(entry.Content);
                }
            }
        }

        return memory.ToArray();
    }

    public static ZipArchive Open(params Entry[] entries) => new(new MemoryStream(Bytes(entries)), ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);

    /// <summary>Writes <paramref name="size"/> as the uncompressed size of entry <paramref name="name"/>, in its local and central headers.</summary>
    public static byte[] WithDeclaredSize(byte[] zip, string name, uint size)
    {
        byte[] result = [.. zip];
        byte[] encoded = Encoding.UTF8.GetBytes(name);
        for (int i = 0; i + 46 <= result.Length; i++)
        {
            // Local header: size at 22, name length at 26, name at 30. Central header: size at 24, name length at 28, name at 46.
            (int size, int length, int at) fields = BitConverter.ToUInt32(result, i) switch
            {
                0x04034b50 => (22, 26, 30),
                0x02014b50 => (24, 28, 46),
                _ => (-1, 0, 0),
            };
            if (fields.size > 0 && BitConverter.ToUInt16(result, i + fields.length) == encoded.Length
                && result.AsSpan(i + fields.at, encoded.Length).SequenceEqual(encoded))
            {
                BitConverter.GetBytes(size).CopyTo(result, i + fields.size);
            }
        }

        return result;
    }
}
