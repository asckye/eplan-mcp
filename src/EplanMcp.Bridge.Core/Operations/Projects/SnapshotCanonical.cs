using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Snapshot objects, pages and their content hashes (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.4). The Host
/// computes the snapshot hash of a snapshot taken in chunks with the same formula. Pure logic.
/// </summary>
public static class SnapshotCanonical
{
    /// <summary><c>PAGE_LASTAUTOMODIFICATIONDATE</c>, a hint (A36).</summary>
    public const int PageModifiedDate = 11024;

    /// <summary><c>PAGE_LASTAUTOMODIFICATIONTIME</c>, a hint (A36).</summary>
    public const int PageModifiedTime = 11001;

    /// <summary>The registered set of <paramref name="profile"/> by number; null for a profile that does not exist.</summary>
    public static IReadOnlyDictionary<int, SnapshotProperty>? Registered(string profile) =>
        profile == SnapshotProfiles.StandardName ? SnapshotProfiles.Standard.ToDictionary(p => p.Id) : null;

    /// <summary>
    /// The type number of a string identifier <c>db/type/oid/index</c> (api-notes A32). Anything else is a defect of the
    /// adapter, so INTERNAL.
    /// </summary>
    public static int TypeOf(string identifier)
    {
        string[] parts = identifier.Split('/');
        return parts.Length == 4 && parts.All(p => p.Length > 0 && p.All(c => c is >= '0' and <= '9'))
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int type)
            ? type
            : throw new BridgeErrorException(ErrorReason.Internal, "An EPLAN object had an identifier of an unknown form, so no snapshot was taken.");
    }

    /// <summary>The contract object of <paramref name="source"/> with its hash.</summary>
    public static SnapshotObject ObjectOf(SnapshotSourceObject source, IReadOnlyDictionary<int, SnapshotProperty> registered)
    {
        var o = new SnapshotObject
        {
            Kind = source.Kind,
            Type = TypeOf(source.Identifier),
            Oid = source.Oid,
            Name = Normalised(source.Name),
            Page = Normalised(source.Page),
            PageType = source.PageType,
            Category = source.Category,
            Shown = Normalised(source.Shown),
            Main = source.Main,
            Pos = source is { X: { } x, Y: { } y } ? SnapshotValues.Position(x, y) : null,
            Parts = source.Parts?.Select(SnapshotValues.Text).ToList(),
            Start = End(source.Start),
            End = End(source.End),
            Values = [.. source.Values.Select(v => SnapshotValues.Value(v, registered)).OfType<SnapshotValue>().OrderBy(v => v.Id).ThenBy(v => v.Index ?? -1)],
        };
        if (o.Kind == "connection")
        {
            o.Name = ConnectionName(o.Start, o.End);
        }

        o.Hash = Sha256(Text(o));
        return o;
    }

    /// <summary>
    /// The contract page of a page and its functions and connections, the page first: its hash, and as its hint the
    /// automatic modification date and time (api-notes A36), which no hash takes.
    /// </summary>
    public static SnapshotPage Page(string profile, SnapshotSourceObject page, IEnumerable<SnapshotSourceObject> objects, IReadOnlyDictionary<int, SnapshotProperty> registered)
    {
        SnapshotObject self = ObjectOf(page, registered);
        List<SnapshotObject> all = [self, .. objects.Select(o => ObjectOf(o, registered))];
        return new SnapshotPage
        {
            Type = self.Type,
            Oid = self.Oid,
            Name = self.Name ?? string.Empty,
            Hash = PageHash(profile, all),
            Modified = SnapshotValues.Hint(Raw(page, PageModifiedDate), Raw(page, PageModifiedTime)),
            Objects = all,
        };
    }

    /// <summary>
    /// A connection's name as object.resolve checks it:<c>type/oid:pin&gt;type/oid:pin</c> of its ends, by number, so
    /// renaming a device does not change it; <c>-</c> for an end that is not connected.
    /// </summary>
    public static string ConnectionName(SnapshotEnd? start, SnapshotEnd? end) => Describe(start) + ">" + Describe(end);

    /// <summary>The canonical text of an object (design 4.4): fixed lines, values escaped, no hint and no connection name.</summary>
    public static string Text(SnapshotObject o)
    {
        var lines = new List<string> { SnapshotProfiles.Format, "kind=" + o.Kind, "type=" + Number(o.Type), "oid=" + Number(o.Oid) };
        if (o.Kind != "connection")
        {
            Add(lines, "name", o.Name);
        }

        Add(lines, "page", o.Page);
        Add(lines, "pagetype", o.PageType);
        Add(lines, "category", o.Category);
        Add(lines, "shown", o.Shown);
        Add(lines, "main", o.Main is { } main ? (main ? "true" : "false") : null);
        Add(lines, "pos", o.Pos);
        o.Parts?.ForEach(part => Add(lines, "part", part));
        if (o.Kind == "connection")
        {
            lines.Add("start=" + Escape(Describe(o.Start)));
            lines.Add("end=" + Escape(Describe(o.End)));
        }

        foreach (SnapshotValue value in o.Values)
        {
            string key = "p." + Number(value.Id) + (value.Index is { } index ? "." + Number(index) : string.Empty);
            if (value.Texts is null)
            {
                lines.Add(key + "=" + Escape(value.Value ?? string.Empty));
            }
            else
            {
                lines.AddRange(value.Texts.Select(t => key + "." + t.Language + "=" + Escape(t.Text)));
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>A page's hash: the header, then one line per object (the page itself included), sorted ordinally.</summary>
    public static string PageHash(string profile, IEnumerable<SnapshotObject> objects) =>
        Hash(profile, objects.Select(o => string.Join("\t", o.Kind, Number(o.Type), Number(o.Oid), o.Hash)));

    /// <summary>A snapshot's hash: the header, then one line per page, sorted ordinally.</summary>
    public static string SnapshotHash(string profile, IEnumerable<SnapshotPage> pages) =>
        Hash(profile, pages.Select(p => string.Join("\t", Number(p.Type), Number(p.Oid), p.Hash)));

    /// <summary>SHA-256 of the UTF-8 text, lowercase hex.</summary>
    public static string Sha256(string text)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string Hash(string profile, IEnumerable<string> lines) =>
        Sha256(string.Join("\n", new[] { SnapshotProfiles.Format + " " + profile }.Concat(lines.OrderBy(l => l, StringComparer.Ordinal))));

    private static SnapshotEnd? End(SnapshotSourceEnd? end) => end is null
        ? null
        : new SnapshotEnd { Type = TypeOf(end.Identifier), Oid = end.Oid, Function = Normalised(end.Function), Pin = Normalised(end.Pin) };

    private static string Describe(SnapshotEnd? end) => end is null ? "-" : Number(end.Type) + "/" + Number(end.Oid) + ":" + end.Pin;

    private static string? Raw(SnapshotSourceObject o, int id) => o.Values.FirstOrDefault(v => v.Id == id && v.Index is null)?.Raw;

    private static string? Normalised(string? text) => text is null ? null : SnapshotValues.Text(text);

    private static void Add(List<string> lines, string key, string? value)
    {
        if (value is not null)
        {
            lines.Add(key + "=" + Escape(value));
        }
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
