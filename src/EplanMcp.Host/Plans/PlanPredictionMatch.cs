using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Plans;

/// <summary>Execution contract for P20: require a real plan, fresh complete inspection and the complete planned function inventory.</summary>
internal static class PlanPredictionMatch
{
    public static bool Matches(PlanStep approved, WritePlan actual, JsonElement args,
        PlanMacroInspectData? inspection = null, IReadOnlyList<PlanMacroFunction>? functions = null)
    {
        if (approved.Prediction is not { } prediction || !JsonElement.DeepEquals(approved.Args, args)
            || actual.MasterData is not null || actual.Project != prediction.GetProperty("project").GetProperty("name").GetString()
            || !Path.IsPathFullyQualified(actual.ProjectPath)
            || PlanSnapshotHashes.ProjectKey(actual.ProjectPath) != prediction.GetProperty("project").GetProperty("path").GetString())
        {
            return false;
        }
        if (approved.Op == "page.update")
        {
            return actual.Changes.Select(c => new PlanChange(c.Action, c.Kind, c.Name, c.Property, c.OldValue, c.NewValue, true))
                .OrderBy(c => c.Name, StringComparer.Ordinal).SequenceEqual(approved.PlannedChanges.OrderBy(c => c.Name, StringComparer.Ordinal));
        }
        if (approved.Op != "macro.insert" || inspection is null || functions is null
            || !JsonElement.DeepEquals(prediction.GetProperty("description"), JsonSerializer.SerializeToElement(inspection, PlanJson.Options)))
        {
            return false;
        }
        JsonElement value = approved.Expected.Single().GetProperty("value");
        if (actual.Changes.Count != 1 || actual.Changes[0] is not { Action: "create", Kind: "macro", Property: null, OldValue: null }
            || actual.Changes[0].Name != args.GetProperty("page").GetString()
            || actual.Changes[0].NewValue != approved.PlannedChanges.Single().NewValue
            || actual.Details?.GetValueOrDefault("macro") != args.GetProperty("macro").GetString()!.Replace('\\', '/')
            || actual.Details?.GetValueOrDefault("objectCount") != inspection.ObjectCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || functions.Count != value.GetProperty("functionCount").GetInt32())
        {
            return false;
        }
        JsonElement[] allowed = [.. value.GetProperty("functions").EnumerateArray()];
        int[] owners = [.. Enumerable.Repeat(-1, functions.Count)];
        return Enumerable.Range(0, allowed.Length).All(i => Assign(i, new bool[functions.Count]));

        bool Fits(int slot, int index)
        {
            PlanMacroFunction function = functions[index];
            JsonElement constraint = allowed[slot];
            return function.Kind == constraint.GetProperty("kind").GetString()
                && function.Category == constraint.GetProperty("category").GetString() && function.Main == constraint.GetProperty("main").GetBoolean()
                && PlanNamePattern.Matches(constraint.GetProperty("namePattern").GetString()!, function.Name)
                && PlanNamePattern.Matches(constraint.GetProperty("visibleNamePattern").GetString()!, function.VisibleName);
        }

        bool Assign(int slot, bool[] seen)
        {
            foreach (int index in Enumerable.Range(0, functions.Count).OrderBy(i => owners[i] >= 0))
            {
                if (!seen[index] && Fits(slot, index))
                {
                    seen[index] = true;
                    if (owners[index] < 0 || Assign(owners[index], seen))
                    {
                        owners[index] = slot;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
