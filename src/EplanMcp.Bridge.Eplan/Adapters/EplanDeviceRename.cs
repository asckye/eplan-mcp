using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Renames all functions of a device (A85), keeping every part of the tag: the prefix in <c>-5Q1</c> and the suffix live in their own properties.</summary>
internal static class EplanDeviceRename
{
    public static void Apply(Project project, Function main, string tag)
    {
        using var parsed = new FunctionPropertyList();
        using var additional = new FunctionPropertyList();
        using var newName = new FunctionBasePropertyList();
        project.ParseNameAndProps((int)main.FunctionCategory, tag, parsed, additional);
        using PropertyValue plant = parsed.DESIGNATION_PLANT;
        using PropertyValue location = parsed.DESIGNATION_LOCATION;
        using PropertyValue prefix = parsed.FUNC_PREFIX;
        using PropertyValue code = parsed.FUNC_CODE;
        using PropertyValue counter = parsed.FUNC_COUNTER;
        using PropertyValue suffix = parsed.FUNC_SUFFIX;
        // A tag part that parses to an empty value is refused by the list (S063111).
        if (!plant.IsEmpty)
        { newName.DESIGNATION_PLANT = plant; }
        if (!location.IsEmpty)
        { newName.DESIGNATION_LOCATION = location; }
        if (!prefix.IsEmpty)
        { newName.FUNC_PREFIX = prefix; }
        newName.FUNC_CODE = code;
        newName.FUNC_COUNTER = counter;
        if (!suffix.IsEmpty)
        { newName.FUNC_SUFFIX = suffix; }
        using var service = new NameService();
        service.RenameDevice(main, newName, false, true);
    }
}
