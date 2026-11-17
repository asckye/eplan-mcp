using System.Windows.Media.Media3D;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

#pragma warning disable CA2000 // A39: EPLAN owns created placements, including rollback paths.

/// <summary>
/// Places rails, ducts and devices as Layout3DSpikeTests observed (api-notes A136). Which source mate snaps differs per
/// part (corners V1-V4, edge midpoints M1-M4, a centre C), so every placement snaps once, measures its box and moves it
/// by the difference so the box's lower left corner lands where the caller asked. Main thread, inside the operation's
/// transaction.
/// </summary>
internal sealed class EplanLayoutPlacer : ILayoutPlacer
{
    public LayoutPlaced Place(string projectPath, long enclosureId, LayoutPlaceStep item, long? railId)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        using var finder = new DMObjectsFinder(project);
        Function3D[] all = [.. EplanNativePreconditions.Valid(finder.GetFunctions3D(null))];
        Cabinet cabinet = all.OfType<Cabinet>().First(c => c.ObjectIdentifier == enclosureId);
        (Plane? panel, Plane? door, Plane? top) = EplanLayoutReader.Surfaces(cabinet);
        Plane? plane = item.Surface switch { "door" => door, "panel" => panel, "top" => top, _ => null };
        MountingRail? rail = item.Surface == "rail" ? all.OfType<MountingRail>().First(r => r.ObjectIdentifier == railId) : null;

        Placement3D created;
        if (item.Kind == "rail")
        {
            var created3d = new MountingRail();
            created3d.Create(project, item.Part, item.Variant, item.Length);
            created = created3d;
        }
        else if (item.Kind == "duct")
        {
            var created3d = new Duct();
            created3d.Create(project, item.Part, item.Variant, item.Length);
            created = created3d;
        }
        else
        {
            Placement3D parent = rail ?? plane!.Parent!;
            created = Component.Create(project, parent, item.Part, item.Variant, -2, []);
        }

        double[] at = rail is not null ? OnRail(created, rail, item.Offset) : OnPlane(created, plane!, item.Vertical ? Math.PI / 2 : 0, item.X, item.Y);
        if (item.Tag is not null && created is Function3D function)
        {
            function.Name = item.Tag;
        }

        return new LayoutPlaced { Index = item.Index, Kind = item.Kind, Id = created.ObjectIdentifier, Tag = item.Tag, Part = item.Part, Box = EplanLayoutReader.Box(created), At = at };
    }

    /// <summary>
    /// Puts the box's lower left corner at (x, y) from the surface's lower left corner; surfaces lie in X/Z (A136). A
    /// second SnapTo does not move a snapped item (live 2026-11-12), so the item snaps once and is then moved by the
    /// measured difference.
    /// </summary>
    private static double[] OnPlane(Placement3D item, Plane plane, double rotation, double x, double y)
    {
        PointMate source = Source(item, plane.BaseMate);
        Rect3D surface = plane.GetBoundingBox(false);
        source.SnapTo(plane.BaseMate, rotation, x, y);
        Rect3D snapped = item.GetBoundingBox(false);
        // A horizontal surface (the top) measures y from its front edge (smallest Y) backwards.
        bool horizontal = EplanLayoutReader.Horizontal(surface);
        if (horizontal)
        {
            Correct(item, surface.X + x - snapped.X, surface.Y + y - snapped.Y, 0);
        }
        else
        {
            Correct(item, surface.X + x - snapped.X, 0, surface.Z + y - snapped.Z);
        }

        Rect3D placed = item.GetBoundingBox(false);
        return [Round(placed.X - surface.X), Round(horizontal ? placed.Y - surface.Y : placed.Z - surface.Z)];
    }

    /// <summary>Puts the box's left (or, on a vertical rail, lower) edge <paramref name="offset"/> from the rail's start.</summary>
    private static double[] OnRail(Placement3D item, MountingRail rail, double offset)
    {
        if (rail.BaseMate is not LineMate line)
        {
            throw new InvalidOperationException("The rail has no line mate to snap onto.");
        }

        PointMate source = Source(item, line);
        Rect3D track = rail.GetBoundingBox(false);
        bool vertical = track.SizeZ > track.SizeX;
        double Along(Rect3D box) => vertical ? box.Z : box.X;
        source.SnapTo(line, offset);
        double error = Along(track) + offset - Along(item.GetBoundingBox(false));
        Correct(item, vertical ? 0 : error, 0, vertical ? error : 0);
        return [Round(Along(item.GetBoundingBox(false)) - Along(track))];
    }

    /// <summary>
    /// Moves the item by a world difference. MoveRelative works in the parent's own axes (live 2026-11-12: on the mounting
    /// panel (0, 0, 40) moved the box 40 mm in world -Y), so each local axis is probed by 1 mm and moved back, and the
    /// world difference is solved in those axes; the message carries the numbers when the result still differs.
    /// </summary>
    private static void Correct(Placement3D item, double dx, double dy, double dz)
    {
        if (Math.Abs(dx) < 0.05 && Math.Abs(dy) < 0.05 && Math.Abs(dz) < 0.05)
        {
            return;
        }

        double[][] axes = new double[3][];
        for (int k = 0; k < 3; k++)
        {
            Rect3D before = item.GetBoundingBox(false);
            item.MoveRelative(k == 0 ? 1 : 0, k == 1 ? 1 : 0, k == 2 ? 1 : 0);
            Rect3D after = item.GetBoundingBox(false);
            item.MoveRelative(k == 0 ? -1 : 0, k == 1 ? -1 : 0, k == 2 ? -1 : 0);
            axes[k] = [after.X - before.X, after.Y - before.Y, after.Z - before.Z];
        }

        double[] local = Solve(axes, [dx, dy, dz]) ?? throw new InvalidOperationException(FormattableString.Invariant(
            $"The local axes of the item's parent could not be measured: {string.Join("; ", axes.Select(a => string.Join(",", a.Select(v => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)))))}."));
        Rect3D start = item.GetBoundingBox(false);
        item.MoveRelative(local[0], local[1], local[2]);
        Rect3D end = item.GetBoundingBox(false);
        double[] moved = [end.X - start.X, end.Y - start.Y, end.Z - start.Z];
        if (Math.Abs(moved[0] - dx) > 0.5 || Math.Abs(moved[1] - dy) > 0.5 || Math.Abs(moved[2] - dz) > 0.5)
        {
            throw new InvalidOperationException(FormattableString.Invariant(
                $"Moving by ({dx:0.#}, {dy:0.#}, {dz:0.#}) moved the box by ({moved[0]:0.#}, {moved[1]:0.#}, {moved[2]:0.#})."));
        }
    }

    /// <summary>Solves columns a, b, c (world moves per local mm) times x = d by Cramer's rule; null when singular.</summary>
    private static double[]? Solve(double[][] columns, double[] d)
    {
        double Det(double[] a, double[] b, double[] c) =>
            (a[0] * ((b[1] * c[2]) - (b[2] * c[1]))) - (b[0] * ((a[1] * c[2]) - (a[2] * c[1]))) + (c[0] * ((a[1] * b[2]) - (a[2] * b[1])));
        double det = Det(columns[0], columns[1], columns[2]);
        return Math.Abs(det) < 1e-6 ? null : [Det(d, columns[1], columns[2]) / det, Det(columns[0], d, columns[2]) / det, Det(columns[0], columns[1], d) / det];
    }

    private static PointMate Source(Placement3D item, Mate target) =>
        item.GetSourceMates(Mate.Enums.PlacementOptions.None).OfType<PointMate>().FirstOrDefault(m => m.CanSnapTo(target))
        ?? throw new InvalidOperationException("The part's 3D macro has no mate that snaps onto this surface or rail.");

    private static double Round(double value) => Math.Round(value, 1);
}
