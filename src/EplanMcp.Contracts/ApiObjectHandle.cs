namespace EplanMcp.Contracts;
/// <summary>Opaque object reference, valid only in its Host session and EPLAN process.</summary>
public sealed class ApiObjectHandle
{
    /// <summary>Random 128-bit token; idle expiry is ten minutes.</summary>
    public string Handle { get; set; } = string.Empty;
    /// <summary>Native type selected by the compiled dispatch table.</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>Checks the closed wire shape without interpreting an object.</summary>
    public static bool Valid(string? handle, string? type) => handle is not null && System.Text.RegularExpressions.Regex.IsMatch(handle, "\\Ah:[0-9a-f]{32}\\z") && type is not null && ApiTryCatalog.HandleType(type);
}
