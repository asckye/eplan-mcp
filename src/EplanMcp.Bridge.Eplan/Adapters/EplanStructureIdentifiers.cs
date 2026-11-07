using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Structure identifiers through EPLAN's <see cref="Location"/> objects (api-notes A106). Keeps none past a call, because EPLAN's objects die with their transaction.</summary>
internal sealed class EplanStructureIdentifiers : IStructureIdentifiers
{
    public IReadOnlyList<StructureIdentifier> Read(string projectPath)
    {
        MainThread.Assert();
        var read = new List<StructureIdentifier>();
        Project project = EplanProjects.ByPath(projectPath);
        foreach (string prefix in StructureNames.Prefixes)
        {
            // By block because only then EPLAN returns them in the order SetSortedLocations gave (api-notes A107).
            foreach (Location location in project.GetLocationObjects(Block(prefix)))
            {
                string name = FullName(location);
                if (name.Length > prefix.Length)
                { read.Add(new StructureIdentifier { Name = name, Description = Description(location), Used = location.IsUsed }); }
            }
        }
        return read;
    }

    public void Create(string projectPath, string name)
    {
        MainThread.Assert();
        Change(() =>
        {
            string prefix = StructureNames.Prefix(name);
            if (!EplanProjects.ByPath(projectPath).CreateLocation(Block(prefix), name.Substring(prefix.Length), Project.LocationSortOrder.Back))
            {
                throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN did not create a structure identifier, so nothing was committed.",
                    retryable: false, new Dictionary<string, string> { ["identifier"] = name });
            }
        });
    }

    public void Describe(string projectPath, string name, string description)
    {
        MainThread.Assert();
        Change(() =>
        {
            using var text = new MultiLangString();
            if (TextLanguages.IsSerialized(description))
            { text.SetAsString(description); }
            else if (description.Length > 0)
            { text.AddString(ISOCode.Language.L___, description); }
            using PropertyValue value = text;
            Find(projectPath, name).Properties.LOCATION_DESCRIPTION = value;
        });
    }

    public void Remove(string projectPath, string name)
    {
        MainThread.Assert();
        Change(() => Find(projectPath, name).Remove());
    }

    public void Sort(string projectPath, IReadOnlyList<string> names)
    {
        MainThread.Assert();
        Change(() =>
        {
            string prefix = StructureNames.Prefix(names[0]);
            if (!EplanProjects.ByPath(projectPath).SetSortedLocations(Block(prefix), [.. names.Select(n => n.Substring(prefix.Length))]))
            {
                throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN did not sort the block, so nothing was committed.",
                    retryable: false, new Dictionary<string, string> { ["identifier"] = prefix });
            }
        });
    }

    public void Rename(string projectPath, string name, string newName)
    {
        MainThread.Assert();
        Change(() =>
        {
            int parent = newName.LastIndexOf('.');
            string segment = parent >= 0 ? newName.Substring(parent + 1) : newName.Substring(StructureNames.Prefix(newName).Length);
            EplanProjects.ByPath(projectPath).ChangeLocationName(Find(projectPath, name), segment);
        });
    }

    private static void Change(Action change)
    {
        try
        {
            change();
        }
        catch (BaseException ex)
        {
            WriteExceptionDiagnostics.Report(ex);
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN refused a change to a structure identifier, so nothing was committed.", ex);
        }
    }

    private static Location Find(string projectPath, string name) =>
        EplanProjects.ByPath(projectPath).GetLocationObjects(Block(StructureNames.Prefix(name))).FirstOrDefault(l => FullName(l) == name)
            ?? throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN does not have this structure identifier any more, so nothing was committed.",
                retryable: false, new Dictionary<string, string> { ["identifier"] = name });

    private static string FullName(Location location)
    {
        using PropertyValue value = location.Properties.LOCATION_FULLNAME_WITHPREFIX;
        return value.IsEmpty ? string.Empty : value.ToString();
    }

    private static string Description(Location location)
    {
        using PropertyValue value = location.Properties.LOCATION_DESCRIPTION;
        if (value.IsEmpty)
        { return string.Empty; }
        using MultiLangString text = value.ToMultiLangString();
        return text.GetAsString();
    }

    private static Project.Hierarchy Block(string prefix) => prefix switch
    {
        "==" => Project.Hierarchy.Functional,
        "=" => Project.Hierarchy.Plant,
        "++" => Project.Hierarchy.Place,
        "+" => Project.Hierarchy.Location,
        "&" => Project.Hierarchy.Document,
        "#" => Project.Hierarchy.UserDef,
        _ => throw new BridgeErrorException(ErrorReason.InvalidArgument, "A structure identifier starts with =, ==, ++, +, & or #.", retryable: false),
    };
}
