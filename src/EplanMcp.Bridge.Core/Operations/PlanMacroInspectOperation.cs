using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read inside invoke, on the dispatcher's main thread/LockingStep; never inserts a macro (A14).</summary>
public sealed class PlanMacroInspectOperation(IProjectCatalog catalog, IMacroReader reader)
    : Operation<PlanMacroInspectArgs, PlanMacroInspectData>
{
    // Deliberately closed syntax: complex structures require a proven ownership reader, not a guessed prefix.
    private static readonly Regex _name = new(@"\A(?<segment>=[\p{L}\p{N}_.]*|\+[\p{L}\p{N}_.]*)*(?:-[\p{L}\p{N}_?.:\-]+)?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex _shown = new(@"\A[\p{L}\p{N}_?.:=+\-]*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public override string Name => OperationNames.PlanMacroInspect;

    protected override PlanMacroInspectData Execute(PlanMacroInspectArgs args)
    {
        string root = reader.MacroRoot() is { } resolved && Directory.Exists(resolved)
            ? resolved : throw Missing("macro:root");
        string path = MacroFiles.Resolve(root, args.Macro);
        IReadOnlyList<OpenProject> projects = catalog.ListOpenProjects();
        OpenProject project = projects.FirstOrDefault(p => p.Current)
            ?? (projects.Count == 1 ? projects[0] : null) ?? throw Missing("macro:open_project");
        try
        {
            // S35/A41: EPLAN can read/insert while this prevents file writes and replacement.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha = SHA256.Create();
            string digest = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", string.Empty).ToLowerInvariant();
            return Describe(reader.ReadMacro(project.Path, path, defaultMultiLine: true), digest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Missing("macro:stable_file");
        }
    }

    internal static PlanMacroInspectData Describe(MacroContent content, string sha256)
    {
        List<int>? variants = content.Representations.FirstOrDefault(r => r.Key == MacroInsertRequest.MultiLine).Value;
        if (variants is null || !variants.Contains(0))
        {
            throw new BridgeErrorException(ErrorReason.InvalidArgument, "Choose a macro with a MultiLine representation and variant 0.",
                retryable: false, new Dictionary<string, string> { ["violations"] = "[\"/macro: MultiLine variant 0 is required\"]" });
        }

        if (!content.Complete || content.FunctionCount != content.Functions.Count || content.ObjectCount < content.FunctionCount
            || content.Representation != MacroInsertRequest.MultiLine || content.Variant != 0)
        {
            throw Missing("macro:complete_variant_0");
        }

        if (!Finite(content.Width) || !Finite(content.Height))
        {
            throw Missing("macro:bounding_frame");
        }

        return new()
        {
            Sha256 = sha256,
            Frame = new() { Right = content.Width, Bottom = -content.Height },
            ObjectCount = content.ObjectCount,
            Functions = [.. content.Functions.Select(DescribeFunction)],
        };
    }

    private static PlanMacroFunction DescribeFunction(MacroFunctionRecord function)
    {
        Match name = _name.Match(function.Name);
        // PLC terminals keep external structure even with numbering=keep (A14). Do not infer ownership from a label.
        if (!name.Success || !_shown.IsMatch(function.VisibleName) || function.Category.StartsWith("PLCTerminal", StringComparison.Ordinal)
            || function.Kind is not ("function" or "terminal" or "plc" or "cable")
            || function.Category.Length == 0 || function.Category.Any(c => !char.IsLetterOrDigit(c) && c != '_')
            || (function.Name.IndexOf('-') < 0 && function.Category != MacroGetOperation.Wire)
            || name.Groups["segment"].Captures.Cast<Capture>().GroupBy(c => c.Value[0]).Any(g => g.Count() > 1))
        {
            throw Missing("macro:structure_ownership");
        }

        return new()
        {
            Kind = function.Kind,
            Category = function.Category,
            Name = function.Name,
            VisibleName = function.VisibleName,
            Main = function.Main,
            StructureSegments = [.. name.Groups["segment"].Captures.Cast<Capture>().Select(c => c.Value)],
        };
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;

    private static BridgeErrorException Missing(string capability) => new(ErrorReason.CapabilityMissing,
        "This macro cannot be inspected completely for plan prediction. Choose a supported macro or ask the engineer to check it.",
        retryable: false, new Dictionary<string, string> { ["missing"] = JsonConvert.SerializeObject(new[] { capability }) });
}
