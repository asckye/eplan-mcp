namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Reads device main functions or exact terminals and appends a part reference inside the write transaction.</summary>
public interface IDevicePartAssigner
{
    /// <summary>Main functions matching the device tag; optionally exact terminal names, including unplaced functions.</summary>
    public IReadOnlyList<DevicePartTarget> ReadMainFunctions(string projectPath, string tag, bool includeTerminals = false);

    /// <summary>Adds one reference to the checked function identified within this EPLAN session.</summary>
    public void AddPart(string projectPath, string mainId, string part, string variant);
}
