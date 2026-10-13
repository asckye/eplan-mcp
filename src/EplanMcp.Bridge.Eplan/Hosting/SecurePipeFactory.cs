using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace EplanMcp.Bridge.Eplan.Hosting;

/// <summary>
/// Creates the Bridge's pipe instances with an explicit security descriptor (SEC-03): only the current user may
/// connect, network logons are denied, and the owner is the current user. The explicit owner matters because the
/// Host connects with <c>PipeOptions.CurrentUserOnly</c>, which checks the owner: under an elevated EPLAN the
/// default owner would be the Administrators group and the Host would refuse the pipe.
/// </summary>
internal sealed class SecurePipeFactory(int maxInstances)
{
    public NamedPipeServerStream Create(string name)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User ?? throw new InvalidOperationException("The current Windows identity has no user SID.");

        var security = new PipeSecurity();
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));

        return new NamedPipeServerStream(name, PipeDirection.InOut, maxInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }
}
