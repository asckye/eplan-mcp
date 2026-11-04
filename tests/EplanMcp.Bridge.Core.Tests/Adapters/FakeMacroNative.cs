using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;

#pragma warning disable CA1822 // Match the native instance API used by the linked adapters.

namespace Eplan.EplApi.Base
{
    internal static class PathMap
    {
        [ThreadStatic] public static string? MacroDirectory;
        public static string SubstitutePath(string value) => value.Replace("$(MD_MACROS)", MacroDirectory ?? "", StringComparison.Ordinal);
        public static string SubstitutePath(string path, string projectPath) => path;
    }
}

namespace Eplan.EplApi.MasterData
{
    internal sealed class MDPart
    {
        public string PartNr { get; set; } = "PXC.2861344";
        public string Variant { get; set; } = "1";
        public MacroPartProperties Properties { get; set; } = new();
    }
    internal sealed class MacroPartProperties
    {
        public MDPropertyValue ARTICLE_GROUPSYMBOLMACRO { get; set; } = new("");
    }
    internal sealed class MDPartsDatabase : IDisposable
    {
        public MDPart[] Parts { get; set; } = [];
        public bool Closed { get; private set; }
        public bool Disposed { get; private set; }
        public MDPart[] GetPartVariants(string part) => [.. Parts.Where(p => p.PartNr == part)];
        public void Close() => Closed = true;
        public void Dispose() { Assert.True(Closed); Disposed = true; }
    }
    internal sealed class MDPartsManagement
    {
        [ThreadStatic] public static MDPartsDatabase? Database;
        public MDPartsDatabase OpenDatabase() => Database!;
    }
}

namespace Eplan.EplApi.DataModel.MasterData
{
    internal sealed class MacroNativeState
    {
        public Dictionary<WindowMacro.Enums.RepresentationType, Dictionary<int, Placement[]>> Representations { get; } = [];
        public List<(WindowMacro.Enums.RepresentationType Representation, int Variant)> Selections { get; } = [];
        public Dictionary<(WindowMacro.Enums.RepresentationType, int), PointD> ReferencePoints { get; } = [];
        public Exception? ReferencePointError { get; set; }
    }
    internal sealed class WindowMacro : IDisposable
    {
        [ThreadStatic] public static MacroNativeState? State;
        public static class Enums
        {
            public enum RepresentationType { MultiLine, Overview, ArticlePlacement3D }
            public enum NumerationMode { Number, None }
        }
        public Enums.RepresentationType RepresentationType { get; private set; }
        public int Variant { get; private set; }
        public Enums.RepresentationType[] RepresentationTypes => [.. State!.Representations.Keys];
        public int[] GetVariants(Enums.RepresentationType type) => [.. State!.Representations[type].Keys];
        public Placement[] Objects => State!.Representations[RepresentationType][Variant];
        public PointD Size => new(20, 30);
        public PointD Location => new(0, 0);
        public PointD ReferencePoint => State!.ReferencePointError is { } error ? throw error
            : State.ReferencePoints.GetValueOrDefault((RepresentationType, Variant));
        public void Open(string path, Project project) => Open(path, project, false);
        public void Open(string path, Project project, bool useCache)
        {
            RepresentationType = State!.Representations.Keys.First();
            Variant = State.Representations[RepresentationType].Keys.First();
        }
        public void Open(string path, Project project, Enums.RepresentationType type, int variant, bool useCache)
        {
            RepresentationType = type;
            Variant = variant;
            State!.Selections.Add((type, variant));
        }
        public void Dispose() { }
    }
}

namespace Eplan.EplApi.HEServices
{
    internal sealed class Insert : IDisposable
    {
        public enum MoveKind { Absolute }
        [ThreadStatic] public static Func<Page, StorableObject[]>? Place;
        [ThreadStatic] public static (WindowMacro.Enums.RepresentationType Representation, int Variant)? Selection;
        public StorableObject[] WindowMacro(WindowMacro macro, WindowMacro.Enums.RepresentationType representation,
            int variant, Page page, PointD point, MoveKind move, WindowMacro.Enums.NumerationMode numbering)
        {
            Selection = (representation, variant);
            return Place!(page);
        }
        public void Dispose() { }
    }
}

namespace Eplan.EplApi.DataModel
{
    internal sealed class MacroBox(long oid) : Placement(oid)
    {
        public Placement[] ReferencedObjects { get; set; } = [];
        public Exception? RemoveError { get; set; }
        public new void Remove()
        {
            if (RemoveError is { } error)
            { throw error; }
            Page.AllPlacements = [.. Page.AllPlacements.Where(p => p != this)];
            IsValid = false;
        }
    }
    internal sealed partial class ArticleReference
    {
        public string VariantNr { get; set; } = "1";
        public int Count { get; set; } = 1;
    }
    internal partial class Function
    {
        public List<(string Part, string Variant, uint Count)> ArticleAdds { get; } = [];
        public Exception? AddArticleError { get; set; }
        public ArticleReference AddArticleReference(string part, string variant, uint count)
        {
            CheckNative?.Invoke();
            ArticleAdds.Add((part, variant, count));
            if (AddArticleError is { } error)
            { throw error; }
            var article = new ArticleReference { PartNr = part, VariantNr = variant, Count = checked((int)count) };
            ArticleReferences = [.. ArticleReferences, article];
            return article;
        }
    }
}
