using System.Collections.Concurrent;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>Runs work on the calling thread, catching everything as the real dispatcher must.</summary>
internal sealed class InlineDispatcher : IMainThreadDispatcher
{
    public int Calls { get; private set; }

    public DispatchResult Run(Func<object?> work, TimeSpan startTimeout)
    {
        Calls++;
        try
        {
            return DispatchResult.Completed(work());
        }
        catch (Exception ex)
        {
            return DispatchResult.Faulted(ex);
        }
    }
}

/// <summary>Refuses every piece of work with a fixed reason; the work must never run.</summary>
internal sealed class BusyDispatcher(BusyReason reason) : IMainThreadDispatcher
{
    public DispatchResult Run(Func<object?> work, TimeSpan startTimeout) => DispatchResult.Busy(reason);
}

internal sealed class ListLog : IBridgeLog
{
    public ConcurrentQueue<(BridgeLogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public void Write(BridgeLogLevel level, string message, Exception? exception = null) => Entries.Enqueue((level, message, exception));
}

internal sealed class FakeCatalog(params OpenProject[] projects) : IProjectCatalog
{
    public IReadOnlyList<OpenProject> ListOpenProjects() => projects;
}

/// <summary>Returns <see cref="Content"/> and records what the operation asked for.</summary>
internal sealed class FakeProjectReader : IProjectReader
{
    public ProjectContent Content { get; set; } = new();

    public List<(string Path, bool IncludeProperties, string? Language)> Calls { get; } = [];

    public ProjectContent Read(string projectPath, bool includeProperties, string? language)
    {
        Calls.Add((projectPath, includeProperties, language));
        return Content;
    }
}

/// <summary>
/// Pages of one project, filtered the way S6 found EPLAN filters (name prefix case-sensitive, page type exact), so
/// the operation's own filtering and paging run against realistic input. Records what it was asked.
/// </summary>
internal sealed class FakePageReader : IPageReader
{
    public List<PageSummary> Pages { get; } = [];

    public IReadOnlyCollection<string> PageTypes { get; set; } = ["Circuit", "TerminalDiagram", "TitlePage", "Overview"];

    public List<(string Path, string? NamePrefix, string? PageType, string? Language)> ListCalls { get; } = [];

    /// <summary>The functions placed on each page, by page name, for <see cref="ReadPage"/>.</summary>
    public Dictionary<string, List<FunctionRecord>> Functions { get; } = new(StringComparer.Ordinal);

    public List<PropertyEntry>? Properties { get; set; }

    public List<(string Path, string PageName, bool IncludeProperties, string? Language)> ReadCalls { get; } = [];

    public IReadOnlyList<PageSummary> ListPages(string projectPath, string? namePrefix, string? pageType, string? language)
    {
        ListCalls.Add((projectPath, namePrefix, pageType, language));
        return [.. Pages.Where(p => (namePrefix is null || p.Name.StartsWith(namePrefix, StringComparison.Ordinal)) && (pageType is null || p.Type == pageType))];
    }

    public IReadOnlyList<string> PageNames(string projectPath) => [.. Pages.Select(p => p.Name)];

    /// <summary>Exact and case-sensitive, like EPLAN's exact name filter.</summary>
    public PageContent? ReadPage(string projectPath, string pageName, bool includeProperties, string? language)
    {
        ReadCalls.Add((projectPath, pageName, includeProperties, language));
        PageSummary? page = Pages.FirstOrDefault(p => p.Name == pageName);
        return page is null
            ? null
            : new PageContent
            {
                Page = new PageDetail { Name = page.Name, Type = page.Type, TypeName = page.Type + " (UI)", Description = page.Description },
                Functions = Functions.TryGetValue(page.Name, out List<FunctionRecord>? functions) ? functions : [],
                Properties = includeProperties ? Properties : null,
            };
    }
}

/// <summary>
/// A project's functions as an EPLAN adapter would read them: page and category narrow the list like FunctionsFilter,
/// everything else is left to the operation. Records every read.
/// </summary>
internal sealed class FakeDeviceReader : IDeviceReader
{
    private static readonly string[] _categories = ["Coil", "NoContact", "NcContact", "Motor", "CircuitBreaker", "Terminal", "PLCBox", "Cable"];

    public List<FunctionRecord> Functions { get; } = [];

    /// <summary>What <see cref="ReadDevice"/> returns, by the main function's full tag (ordinal).</summary>
    public Dictionary<string, DeviceRecord> Devices { get; } = new(StringComparer.Ordinal);

    public List<(string Path, string? Page, string? Category)> Reads { get; } = [];

    public List<(string Path, string Tag, string? Language, bool WithProperties)> DeviceReads { get; } = [];

    public int NameReads { get; private set; }

    public IReadOnlyList<FunctionName> ReadMainFunctionNames(string projectPath)
    {
        NameReads++;
        return [.. Functions.Where(f => f.IsMain).Select(f => new FunctionName { Name = f.Name, VisibleName = f.VisibleName })];
    }

    public DeviceRecord? ReadDevice(string projectPath, string tag, string? language, bool withProperties)
    {
        DeviceReads.Add((projectPath, tag, language, withProperties));
        return language == "xx_XX"
            ? throw new BridgeErrorException(ErrorReason.InvalidArgument, "'language' is not a language EPLAN knows.")
            : Devices.GetValueOrDefault(tag);
    }

    public bool IsCategory(string category) => _categories.Contains(category, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<FunctionRecord>? ReadFunctions(string projectPath, string? page, string? category)
    {
        Reads.Add((projectPath, page, category));
        if (page is not null && !Functions.Any(f => f.Page == page))
        {
            return null;
        }

        return [.. Functions.Where(f => (page is null || f.Page == page) && (category is null || string.Equals(f.Category, category, StringComparison.OrdinalIgnoreCase)))];
    }
}

/// <summary>
/// A project's connections narrowed the way the EPLAN adapter narrows them: by device, those starting or ending at the
/// device's main function or one of its auxiliary functions (here: the same tag, or the tag with a ':' suffix, S6); by
/// page, those placed on it. Records every read.
/// </summary>
internal sealed class FakeConnectionReader : IConnectionReader
{
    public List<string> Pages { get; } = [];

    public List<ConnectionRecord> Connections { get; } = [];

    public List<(string Path, string? Device, string? Page)> Reads { get; } = [];

    public IReadOnlyList<ConnectionRecord>? ReadConnections(string projectPath, string? device, string? page)
    {
        Reads.Add((projectPath, device, page));
        if (page is not null && !Pages.Contains(page, StringComparer.Ordinal))
        {
            return null;
        }

        return [.. Connections.Where(c => (device is null || At(c.StartDevice, device) || At(c.EndDevice, device)) && (page is null || c.Page == page))];
    }

    private static bool At(string? function, string device) =>
        function is not null && (function == device || function.StartsWith(device + ":", StringComparison.Ordinal));
}

/// <summary>A project's part references and part data as the EPLAN adapter reads them; "xx_XX" is a language EPLAN does not know.</summary>
internal sealed class FakePartReader : IPartReader
{
    public ProjectParts Parts { get; } = new();

    public List<(string Path, string? Language)> Reads { get; } = [];

    public ProjectParts ReadParts(string projectPath, string? language)
    {
        Reads.Add((projectPath, language));
        return language == "xx_XX"
            ? throw new BridgeErrorException(ErrorReason.InvalidArgument, "'language' is not a language EPLAN knows.")
            : Parts;
    }
}

/// <summary>An operation whose behaviour is a delegate, for router and connection tests.</summary>
internal sealed class DelegateOperation(string name, Func<object?> execute) : IOperation
{
    public string Name => name;

    public object ParseArgs(JToken? args) => args ?? JValue.CreateNull();

    public object? Execute(object args) => execute();
}

internal sealed class FixedMapper(BridgeError? error) : IExceptionMapper
{
    public BridgeError? Map(Exception exception) => error;
}

/// <summary>
/// Creates pages in a <see cref="FakePageReader"/>'s list the way S9 found EPLAN does (api-notes A13): a name that is
/// taken (any letter case) throws ALREADY_EXISTS, and <see cref="Drop"/> stands for a name part the project's page
/// structure does not use. <see cref="ChangeDescription"/> and <see cref="FailOn"/> inject other failures.
/// </summary>
internal sealed class FakePageWriter(FakePageReader pages) : IPageWriter
{
    public List<(string Path, string Name, string Type, string? Description, string? Language)> Creates { get; } = [];

    /// <summary>A part EPLAN drops from every new name, e.g. "&amp;EFS1".</summary>
    public string? Drop { get; set; }

    public Func<string?, string?>? ChangeDescription { get; set; }

    /// <summary>The create (counted from 1) that throws like an EPLAN failure.</summary>
    public int? FailOn { get; set; }

    public bool KnowsLanguage(string language) => language != "xx_XX";

    public PageSummary Create(string projectPath, PageName name, string pageType, string? description, string? language)
    {
        Creates.Add((projectPath, name.Text, pageType, description, language));
        if (Creates.Count == FailOn)
        {
            throw new InvalidOperationException("EPLAN failed");
        }

        string actual = Drop is null ? name.Text : name.Text.Replace(Drop, string.Empty, StringComparison.Ordinal);
        if (pages.Pages.Any(p => string.Equals(p.Name, actual, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BridgeErrorException(ErrorReason.AlreadyExists, "EPLAN: the name exists");
        }

        var page = new PageSummary { Name = actual, Type = pageType, Description = ChangeDescription is null ? description : ChangeDescription(description) };
        pages.Pages.Add(page);
        return page;
    }
}

/// <summary>
/// Page descriptions by page name, stored as EPLAN stores them (raw <c>code@text;</c> per language, api-notes A11).
/// <see cref="Tamper"/> changes what a write stores, standing for EPLAN not keeping something.
/// </summary>
internal sealed class FakePageDescriptions : IPageDescriptions
{
    public Dictionary<string, List<TextEntry>> Pages { get; } = new(StringComparer.Ordinal);

    public string GuiLanguage { get; set; } = "zh_CN";

    public List<(string Page, string[] Entries)> Writes { get; } = [];

    public Func<List<TextEntry>, List<TextEntry>>? Tamper { get; set; }

    public string? LanguageCode(string? language) => language is null ? GuiLanguage : language == "xx_XX" ? null : language;

    public PageDescription Read(string projectPath, string pageName)
    {
        List<TextEntry> entries = Pages.GetValueOrDefault(pageName) ?? [];
        return new PageDescription(entries.Count == 0 ? null : string.Concat(entries.Select(e => $"{e.Language}@{e.Text};")), [.. entries]);
    }

    public PageDescription Write(string projectPath, string pageName, IReadOnlyList<TextEntry> entries)
    {
        Writes.Add((pageName, [.. entries.Select(e => $"{e.Language}@{e.Text}")]));
        Pages[pageName] = Tamper is null ? [.. entries] : Tamper([.. entries]);
        return Read(projectPath, pageName);
    }
}

/// <summary>
/// A project's functions for renames, renamed the way S9 found EPLAN renames them (api-notes A13): every function named
/// the old tag gets the new name, without a check for duplicates. <see cref="Drop"/> stands for a structure part the
/// category does not use; <see cref="LeaveBehind"/> for a function EPLAN did not rename.
/// </summary>
internal sealed class FakeDeviceRenamer : IDeviceRenamer
{
    /// <summary>Name, session identifier, main or not, page and, for main functions, the tag without structure.</summary>
    public List<(string Name, string Id, bool IsMain, string? Page, string DeviceTag)> Functions { get; } = [];

    /// <summary>The auxiliary function names of a main function, by its session identifier; default: its other same-named functions.</summary>
    public Dictionary<string, List<string>> SubFunctions { get; } = new(StringComparer.Ordinal);

    public string? Drop { get; set; }

    public bool LeaveBehind { get; set; }

    public List<(string Tag, string NewTag)> Renames { get; } = [];

    public DeviceNames? ReadDevice(string projectPath, string tag)
    {
        var main = Functions.FirstOrDefault(f => f.IsMain && f.Name == tag);
        if (main.Name is null)
        {
            return null;
        }

        var named = Functions.Where(f => f.Name == tag).ToList();
        return new DeviceNames
        {
            Tag = tag,
            DeviceTag = main.DeviceTag,
            Functions = [.. named.Select(f => new DeviceNames.NamedFunction { Id = f.Id, IsMain = f.IsMain, Page = f.Page })],
            SubFunctions = SubFunctions.TryGetValue(main.Id, out List<string>? subs) ? subs : [.. named.Where(f => !f.IsMain).Select(f => f.Name)],
        };
    }

    public IReadOnlyList<string> FunctionNames(string projectPath) => [.. Functions.Select(f => f.Name)];

    public RenameReadBack Rename(string projectPath, string tag, string newTag)
    {
        Renames.Add((tag, newTag));
        string actual = Drop is null ? newTag : newTag.Replace(Drop, string.Empty, StringComparison.Ordinal);
        bool leftOne = false;
        for (int i = 0; i < Functions.Count; i++)
        {
            if (Functions[i].Name == tag)
            {
                if (LeaveBehind && !Functions[i].IsMain && !leftOne)
                {
                    leftOne = true;
                    continue;
                }

                Functions[i] = Functions[i] with { Name = actual };
            }
        }

        return new RenameReadBack
        {
            MainName = Functions.First(f => f.IsMain && f.Name == actual).Name,
            VisibleName = "-" + actual.Split('-')[^1],
            Remaining = Functions.Count(f => f.Name == tag),
            Renamed = Functions.Count(f => f.Name == actual),
        };
    }
}

/// <summary>
/// Returns <see cref="Content"/> for any macro below a real directory and records what was opened, and in which project.
/// The content is S10's <c>Contactor_Feedback_Control.ema</c>: MultiLine with variants 0–5, 52 x 268 mm, 28 objects.
/// </summary>
internal sealed class FakeMacroReader(string root) : IMacroReader
{
    public MacroContent Content { get; set; } = new()
    {
        Representations = [new("MultiLine", [0, 1, 2, 3, 4, 5])],
        Width = 52,
        Height = 268,
        ObjectCount = 28,
    };

    public string? Opened { get; private set; }

    public string? OpenedIn { get; private set; }

    public string? MacroRoot() => root;

    public MacroContent ReadMacro(string projectPath, string macroPath)
    {
        (OpenedIn, Opened) = (projectPath, macroPath);
        return Content;
    }
}

/// <summary>
/// Target pages, and window macros inserted the way S9 and S10 found EPLAN inserts them (api-notes A13, A14):
/// <see cref="Placed"/> stands for EPLAN's numbering and gives the inserted functions by mode (true: number); their
/// main functions join the project's in <paramref name="project"/> and their objects the page. <see cref="Stacked"/>
/// stands for EPLAN refusing to place the same symbols on top of each other ("S029181").
/// </summary>
internal sealed class FakeMacroInserter(FakeDeviceReader project) : IMacroInserter
{
    public Dictionary<string, MacroTargetPage> Pages { get; } = new(StringComparer.Ordinal);

    public Func<bool, List<MacroFunctionRecord>> Placed { get; set; } = _ => [];

    public bool Stacked { get; set; }

    public List<(string Page, string Macro, int Variant, double X, double Y, bool Number)> Inserts { get; } = [];

    public MacroTargetPage? ReadPage(string projectPath, string pageName) => Pages.GetValueOrDefault(pageName);

    public PlacedMacro Insert(string projectPath, string pageName, string macroPath, int variant, double x, double y, bool number)
    {
        Inserts.Add((pageName, macroPath, variant, x, y, number));
        if (Stacked)
        {
            throw new BridgeErrorException(ErrorReason.InvalidArgument, "EPLAN: the same symbols on top of each other");
        }

        List<MacroFunctionRecord> functions = Placed(number);
        project.Functions.AddRange(functions.Where(f => f.Main).Select(f => new FunctionRecord { Name = f.Name, VisibleName = f.VisibleName, IsMain = true, Category = f.Category, Page = pageName }));
        Pages[pageName].Objects.AddRange(functions.Select((f, i) => "9/9/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/0"));
        return new PlacedMacro { Functions = functions, ObjectCount = functions.Count + 9 };
    }
}

/// <summary>
/// Transactions that behave like EPLAN's SafetyPoint as far as a fake project can: <see cref="Snapshot"/> runs when one
/// begins and returns how to restore that state, which a rollback does. Records every call in order and throws where
/// <see cref="FailAt"/> says ("Begin", "Commit", "CloseUndo", "Rollback" or "Dispose").
/// </summary>
internal sealed class FakeTransactions : ITransactionScopeFactory
{
    public List<string> Calls { get; } = [];

    public string? FailAt { get; set; }

    public Func<Action>? Snapshot { get; set; }

    public ITransactionScope Begin(string undoDescription)
    {
        Step("Begin " + undoDescription, "Begin");
        return new Scope(this, Snapshot?.Invoke());
    }

    private void Step(string call, string step)
    {
        Calls.Add(call);
        if (FailAt == step)
        {
            throw new InvalidOperationException(step + " failed");
        }
    }

    private sealed class Scope(FakeTransactions owner, Action? restore) : ITransactionScope
    {
        public void Commit() => owner.Step("Commit", "Commit");

        public void CloseUndo() => owner.Step("CloseUndo", "CloseUndo");

        public void Rollback()
        {
            owner.Step("Rollback", "Rollback");
            restore?.Invoke();
        }

        public void Dispose() => owner.Step("Dispose", "Dispose");
    }
}

/// <summary>
/// Pages' connections and objects, generated the way S9 and S10 found EPLAN generates them (api-notes A13, A14): the
/// connections <see cref="Drawn"/> on a page become connections once, the <see cref="Stale"/> ones are removed, and a
/// page whose connections are complete keeps them. <see cref="Failure"/> is thrown by Generate after the change, like
/// an EPLAN failure midway. Records every generation.
/// </summary>
internal sealed class FakeConnectionGenerator : IConnectionGenerator
{
    public Dictionary<string, List<ConnectionRecord>> Connections { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<string>> Objects { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<ConnectionRecord>> Drawn { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<ConnectionRecord>> Stale { get; } = new(StringComparer.Ordinal);

    public Exception? Failure { get; set; }

    public List<string[]> Generates { get; } = [];

    public IReadOnlyList<IReadOnlyList<ConnectionRecord>> ReadConnections(string projectPath, IReadOnlyList<string> pages) =>
        [.. pages.Select(page => (IReadOnlyList<ConnectionRecord>)[.. Connections.GetValueOrDefault(page) ?? []])];

    public IReadOnlyList<IReadOnlyList<string>> ObjectIds(string projectPath, IReadOnlyList<string> pages) =>
        [.. pages.Select(page => (IReadOnlyList<string>)[.. Objects.GetValueOrDefault(page) ?? []])];

    public void Generate(string projectPath, IReadOnlyList<string> pages)
    {
        Generates.Add([.. pages]);
        foreach (string page in pages)
        {
            if (!Connections.TryGetValue(page, out List<ConnectionRecord>? connections))
            {
                connections = Connections[page] = [];
            }

            connections.RemoveAll(c => Stale.GetValueOrDefault(page)?.Contains(c) == true);
            connections.AddRange(Drawn.GetValueOrDefault(page) ?? []);
            Drawn.Remove(page);
        }

        if (Failure is not null)
        {
            throw Failure;
        }
    }
}

/// <summary>
/// A project's pages and check messages, checked the way S6 and S11 found EPLAN checks (api-notes A11, A16): a check
/// replaces the project's messages with <see cref="Found"/>. <see cref="OnRun"/> runs during the check, standing for
/// whatever happens meanwhile; <see cref="Failure"/> is thrown after the change, like an EPLAN failure midway. Records
/// every check.
/// </summary>
internal sealed class FakeCheckRunner : ICheckRunner, IProjectMessageReader
{
    /// <summary>Each page's session identifier, by its exact name, in EPLAN's page order.</summary>
    public Dictionary<string, string> Pages { get; } = new(StringComparer.Ordinal);

    public List<ProjectMessageRecord> Messages { get; set; } = [];

    public List<ProjectMessageRecord> Found { get; } = [];

    public Action? OnRun { get; set; }

    public Exception? Failure { get; set; }

    public List<(string[]? Pages, string Scheme)> Runs { get; } = [];

    public IReadOnlyList<string> PageIds(string projectPath, IReadOnlyList<string>? pages) =>
        pages is null ? [.. Pages.Values] : [.. pages.Select(page => Pages[page])];

    public void Run(string projectPath, IReadOnlyList<string>? pages, string scheme)
    {
        Runs.Add((pages?.ToArray(), scheme));
        Messages = [.. Found];
        OnRun?.Invoke();
        if (Failure is not null)
        {
            throw Failure;
        }
    }

    public IReadOnlyList<ProjectMessageRecord> ReadMessages(string projectPath) => [.. Messages];
}

/// <summary>
/// A project's report pages, updated the way S11 found EPLAN updates them (api-notes A16): updating all reports makes
/// the report pages <see cref="AllAfter"/> (unchanged when null); updating one page removes the pages
/// <see cref="Removes"/> names for it. <see cref="OnUpdate"/> runs during each update, standing for whatever happens
/// meanwhile; <see cref="Failure"/> is thrown after the change, like an EPLAN failure midway. Records every update.
/// </summary>
internal sealed class FakeReportUpdater : IReportUpdater
{
    public List<ReportPageRecord> Reports { get; set; } = [];

    public List<ReportPageRecord>? AllAfter { get; set; }

    public Dictionary<string, string[]> Removes { get; } = new(StringComparer.Ordinal);

    public Action<string?>? OnUpdate { get; set; }

    public Exception? Failure { get; set; }

    /// <summary>Each update in order: a page name, or null for all reports.</summary>
    public List<string?> Updates { get; } = [];

    public IReadOnlyList<ReportPageRecord> ReportPages(string projectPath) => [.. Reports];

    public void UpdateAll(string projectPath)
    {
        Updates.Add(null);
        Reports = AllAfter ?? Reports;
        After(null);
    }

    public bool Update(string projectPath, string page)
    {
        Updates.Add(page);
        bool found = Reports.Any(r => r.Name == page);
        Reports = [.. Reports.Where(r => !(Removes.GetValueOrDefault(page) ?? []).Contains(r.Name))];
        After(page);
        return found;
    }

    private void After(string? page)
    {
        OnUpdate?.Invoke(page);
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}

/// <summary>
/// Copies the way S12 found CopyProject(Snapshot) does (api-notes A18): the copy appears at the given path, with
/// <see cref="Pages"/> pages (default: as many as <c>pages</c> has); <see cref="Failure"/> and <see cref="Delay"/> stand
/// for a failed or slow copy. Records every copy and count.
/// </summary>
internal sealed class FakeProjectBackup(FakePageReader? pages = null) : IProjectBackup
{
    public List<(string Project, string Copy)> Copies { get; } = [];

    public List<string> Counted { get; } = [];

    public int? Pages { get; set; }

    public Exception? Failure { get; set; }

    public TimeSpan Delay { get; set; }

    public void Copy(string projectPath, string copyPath)
    {
        Copies.Add((projectPath, copyPath));
        Thread.Sleep(Delay);
        if (Failure is not null)
        {
            throw Failure;
        }

        File.WriteAllText(copyPath, "copy");
    }

    public int CountPages(string copyPath)
    {
        Counted.Add(copyPath);
        return Pages ?? pages?.Pages.Count ?? 0;
    }
}

/// <summary>
/// Pages of a <see cref="FakePageReader"/> and the project's functions, removed the way S12 found EPLAN removes a page
/// (api-notes A18): the page and the functions on it go, functions of the same tag on other pages stay. A page's object
/// identifiers are its own and its functions'. <see cref="Keep"/> stands for a page EPLAN did not remove,
/// <see cref="Failure"/> for EPLAN refusing. Records every removal.
/// </summary>
internal sealed class FakePageRemover(FakePageReader pages) : IPageRemover
{
    public List<PlacedFunction> Placed { get; } = [];

    public List<string> Removed { get; } = [];

    public string? Keep { get; set; }

    public Exception? Failure { get; set; }

    public RemovablePage? ReadPage(string projectPath, string pageName) =>
        pages.Pages.FirstOrDefault(p => p.Name == pageName) is { } page
            ? new RemovablePage { Name = page.Name, Type = page.Type, ObjectIds = ["page " + page.Name, .. Placed.Where(f => f.Page == page.Name).Select(f => f.Id)] }
            : null;

    public IReadOnlyList<PlacedFunction> Functions(string projectPath) => [.. Placed];

    public void Remove(string projectPath, string pageName)
    {
        Removed.Add(pageName);
        if (Failure is not null)
        {
            throw Failure;
        }

        if (pageName != Keep)
        {
            pages.Pages.RemoveAll(p => p.Name == pageName);
            Placed.RemoveAll(f => f.Page == pageName);
        }
    }
}

/// <summary>
/// The functions of a project's pages, numbered the way S12 found <c>Renumber.DeviceTags</c> with PostNumerate numbers
/// them (api-notes A18): <see cref="Numbers"/> gives the new tag of each device EPLAN numbers, connection points keep
/// their '?', and a device not in it (S12: <c>=GAA+A1-?QA1</c>) stays as it was. A scheme other than "", "Standard" or
/// null throws like EPLAN's InvalidScheme, with S12's message. <see cref="NewIds"/> stands for EPLAN giving functions new
/// session identifiers. Records every call.
/// </summary>
internal sealed class FakeDeviceRenumberer : IDeviceRenumberer
{
    /// <summary>S12's message for the scheme "Default", in Chinese as EPLAN's user interface was.</summary>
    public const string InvalidSchemeMessage = "S029131配置无效。参数名称:'strSchemePerIdentifier'。参数值:'Default'。请使用下列配置中的其中一个:'Kennbuchstabe + Zähler Fluidtechnik, Kennbuchstabe + Zähler/Zubehör Fluidtechnik, Kennbuchstabe Zähler, Media_code, Standard'。";

    public List<string> Pages { get; } = [];

    public List<PlacedFunction> Functions { get; } = [];

    public Dictionary<string, string> Numbers { get; } = new(StringComparer.Ordinal);

    public bool NewIds { get; set; }

    public List<(IReadOnlyList<string>? Pages, bool OnlyUnnumbered, string? Scheme, int Start, int Step)> Renumbers { get; } = [];

    public IReadOnlyList<PlacedFunction>? ReadFunctions(string projectPath, string? page) =>
        page is null ? [.. Functions.Select(Copy)]
        : Pages.Contains(page) ? [.. Functions.Where(f => f.Page == page).Select(Copy)]
        : null;

    public void Renumber(string projectPath, IReadOnlyList<string>? pages, bool onlyUnnumbered, string? scheme, int start, int increment)
    {
        Renumbers.Add((pages, onlyUnnumbered, scheme, start, increment));
        if (scheme is not (null or "" or "Standard"))
        {
            throw NumberingSchemes.Unknown(scheme, InvalidSchemeMessage, new InvalidOperationException(InvalidSchemeMessage));
        }

        foreach (PlacedFunction function in Functions.Where(f => pages is null || pages.Contains(f.Page!)))
        {
            string device = DeviceRenumberRequest.DeviceOf(function.Name);
            if (Numbers.TryGetValue(device, out string? tag))
            {
                function.Name = string.Concat(tag, function.Name.AsSpan(device.Length));
            }

            if (NewIds)
            {
                function.Id += "'";
            }
        }
    }

    private static PlacedFunction Copy(PlacedFunction f) => new() { Name = f.Name, IsMain = f.IsMain, Page = f.Page, Id = f.Id };
}

/// <summary>
/// Pages of a <see cref="FakePageReader"/> renumbered the way S12 found <c>Renumber.Pages</c> per structure renumbers
/// them (api-notes A18): in the order given, from the start in steps, each in its own structure, keeping its session
/// identifier; EPLAN's check for a taken name is <see cref="Refuse"/>, otherwise a taken number gives two pages one name.
/// Records every call.
/// </summary>
internal sealed class FakePageRenumberer(FakePageReader pages) : IPageRenumberer
{
    private readonly Dictionary<PageSummary, string> _ids = [];

    public bool Refuse { get; set; }

    public List<(string[] Pages, int Start, int Step, bool KeepText, string Subpages)> Renumbers { get; } = [];

    public string? PageId(string projectPath, string pageName) =>
        pages.Pages.FirstOrDefault(p => p.Name == pageName) is { } page ? Id(page) : null;

    public IReadOnlyList<string> Renumber(string projectPath, IReadOnlyList<string> names, int start, int increment, bool keepText, string subpages)
    {
        Renumbers.Add(([.. names], start, increment, keepText, subpages));
        if (Refuse)
        {
            throw new BridgeErrorException(ErrorReason.AlreadyExists, "EPLAN: the page exists");
        }

        PageSummary[] targets = [.. names.Select(name => pages.Pages.First(p => p.Name == name))];
        for (int i = 0; i < targets.Length; i++)
        {
            Id(targets[i]);
            targets[i].Name = PageNames.StructureOf(targets[i].Name) + "/" + (start + (i * increment)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return [.. targets.Select(p => p.Name)];
    }

    private string Id(PageSummary page) => _ids.TryGetValue(page, out string? id) ? id : _ids[page] = "id " + page.Name;
}

/// <summary>A project's terminal strips as the EPLAN adapter reads them, in EPLAN's (unsorted) order; records every read.</summary>
internal sealed class FakeTerminalReader : ITerminalReader
{
    public List<TerminalStripListItem> Strips { get; } = [];

    public List<string> Reads { get; } = [];

    public IReadOnlyList<TerminalStripListItem> ReadStrips(string projectPath)
    {
        Reads.Add(projectPath);
        return Strips;
    }
}
