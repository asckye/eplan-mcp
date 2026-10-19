using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using DiffEngine;

namespace EplanMcp.Host.Tests;

/// <summary>Verify settings for every snapshot test in this assembly (docs/standards/testing.md 3).</summary>
internal static class SnapshotDefaults
{
    /// <summary>
    /// A mismatch only fails the test and writes a .received file; it is never accepted automatically, and no diff
    /// tool opens on the desktop during a test run. The failure message lists the changed lines, so the change can be
    /// read in the test output (also in a CI annotation) without opening both files.
    /// </summary>
    [ModuleInitializer]
    public static void Initialize()
    {
        DiffRunner.Disabled = true;
        VerifierSettings.RegisterStringComparer("json", (received, verified, _) => Task.FromResult(
            received == verified ? CompareResult.Equal : CompareResult.NotEqual(ChangedLines(verified, received))));
    }

    /// <summary>The lines that differ, as "-" (verified) and "+" (received) lines with their line numbers.</summary>
    public static string ChangedLines(string verified, string received)
    {
        string[] before = verified.Split('\n');
        string[] after = received.Split('\n');

        // Skip the common head and tail, then diff the rest by its longest common subsequence of lines.
        int skip = 0;
        while (skip < before.Length && skip < after.Length && before[skip] == after[skip])
        {
            skip++;
        }

        int n = before.Length - skip;
        int m = after.Length - skip;
        while (n > 0 && m > 0 && before[skip + n - 1] == after[skip + m - 1])
        {
            n--;
            m--;
        }

        int[][] common = [.. Enumerable.Range(0, n + 1).Select(_ => new int[m + 1])];
        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                common[i][j] = before[skip + i] == after[skip + j]
                    ? common[i + 1][j + 1] + 1
                    : Math.Max(common[i + 1][j], common[i][j + 1]);
            }
        }

        var diff = new StringBuilder();
        int x = 0;
        int y = 0;
        while (x < n || y < m)
        {
            if (x < n && y < m && before[skip + x] == after[skip + y])
            {
                x++;
                y++;
            }
            else if (x < n && (y == m || common[x + 1][y] >= common[x][y + 1]))
            {
                diff.Append(CultureInfo.InvariantCulture, $"- {skip + x + 1,5}: {before[skip + x]}\n");
                x++;
            }
            else
            {
                diff.Append(CultureInfo.InvariantCulture, $"+ {skip + y + 1,5}: {after[skip + y]}\n");
                y++;
            }
        }

        return diff.ToString();
    }
}
