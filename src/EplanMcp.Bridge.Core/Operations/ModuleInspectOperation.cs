using System.Security.Cryptography;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read-only native evidence, under the dispatcher's LockingStep (A33/A51/A56).</summary>
public sealed class ModuleInspectOperation(IModuleInspectionReader reader, Func<bool?> licence) : Operation<ModuleInspectArgs, ModuleInspectData>
{
    private static readonly string[] _coverage = "functions pages connections crossPageReferences placeholderBindings properties".Split(' ');
    public override string Name => OperationNames.ModuleInspect;
    public override object ParseArgs(JToken? args)
    {
        if (args is not JObject input || input.Properties().Any(p => p.Name is not ("project" or "macro" or "sha256" or "pages"))
            || input["project"]?.Type != JTokenType.String || input["macro"]?.Type != JTokenType.String || input["sha256"]?.Type != JTokenType.String
            || input["pages"] is not JArray pages || pages.Any(p => p.Type != JTokenType.String))
        { throw InvalidArgument("/: supply project, macro, sha256 and pages with no unknown members or coerced values."); }
        return base.ParseArgs(args);
    }
    protected override ModuleInspectData Execute(ModuleInspectArgs args)
    {
        if (!ProjectPaths.IsPlainFullPath(args.Project) || !args.Project.EndsWith(".elk", StringComparison.OrdinalIgnoreCase)
            || !ProjectPaths.IsPlainFullPath(args.Macro) || !args.Macro.EndsWith(".ema", StringComparison.OrdinalIgnoreCase)
            || args.Sha256 is not { Length: 64 } || args.Sha256.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || args.Pages is null || args.Pages.Count is < 1 or > 50 || args.Pages.Distinct(StringComparer.Ordinal).Count() != args.Pages.Count
            || args.Pages.Any(p => string.IsNullOrWhiteSpace(p) || p.IndexOfAny([':', '\\']) >= 0))
        { throw InvalidArgument("/: require local .elk/.ema paths, lowercase SHA-256 and 1..50 distinct full page names."); }
        CheckLicence();
        using var file = new FileStream(args.Macro, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        if (!string.Equals(BitConverter.ToString(sha.ComputeHash(file)).Replace("-", string.Empty), args.Sha256, StringComparison.OrdinalIgnoreCase))
        { throw new BridgeErrorException(ErrorReason.PlanStale, "The module macro changed; verify the pack before retrying."); }
        ModuleInspectData result = reader.Read(args);
        result.MacroHash = args.Sha256;
        result.ApiExtensionLicence = CheckLicence();
        result.Complete &= result.Gaps.Count == 0 && result.Coverage.Count == 6 && _coverage.All(k => result.Coverage.TryGetValue(k, out bool covered) && covered);
        return result;
    }
    private bool CheckLicence() => licence() == true ? true : throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Confirm the API Extension licence before inspecting modules.", false, new Dictionary<string, string> { ["missing"] = "licence:APIExtension" });
}
