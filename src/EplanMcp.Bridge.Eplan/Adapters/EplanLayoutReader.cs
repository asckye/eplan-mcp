using System.Windows.Media.Media3D;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads the 3D mounting layout for <see cref="LayoutGetOperation"/>, as Layout3DSpikeTests observed it (api-notes A136):
/// enclosures hang below their installation space, the mounting panel is a <see cref="MountingPanel"/> child, the door is
/// the child whose surface lies furthest to the front (smallest Y). Main thread, inside the dispatcher's LockingStep.
/// </summary>
internal sealed class EplanLayoutReader : ILayoutReader
{
    /// <summary>INSTALLATIONSPACE_DESIGNATION: the name layout tools and InsertModelViewAction take (A136).</summary>
    internal const int DesignationId = 36452;

    public LayoutRecord Read(string projectPath)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        var record = new LayoutRecord();
        foreach (InstallationSpace space in project.InstallationSpaces)
        {
            record.Spaces.Add(new LayoutSpace { Designation = Designation(space), Name = space.Name ?? string.Empty });
        }

        using var finder = new DMObjectsFinder(project);
        foreach (Function3D function in EplanNativePreconditions.Valid(finder.GetFunctions3D(null)))
        {
            string name = function.Name ?? string.Empty;
            record.Names3D.Add(name);
            if (!function.IsPlaced || SpaceOf(function) is not { } owner)
            {
                continue;
            }

            string designation = Designation(owner);
            record.Objects.Add(new LayoutObject
            {
                Space = designation,
                Id = function.ObjectIdentifier,
                Kind = function.GetType().Name,
                Tag = function is Component && name.Contains('-') ? name : null,
                Parts = [.. function.ArticleReferences.Select(a => a.PartNr ?? string.Empty)],
                Parent = function.Parent?.GetType().Name,
                ParentId = function.Parent is InstallationSpace ? null : function.Parent?.ObjectIdentifier,
                Box = Box(function),
            });
            if (function is Cabinet cabinet && cabinet.Parent is InstallationSpace)
            {
                record.Spaces.First(s => s.Designation == designation).Enclosures.Add(Enclosure(cabinet));
            }
        }

        foreach (Function function in EplanNativePreconditions.Valid(finder.GetFunctions(null)).ToArray())
        {
            if (function.IsMainFunction && function.ArticleReferences is { Length: > 0 } parts && !string.IsNullOrEmpty(function.Name))
            {
                record.Devices.Add(new LayoutDevice { Tag = function.Name, Part = parts[0].PartNr ?? string.Empty });
            }
        }

        return record;
    }

    internal static string Designation(InstallationSpace space) => space.Properties[DesignationId].ToString() ?? string.Empty;

    private static InstallationSpace? SpaceOf(Placement3D placement)
    {
        for (Placement3D? node = placement.Parent; node is not null; node = node.Parent)
        {
            if (node is InstallationSpace space)
            {
                return space;
            }
        }

        return null;
    }

    private static LayoutEnclosure Enclosure(Cabinet cabinet)
    {
        Placement3D[] all = [.. Descendants(cabinet)];
        Plane[] planes = [.. all.OfType<Plane>().Where(p => p.PlaneMode == Placement3D.Enums.PlaneModeType.Master)];
        Plane? panel = planes.FirstOrDefault(p => p.Parent is MountingPanel);
        // The door: of the surfaces not on the mounting panel or the body, the one furthest to the front (A136: 795x16x595 at the front).
        Plane? door = planes.Where(p => p.Parent is Component).OrderBy(p => p.GetBoundingBox(false).Y).FirstOrDefault();
        return new LayoutEnclosure
        {
            Id = cabinet.ObjectIdentifier,
            Parts = [.. cabinet.ArticleReferences.Select(a => a.PartNr ?? string.Empty)],
            Box = Box(cabinet),
            Panel = panel is null ? null : Surface(panel),
            Door = door is null ? null : Surface(door),
        };
    }

    private static LayoutSurface Surface(Plane plane)
    {
        Rect3D box = plane.GetBoundingBox(false);
        // A surface lies in X/Z (panel, door): width across, height up (A136).
        return new LayoutSurface { Id = plane.Parent?.ObjectIdentifier ?? 0, Width = Round(box.SizeX), Height = Round(box.SizeZ), Box = Box(plane) };
    }

    private static IEnumerable<Placement3D> Descendants(Placement3D root)
    {
        foreach (Placement3D child in root.Children)
        {
            yield return child;
            foreach (Placement3D grandchild in Descendants(child))
            {
                yield return grandchild;
            }
        }
    }

    internal static double[] Box(Placement3D placement)
    {
        Rect3D box = placement.GetBoundingBox(false);
        return box.IsEmpty ? [] : [Round(box.X), Round(box.Y), Round(box.Z), Round(box.SizeX), Round(box.SizeY), Round(box.SizeZ)];
    }

    private static double Round(double value) => Math.Round(value, 1);
}
