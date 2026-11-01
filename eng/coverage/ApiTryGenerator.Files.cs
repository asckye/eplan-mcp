using System.Text.RegularExpressions;
using EplanMcp.Host.Tools;

namespace EplanMcp.Coverage;

internal static partial class ApiTryGenerator
{
    private static bool FileMatch(string text, string pattern) => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    internal static string? FileDirection(ApiEntry entry, ApiDetail member, ApiParameter parameter)
    {
        string name = parameter.Name;
        if (parameter.Type != "System.String" || FileMatch(name, "project|linkfile|directory|folder|dir$|template")
            || !FileMatch(name, "file|archive|macro|xml") && !(entry.Name == "ImportData" && entry.Type == "T:Eplan.EplApi.HEServices.PlcService" && name == "strFullName"))
        { return null; }
        string? Direction(string text, string input, string output)
        {
            bool read = FileMatch(text, input), write = FileMatch(text, output);
            return read == write ? null : read ? "input" : "output";
        }
        // Explicit parameter direction wins; documentation then method/service verbs supply a missing direction.
        if (FileMatch(name, "input|import|source|output|export|target|destination"))
        { return Direction(name, "input|import|source", "output|export|target|destination"); }
        if (!string.IsNullOrEmpty(member.Summary))
        { return Direction(member.Summary!, "\\b(import|read|load)s?\\b", "\\b(export|write|save)s?\\b"); }
        return FileMatch(entry.Name, "Import.*Export|Export.*Import|Read.*Write|Write.*Read") ? null
            : Direction(entry.Name + (entry.Type.EndsWith(".Insert", StringComparison.Ordinal) ? "Import" : ""), "^(Import|Read|Load)|Import$", "^(Export|Write|Save)");
    }
    private static string? FileExclusion(ApiEntry entry, ApiDetail member)
    {
        string? original = ApiTryServices.Exclusion(entry, member);
        if (original != "external_path_parameter" || FileMatch(entry.Type + "." + entry.Name + " " + member.Summary, "print|send|mail|publish|masterdata|parts|database|backup|restore"))
        { return original; }
        ApiParameter[] remaining = [.. member.Parameters!.Where(p => FileDirection(entry, member, p) is null
            && !(p.Name == "fileFormat" && p.Type == "Eplan.EplApi.HEServices.DeviceService.Format"))];
        return member.Parameters!.Any(p => FileDirection(entry, member, p) is not null) && ApiTryServices.Exclusion(entry, member with { Parameters = remaining }) is null ? null : original;
    }
    private static bool FileOutput(ApiEntry entry) => entry.Details.Where(d => d.Kind == "overload")
        .Any(d => d.Parameters!.Any(p => FileDirection(entry, d, p) == "output"));
    private static string FileCatalog((ApiEntry Entry, ApiDetail Member)[] methods, Dictionary<string, ApiTryServices.Service> services) =>
        "    public static string? PathDirection(string signature, string name) => (signature, name) switch\n    {\n"
        + string.Join("\n", methods.Where(m => services.ContainsKey(m.Entry.Type[2..])).SelectMany(m => m.Member.Parameters!
            .Where(p => FileDirection(m.Entry, m.Member, p) is not null).Select(p => "        (" + Literal(m.Member.Signature!) + ", " + Literal(ParameterName(p)) + ") => " + Literal(FileDirection(m.Entry, m.Member, p)!) + ",")))
        + "\n        _ => null,\n    };\n";
}
