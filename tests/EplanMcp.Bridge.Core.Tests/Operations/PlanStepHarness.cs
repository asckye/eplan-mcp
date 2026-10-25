using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using EplanMcp.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

internal sealed class PlanStepHarness : IDisposable
{
    public const string Page = "+A1/1";
    public PlanGuardReader GuardReader { get; } = new();
    public FakeCatalog Catalog { get; } = new(new OpenProject { Name = "Line1", Path = PlanGuardReader.Project, Current = true });
    public FakePageReader Pages { get; } = new();
    public FakePageWriter Writer { get; }
    public FakePageDescriptions Descriptions { get; } = new();
    public FakeDeviceReader Devices { get; } = new();
    public FakeDeviceRenamer Renamer { get; } = new();
    public FakePageRemover Remover { get; }
    public FakePageRenumberer PageRenumberer { get; }
    public FakeDeviceRenumberer DeviceRenumberer { get; } = new();
    public FakeMacroReader Macros { get; }
    public FakeMacroInserter Inserter { get; }
    public FakeTransactions Transactions { get; } = new();
    public FakeProjectBackup Backup { get; }
    public InlineDispatcher Dispatcher { get; } = new();
    public OperationRouter Router { get; }

    public PlanStepHarness(string macroRoot = "")
    {
        Pages.Pages.Add(new() { Name = Page, Type = "Circuit" });
        Writer = new(Pages);
        Remover = new(Pages);
        PageRenumberer = new(Pages);
        Macros = new(macroRoot);
        Inserter = new(Devices);
        Inserter.Pages[Page] = new()
        {
            Name = Page,
            Type = "Circuit"
        };
        Backup = new(Pages);
        Renamer.Functions.Add(("+A1-K1", "28/17/2/0", true, Page, "K1"));
        Devices.Functions.Add(new() { Name = "+A1-K1", IsMain = true, Page = Page });
        DeviceRenumberer.Pages.Add(Page);
        DeviceRenumberer.Functions.Add(new() { Name = "+A1-?K2", Page = Page, Id = "28/17/3/0", IsMain = true });
        DeviceRenumberer.Numbers["+A1-?K2"] = "+A1-K2";
        Transactions.Snapshot = () =>
        {
            PageSummary[] before = [.. Pages.Pages];
            return () =>
            {
                Pages.Pages.Clear();
                Pages.Pages.AddRange(before);
            };
        };
        var guard = new PlanStepGuardOperation(GuardReader, GuardReader, GuardReader, GuardReader, () => GuardReader.Milliseconds);
        var operation = new PlanStepExecuteOperation(guard, Catalog, Pages, Writer, Descriptions, Devices, Renamer, Remover, PageRenumberer, DeviceRenumberer, Macros, Inserter, Backup, Transactions);
        Router = new([operation], Dispatcher, null, new ListLog(), TimeSpan.FromSeconds(10));
    }

    public PlanStepExecuteArgs Step(string op)
    {
        string project = PlanGuardReader.Project;
        var args = new PlanStepExecuteArgs
        {
            PlanId = "0123456789abcdef0123456789abcdef",
            Index = 2,
            Total = 3,
            Guard = GuardReader.Args(),
        };
        (IOperation Plan, object Args) selected;
        switch (op)
        {
            case "page.create":
                args.PageCreate = new()
                {
                    Project = project,
                    Pages = [new()
                    {
                        Name = "+A1/2",
                        Description = "New"
                    }
                    ]
                };
                selected = (new PageCreatePlanOperation(Catalog, Pages, Writer), args.PageCreate);
                break;
            case "page.update":
                args.PageUpdate = new()
                {
                    Project = project,
                    Language = "en_US",
                    Pages = [new()
                    {
                        Name = Page,
                        Description = "New"
                    }
                    ]
                };
                selected = (new PageUpdatePlanOperation(Catalog, Pages, Descriptions), args.PageUpdate);
                break;
            case "page.delete":
                args.PageDelete = new()
                {
                    Project = project,
                    Pages = [Page]
                };
                selected = (new PageDeletePlanOperation(Catalog, Remover), args.PageDelete);
                break;
            case "page.renumber":
                args.PageRenumber = new()
                {
                    Project = project,
                    Pages = [Page],
                    Start = 3
                };
                selected = (new PageRenumberPlanOperation(Catalog, Pages, PageRenumberer), args.PageRenumber);
                break;
            case "device.update":
                args.DeviceUpdate = new()
                {
                    Project = project,
                    Tag = "+A1-K1",
                    NewTag = "-K9"
                };
                selected = (new DeviceUpdatePlanOperation(Catalog, Devices, Renamer), args.DeviceUpdate);
                break;
            case "device.renumber":
                args.DeviceRenumber = new()
                {
                    Project = project,
                    Pages = [Page]
                };
                selected = (new DeviceRenumberPlanOperation(Catalog, DeviceRenumberer), args.DeviceRenumber);
                break;
            case "macro.insert":
                args.MacroInsert = new()
                {
                    Project = project,
                    Page = Page,
                    Macro = "test.ema",
                    X = 40,
                    Y = 280,
                    Numbering = "keep"
                };
                selected = (new MacroInsertPlanOperation(Catalog, Pages, Devices, Macros, Inserter), args.MacroInsert);
                break;
            default:
                throw new InvalidOperationException(op);
        }

        var plan = (WritePlan)selected.Plan.Execute(selected.Args)!;
        args.ApprovedChanges = plan.Changes;
        args.ApprovedDetails = plan.Details;
        args.Fingerprint = plan.Fingerprint;
        return args;
    }

    public JToken Run(PlanStepExecuteArgs args, string request = "step") => Router.Invoke(OperationNames.PlanStepExecute, BridgeJson.FromObject(args), request);
    public string Outcome(string request = "step") => (string)Router.Invoke(OperationNames.OutcomeGet, new JObject { ["requestId"] = request }, "query")["status"]!;
    public void Dispose() => GuardReader.Dispose();
}
