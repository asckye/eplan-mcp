using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.DeviceUpdateExecute"/>: checks the arguments and the plan's fingerprint again, then renames
/// the main and every auxiliary function in one transaction and reads back; a tag EPLAN changed, or a function left
/// with the old tag, rolls the rename back (docs/tools/eplan_device_update.md, section 5; api-notes A13).
/// </summary>
public sealed class DeviceUpdateExecuteOperation(IProjectCatalog catalog, IDeviceReader reader, IDeviceRenamer renamer, ITransactionScopeFactory transactions,
    IPartsDatabase? parts = null, IDevicePartAssigner? assigner = null)
    : Operation<DeviceUpdateArgs, DeviceUpdateData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_device_update".</summary>
    public const string Tool = "eplan_device_update";

    public override string Name => OperationNames.DeviceUpdateExecute;

    protected override DeviceUpdateData Execute(DeviceUpdateArgs args)
    {
        DeviceUpdateRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one device.update.plan returned.");
            }

            request = DeviceUpdateRequest.Check(args, catalog, reader, renamer, parts, assigner);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The device's functions changed, or the new tag came into use, after the plan was made, so nothing was renamed. Call eplan_device_update again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }

            request.CheckFree();
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        // A93: UpdateDevice under the SafetyPoint scope leaves EPLAN refusing the undo (S328050); a second part needs that scope.
        ITransactionScopeFactory scope = request.UpdateFromParts ? assigner!.PartTransactions(request.Project.Path) : transactions;
        Committed<RenameReadBack> committed = TransactionRunner.Run(scope, Tool, () =>
        {
            RenameReadBack back = request.Rename ? renamer.Rename(request.Project.Path, request.Device.Tag, request.NewTag) : new RenameReadBack
            { MainName = request.ResultTag, VisibleName = request.Main?.VisibleName ?? string.Empty, Renamed = request.Device.Functions.Count };
            // A17: a kind that ignores a part of the tag gets a third name at once, and the open transaction can still take
            // it back. The old name alone proves nothing: wrappers can show it until the commit (A85).
            if (request.Rename && back.MainName != request.NewTag && back.MainName != request.Device.Tag
                && !(request.PointsOnly && DeviceUpdateRequest.BelongsTo(back.MainName, request.NewTag)))
            { throw Misnamed(request, back.MainName); }
            if (request.Part is { } part)
            { assigner!.AddPart(request.Project.Path, request.Main!.Id, part.PartNumber, part.Variant!, request.Count); }
            return back;
        }, verifyCommitted: back =>
        {
            if (request.Rename)
            { VerifyRename(request, back); }
            if (request.UpdateFromParts)
            {
                // A93: the second transaction stays inside the undo step.
                assigner!.TransferPartData(request.Project.Path, request.Main!.Id);
                if (DeviceUpdateRequest.ReferencesFingerprint(DeviceUpdateRequest.MainFunction(assigner, request.Project.Path, request.NewTag, includeTerminals: true).Parts)
                    != DeviceUpdateRequest.ReferencesFingerprint(request.Main.Parts))
                { throw InvalidArgument("EPLAN changed the part references while updating from them; the change was rolled back."); }
                return;
            }
            if (request.Part is not { } part)
            { return; }
            // A79/A82: reacquire the target function and its ArticleReferences after commit.
            DevicePartTarget main = DeviceUpdateRequest.MainFunction(assigner!, request.Project.Path, request.NewTag, includeTerminals: !request.Rename);
            if (main.IsMain != request.Main!.IsMain || main.Category != request.Main.Category || !request.Rename && main.Id != request.Main!.Id || main.Name != request.ResultTag || DeviceUpdateRequest.ReferencesFingerprint(main.Parts)
                != DeviceUpdateRequest.ReferencesFingerprint(request.Main!.Parts.Append(new DevicePart { PartNumber = part.PartNumber, Variant = part.Variant, Count = request.Count })))
            { throw InvalidArgument("The committed function or added part reference did not match the plan; the change was rolled back."); }
        }, verifyRollback: () =>
        {
            if (request.Rename && renamer.ReadDevice(request.Project.Path, request.Device.Tag) is null)
            { throw new InvalidOperationException("The device's original tag was not restored."); }
            if (request.Main is not { } before)
            { return; }
            DevicePartTarget restored = DeviceUpdateRequest.MainFunction(assigner!, request.Project.Path, request.Device.Tag, includeTerminals: !request.Rename);
            if (restored.IsMain != before.IsMain || restored.Category != before.Category || !request.Rename && restored.Id != before.Id || restored.Name != before.Name || DeviceUpdateRequest.ReferencesFingerprint(restored.Parts) != DeviceUpdateRequest.ReferencesFingerprint(before.Parts))
            { throw new InvalidOperationException("The original function and part references were not restored."); }
        });
        return new DeviceUpdateData
        {
            Project = request.Project.Name,
            Tag = committed.Result.MainName,
            PreviousTag = request.PreviousTag,
            VisibleTag = DeviceTags.Shown(committed.Result.VisibleName),
            Functions = committed.Result.Renamed,
            Pages = [.. request.Pages.Take(DeviceUpdateRequest.MaxPages)],
            Part = request.Part?.PartNumber,
            Variant = request.Part?.Variant,
            Warning = committed.Warning,
        };
    }

    private void VerifyRename(DeviceUpdateRequest request, RenameReadBack back)
    {
        // PDF redraw 2026-11-06: without a higher-level function EPLAN stores +F1-21BG1 as =+F1-21BG1; the empty '=' is the same tag.
        DeviceNames? actual = renamer.ReadDevice(request.Project.Path, request.NewTag)
            ?? (DevicePlaceRequest.WithoutEmptyFunction(back.MainName) == request.NewTag ? renamer.ReadDevice(request.Project.Path, back.MainName) : null);
        if (actual is null)
        {
            throw Misnamed(request, back.MainName);
        }

        string[] expected = [.. request.Device.Functions.Select(f => Signature(f, request.NewTag + (f.Name.Length == 0 ? "" : f.Name.Substring(request.Device.Tag.Length))))
            .Concat(request.MergeTarget?.Functions.Select(f => Signature(f, f.Name)) ?? []).OrderBy(n => n, StringComparer.Ordinal)];
        string[] names = [.. actual.Functions.Select(f => Signature(f, f.Name.Length == 0 ? actual.Tag : f.Name)).OrderBy(n => n, StringComparer.Ordinal)];
        string Signature(DeviceNames.NamedFunction function, string name) => request.MergeTarget is null ? name
            : $"{name}\t{function.IsMain}\t{function.Category}\t{function.Page}";
        if (!expected.SequenceEqual(names, StringComparer.Ordinal)
            || renamer.FunctionNames(request.Project.Path).Any(n => DeviceUpdateRequest.BelongsTo(n, request.Device.Tag)))
        {
            throw ReadBack(
                $"After the rename, the functions did not match the plan or still carried '{request.Device.Tag}', so the change was rolled back. Ask the engineer to check the device in EPLAN.",
                request.NewTag,
                request.Device.Tag);
        }
        back.MainName = actual.Tag;
        back.VisibleName = actual.VisibleName;
        back.Renamed = request.Device.Functions.Count;
    }

    private static BridgeErrorException Misnamed(DeviceUpdateRequest request, string actual) => ReadBack(
        $"EPLAN named the device '{actual}' instead of '{request.NewTag}': devices of this kind do not use every part of the new tag, so nothing was renamed. Use the structure that similar devices have (eplan_device_list).",
        request.NewTag,
        actual);

    private static BridgeErrorException ReadBack(string message, string requested, string actual) =>
        new(ErrorReason.InvalidArgument, message, retryable: false, new Dictionary<string, string> { ["requested"] = requested, ["actual"] = actual });
}
