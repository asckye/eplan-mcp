using Eplan.EplApi.ApplicationFramework;
using EplanMcp.Bridge.Eplan.Hosting;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// The add-in entry point EPLAN calls (api-notes B2). Registered with <c>bLoadOnStart = true</c> so that
/// <see cref="OnInitGui"/> runs on the main thread once EPLAN's UI is up, which is where the Bridge starts.
/// Every method catches everything: an exception escaping an EPLAN callback must never reach EPLAN (coding
/// standards 9.6, rule 17).
/// </summary>
public sealed class EplanAddIn : IEplAddIn, IEplAddInShadowCopy
{
    /// <summary>
    /// EPLAN calls this before <see cref="OnInit"/> with where the add-in was registered from, before it loads the
    /// shadow copy (api-notes B2); the handshake reports it so that install-addin can tell which registration loaded.
    /// Whether EPLAN passes the DLL or its folder is not verified; it is reported as passed.
    /// </summary>
    public void OnBeforeInit(string strOriginalAssemblyPath)
    {
        try
        {
            BridgeHost.AddinPath = strOriginalAssemblyPath;
        }
        catch (Exception ex)
        {
            StartupFailureLog.Write(ex);
        }
    }

    public bool OnRegister(ref bool bLoadOnStart)
    {
        bLoadOnStart = true;
        return true;
    }

    public bool OnUnregister() => true;

    public bool OnInit() => true;

    public bool OnInitGui()
    {
        try
        {
            BridgeHost.Start();
        }
        catch (Exception ex)
        {
            StartupFailureLog.Write(ex);
        }

        return true;
    }

    public bool OnExit()
    {
        try
        {
            BridgeHost.Stop();
        }
        catch (Exception ex)
        {
            StartupFailureLog.Write(ex);
        }

        return true;
    }
}
