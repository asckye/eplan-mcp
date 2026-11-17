using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// EPLAN exceptions → error reasons (coding standards 9.6, rule 16). Anything not listed becomes INTERNAL in the
/// router, including <c>NoLockingStepException</c>, which can only mean a defect in our dispatcher.
/// </summary>
internal sealed class EplanExceptionMapper : IExceptionMapper
{
    private const string LicenceMissingText = "API License missing"; // LicenseChecker's message (api-notes B8)

    public BridgeError? Map(Exception exception) => exception switch
    {
        LockingException locking => new BridgeError
        {
            Reason = ErrorReason.LockFailed,
            Message = "EPLAN objects are locked, probably by another user. Retry later or ask the engineer who holds the lock.",
            Retryable = true,
            Metadata = new Dictionary<string, string> { ["lockedObjects"] = Truncate(string.Join("; ", locking.GetAllFailed2LockAsString() ?? [])) },
        },
        ProjectLockingException => new BridgeError
        {
            Reason = ErrorReason.LockFailed,
            Message = "A project could not be locked, probably because another user holds it. Retry later.",
            Retryable = true,
        },
        _ when exception.Message.IndexOf(LicenceMissingText, StringComparison.OrdinalIgnoreCase) >= 0 => new BridgeError
        {
            Reason = ErrorReason.LicenceMissing,
            Message = "EPLAN reports that the API extension licence is missing or has expired. Ask the engineer to check the EPLAN licence, or run 'eplanmcp doctor'.",
        },
        _ => null,
    };

    private static string Truncate(string? text) => text is null ? string.Empty : text.Length <= 500 ? text : text.Substring(0, 500) + "…";
}
