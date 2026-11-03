namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Reads device main functions and appends a part reference inside the write transaction.</summary>
public interface IDevicePartAssigner
{
    /// <summary>Main functions matching the full or shown device tag, including unplaced functions.</summary>
    public IReadOnlyList<DevicePartTarget> ReadMainFunctions(string projectPath, string tag);

    /// <summary>Adds one reference to the checked main function identified within this EPLAN session.</summary>
    public void AddPart(string projectPath, string mainId, string part, string variant);
}
