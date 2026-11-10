# 工具规格：`eplan_action_catalog_search`

| 项 | 内容 |
|---|---|
| 分组 / Profile | action_catalog / full |
| 级别 | Read |
| 状态 | Implemented (v0.4.1) |
| MCP 注解 | readOnly=true、destructive=false、idempotent=true、openWorld=false |

用于发现官方 action 用法、本机界面定义与注册表条目；目录不授予执行权限，不调用 EPLAN 或 Bridge。

输入：`text`（名称或界面文字包含，忽略大小写，1–500 字符）、`tier`（A / C-candidate / excluded）、`source`（official / ui / registry）、`registry_kind`（action / interaction / asm_action），过滤取交集；`limit` 默认 20、范围 1–100；`cursor` 使用返回的 PageCursor。

输出：`catalogVersion`、`installationVersion`、`gaps`、`items`、`total`、`nextCursor`、`truncated`、`hint`。每项包含名称、来源、TYPE、位置、界面文字、已知参数、级别、排除原因、白名单条目及验证引用、来源引用，以及注册类别 `registryKind` 和所属模块 `module`。同名已有 official 或 ui 行时只补注册元数据，不另加 registry 行；官方 TYPE 行保持独立。`total` 统计过滤后的定义行数，不是去重名称数，总数以本机安装注册表为准。

官方数据嵌入 Host，来源为 actions.json 与 api-actions 表的一一对应行。本机目录沿用 Host 安装发现得到的 Bin，扫描相邻 Cfg/Bin 内 MFTools、Ribbon、Menu XML，并单独读取 `Cfg/regPlatform.xml`、`Cfg/regMinimal.xml` 的 `Settings/CAT/MOD/LEV1[@name='Services']/LEV2/LEV3`。只收录 Actions、Interactions、AsmActions 三类；不读取 Classes、Dialogs 等其他类别，也不读取 MFTools 的 Dialogs、Shortcuts 区段。`module` 取条目各 LEV4 中第一个非空 ModuleName。

Interactions 的 `tier` 固定为 `excluded`，`excludedReason` 为 `interaction: not callable by name; use a drawing tool`。其他条目沿用原分级：A 仅匹配已发布白名单的 action + TYPE，C-candidate 未验证；ActionTryPolicy 和安全拒绝名单不变。

多安装无法选择、文件缺失或不可读、损坏、未知结构、版本不一致、未存储界面文字均返回 `gaps`；注册表文件缺失不算错误。不推断资源 ID 对应的文字。按 [ADR-0030](../adr/0030-action-catalog-tiers.md)，本机文件及完整名单不进仓库，也不分发。XML 禁止 DTD 和外部实体，并限制文件数、深度及大小。

`INVALID_ARGUMENT`：一次报告全部过滤、分页错误；移除旧游标重新查询。结果预算沿用 API 目录分页，数据清洗后返回。

测试：Small/Medium 用虚构名称的最小 XML 验证三类解析、其他类别忽略、同名合并、缺失文件缺口、interaction 排除、过滤和游标；Host 协议测试检查注册与只读注解。Large 保留 UI action 超过 900 和白名单断言，另断言 registry 行数超过 1300、按名称找到 AfGetSettingAction、XCabIaChangeAngle 为 excluded；只读且不执行 action，本批未运行。评测场景：查询图层导出的 TYPE 和白名单证据后结束，不执行 action。
