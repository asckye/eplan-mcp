namespace EplanMcp.Packs;

public static class FunctionDesignation
{
    public static string? Letters(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        int dash = name.LastIndexOf('-');
        if (dash < 0)
        {
            return null;
        }

        int start = dash + 1;
        int end = name.IndexOf(':', start);
        if (end < 0)
        {
            end = name.Length;
        }

        while (start < end && char.IsDigit(name[start]))
        {
            start++;
        }

        int cursor = start;
        while (cursor < end && char.IsLetter(name[cursor]))
        {
            cursor++;
        }

        return cursor == start ? null : name[start..cursor];
    }
}
