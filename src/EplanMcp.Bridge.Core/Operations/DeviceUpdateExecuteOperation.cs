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

        Committed<RenameReadBack> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            RenameReadBack back = request.Rename ? Rename(request) : new RenameReadBack
            { MainName = request.ResultTag, VisibleName = request.Main?.VisibleName ?? string.Empty, Renamed = request.Device.Functions.Count };
            if (request.Part is { } part)
            { assigner!.AddPart(request.Project.Path, request.Main!.Id, part.PartNumber, part.Variant!); }
            return back;
        }, verifyCommitted: _ =>
        {
            if (request.Part is not { } part)
            { return; }
            // A79/A82: reacquire the main function and its ArticleReferences after commit.
            DevicePartTarget main = DeviceUpdateRequest.MainFunction(assigner!, request.Project.Path, request.NewTag);
            if (main.Id != request.Main!.Id || main.Name != request.ResultTag || DeviceUpdateRequest.ReferencesFingerprint(main.Parts)
                != DeviceUpdateRequest.ReferencesFingerprint(request.Main.Parts.Append(new DevicePart { PartNumber = part.PartNumber, Variant = part.Variant, Count = 1 })))
            { throw InvalidArgument("The committed main function or added part reference did not match the plan; the change was rolled back."); }
        }, verifyRollback: () =>
        {
            if (request.Main is not { } before)
            { return; }
            DevicePartTarget restored = DeviceUpdateRequest.MainFunction(assigner!, request.Project.Path, request.Device.Tag);
            if (restored.Id != before.Id || restored.Name != before.Name || DeviceUpdateRequest.ReferencesFingerprint(restored.Parts) != DeviceUpdateRequest.ReferencesFingerprint(before.Parts))
            { throw new InvalidOperationException("The original main function and part references were not restored."); }
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

    private RenameReadBack Rename(DeviceUpdateRequest request)
    {
        RenameReadBack back = renamer.Rename(request.Project.Path, request.Device.Tag, request.NewTag);
        if (!string.Equals(back.MainName, request.NewTag, StringComparison.Ordinal))
        {
            throw ReadBack(
                $"EPLAN named the device '{back.MainName}' instead of '{request.NewTag}': devices of this kind do not use every part of the new tag, so nothing was renamed. Use the structure that similar devices have (eplan_device_list).",
                request.NewTag,
                back.MainName);
        }

        return back.Remaining == 0
            ? back
            : throw ReadBack(
                $"After the rename, functions still carried '{request.Device.Tag}', so nothing was renamed. Ask the engineer to check the device in EPLAN.",
                request.NewTag,
                request.Device.Tag);
    }

    private static BridgeErrorException ReadBack(string message, string requested, string actual) =>
        new(ErrorReason.InvalidArgument, message, retryable: false, new Dictionary<string, string> { ["requested"] = requested, ["actual"] = actual });
}
