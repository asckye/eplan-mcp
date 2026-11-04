using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.Tests;

internal sealed partial class SmallProjectRedrawDriver
{
    private async Task InvokeItemsAsync(string page, string tool, JObject args, JObject[] items)
    {
        JObject? rejection = null;
        await InvokeAsync(page, tool, args, items, onFailure: result => rejection = result);
        if (items.Length < 2 || args["items"] is not JArray batch || batch.Count != items.Length
            || rejection?["error"] is not JObject error || Text(error["reason"]) != "INVALID_ARGUMENT"
            || Text(error["execution"]) != "not_started")
        { return; }

        report.Note(page, "Batch", "", "Parameter validation rejected the batch before execution; retrying each item once.");
        for (int index = 0; index < batch.Count; index++)
        {
            var single = (JObject)args.DeepClone();
            single["items"] = new JArray(batch[index].DeepClone());
            await InvokeAsync(page, tool, single, [items[index]]);
        }
    }

    private async Task<JObject?> InvokeAsync(string page, string tool, JObject args, JObject[] items, bool write = true,
        Action<JObject>? onFailure = null)
    {
        if (tool != "eplan_macro_list")
        { args["project"] = project; }
        if (write)
        { args["dry_run"] = false; }
        if (tool is "eplan_placement_create" or "eplan_connection_create")
        { args["layout_guard"] = "warn"; }
        if (tool is "eplan_page_get" or "eplan_page_review" or "eplan_connection_create" or "eplan_placement_create" or "eplan_placement_update")
        { args["response_format"] = "detailed"; }
        try
        {
            JObject result = await call(tool, args);
            if (write && Text(result["error"]?["execution"] ?? result["execution"]) != "not_started")
            { _drawingCache.Remove(page); }
            bool success = result["error"] is null;
            bool noConnections = tool == "eplan_connection_generate" && success && Text(result["execution"]) == "not_started";
            if (write && Text(result["execution"]) != "committed" && !noConnections)
            { success = false; }
            string? error = success ? null : (result["error"] ?? result).ToString(Newtonsoft.Json.Formatting.None);
            report.RecordCall(page, tool, items.Select(Type), items.Select(Id), args, result, success, error);
            if (success && tool == "eplan_placement_create")
            {
                foreach (JObject item in items.Where(IsGraphic))
                { report.RecordOrigin(page, item, "manual_graphics"); }
            }
            if (noConnections)
            { report.Note(page, "Connection", "", "Connection generation did not start because no connections were added or removed; see the recorded result."); }
            if (!success)
            { onFailure?.Invoke(result); }
            return success ? result : null;
        }
        catch (OperationCanceledException) when (TestContext.Current.CancellationToken.IsCancellationRequested)
        { throw; }
        catch (Exception error)
        {
            if (write)
            { _drawingCache.Remove(page); }
            report.RecordCall(page, tool, items.Select(Type), items.Select(Id), args, null, false, error.GetType().Name + ": " + error.Message);
            return null;
        }
    }
}
