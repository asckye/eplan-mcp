namespace EplanMcp.Workbench.Approvals;

/// <summary>
/// The writes the engineer started in the Workbench and that have not ended yet (ADR-0065 decision 3): each one by its
/// tool and arguments hash. A question from the Workbench's own Host that matches one is answered for that click, once;
/// anything else is asked as usual.
/// </summary>
internal sealed class EngineerWrites
{
    private readonly Lock _lock = new();
    private readonly List<Entry> _open = [];

    /// <summary>Registers a write before its call is sent; disposing the result forgets it, answered or not.</summary>
    public IDisposable Begin(string tool, string argumentsHash)
    {
        var entry = new Entry(this, tool, argumentsHash);
        lock (_lock)
        {
            _open.Add(entry);
        }

        return entry;
    }

    /// <summary>Whether a started write has this tool and hash; a match is used up, so a second question asks the engineer.</summary>
    public bool TryClaim(string tool, string argumentsHash)
    {
        lock (_lock)
        {
            int index = _open.FindIndex(e => string.Equals(e.Tool, tool, StringComparison.Ordinal) && string.Equals(e.Hash, argumentsHash, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            _open.RemoveAt(index);
            return true;
        }
    }

    private void Forget(Entry entry)
    {
        lock (_lock)
        {
            _open.Remove(entry);
        }
    }

    private sealed class Entry(EngineerWrites owner, string tool, string hash) : IDisposable
    {
        public string Tool { get; } = tool;

        public string Hash { get; } = hash;

        public void Dispose() => owner.Forget(this);
    }
}
