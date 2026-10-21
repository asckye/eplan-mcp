using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace EplanMcp.Contracts.Tests;

/// <summary>
/// The Host serializes contracts with System.Text.Json and the Bridge with Newtonsoft.Json (ADR-0003). Every DTO must
/// survive a trip through one serializer and back through the other, in both directions, with camelCase names.
/// </summary>
[Trait("Size", "Small")]
public sealed class SerializerCompatibilityTests
{
    private static readonly JsonSerializerOptions _stj = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerSettings _newtonsoft = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
    };

    public static TheoryData<object> Samples() =>
    [
        new HandshakeRequest { ProtocolVersion = 1, MinProtocolVersion = 1, HostVersion = "0.1.0" },
        new HandshakeResult
        {
            ProtocolVersion = 1,
            BridgeVersion = "0.1.0",
            Eplan = new EplanInstanceInfo { ProcessId = 4242, Version = "2026.0.3.25421", Variant = "Pro Panel", ApiExtensionLicence = true },
        },
        new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "0.1.0", Eplan = new EplanInstanceInfo { ProcessId = 1, Version = "x" } },
        new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "0.2.0", Eplan = new EplanInstanceInfo { ProcessId = 1 }, Operations = ["page.list", "project.list"] },
        new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "0.3.0", Eplan = new EplanInstanceInfo { ProcessId = 1 }, BuildTarget = ".NETFramework,Version=v4.8.1" },
        new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "0.4.0", Eplan = new EplanInstanceInfo { ProcessId = 1, Variant = "Electric P8", Offline = true } },
        new BridgeError
        {
            Reason = ErrorReason.EplanBusy,
            Message = "EPLAN is busy.",
            Retryable = true,
            Metadata = new Dictionary<string, string> { ["waitedSeconds"] = "10" },
        },
        new BridgeError { Reason = ErrorReason.Internal, Message = "m" },
        new BridgeError { Reason = ErrorReason.OutcomeUnknown, Message = "m", Execution = ExecutionState.Unknown },
        new OutcomeGetArgs { RequestId = "c0a8" },
        new OperationCancelArgs { RequestId = "c0a8" },
        new OperationCancelArgs(),
        new OperationCancelData { Found = true },
        new OperationCancelData(),
        new ProgressNotification { RequestId = "c0a8", Progress = 3, Total = 20, Message = "Exporting page 3 of 20" },
        new ProgressNotification { RequestId = "c0a8", Progress = 40 },
        new ApprovalAskArgs
        {
            RequestId = "c0a8",
            Fingerprint = "9f86",
            ArgumentsHash = "e3b0",
            DeadlineUnixMs = 1_792_483_200_000,
            Title = "批准 AI 修改 EPLAN 项目？",
            Summary = "eplan_page_create · Line1",
            Changes = ["create page =GAA+A1&EFS1/5", "update page =GAA+A1&EFS1/6: description '' -> '电源'"],
            Note = "Ctrl+Z",
            ApproveLabel = "批准",
            RejectLabel = "拒绝",
            CountdownFormat = "{0} 后自动拒绝",
        },
        new ApprovalAskData { RequestId = "c0a8", Fingerprint = "9f86", ArgumentsHash = "e3b0", Decision = ApprovalDecisions.Granted },
        new OutcomeGetData { Status = OutcomeStatus.Running, Operation = "page.create.execute" },
        new OutcomeGetData { Status = OutcomeStatus.RolledBack, Operation = "page.create.execute", Reason = ErrorReason.EplanOperationFailed },
        new WritePlan
        {
            Changes =
            [
                new PlannedChange { Action = "create", Kind = "page", Name = "=GAA+A1&EFS1/5", Property = null, NewValue = "电源" },
                new PlannedChange { Action = "update", Kind = "device", Name = "=GAA+A1-FC1", Property = "tag", OldValue = "=GAA+A1-FC1", NewValue = "=GAA+A1-QA1" },
            ],
            Fingerprint = "9f86d081884c7d65",
        },
        new WritePlan(),
        new PageCreateArgs
        {
            Project = "Line1",
            Pages = [new PageCreateItem { Name = "=GAA+A1&EFS1/5", PageType = "Overview", Description = "电源" }, new PageCreateItem { Name = "/6" }],
            Language = "zh_CN",
            Fingerprint = "9f86d081884c7d65",
        },
        new PageCreateArgs(),
        new PageCreateData { Project = "Line1", Pages = [new PageSummary { Name = "=GAA+A1&EFS1/5", Type = "Circuit", Description = "电源" }], Warning = "w" },
        new PageCreateData(),
        new PageUpdateArgs { Project = "Line1", Pages = [new PageUpdateItem { Name = "=GAA+A1&EFS1/3", Description = "主回路" }, new PageUpdateItem { Name = "=A/1" }], Language = "zh_CN", Fingerprint = "f" },
        new PageUpdateArgs(),
        new PageUpdateData
        {
            Project = "Line1",
            Language = "zh_CN",
            Pages = [new PageDescriptionChange { Name = "=GAA+A1&EFS1/3", Description = "主回路", Previous = "电源" }, new PageDescriptionChange { Name = "=A/1" }],
            Unchanged = 2,
            Warning = "w",
        },
        new PageUpdateData(),
        new WritePlan { Project = "Line1", Fingerprint = "f", Details = new Dictionary<string, string> { ["functions"] = "3", ["pages"] = "=F+A2&EFS1/1\n=K1+A2&EFS1/2" } },
        new DeviceUpdateArgs { Project = "Line1", Tag = "-KF1", NewTag = "-KF19", Fingerprint = "f" },
        new DeviceUpdateArgs(),
        new DeviceUpdateData { Project = "Line1", Tag = "=F+A2-KF19", PreviousTag = "=F+A2-KF1", VisibleTag = "-KF19", Functions = 3, Pages = ["=F+A2&EFS1/1"], Warning = "w" },
        new DeviceUpdateData(),
        new ConnectionGenerateArgs { Project = "Line1", Pages = ["=GAA+A1&EFS1/60", "=GAA+A1&EFS1/1"], Fingerprint = "f" },
        new ConnectionGenerateArgs(),
        new ConnectionGenerateData
        {
            Project = "Line1",
            Pages =
            [
                new ConnectionGeneratePage
                {
                    Page = "=GAA+A1&EFS1/60",
                    After = 9,
                    AddedCount = 9,
                    RemovedCount = 1,
                    Added = [new ChangedConnection { From = new ConnectionEnd { Device = "+A1-XG9:?1" }, To = new ConnectionEnd { Device = "=GAA+A1-?QA1", Pin = "14" } }],
                    Removed = [new ChangedConnection { From = new ConnectionEnd { Device = "-A", Pin = "1" } }],
                },
                new ConnectionGeneratePage { Page = "=GAA+A1&EFS1/1", Before = 40, After = 40 },
            ],
            Warning = "w",
        },
        new ConnectionGenerateData(),
        new MacroInsertArgs { Project = "Line1", Page = "=GAA+A1&EFS1/60", Macro = "EPLAN_Macro/宏.ema", X = 40.5, Y = 280, Variant = 2, Numbering = "keep", Fingerprint = "f" },
        new MacroInsertArgs(),
        new MacroInsertData
        {
            Project = "Line1",
            Page = "=GAA+A1&EFS1/60",
            Macro = "EPLAN_Macro/宏.ema",
            Devices = [new PageDevice { Tag = "=GAA+A1-?QA1", VisibleTag = "-?QA1", Category = "NoContact" }, new PageDevice { Tag = "+A1-WG7", Category = "Cable" }],
            DeviceCount = 8,
            ObjectCount = 28,
            PageSizeKnown = true,
            Warning = "w",
        },
        new MacroInsertData(),
        new CheckRunArgs { Project = "Line1", Pages = ["=GAA+A1&EFS1/1"], Scheme = "检查方案", Fingerprint = "f" },
        new CheckRunArgs(),
        new CheckRunData
        {
            Project = "Line1",
            PageCount = 321,
            Scheme = "检查方案",
            Before = 2,
            Messages = [new CheckMessage { Number = 4025, Severity = CheckMessage.Warning, Region = "Connections", Text = "端子 -X1 没有连接", Target = new CheckObject { Kind = "Terminal", Name = "=GAA+A1-X1:1" } }],
            Total = 20,
            Errors = 0,
            Warnings = 6,
            Hints = 14,
            Warning = "w",
        },
        new CheckRunData(),
        new ReportUpdateArgs { Project = "Line1", Pages = ["&AAB1/1"], Fingerprint = "f" },
        new ReportUpdateArgs(),
        new ReportUpdateData
        {
            Project = "Line1",
            Scope = ReportUpdateData.PagesScope,
            Before = 204,
            After = 205,
            AddedCount = 1,
            RemovedCount = 1,
            Added = ["+A2&EPA1/1.2"],
            Removed = ["&部件/1"],
            Warning = "w",
        },
        new ReportUpdateData(),
        new ExportArgs
        {
            Project = "Line1",
            Pages = ["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2"],
            Scheme = "EPLAN_default_value",
            Color = "black_white",
            Format = "jpg",
            WidthPx = 2000,
            Directory = @"C:\Users\u\EplanMcp\exports\Line1\20261021T083000Z-k3f9q2\",
            FileName = "电气图.pdf",
        },
        new ExportArgs(),
        new ExportPlanData { Project = "Line1", PageCount = 321, Scheme = "Standard Settings" },
        new ExportPlanData(),
        new ExportRunData { Project = "Line1", PageCount = 2, Files = [new ExportedPage { Page = "=GAA+A1&EFS1/60", Path = "=GAA+A1&EFS1_60.png" }], Warning = "w" },
        new ExportRunData(),
        new ProjectListArgs(),
        new ProjectListData
        {
            Projects =
            [
                new OpenProject { Name = "Line1", Path = @"D:\Projects\Line1", Current = true },
                new OpenProject { Name = "项目 二", Path = @"D:\项目\二", ReadOnly = true },
            ],
        },
        new ProjectListData(),
        new ListData<OpenProject> { Project = "项目 二", Items = [new OpenProject { Name = "Line1", Path = "p" }], Total = 7 },
        new ListData<OpenProject>(),
        new ProjectGetArgs { Project = @"D:\项目\二", Language = "zh_CN", IncludeProperties = true },
        new ProjectGetArgs(),
        new ProjectGetData
        {
            Project = new OpenProject { Name = "Line1", Path = @"D:\Projects\Line1", Current = true, ReadOnly = true },
            Pages = 321,
            Devices = 420,
            Connections = 2060,
            PartReferences = 2900,
            PagesByType = [new PageTypeCount { Type = "Circuit", Count = 140 }, new PageTypeCount { Type = "TitlePage", Count = 3 }],
            Properties =
            [
                new PropertyEntry { Id = 10000, Name = "项目名称", Value = "Line1" },
                new PropertyEntry { Id = 10901, Index = 2, Value = "多行\n文本" },
            ],
        },
        new ProjectGetData(),
        new PageListArgs { Project = "Line1", NamePrefix = "=GAA+A1&EFS1/", PageType = "Circuit", Text = "电源", Language = "zh_CN", Offset = 50, Limit = 50 },
        new PageListArgs(),
        new ListData<PageSummary>
        {
            Project = "Line1",
            Items =
            [
                new PageSummary { Name = "=GAA+A1&EFS1/1", Type = "Circuit", Description = "电源\n24 V" },
                new PageSummary { Name = "&AAA1/1", Type = "TitlePage" },
            ],
            Total = 140,
        },
        new PageGetArgs { Project = "Line1", Page = "=GAA+A1&EFS1/1", Language = "en_US", IncludeProperties = true },
        new PageGetArgs(),
        new PageGetData
        {
            Project = "项目 二",
            Page = new PageDetail { Name = "&AAA1/1", Type = "TitlePage", TypeName = "标题页/封页", Description = "封页" },
            Devices =
            [
                new PageDevice { Tag = "=GAA+A1-FC1", VisibleTag = "-FC1", Category = "CircuitBreaker" },
                new PageDevice { Tag = "=GAA+A1-TA1", Category = "Transformer" },
            ],
            Properties = [new PropertyEntry { Id = 11011, Name = "页描述", Value = "封页" }],
        },
        new PageGetData(),
        new DeviceListArgs { Project = "Line1", TagContains = "-K1", Page = "=A1&EFS1/1", Category = "Coil", HasPart = false, IncludeAuxiliary = true, Offset = 50, Limit = 50 },
        new DeviceListArgs(),
        new ListData<DeviceListItem>
        {
            Project = "项目 二",
            Items =
            [
                new DeviceListItem { Tag = "=GAA+A1-FC1", VisibleTag = "-FC1", Main = true, Category = "CircuitBreaker", Page = "=GAA+A1&EFS1/1", Parts = 2 },
                new DeviceListItem { Tag = "+A1-FC2:1", Category = "Terminal" },
            ],
            Total = 2876,
        },
        new DeviceGetArgs { Project = @"D:\Projects\Line1", Tag = "-KF1", Language = "zh_CN", Detailed = true },
        new DeviceGetArgs(),
        new DeviceGetData
        {
            Project = "Line1",
            Device = new DeviceFunction { Tag = "=F+A2-KF1", VisibleTag = "-KF1", Category = "Coil", Page = "=F+A2&EFS1/1", Text = "功能文本", Pins = ["A1", "A2"] },
            Functions = [new DeviceFunction { Tag = "=F+A2-KF1", Category = "NoContact", Pins = ["13", "14"] }],
            FunctionCount = 3,
            Parts = [new DevicePart { PartNumber = "SIE.3RT2016-1BB41", Variant = "1", Count = 2, Description = "接触器", Manufacturer = "SIE" }, new DevicePart()],
            Properties = [new PropertyEntry { Id = 20011, Name = "Function text", Value = "1,5" }, new PropertyEntry { Name = "UserProp", Value = "x" }],
        },
        new DeviceGetData(),
        new ConnectionListArgs { Project = "Line1", Device = "-FC1", Page = "=GAA+A1&EFS1/1", Offset = 50, Limit = 50 },
        new ConnectionListArgs(),
        new ListData<ConnectionListItem>
        {
            Project = "项目 二",
            Items =
            [
                new ConnectionListItem
                {
                    From = new ConnectionEnd { Device = "=GAA+A1-FC1", Pin = "2" },
                    To = new ConnectionEnd { Device = "=GAA+A1-XD1" },
                    Page = "=GAA+A1&EFS1/1",
                    CrossSection = "1,5",
                    Colour = "GY",
                },
                new ConnectionListItem { Kind = ConnectionListItem.Kind3D, Colour = "绿色/黄色" },
            ],
            Total = 40,
        },
        new CableGetArgs { Project = "Line1", Cable = "-WG2", Language = "zh_CN", Offset = 100, Limit = 100 },
        new CableGetArgs(),
        new CableGetData
        {
            Project = "项目 二",
            Cable = new CableInfo
            {
                Tag = "+B1-WG2",
                VisibleTag = "-WG2",
                Page = "=F+A2&EFS1/2",
                Type = "ÖLFLEX CLASSIC 110",
                WireCount = "3",
                CrossSection = "1,5",
                Length = "5 m",
                Sources = ["=F+B1-SG1"],
                Targets = ["+A2-XG4:2"],
                Parts = ["LAPP.00101264"],
            },
            Cores = [new ConnectionListItem { From = new ConnectionEnd { Device = "=F+B1-SG1", Pin = "1" }, Colour = "BK", Designation = "433" }],
            Total = 2,
        },
        new CableGetData(),
        new TerminalStripListArgs { Project = "Line1", NameContains = "+A1", Offset = 50, Limit = 50 },
        new TerminalStripListArgs(),
        new ListData<TerminalStripListItem>
        {
            Project = "项目 二",
            Items = [new TerminalStripListItem { Name = "+A2-XD2", Terminals = 5, Bridges = 3 }, new TerminalStripListItem()],
            Total = 45,
        },
        new TerminalStripGetArgs { Project = "Line1", Strip = "-XD2", Offset = 100, Limit = 100 },
        new TerminalStripGetArgs(),
        new TerminalStripGetData
        {
            Project = "项目 二",
            Strip = new TerminalStripListItem { Name = "+A2-XD2", Terminals = 5, Bridges = 3 },
            Terminals =
            [
                new TerminalItem
                {
                    Name = "+A2-XD2:1",
                    Designation = "1",
                    Main = true,
                    Page = "=EA+A2&EFS1/1",
                    Inside = [new ConnectionEnd { Device = "=EA+A2-QA1", Pin = "2" }],
                    Outside = [new ConnectionEnd { Device = "+B1-WG2" }],
                    BridgedWith = ["+A2-XD2:2"],
                    Plc = "=K1+A2-KE1",
                },
                new TerminalItem(),
            ],
            Total = 5,
        },
        new TerminalStripGetData(),
        new PlcGetArgs { Project = "Line1", Plc = "-KE1", Language = "zh_CN", Offset = 100, Limit = 100 },
        new PlcGetArgs(),
        new PlcGetData
        {
            Project = "项目 二",
            Plc = new PlcInfo { Tag = "=K1+A2-KE1", VisibleTag = "-KE1", Page = "=K1+A2&EFA1/1", Parts = ["PXC.2703994"], Station = "站 1", Rack = "0", Slot = "2", Cpu = true },
            Channels =
            [
                new PlcChannel
                {
                    Name = "=K1+A2-KE1:2.3",
                    Page = "=K1+A2&EFS1/3",
                    Main = true,
                    Address = "I0.3",
                    SymbolicAddress = "启动",
                    DataType = "BOOL",
                    Text = "按钮",
                    TextLanguage = "de_DE",
                    Connected = [new ConnectionEnd { Device = "=K1+A2-SF1", Pin = "13" }],
                },
                new PlcChannel(),
            ],
            Total = 34,
            Connected = 20,
        },
        new PlcGetData(),
        new PartListArgs { Project = "Line1", Manufacturer = "SIE", Text = "接触器", Language = "zh_CN", Offset = 50, Limit = 50 },
        new PartListArgs(),
        new ListData<PartListItem>
        {
            Project = "项目 二",
            Items =
            [
                new PartListItem { PartNumber = "SIE.3RT2016-1BB41", Variant = "1", Quantity = 4, Devices = 3, Description = "接触器", Manufacturer = "SIE" },
                new PartListItem { PartNumber = "14AWG-BLK", Quantity = 120, Connections = 120 },
            ],
            Total = 293,
        },
        new PartSearchArgs { Query = "3RH21", Manufacturer = "SIE", Language = "zh_CN", Offset = 20, Limit = 20 },
        new PartSearchArgs(),
        new PartSearchData
        {
            Database = @"$(MD_PARTS)\EPLAN_parts.alk",
            Items =
            [
                new PartSearchItem
                {
                    PartNumber = "SIE.3RH2122-1HB40",
                    Variant = "1",
                    Manufacturer = "SIE",
                    TypeNumber = "3RH2122-1HB40",
                    OrderNumber = "3RH21221HB40",
                    ProductGroup = "ElectricalRelay",
                    Description = "辅助接触器",
                    DescriptionLanguage = "de_DE",
                    HasMacro = true,
                },
                new PartSearchItem(),
            ],
            Total = 464,
        },
        new PartSearchData(),
        new MessageListArgs { MinLevel = SystemMessage.Error, Offset = 20, Limit = 20 },
        new MessageListArgs(),
        new MessageListData
        {
            Items =
            [
                new SystemMessage { Level = SystemMessage.Error, Number = "S085016", Text = "文件 D:\\项目\\a.elk 未找到\n第二行", Occurrences = 2 },
                new SystemMessage { Level = SystemMessage.Fatal, Text = "x" },
            ],
            Total = 45,
        },
        new MessageListData(),
        new CheckListArgs { Project = "Line1", Severity = CheckMessage.Warning, IncludeDone = true, Offset = 50, Limit = 50 },
        new CheckListArgs(),
        new CheckListData
        {
            Project = "项目 二",
            Items =
            [
                new CheckMessage
                {
                    Number = 4025,
                    Severity = CheckMessage.Warning,
                    Region = "Connections",
                    Text = "端子 =GAA+A1-X1:1 没有连接",
                    Done = true,
                    Target = new CheckObject { Kind = "Terminal", Name = "=GAA+A1-X1:1" },
                },
                new CheckMessage { Number = 13001, Severity = CheckMessage.Hint, Region = "PartsData", Text = "h", Target = new CheckObject { Kind = "Connection" } },
                new CheckMessage { Number = 1, Text = "u" },
            ],
            Total = 20,
            Errors = 1,
            Warnings = 6,
            Hints = 14,
            Done = 3,
        },
        new CheckListData(),
        new PageDeleteArgs
        {
            Project = "Line1",
            Pages = ["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2"],
            Fingerprint = "f",
            Backup = new BackupTarget { Path = @"C:\Users\u\EplanMcp\backups\Line1\20261021T083000123Z\Line1.elk", MaxSeconds = 31 },
        },
        new PageDeleteArgs(),
        new PageDeleteData
        {
            Project = "项目 二",
            Pages = [new DeletedPage { Name = "=GAA+A1&EFS1/1", Type = "Circuit", Functions = 59 }],
            Orphaned = [new OrphanedDevice { Tag = "+A1-WE1", FunctionsLeft = 6, Pages = ["=GAA+A1&EFS1/2"] }],
            OrphanedCount = 4,
            Warning = "w",
        },
        new PageDeleteData(),
        new DeviceRenumberArgs { Project = "Line1", Pages = ["=GAA+A1&EFS1/90"], OnlyUnnumbered = false, Scheme = "Standard", Start = 10, Step = 5, Fingerprint = "f", Backup = new BackupTarget { Path = "p", MaxSeconds = 150 } },
        new DeviceRenumberArgs(),
        new DeviceRenumberData
        {
            Project = "项目 二",
            Changed = [new RenamedObject { From = "+A1-?XG1", To = "+A1-XG2" }],
            ChangedCount = 4,
            StillUnnumbered = ["=GAA+A1-?QA1"],
            StillUnnumberedCount = 1,
            Warning = "w",
        },
        new DeviceRenumberData(),
        new PageRenumberArgs { Project = "Line1", Pages = ["=GAA+A1&EFS1/90"], Start = 200, Step = 10, KeepText = true, Subpages = "to_main", Fingerprint = "f", Backup = new BackupTarget { Path = "p", MaxSeconds = 31 } },
        new PageRenumberArgs(),
        new PageRenumberData { Project = "项目 二", Changed = [new RenamedObject { From = "=GAA+A1&EFS1/90", To = "=GAA+A1&EFS1/200" }], Unchanged = 1, Warning = "w" },
        new PageRenumberData(),
    ];

    [Theory]
    [MemberData(nameof(Samples))]
    public void SystemTextJsonToNewtonsoft_RoundTrips(object sample)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(sample, sample.GetType(), _stj);
        object? back = JsonConvert.DeserializeObject(json, sample.GetType(), _newtonsoft);

        Assert.Equal(Canonical(sample), Canonical(back!));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void NewtonsoftToSystemTextJson_RoundTrips(object sample)
    {
        string json = JsonConvert.SerializeObject(sample, _newtonsoft);
        object? back = System.Text.Json.JsonSerializer.Deserialize(json, sample.GetType(), _stj);

        Assert.Equal(Canonical(sample), Canonical(back!));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void BothSerializers_ProduceTheSamePropertyNames(object sample)
    {
        using JsonDocument fromStj = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(sample, sample.GetType(), _stj));
        using JsonDocument fromNewtonsoft = JsonDocument.Parse(JsonConvert.SerializeObject(sample, _newtonsoft));

        Assert.Equal(Names(fromStj.RootElement), Names(fromNewtonsoft.RootElement));
    }

    [Fact]
    public void HandshakeResult_UsesCamelCaseOnTheWire()
    {
        string json = JsonConvert.SerializeObject(new HandshakeResult { Eplan = new EplanInstanceInfo { ProcessId = 7 } }, _newtonsoft);

        Assert.Contains("\"protocolVersion\":", json, StringComparison.Ordinal);
        Assert.Contains("\"eplan\":{\"processId\":7", json, StringComparison.Ordinal);
    }

    [Fact]
    public void HandshakeResult_FromAnAddInWithoutTheOfflineField_ReadsAsNotOffline()
    {
        HandshakeResult? result = System.Text.Json.JsonSerializer.Deserialize<HandshakeResult>("""{"protocolVersion":1,"eplan":{"processId":7}}""", _stj);

        Assert.False(result!.Eplan.Offline);
        Assert.Contains("\"offline\":true", JsonConvert.SerializeObject(new EplanInstanceInfo { Offline = true }, _newtonsoft), StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressNotification_HasTheNamesOfM1sReservedShape()
    {
        // M1 design 4.2 reserved {id, progress, total?, message?}; M4 design 4.3 keys it by the request ID instead.
        string json = JsonConvert.SerializeObject(new ProgressNotification { RequestId = "r", Progress = 1, Total = 2, Message = "m" }, _newtonsoft);

        Assert.Equal("""{"requestId":"r","progress":1,"total":2,"message":"m"}""", json);
    }

    /// <summary>A serializer-independent rendering used for equality: STJ with fixed options.</summary>
    private static string Canonical(object value) => System.Text.Json.JsonSerializer.Serialize(value, value.GetType(), _stj);

    private static List<string> Names(JsonElement element, string prefix = "")
    {
        var names = new List<string>();
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (System.Text.Json.JsonProperty property in element.EnumerateObject())
            {
                names.Add(prefix + property.Name);
                names.AddRange(Names(property.Value, prefix + property.Name + "."));
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                names.AddRange(Names(item, prefix + "[]."));
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }
}
