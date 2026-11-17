using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary><see cref="OperationNames.PlcRackPlan"/>: the checks of <see cref="PlcRackRequest"/> and what a rack would change.</summary>
public sealed class PlcRackPlanOperation(IProjectCatalog catalog, IPageReader pages, IMacroReader macros, IMacroInserter inserter, IDeviceRenamer names)
    : Operation<PlcRackArgs, WritePlan>
{
    public override string Name => OperationNames.PlcRackPlan;

    protected override WritePlan Execute(PlcRackArgs args)
    {
        PlcRackRequest request = PlcRackRequest.Check(args, catalog, pages, macros, inserter, names);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Fingerprint = request.Fingerprint(),
            Changes = request.Changes(),
        };
    }
}

/// <summary>
/// <see cref="OperationNames.PlcRackExecute"/> (docs/tools/eplan_plc_rack.md, section 4): in one transaction whose undo
/// step also spans the part data transfer after the commit (A93), each card in turn gets its overview inserted, its
/// main function and sub-devices renamed to the given tags and its parts added, then its rack picture inserted and
/// renamed to the same tag, which joins it to the card (A122). After the commit each card must be one main function
/// that carries all its parts; anything else rolls the whole rack back.
/// </summary>
public sealed class PlcRackExecuteOperation(
    IProjectCatalog catalog, IPageReader pages, IMacroReader macros, IMacroInserter inserter, IDeviceRenamer renamer, IDevicePartAssigner assigner)
    : Operation<PlcRackArgs, PlcRackData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_plc_rack".</summary>
    public const string Tool = "eplan_plc_rack";

    public override string Name => OperationNames.PlcRackExecute;

    protected override PlcRackData Execute(PlcRackArgs args)
    {
        PlcRackRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw WriteArguments.Invalid(["'fingerprint' is required: the one plc.rack.plan returned."]); }
            request = PlcRackRequest.Check(args, catalog, pages, macros, inserter, renamer);
            if (!string.Equals(request.Fingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale,
                    "The pages, the macros or the tags in use changed after the plan was made, so nothing was drawn. Call eplan_plc_rack again for a new plan.");
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        string project = request.Project.Path;
        var results = new List<PlcRackCardResult>();
        Committed<int> committed = TransactionRunner.Run(assigner.PartTransactions(project), Tool, () =>
        {
            for (int i = 0; i < request.Cards.Count; i++)
            { Draw(request.Cards[i], request.Overviews[i], request.Pictures[i]); }
            return request.Cards.Count;
        }, verifyCommitted: _ =>
        {
            results.Clear();
            foreach (PlcRackCardPlan card in request.Cards)
            {
                DevicePartTarget main = Main(card.Tag);
                assigner.TransferPartData(project, main.Id);
                assigner.AssignUnplacedFunctions(project, card.Tag);
                main = Main(card.Tag);
                string[] parts = [.. main.Parts.Select(p => p.PartNumber)];
                string[] missing = [.. card.ExtraParts.Prepend(card.Part).Where(p => !parts.Contains(p, StringComparer.Ordinal))];
                if (missing.Length > 0)
                { throw Failed($"{card.Tag} does not carry {string.Join(", ", missing)} after the commit; the rack was rolled back."); }
                results.Add(new PlcRackCardResult
                {
                    Tag = card.Tag,
                    OverviewPage = card.OverviewPage,
                    RackX = card.RackX,
                    Width = card.Width,
                    Parts = [.. parts],
                    SubDevices = [.. card.SubDevices.Select(s => s.Tag!.Trim())],
                });
            }
        });
        return new PlcRackData { Project = request.Project.Name, RackPage = request.RackPage, Cards = results, Warning = committed.Warning };

        void Draw(PlcRackCardPlan card, MacroInsertRequest overview, MacroInsertRequest picture)
        {
            // Each main function is read under its macro tag, before the rename (A85: a new name is provisional until
            // the commit), and keeps its identity through it. A sub-device without a mapping becomes the card's tag plus
            // its suffix, so that the next card's macro does not join it.
            (string macroTag, string[] subs) = Insert(overview);
            string mainId = Main(macroTag).Id;
            foreach (string sub in subs)
            {
                PlcRackSubDevice? mapped = Mapped(card, macroTag, sub);
                string id = Main(sub).Id;
                Rename(card, sub, mapped?.Tag!.Trim() ?? card.Tag + sub.Substring(macroTag.Length));
                if (mapped?.Part?.Trim() is { Length: > 0 } subPart)
                { assigner.AddPart(project, id, subPart, DevicePlaceRequest.DefaultVariant); }
            }

            Rename(card, macroTag, card.Tag);
            foreach (string part in card.ExtraParts.Prepend(card.Part))
            { assigner.AddPart(project, mainId, part, part == card.Part ? card.PartVariant ?? DevicePlaceRequest.DefaultVariant : DevicePlaceRequest.DefaultVariant); }

            // The picture's devices take the same tags, which joins them to the card and its sub-devices (A122).
            (string pictureTag, string[] pictureSubs) = Insert(picture);
            foreach (string sub in pictureSubs)
            { Rename(card, sub, Mapped(card, pictureTag, sub)?.Tag!.Trim() ?? card.Tag + sub.Substring(pictureTag.Length)); }
            Rename(card, pictureTag, card.Tag);
        }

        static PlcRackSubDevice? Mapped(PlcRackCardPlan card, string macroTag, string sub) =>
            card.SubDevices.FirstOrDefault(s => string.Equals(macroTag + s.MacroSuffix!.Trim(), sub, StringComparison.Ordinal));

        // The card's own device among the inserted ones, the main function whose tag the others extend, and those others
        // (longest first, so a nested sub-device is renamed before the one it extends).
        (string Main, string[] Subs) Insert(MacroInsertRequest insert)
        {
            PlacedMacro placed = inserter.Insert(project, insert.Page.Name, insert.MacroPath, insert.Variant, insert.X, insert.Y, number: false,
                insert.Representation, macroBox: false);
            // A rack picture's box is not a main function (A122, live 2026-11-15: the ET 200SP picture placed none), so
            // without one the device is the shortest tag among all placed functions.
            IEnumerable<MacroFunctionRecord> owners = placed.Functions.Any(f => f.Main) ? placed.Functions.Where(f => f.Main) : placed.Functions;
            string[] mains = [.. owners.Select(f => f.Name.Split(':')[0]).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(m => m.Length)];
            string main = mains.FirstOrDefault()
                ?? throw Failed($"Inserting {insert.Macro} on {insert.Page.Name} placed no function; the rack was rolled back.");
            return (main, [.. mains.Skip(1).Where(m => m.StartsWith(main + "-", StringComparison.Ordinal)).OrderByDescending(m => m.Length)]);
        }

        void Rename(PlcRackCardPlan card, string from, string to)
        {
            if (string.Equals(from, to, StringComparison.Ordinal))
            { return; }
            RenameReadBack back = renamer.Rename(project, from, to);
            // A picture without a main function reads back as one of its connection points, the tag plus ":L+" (live 2026-11-15).
            if (back.MainName != to && back.MainName != from && !back.MainName.StartsWith(to + ":", StringComparison.Ordinal))
            { throw Failed($"Renaming {from} for card {card.Tag} gave {back.MainName}, not {to}; the rack was rolled back."); }
        }

        DevicePartTarget Main(string tag) => assigner.ReadMainFunctions(project, tag).FirstOrDefault(t => t.IsMain)
            ?? throw Failed($"No main function {tag} was found; the rack was rolled back.");
    }

    private static BridgeErrorException Failed(string message) => new(ErrorReason.EplanOperationFailed, message);
}
