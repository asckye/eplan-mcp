using System.Globalization;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The page name format eplan_page_create takes (docs/tools/eplan_page_create.md, section 2):
/// <c>[=value][+value][&amp;value]/number</c>, the parts in this order. Values are not empty and hold no white space,
/// control characters or <c>=+&amp;/#</c>; the number is a positive integer without leading zeros. Other prefixes
/// (<c>==</c>, <c>++</c>, <c>#</c>) are not supported in this version.
/// </summary>
public static class PageNames
{
    private const string Prefixes = "=+&";

    private static readonly char[] _separators = ['=', '+', '&', '/', '#'];

    /// <summary>The parts of <paramref name="name"/>, or null with <paramref name="problem"/> saying what is wrong.</summary>
    public static PageName? Parse(string? name, out string? problem)
    {
        problem = Problem(name, out string?[] values, out int counter);
        return problem is null ? new PageName(name!, values[0], values[1], values[2], counter) : null;
    }

    /// <summary>The part of a full page name before its last <c>/</c>: the structure the page is in.</summary>
    public static string StructureOf(string pageName)
    {
        int slash = pageName.LastIndexOf('/');
        return slash < 0 ? pageName : pageName.Substring(0, slash);
    }

    private static string? Problem(string? name, out string?[] values, out int counter)
    {
        values = new string?[Prefixes.Length];
        counter = 0;
        if (string.IsNullOrEmpty(name))
        {
            return "is required: a full page name such as =GAA+A1&EFS1/5.";
        }

        int position = 0;
        int last = -1;
        while (position < name!.Length && name[position] != '/')
        {
            char prefix = name[position];
            int kind = Prefixes.IndexOf(prefix);
            if (prefix == '#' || (kind >= 0 && position + 1 < name.Length && name[position + 1] == prefix))
            {
                return $"uses '{(prefix == '#' ? "#" : new string(prefix, 2))}', which this version does not support; use only =, +, & and /.";
            }

            if (kind < 0)
            {
                return "must start with =, + or & followed by a value, or with / and the page number, e.g. =GAA+A1&EFS1/5.";
            }

            if (kind <= last)
            {
                return "must have its parts in the order =, +, &, / and each at most once.";
            }

            int end = name.IndexOfAny(_separators, position + 1);
            string value = name.Substring(position + 1, (end < 0 ? name.Length : end) - position - 1);
            if (value.Length == 0 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            {
                return $"has an empty value, white space or a control character after '{prefix}'.";
            }

            values[kind] = value;
            last = kind;
            position += value.Length + 1;
        }

        if (position >= name.Length)
        {
            return "needs '/' and the page number at the end, e.g. =GAA+A1&EFS1/5.";
        }

        string number = name.Substring(position + 1);
        return number.Length == 0 || number[0] == '0' || !number.All(c => c is >= '0' and <= '9')
            || !int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out counter)
            ? "must end with a page number from 1 without leading zeros, e.g. /5."
            : null;
    }
}
