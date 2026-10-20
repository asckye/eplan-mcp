using System.Globalization;
using EplanMcp.Evals.Agent;

namespace EplanMcp.Evals.Run;

/// <summary>
/// The options of <c>run</c> (agent evals design 4.4). <see cref="Set"/> is <c>dev</c>, <c>holdout</c>, <c>all</c> or
/// null when not given; the holdout set is meant for acceptance runs only (testing.md 5). <see cref="Template"/> is the
/// .elk file tasks on a copy copy, by default EPLAN's sample project (<see cref="ProjectCopies.FindTemplate"/>).
/// </summary>
internal sealed record RunOptions(string? Tasks, string? Set, string? Task, int Repeat, string Model, string? Claude, string? Eplanmcp, string? Template = null)
{
    public const int MaxRepeat = 10;

    public const string Usage = "run [--tasks <dir>] [--set dev|holdout|all] [--task <id>] [--repeat <n>] [--model <model>] [--claude <path>] [--eplanmcp <path>] [--template <.elk>]";

    public static RunOptions Default { get; } = new(null, null, null, 1, ClaudeCommand.DefaultModel, null, null);

    /// <summary>The development set by default; a task named with <c>--task</c> is found in either set.</summary>
    public string SetToRun => Set ?? (Task is null ? "dev" : "all");

    /// <summary>The options after <c>run</c>, or null with the reason in <paramref name="error"/>.</summary>
    public static RunOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        RunOptions options = Default;
        for (int i = 0; i < args.Count; i += 2)
        {
            if (i + 1 >= args.Count)
            {
                error = $"{args[i]} needs a value";
                return null;
            }

            string value = args[i + 1];
            switch (args[i])
            {
                case "--tasks":
                    options = options with { Tasks = value };
                    break;
                case "--set" when value is "dev" or "holdout" or "all":
                    options = options with { Set = value };
                    break;
                case "--set":
                    error = $"--set is dev, holdout or all, not {value}";
                    return null;
                case "--task":
                    options = options with { Task = value };
                    break;
                case "--repeat" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int repeat) && repeat is >= 1 and <= MaxRepeat:
                    options = options with { Repeat = repeat };
                    break;
                case "--repeat":
                    error = $"--repeat is a whole number from 1 to {MaxRepeat}, not {value}";
                    return null;
                case "--model":
                    options = options with { Model = value };
                    break;
                case "--claude":
                    options = options with { Claude = value };
                    break;
                case "--eplanmcp":
                    options = options with { Eplanmcp = value };
                    break;
                case "--template":
                    options = options with { Template = value };
                    break;
                default:
                    error = $"unknown option {args[i]}";
                    return null;
            }
        }

        error = null;
        return options;
    }
}
