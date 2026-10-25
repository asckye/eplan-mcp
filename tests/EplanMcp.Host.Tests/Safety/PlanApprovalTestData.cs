using System.Text;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Plans;

namespace EplanMcp.Host.Tests.Safety;

internal static class PlanApprovalTestData
{
    public const string ChangesPath = @"D:\Review\state\plans\0123456789abcdef0123456789abcdef\changes.txt";
    public const string Backup = @"D:\Review\backups\Demo\before-plan";

    public static PlanRecord Record(int changes = 1, string value = "Example")
    {
        PlanDefinition definition = PlanTestData.Definition();
        PlanStep step = definition.Steps[0] with
        {
            Index = 1,
            Expected = [],
            PlannedChanges = [.. Enumerable.Range(0, changes).Select(_ => new PlanChange("update", "page", "+A/1", "description", "Before", value))],
        };
        definition = definition with { Steps = [step] };
        return WithDigest(definition);
    }

    public static PlanRecord WithDigest(PlanDefinition definition)
    {
        definition = definition with
        {
            Digests = definition.Digests with { ChangesDigest = PlanStore.Hash(Encoding.UTF8.GetBytes(PlanDiffPreview.For(definition.Steps))) },
        };
        return new(1, PlanCanonical.Hash(definition), definition, new());
    }

    public static PlanApprovalView View(PlanRecord? record = null) => new(record ?? Record(), ChangesPath, Backup);
}
