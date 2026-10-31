using System.Reflection;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;
namespace EplanMcp.Coverage.Tests;

/// <summary>Runs the actual identity code against native-free doubles, comparing the pre-slice implementation byte for byte.</summary>
[Trait("Size", "Medium")]
public sealed class ObjectLocatorCompatibilityTests
{
    private static readonly Lazy<MethodInfo> _probe = new(Compile);
    [Theory]
    [InlineData("page")]
    [InlineData("function")]
    [InlineData("connection")]
    [InlineData("terminal")]
    [InlineData("plcterminal")]
    [InlineData("terminalstrip")]
    [InlineData("plc")]
    [InlineData("cable")]
    [InlineData("blackbox")]
    [InlineData("placeholder")]
    [InlineData("interruptionpoint")]
    [InlineData("potentialdefinition")]
    [InlineData("locationbox")]
    [InlineData("macrobox")]
    public void ExistingKind_HasBitIdenticalIdentityAndDigest(string kind)
    {
        var identities = (string[])_probe.Value.Invoke(null, [kind])!;
        Assert.Equal(identities[0], identities[1]);
        Assert.Equal(SnapshotCanonical.Sha256(identities[0]), SnapshotCanonical.Sha256(identities[1]));
    }
    private static MethodInfo Compile()
    {
        string root = Program.RepositoryRoot(AppContext.BaseDirectory)!;
        string Methods(string file, params string[] names) => string.Join("\n", CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, file))).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => names.Contains(m.Identifier.ValueText, StringComparer.Ordinal)).Select(m => m.ToFullString()));
        string source = """
            using System;
            using Eplan.EplApi.Base;
            using Eplan.EplApi.Base.Enums;
            using Eplan.EplApi.DataModel;
            using Eplan.EplApi.DataModel.EObjects;
            using Eplan.EplApi.DataModel.Graphics;
            using EplanMcp.Bridge.Core.Operations;
            using EplanMcp.Contracts;
            using Newtonsoft.Json;
            """ + Doubles
            + "class EplanPropertyReadAdapter {" + Methods("src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs", "Describe", "End") + "}\n"
            + "class EplanObjectReader {" + Methods("src/EplanMcp.Bridge.Eplan/Adapters/EplanObjectReader.cs", "Describe", "Legacy") + "}\n"
            + "class BeforeSlice {" + BeforeSlice + "}\n"
            + """
                public static class Probe
                {
                    public static string[] Run(string kind)
                    {
                        StorableObject obj = kind switch
                        {
                            "page" => new Page(), "function" => new Function(), "connection" => new Connection(),
                            "terminal" => new Terminal(), "plcterminal" => new Terminal { FunctionCategory = FunctionCategory.PLCTerminal },
                            "terminalstrip" => new TerminalStrip(), "plc" => new PLC(), "cable" => new Cable(), "blackbox" => new BoxedDevice(),
                            "placeholder" => new PlaceHolder(), "interruptionpoint" => new InterruptionPoint(),
                            "potentialdefinition" => new PotentialDefinition(), "locationbox" => new LocationBox(), "macrobox" => new MacroBox(),
                            _ => throw new InvalidOperationException(),
                        };
                        return new[] { JsonConvert.SerializeObject(BeforeSlice.Describe(obj, kind)), JsonConvert.SerializeObject(EplanObjectReader.Describe(obj, kind)) };
                    }
                }
                """;
        string[] paths = [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator),
            typeof(SnapshotObject).Assembly.Location, typeof(SnapshotCanonical).Assembly.Location, typeof(JsonConvert).Assembly.Location];
        CSharpCompilation compilation = CSharpCompilation.Create("ObjectIdentityProbe", [CSharpSyntaxTree.ParseText(source)],
            paths.Distinct(StringComparer.OrdinalIgnoreCase).Select(p => MetadataReference.CreateFromFile(p)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var result = compilation.Emit(image);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return Assembly.Load(image.ToArray()).GetType("Probe")!.GetMethod("Run")!;
    }
    // Frozen pre-slice identity implementation, including Page's exclusion from Placement.Location (A60).
    private const string BeforeSlice = """
        internal static SnapshotObject Describe(StorableObject obj, string kind)
        {
            SnapshotObject identity = EplanPropertyReadAdapter.Describe(obj);
            identity.Kind = kind;
            if (obj is Placement placement and not Function and not Page)
            {
                identity.Page = placement.Page?.Name;
                identity.Name = obj is FunctionBase function ? function.Name : obj is PlaceHolder holder ? holder.Name : string.Empty;
                PointD position = placement.Location;
                identity.Pos = SnapshotValues.Position(position.X, position.Y);
            }
            return identity;
        }
        """;
    private const string Doubles = """
        namespace Eplan.EplApi.Base { public record struct PointD(double X, double Y); }
        namespace Eplan.EplApi.Base.Enums { public enum FunctionCategory { Motor, PLCTerminal } }
        namespace Eplan.EplApi.DataModel
        {
            public class StorableObject { public long ObjectIdentifier => 42; public string ToStringIdentifier() => "1/17/42/0"; }
            public class Placement : StorableObject { public Page Page => new(); public virtual PointD Location => new(12.3456789, -0.000001); }
            public class Group : Placement { }
            public class Page : Group { public string Name => "=A+一/1"; public override PointD Location => throw new InvalidOperationException("Never read Page.Location"); }
            public class FunctionBase : Placement { public string Name => "=A+一-X1"; }
            public class Function : FunctionBase { public bool IsMainFunction => true; public FunctionCategory FunctionCategory { get; set; } }
            public class Connection : StorableObject { public Pin StartPin => new(); public Pin EndPin => new(); public Page Page => new(); }
            public class Pin { public Function ParentFunction => new(); public string Name => "1"; }
            public class BoxedDevice : Function { }
            public class InterruptionPoint : FunctionBase { }
            public class PotentialDefinition : FunctionBase { }
            public class LocationBox : FunctionBase { }
            public class MacroBox : Placement { }
        }
        namespace Eplan.EplApi.DataModel.EObjects
        {
            public class Terminal : Function { }
            public class TerminalStrip : Function { }
            public class PLC : Function { }
            public class Cable : Function { }
        }
        namespace Eplan.EplApi.DataModel.Graphics { public class PlaceHolder : Placement { public string Name => "holder"; } }
        namespace Eplan.EplApi.DataModel.E3D { public class Connection3D : Connection { } }
        static class EplanConnectionReader { internal static Page PageOf(Connection obj) => obj.Page; }
        """;
}
