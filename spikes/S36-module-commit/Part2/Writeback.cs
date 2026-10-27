using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using Newtonsoft.Json.Linq;
using EplanInsert = Eplan.EplApi.HEServices.Insert;

namespace EplanMcp.Spikes.S36.Part2;

#pragma warning disable CA2000 // Created/returned StorableObjects belong to EPLAN (A39).
internal sealed partial class Probe
{
    private JObject? _mutationGuard;
    private PlaceHolder BuildHolder(Page page, string name, bool external)
    {
        Function target = Target(page);
        var holder = new PlaceHolder();
        holder.Create(page);
        holder.Name = name;
        holder.AddRecord("Apply");
        foreach ((string variable, int id, int index) in _bindings) { Bind(holder, target, variable, id, index); }
        Set(holder, "Boolean", "1");
        Set(holder, "Plain", "S36P2 plain after");
        using PropertyValue? partBefore = Property(target, 20100, 1);
        string? original = partBefore is null || partBefore.IsEmpty ? null : partBefore.ToString();
        string part = _parts.First(p => p != original); // Only string candidates discovered before SafetyPoint; never load master data.
        Set(holder, "Part", part);
        using PropertyValue? before = Property(target, 20011);
        using MultiLangString translated = before is null || before.IsEmpty ? new MultiLangString() : before.ToMultiLangString();
        JObject beforeLanguages = Languages(translated);
        translated.AddString(ISOCode.Language.L_en_US, "S36P2 English after");
        // A51: SetValue receives the ENTIRE language map, including languages beyond the three seeded languages.
        holder.SetValue("Apply", "Multilingual", translated);
        if (external)
        {
            Bind(holder, Require(SentinelCounter), "Page", 11011, 0);
            holder.AddVariable("Project");
            using AnyPropertyId field = Id(10901, 1000);
            using MultiLangString entry = Text("<Project>");
            holder.SetProjectPropertyEntry(field, entry);
            Set(holder, "Page", "S36P2 page applied");
            Set(holder, "Project", "S36P2 project applied");
        }
        log("holder_created", new { name, target = target.ObjectIdentifier, originalPart = original, requestedPart = part,
            beforeLanguages, requestedLanguages = Languages(translated), external });
        return holder;
    }
    private static void Bind(PlaceHolder holder, StorableObject obj, string variable, int id, int index)
    {
        holder.AddVariable(variable);
        if (holder.FindReference(obj) < 0) { holder.AddReference(obj); }
        using AnyPropertyId property = Id(id, index);
        using MultiLangString entry = Text("<" + variable + ">");
        holder.SetPropertyEntry(obj, property, entry);
    }
    private static void Set(PlaceHolder holder, string variable, string value)
    {
        using MultiLangString text = Text(value);
        holder.SetValue("Apply", variable, text);
    }
    internal void Writeback()
    {
        SetStage("writeback.holder.create");
        Page source = Require(SourceCounter);
        PlaceHolder holder = BuildHolder(source, HolderName, external: false);
        JObject expected = ValueTable(holder);
        OutputFolder = Evidence.FreshOutput();
        string macro = Path.Combine(OutputFolder, "module.ema");
        log("macro_destination", new { folder = OutputFolder, outsideTransaction = true });
        using (var created = new WindowMacro())
        using (MultiLangString description = Text("S36P2 writeback"))
        {
            SetStage("writeback.macro.create");
            created.Create(macro, WindowMacro.Enums.RepresentationType.MultiLine, 0, source.AllPlacements, description);
            SetStage("writeback.macro.close_created");
        } // Close the created macro handle before opening a new one; never dispose macro.Objects.
        log("macro_saved", Evidence.Files(OutputFolder));
        Page destination = NewPage(TargetCounter, "S36P2 reinserted");
        using (var reopened = new WindowMacro())
        {
            SetStage("writeback.macro.reopen");
            reopened.Open(macro, project, WindowMacro.Enums.RepresentationType.MultiLine, 0, false); // Read the file, bypass cache.
            PlaceHolder saved = reopened.Objects.OfType<PlaceHolder>().Single(h => h.Name == HolderName);
            JObject actualTable = ValueTable(saved);
            bool matches = JToken.DeepEquals(expected, actualTable);
            log("macro_reopened", new { matches, table = actualTable });
            if (!matches) { throw new InvalidOperationException("Saved placeholder/value set differs after reopening the macro file."); }
            using var insert = new EplanInsert();
            SetStage("writeback.macro.reinsert");
            StorableObject[] inserted = insert.WindowMacro(reopened, WindowMacro.Enums.RepresentationType.MultiLine, 0, destination,
                new PointD(100, 150), EplanInsert.MoveKind.Absolute, WindowMacro.Enums.NumerationMode.Number);
            log("macro_reinserted", new { objects = inserted?.Length, page = destination.Name });
            SetStage("writeback.macro.close_reopened");
        }
        PlaceHolder copy = Holder(destination);
        JObject copiedTable = ValueTable(copy);
        log("before_apply", new { expected, copiedTable, target = Read() });
        if (!JToken.DeepEquals(expected, copiedTable)) { throw new InvalidOperationException("Value set changed on reinsertion."); }
        SetStage("writeback.apply_record");
        copy.ApplyRecord("Apply", false); // Controlled holder binds only object properties.
        SetStage("writeback.property_acceptance");
        log("after_apply", ValidateWriteback());
    }
    internal JObject ValidateWriteback()
    {
        Page page = Require(TargetCounter);
        PlaceHolder holder = Holder(page);
        var checks = new JArray();
        foreach ((string variable, int id, int index) in _bindings)
        {
            using MultiLangString expected = holder.GetValue("Apply", variable);
            using AnyPropertyId property = Id(id, index);
            StorableObject[] targets = holder.AssignedObjects.Where(o =>
            {
                using MultiLangString? entry = holder.GetPropertyEntry(o, property);
                return entry is not null && Languages(entry).Properties().Any(p => ((string?)p.Value)?.IndexOf("<" + variable + ">", StringComparison.Ordinal) >= 0);
            }).ToArray();
            if (targets.Length != 1 || targets[0] is not Function function || function.Page?.ObjectIdentifier != page.ObjectIdentifier)
            { throw new InvalidOperationException("Reinserted binding does not identify exactly one function on its own target page."); }
            JObject actual = Actual(function, id, index);
            JObject expectedLanguages = Languages(expected);
            string requested = expected.GetStringToDisplay(ISOCode.Language.L___);
            bool matches = !HasReadGap(actual) && (bool?)actual["available"] == true && variable switch
            {
                "Boolean" => (bool?)actual["boolean"] == true,
                "Multilingual" => JToken.DeepEquals(expectedLanguages, actual["languages"]),
                "Part" => (string?)actual["raw"] == requested && function.ArticleReferences is { Length: 1 } refs &&
                    refs[0] is not null && refs[0].PartNr == requested && refs[0].Count == 1,
                _ => (string?)actual["raw"] == requested,
            };
            checks.Add(new JObject { ["variable"] = variable, ["matches"] = matches, ["expected"] = expectedLanguages,
                ["object"] = Locator(function, page), ["actual"] = actual });
        }
        var result = new JObject { ["checks"] = checks, ["ok"] = checks.All(c => (bool?)c["matches"] == true) };
        log("property_acceptance", result);
        if ((bool?)result["ok"] != true) { throw new InvalidOperationException("Writeback property/translation/part-reference acceptance failed."); }
        return result;
    }
    internal void PrepareMutation()
    {
        SetStage("mutate.guard.discover");
        JObject guard = Closure();
        log("guard", guard);
        SetStage("mutate.guard.validate");
        if ((bool?)guard["complete"] != true)
        {
            log("guard_refused", new { reason = "CLOSURE_UNPROVEN", outcome = "not_started", applyCalled = false, mutationCalled = false });
            throw new InvalidOperationException("Cannot prove closure; refused before creating SafetyPoint/UndoStep.");
        }
        Function target = Target(Require(SourceCounter));
        bool bound = guard["holders"]!.SelectMany(h => h["bindings"]!).Any(b => (long?)b["object"]?["oid"] == target.ObjectIdentifier && (int?)b["propertyId"] == 20093);
        if (!bound) { throw new InvalidOperationException("The mutation target is not in the recorded closure."); }
        _mutationGuard = guard;
    }
    internal void Mutate(bool fail)
    {
        JObject guard = _mutationGuard ?? throw new InvalidOperationException("Prepare the mutation guard before starting the write chain.");
        SetStage("mutate.property.read");
        Function target = Target(Require(SourceCounter));
        using PropertyValue? before = Property(target, 20093);
        bool requested = !(before?.ToBool() ?? false);
        SetStage("mutate.property.set");
        ((FunctionPropertyList)Properties(target)).FUNC_SUPPRESSINDEVICELIST = requested;
        SetStage("mutate.property.readback");
        JObject actual = Actual(target, 20093, 0);
        log("mutated", new { target = target.ObjectIdentifier, propertyId = 20093, requested, actual });
        if ((bool?)actual["boolean"] != requested) { throw new InvalidOperationException("The bound-property mutation did not take effect."); }
        try { ExecuteGuarded(guard); }
        catch (StaleRefusal)
        {
            if (fail)
            {
                SetStage("mutate.failure.inject");
                throw new InjectedFailure("S36P2 deliberate failure after mutation and verified stale refusal.");
            }
            throw; // The mutation belongs to this experiment and is always rolled back.
        }
        throw new InvalidOperationException("Guard unexpectedly accepted the bound-property mutation.");
    }
    private void ExecuteGuarded(JObject guard)
    {
        SetStage("mutate.execute.recheck");
        // This negative probe only changes one property directly; it never calls ApplyRecord or opens another native scope.
        JObject execute = Closure();
        log("execute_recheck", execute);
        bool tableUnchanged = JToken.DeepEquals(new JArray(guard["holders"]!.Select(h => h["table"]!)), new JArray(execute["holders"]!.Select(h => h["table"]!)));
        bool stale = !JToken.DeepEquals(guard, execute);
        bool complete = (bool?)execute["complete"] == true;
        SetStage("mutate.execute.decide");
        log("guard_decision", new { refused = stale || !complete, reason = !complete ? "CLOSURE_UNPROVEN" : stale ? "PLAN_STALE" : "ACCEPTED",
            tableUnchanged, applyCalled = false, changes = Evidence.Changes(guard, execute) });
        if (!complete || !tableUnchanged) { throw new InvalidOperationException("Refused, but the unchanged-table/complete-closure mutation evidence is missing."); }
        if (stale) { throw new StaleRefusal(); }
        throw new InvalidOperationException("Guard unexpectedly accepted the bound-property mutation; no ApplyRecord was called.");
    }
    internal bool HasWriteback => Find(TargetCounter) is not null;
    internal sealed class StaleRefusal : Exception;
    internal sealed class InjectedFailure(string message) : Exception(message);
}
