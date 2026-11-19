namespace EplanMcp.Contracts;

public static class UnplacedDeleteTags
{
    public static void Validate(IReadOnlyList<string?>? tags, List<string> errors)
    {
        if (tags is null || tags.Count is < 1 or > 20)
        {
            errors.Add("/unplaced requires an array of 1..20 distinct full device tags.");
        }
        if (tags is null)
        {
            return;
        }

        var indices = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < tags.Count; i++)
        {
            string? tag = tags[i];
            if (tag is null || tag.Length is < 2 or > 128 || tag.Any(char.IsControl) || string.IsNullOrWhiteSpace(tag))
            {
                errors.Add($"/unplaced/{i} requires a full tag of 2..128 characters without control characters.");
                continue;
            }
            if (!indices.TryGetValue(tag, out List<int>? matches))
            {
                matches = [];
                indices.Add(tag, matches);
            }
            else
            {
                errors.Add($"/unplaced/{i} duplicates /unplaced/{matches[0]}.");
            }
            matches.Add(i);
        }

        foreach (var child in indices)
        {
            for (int separator = child.Key.IndexOf(':'); separator >= 0; separator = child.Key.IndexOf(':', separator + 1))
            {
                string parent = child.Key.Substring(0, separator);
                if (indices.TryGetValue(parent, out List<int>? parents))
                {
                    string message = $"'{child.Key}' depends on '{parent}'; delete them in separate calls.";
                    foreach (int index in parents.Concat(child.Value))
                    {
                        errors.Add($"/unplaced/{index} {message}");
                    }
                }
            }
        }
    }
}
