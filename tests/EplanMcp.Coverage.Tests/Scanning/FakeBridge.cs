using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace EplanMcp.Coverage.Tests.Scanning;

/// <summary>
/// A miniature Bridge for the scanner: a fake EPLAN assembly (named like the real ones), a Bridge.Core operation layer and
/// adapters, compiled in memory under a made-up repository root. Reads the runtime's reference assemblies from disk.
/// </summary>
internal static class FakeBridge
{
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "fake-repo");

    public const string Eplan = """
        namespace Eplan.EplApi.HEServices
        {
            public class Export
            {
                public enum Format { PNG, JPG }
                public void PdfPages(string file) { }
                public void PdfPages(string file, int degree) { }
                public void Dxf(string folder) { }
                public void Image(Format format) { }
                public int Count { get; set; }
                public string this[int index] { get => ""; set { } }
                public static implicit operator string(Export export) => "";
            }
        }
        namespace Eplan.EplApi.ApplicationFramework
        {
            public interface IEplAddIn { bool OnInit(); }
            public class ActionCallingContext { public void AddParameter(string name, string value) { } }
            public class Action { public bool Execute(ActionCallingContext context) => true; }
            public class ActionManager { public Action FindAction(string name) => new Action(); }
            public class CommandLineInterpreter { public bool Execute(string command) => true; public bool Execute(string name, ActionCallingContext context) => true; }
        }
        """;

    public const string Core = """
        namespace EplanMcp.Bridge.Core.Operations
        {
            public enum ExportKind { Pdf, Dxf, Image }
            public interface IExporter { void ExportPdf(); void ExportDxf(); void ExportImage(); void Shared(ExportKind kind); }
            public sealed class ExportRunOperation(ExportKind kind, IExporter exporter)
            {
                public void Execute(object args)
                {
                    exporter.Shared(kind);
                    if (kind == ExportKind.Image)
                    {
                        exporter.ExportImage();
                        return;
                    }

                    if (kind == ExportKind.Pdf) { exporter.ExportPdf(); } else { exporter.ExportDxf(); }
                }
            }
        }
        """;

    public const string Adapter = """
        using Eplan.EplApi.ApplicationFramework;
        using Eplan.EplApi.HEServices;
        using EplanMcp.Bridge.Core.Operations;
        namespace EplanMcp.Bridge.Eplan.Adapters
        {
            internal sealed class FakeExporter : IExporter
            {
                public void ExportPdf() => Run(e => e.PdfPages("a.pdf", 1));
                public void ExportDxf() => new Export().Dxf("x");
                public void ExportImage() { var export = new Export(); export.Image(Export.Format.JPG); }
                public void Shared(ExportKind kind)
                {
                    var export = new Export();
                    export.Count = export.Count + 1;
                    export[0] = export[1];
                    string text = export;
                    switch (kind)
                    {
                        case ExportKind.Dxf:
                            export.PdfPages("dxf-only");
                            break;
                    }
                }

                private static void Run(System.Action<Export> run) => run(new Export());
            }

            public sealed class FakeAddIn : IEplAddIn
            {
                public bool OnInit() => Close() && ExportScheme() && Label();

                private static bool Close() => new ActionManager().FindAction("XPrjActionProjectClose").Execute(new ActionCallingContext());

                private static bool ExportScheme()
                {
                    var context = new ActionCallingContext();
                    context.AddParameter("TYPE", "PDFPROJECTSCHEME");
                    return new CommandLineInterpreter().Execute("export", context);
                }

                private static bool Label() => new CommandLineInterpreter().Execute("label /TYPE:EXPORT /CONFIGSCHEME:x");
            }
        }
        """;

    public static CSharpCompilation Compile()
    {
        MetadataReference[] runtime = [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p) is "System.Runtime.dll" or "System.Private.CoreLib.dll" or "netstandard.dll")
            .Select(p => MetadataReference.CreateFromFile(p))];
        CSharpCompilation eplan = CSharpCompilation.Create("Eplan.EplApi.HEServicesu", [CSharpSyntaxTree.ParseText(Eplan)], runtime, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        Assert.True(eplan.Emit(image).Success);
        SyntaxTree[] trees =
        [
            CSharpSyntaxTree.ParseText(Core, path: Path.Combine(Root, "src", "EplanMcp.Bridge.Core", "Operations", "Ops.cs")),
            CSharpSyntaxTree.ParseText(Adapter, path: Path.Combine(Root, "src", "EplanMcp.Bridge.Eplan", "Adapters", "FakeExporter.cs")),
        ];
        CSharpCompilation bridge = CSharpCompilation.Create("Bridge", trees, [.. runtime, MetadataReference.CreateFromImage(image.ToArray())], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(bridge.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        return bridge;
    }
}
