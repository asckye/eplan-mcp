namespace EplanMcp.Contracts;

/// <summary>Drawing name equivalence shared by planning and target comparison (A55).</summary>
public static class DrawingNames
{
    /// <summary>A55: ignore empty structure segments and tolerate a dropped unused function structure.</summary>
    public static bool SameName(string requested, string actual, string page)
    {
        requested = WithoutEmptyStructure(requested);
        actual = WithoutEmptyStructure(actual);
        string full = WithoutEmptyStructure(requested.StartsWith("-", StringComparison.Ordinal) ? DeviceStructureOf(page) + requested : requested);
        int end = full.IndexOfAny(['+', '-', '&', '#']);
        // A121: on a bare page number (no structure) EPLAN stores -DI2 as DI2.
        bool bare = IsBarePage(page) && full.StartsWith("-", StringComparison.Ordinal) && full.Substring(1) == actual;
        return requested == actual || full == actual || bare || full.StartsWith("=", StringComparison.Ordinal) && end > 0 && full.Substring(end) == actual;
    }
    /// <summary>
    /// A55: device structure excludes the document type but retains a following structure segment. A bare page number
    /// without '/' has no structure (A121: the NFPA basic projects name pages 1, 2, 3).
    /// </summary>
    public static string DeviceStructureOf(string page)
    {
        string structure = page.LastIndexOf('/') is var slash && slash >= 0 ? page.Substring(0, slash) : IsStructure(page) ? page : string.Empty;
        int type = structure.IndexOf('&'), end = type < 0 ? -1 : structure.IndexOf('#', type);
        return type < 0 ? structure : structure.Substring(0, type) + (end < 0 ? string.Empty : structure.Substring(end));
    }
    /// <summary>The page's higher-level function without its '=' (GAA on =GAA+A1&amp;EFS1/3); empty when the page has none.</summary>
    public static string HigherLevelFunctionOf(string page)
    {
        string structure = DeviceStructureOf(page);
        for (int i = 0; i < structure.Length; i++)
        {
            // A single '=' starts the higher-level function (DESIGNATION_PLANT, 1100); '==' is the functional assignment (1300).
            if (structure[i] != '=' || i + 1 < structure.Length && structure[i + 1] == '=' || i > 0 && structure[i - 1] == '=')
            { continue; }
            int end = structure.IndexOfAny(['=', '+', '&', '#', '-'], i + 1);
            return end < 0 ? structure.Substring(i + 1) : structure.Substring(i + 1, end - i - 1);
        }
        return string.Empty;
    }
    /// <summary>Whether a name without '/' is a structure (=GAA+A1) rather than a bare page number (4).</summary>
    public static bool IsStructure(string name) => name.Length > 0 && "=+&#".Contains(name[0]);

    /// <summary>Whether a page name is a bare page number without structure (A121: the NFPA basic projects name pages 1, 2, 3).</summary>
    public static bool IsBarePage(string page) => !page.Contains('/') && !IsStructure(page);

    private static string WithoutEmptyStructure(string name)
    {
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length;)
        {
            int start = i++;
            if (!"=+&#".Contains(name[start]))
            { result.Append(name.Substring(start)); break; }
            if (name[start] is '=' or '+' && i < name.Length && name[i] == name[start])
            { i++; }
            int value = i;
            while (i < name.Length && !"=+&#-".Contains(name[i]))
            { i++; }
            if (i > value)
            { result.Append(name, start, i - start); }
        }
        return result.ToString();
    }
}
