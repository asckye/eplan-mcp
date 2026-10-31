using System.Globalization;
using System.Text;
using EplanMcp.Coverage.Mapping;
using EplanMcp.Coverage.Scanning;

namespace EplanMcp.Coverage.Reporting;

/// <summary>
/// docs/eplan/coverage-report.md: <see cref="CoverageReport"/> for people, in Chinese like the other docs. Every
/// percentage is written with its numerator and denominator, and every denominator with what it leaves out.
/// </summary>
internal static class CoverageMarkdown
{
    public const string RelativePath = "docs/eplan/coverage-report.md";

    private static readonly Dictionary<string, string> _layerNames = new(StringComparer.Ordinal)
    {
        ["operation"] = "操作层（重载组，主分母）",
        ["operation.method"] = "　其中方法",
        ["operation.property"] = "　其中对象属性",
        ["data_dictionary"] = "数据字典（对象类型）",
        ["action"] = "action（按行）",
        ["action.type"] = "　其中带 TYPE 的行",
    };

    public static string Render(string repositoryRoot)
    {
        CoverageReport report = CoverageReport.Build(repositoryRoot);
        ToolMapping mapping = ToolMapping.Build(repositoryRoot);
        BridgeUsage usage = CoverageJson.Read<BridgeUsage>(Path.Combine(repositoryRoot, BridgeUsage.RelativePath));
        var md = new StringBuilder();
        void Line(string text = "") => md.Append(text).Append('\n');

        Line("# EPLAN API 覆盖报告");
        Line();
        if (CoverageCheck.IsStale(repositoryRoot, usage))
        {
            Line($"> **Bridge 扫描已过期**：`{BridgeUsage.RelativePath}` 是 {usage.ScannedOn} 按 Bridge 源码 {usage.ScannedSources} 扫描的，之后 `src/EplanMcp.Bridge.Eplan`、`src/EplanMcp.Bridge.Core` 或 `OperationNames` 有了改动。下面与 Bridge 有关的数字可能已经不准；在装有 EPLAN 的机器上（EPLAN 不必运行）运行 `{CoverageCheck.ScanCommand}`，再提交更新后的文件。");
            Line();
        }

        Line("> 由 `dotnet run --project eng/coverage -- generate` 从仓库中的文件生成，不要手工修改；CI 用 `check` 核对它与输入一致。机器可读的同一份数据在 `catalog/coverage.json`。说明见 [catalog/README.md](../../catalog/README.md)。");
        Line($"> 输入：目录 `{report.Catalog}/`（SDK 部分{(report.SdkCatalog ? "已提交" : "**尚未提交**")}，action 部分{(report.ActionCatalog ? "已提交" : "**尚未提交**")}）；`catalog/mapping/bridge-usage.json`（{usage.ScannedOn} 在 EPLAN {usage.EplanApi} 的程序集上扫描，Bridge 源码 {usage.ScannedSources}）；`catalog/mapping/tools.json`；`catalog/mapping/overlay.yaml`。");
        Line();
        Line("## 口径");
        Line("- C 级方法的逐签名选择、读写分类和排除理由见 [生成清单](../../catalog/api-try.g.tsv)；未实机验证，不计为 live_verified。");
        if (File.Exists(Path.Combine(repositoryRoot, report.Catalog, "summary.json")))
        {
            string[][] trials = [.. ApiTryGenerator.Report(repositoryRoot).Split('\n').Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t'))];
            Line("- C 级方法选择：" + string.Join("，", trials.Where(r => r[2] != "excluded").GroupBy(r => r[2]).Select(g => g.Key + " " + g.Select(r => r[0]).Distinct(StringComparer.Ordinal).Count().ToString(CultureInfo.InvariantCulture) + " 组"))
                + "；排除（按签名）：" + string.Join("，", trials.Where(r => r[2] == "excluded").GroupBy(r => r[3]).OrderByDescending(g => g.Count()).Select(g => g.Key + " " + g.Count().ToString(CultureInfo.InvariantCulture))) + "。");
        }
        Line("- 计数单位（[设计文档](../design/2026-10-21-api-capability-coverage.md)第 3 节，负责人 2026-10-21 的决定 Q1–Q3）：操作层按重载组，同一类型中同名的方法或对象属性算一组，对象属性计入操作层（Q1），另给出只算方法的数字；数据字典按对象类型，Bridge 用到它的某个属性 ID 或它的 `<对象>PropertyList` 类型就算这个对象类型已实现；action 按行，带 TYPE 的行另算。类型、构造函数、`Finalize`/`Dispose`、字段、枚举值只作为依赖计数，不算覆盖率。");
        Line("- 状态（设计文档第 4 节），每一级都包含更高的级别：implemented 表示 Bridge 中有代码到达它，经 MCP 工具，或只经插件的基础设施（启动、调度、授权检查）；automated_test 表示到达它的某个工具有 Small 或 Medium 测试，或它的 Bridge 操作有 Bridge.Core 测试（这些测试用假的 EPLAN，不执行 EPLAN 成员）；live_verified 表示这个工具还在 overlay 中有写明工具名、结果通过的实机记录（api-notes、规格或 Large 测试 README，带日期和版本）。另有 env_blocked（overlay 标为被环境挡住且没有实现）、excluded（目录的机械规则或 overlay 排除，不进入分母）、部分重载（重载组中只用到一部分重载）和无文档（不在 IntelliSense 中，仍在分母里，Q2）。");
        Line("- 工具级的证据代表工具到达的全部单元，所以 automated_test 和 live_verified 是上限：一次实机运行不一定走到工具的每个分支。到达关系也偏向\"可能到达\"：扫描判断不了的分支都算到达。");
        Line();

        Line("## 1. Bridge 用到的 EPLAN API");
        int reached = usage.Usages.Count - mapping.Infrastructure.Count - mapping.UnreachedGenerated.Count;
        string generated = mapping.UnreachedGenerated.Count == 0 ? string.Empty : $"，{mapping.UnreachedGenerated.Count} 个只出现在没有操作到达的生成代码中（不计入覆盖，设计文档第 8 节）";
        Line($"Bridge 共用到 {usage.Usages.Count} 个 ID（{Kinds(usage.Usages.Keys)}），其中 {reached} 个能被某个 Bridge 操作到达，{mapping.Infrastructure.Count} 个只属于基础设施{generated}。按命名空间：");
        Line();
        Line("| 命名空间 | ID 数 |");
        Line("|---|---:|");
        foreach (IGrouping<string, string> ns in usage.Usages.Keys.GroupBy(Namespace).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Line($"| `{ns.Key}` | {ns.Count()} |");
        }

        Line();
        Line("## 2. 登记与实现覆盖");
        if (!report.SdkCatalog)
        {
            Line($"目录的 SDK 部分（`{report.Catalog}/types.jsonl`、`members/`、`data-dictionary.jsonl`）尚未提交，所以操作层、数据字典和依赖层还不能计算；提交后重新运行 `generate`，本节和第 3 节会自动填上。");
            Line();
        }

        if (!report.ActionCatalog)
        {
            Line($"目录的 action 部分（`{report.Catalog}/actions.json`）尚未提交，所以 action 和 TYPE 分支还不能计算。");
            Line();
        }

        if (report.SdkCatalog || report.ActionCatalog)
        {
            Line("| 层 | 登记 | 排除（原因：数量） | 分母 | implemented | 其中经工具 | 其中部分重载 | automated_test | live_verified | env_blocked | 无文档 |");
            Line("|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach ((string layer, LayerCount c) in report.Layers.Where(l => l.Value.Registered > 0).OrderBy(l => Array.IndexOf(_layerNames.Keys.ToArray(), l.Key)))
            {
                string excluded = c.Excluded.Count == 0 ? "—" : string.Join("，", c.Excluded.Select(e => $"{e.Key}：{e.Value}"));
                Line($"| {_layerNames.GetValueOrDefault(layer, layer)} | {c.Registered} | {excluded} | {c.Denominator} | {Pct(c.Implemented, c.Denominator)} | {c.ByTools} | {c.Partial} | {Pct(c.AutomatedTest, c.Denominator)} | {Pct(c.LiveVerified, c.Denominator)} | {c.EnvBlocked} | {c.Undocumented} |");
            }

            Line();
            Line("排除原因中带 `overlay:` 的来自 overlay.yaml（第 7 节），其余是目录的机械规则（设计文档第 3.3 节）。");
        }

        if (report.SdkCatalog)
        {
            LayerCount op = report.Layers["operation"], methods = report.Layers["operation.method"];
            int Mechanical(LayerCount c) => c.Denominator + c.Excluded.Where(e => e.Key.StartsWith("overlay:", StringComparison.Ordinal)).Sum(e => e.Value);
            int others = op.Registered - methods.Registered - report.Layers["operation.property"].Registered;
            Line($"- 主分母的推导：登记 {op.Registered} 组（其中 {others} 组运算符和委托成员不在方法、属性两行中，已全部被机械规则排除），目录的机械规则排除后 {Mechanical(op)} 组，再按 overlay 排除后 {op.Denominator} 组。按机械排除后的 {Mechanical(op)} 组计，implemented 为 {Pct(op.Implemented, Mechanical(op))}。");
            Line($"- 只算方法：机械排除后 {Mechanical(methods)} 组，overlay 排除后 {methods.Denominator} 组，implemented {Pct(methods.Implemented, methods.Denominator)}，live_verified {Pct(methods.LiveVerified, methods.Denominator)}。");
            Line($"- 数据字典的属性 ID（不进分母，只作参考）：Bridge 用到 {report.DictionaryProperties.Used} 个，未过时的共 {report.DictionaryProperties.Registered} 个。");
            Line($"- 依赖层：Bridge 用到 {report.Dependencies.Used} 个类型和类型层成员，目录登记 {report.Dependencies.Registered} 个（只计数，不算覆盖率）。");
        }

        if (report.ActionCatalog)
        {
            Line($"- action 名称：Bridge 用到 {report.ActionNames.Used} 个，没有整个排除的 action 共 {report.ActionNames.Registered} 个。");
        }

        Line();
        if (report.Unmatched.Count > 0)
        {
            Line($"Bridge 用到、目录中却没有的 ID（{report.Unmatched.Count} 个）：{string.Join("、", report.Unmatched.Select(id => $"`{id}`"))}。");
            Line();
        }

        Line("## 3. 按命名空间（操作层）与按对象类型（数据字典）");
        if (!report.SdkCatalog)
        {
            Line("等目录的 SDK 部分提交（见第 2 节）。");
        }
        else
        {
            Line("| 命名空间（overlay 排除后） | 分母 | implemented | live_verified |");
            Line("|---|---:|---:|---:|");
            report.OperationsByNamespace.ToList().ForEach(s => Line($"| `{s.Scope}` | {s.Denominator} | {Pct(s.Implemented, s.Denominator)} | {s.LiveVerified} |"));
            Line();
            UnitCoverage[] objects = [.. report.Units.Where(u => u.Layer == ApiCatalog.DataDictionary)];
            Line($"数据字典中已实现的对象类型（{objects.Length} 个）：{(objects.Length == 0 ? "无" : string.Join("、", objects.Select(u => $"{u.Unit[2..]}（{u.Status}）")))}。");
            Line();
            Line($"无文档的重载组（不在 IntelliSense 中，仍在分母里，Q2）：共 {report.UndocumentedByNamespace.Sum(s => s.Denominator)} 组，按命名空间：");
            Line();
            Line("| 命名空间 | 无文档的组 | implemented |");
            Line("|---|---:|---:|");
            report.UndocumentedByNamespace.ToList().ForEach(s => Line($"| `{s.Scope}` | {s.Denominator} | {s.Implemented} |"));
        }

        Line();
        Line("## 4. 实机验证");
        ToolEntry[] verified = [.. mapping.Tools.Where(t => t.Live.Any(r => r.Verifies))];
        Line($"有实机记录（来源写明工具名、结果为 pass 或 pass_with_findings）的工具：{Pct(verified.Length, mapping.Tools.Count)}，共 {verified.Sum(t => t.Live.Count(r => r.Verifies))} 条记录。版本和变体按来源原样填写，来源没写的标\"未注明\"。");
        Line();
        Line("| 工具 | 日期 | EPLAN | 变体 | 结果 | 来源 | 说明 |");
        Line("|---|---|---|---|---|---|---|");
        foreach ((ToolEntry tool, LiveRecord r) in mapping.Tools.SelectMany(t => t.Live.Select(r => (t, r))))
        {
            string result = r.Basis == "inferred" ? r.Result + "（推断，不计入）" : r.Result;
            Line($"| {tool.Name} | {r.Date} | {r.Eplan ?? "未注明"} | {r.Variant ?? "未注明"} | {result} | {r.Source} | {Cell(r.Note)} |");
        }

        Line();
        Line("## 5. 工程任务视图（按工具）");
        Line("API ID 是工具经 Bridge 操作到达的全部 ID；操作层、数据字典、action 列是对应的目录单元数（目录未提交时为 0）；测试是提到这个工具的测试类数。");
        Line();
        Line("| 工具 | 级别 | Bridge 操作 | API ID | 操作层 | 数据字典 | 依赖 | action | 测试 S/M/L | api-notes | 最近一次实机 |");
        Line("|---|---|---|---:|---:|---:|---:|---:|---|---|---|");
        foreach (ToolCoverage t in report.Tools)
        {
            string live = t.LatestLive is { } r ? $"{r.Date} {r.Result}（{r.Source}）" : "—";
            string note = mapping.Tools.Single(m => m.Name == t.Name).Note is { } n ? $"（{n}）" : "";
            Line($"| {t.Name} | {t.Safety} | {(t.Operations.Count == 0 ? "—" + note : string.Join("<br>", t.Operations))} | {t.ApiIds} | {t.OperationGroups} | {t.DataDictionary} | {t.Dependencies} | {t.Actions} | {t.Small}/{t.Medium}/{t.Large} | {(t.ApiNotes.Count == 0 ? "—" : string.Join(" ", t.ApiNotes))} | {live} |");
        }

        Line();
        Line("## 6. 按版本、模块、授权的可用性");
        Line("| 范围 | 状态 | 说明 | 来源 |");
        Line("|---|---|---|---|");
        report.Availability.ToList().ForEach(a => Line($"| {a.Scope} | {a.Status} | {Cell(a.Note)} | {a.Source} |"));
        Line();
        Line("## 7. 不能从代码推导、来自 overlay 的规则");
        Line("| 匹配 | 状态 | 原因、条件或说明 | 来源 |");
        Line("|---|---|---|---|");
        report.OverlayRules.ToList().ForEach(r => Line($"| `{r.Match}` | {r.Status} | {Cell(r.Reason ?? r.Condition ?? r.Note)} | {r.Source} |"));
        return md.ToString();
    }

    private static string Pct(int numerator, int denominator) => denominator == 0
        ? $"{numerator} / 0"
        : string.Create(CultureInfo.InvariantCulture, $"{numerator} / {denominator}（{100.0 * numerator / denominator:0.0}%）");

    private static string Cell(string? text) => string.IsNullOrEmpty(text) ? "—" : text.Replace("|", "\\|", StringComparison.Ordinal);

    private static string Kinds(IEnumerable<string> ids) => string.Join("，", ids.GroupBy(id => id[0]).OrderBy(g => "MPETFA".IndexOf(g.Key, StringComparison.Ordinal))
        .Select(g => $"{g.Key switch { 'M' => "方法", 'P' => "属性", 'T' => "类型", 'F' => "字段和枚举值", 'E' => "事件", _ => "action" }} {g.Count()}"));

    /// <summary>
    /// The namespace of an ID: Eplan.EplApi.&lt;Area&gt;, plus a fourth part when it is one of the API's sub-namespaces
    /// (a doc-comment ID does not mark where the namespace ends).
    /// </summary>
    private static string Namespace(string id)
    {
        string[] parts = id[2..].Split('(')[0].Split('.');
        return id.StartsWith("A:", StringComparison.Ordinal) ? "（action）"
            : parts.Length > 4 && _subNamespaces.Contains(parts[3]) ? string.Join('.', parts.Take(4))
            : string.Join('.', parts.Take(3));
    }

    private static readonly HashSet<string> _subNamespaces = new(StringComparer.Ordinal)
    {
        "E3D", "EObjects", "Exceptions", "Ged", "Graphics", "Helpers", "Internal", "MasterData", "Planning", "Revisions", "Topology",
    };
}
