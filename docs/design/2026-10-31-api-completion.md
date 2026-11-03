# 设计文档：EPLAN API 补齐（ADR-0032 实施计划）

| 项 | 内容 |
|---|---|
| 状态 | Approved（项目负责人 2026-10-30 接受 ADR-0032，并决定推迟 v0.5.0、先补齐 API） |
| 作者 | 维护者 |
| 相关 ADR | [ADR-0032](../adr/0032-generic-api-layer.md)、[ADR-0030](../adr/0030-action-catalog-tiers.md)、[ADR-0021](../adr/0021-action-run-whitelist.md)、[ADR-0005](../adr/0005-safety-model.md) |
| 最后更新 | 2026-11-01 |

## 1. 背景

2026-10-30 的客户页验收预演在 scratch 上全部阶段通过，但与原图仍有 6 处差异，负责人要求"和原版一模一样"，决定推迟 v0.5.0，先把 EPLAN API 相关工具全部做完，再重画客户页。

[覆盖报告](../eplan/coverage-report.md)（2026-11-01）的现状：操作层 4014 组中实现 1010 组（25.2%）、实机验证 171 组（4.3%）；对象类型 68 种中实现 63 种（92.6%）；官方 action 137 行中实现 7 行（5.1%）；界面 action 约 1050 个。报告已标注 Bridge 扫描过期，数字随生成报告更新。负责人 2026-10-30 提供的 EPLAN 功能区截图（开始、插入、编辑、视图、设备、连接、工具、预规划、主数据）中约 150 个按钮，能用约 45 个，部分能用约 15 个，其余约 90 个没有工具。

## 2. 目标与非目标
- **目标**
  - 覆盖报告四个分母（操作层、对象类型、官方 action、界面 action）中，每一项都至少可查（B 级），能在 scratch 中经审批执行（C 级），或有明确的排除理由。
  - 截图中每个功能区按钮都有对应的工具、C 级条目或排除理由，并按本计划第 4 节逐批变为 A 级。
  - 客户验收页的 6 处差异全部由工具解决，重画结果与原图一致（第 4.5 节）。
  - 每一批都有 Small/Medium 测试与 scratch 上的 Large 测试，批量验证结果写回覆盖报告。
- **非目标**
  - 不为每个重载组单独注册 MCP 工具（发现策略见[手册 MCP-34](../standards/engineering-handbook.html#mcp-catalog)）。
  - 不提供基于字符串的任意反射调用（ADR-0032）。
  - 不在 scratch 之外执行未验证能力；不改变已发布工具的契约。
  - 不分发 EPLAN 安装文件或整理出的界面 action 清单（ADR-0030）。

## 3. 方案概述

```mermaid
flowchart LR
  subgraph B[B 级：可查]
    API[API 目录<br/>4649 组]
    OBJ[对象类型通用读<br/>68 种]
    ACT[action 目录<br/>官方 + 界面]
  end
  subgraph C[C 级：scratch 中审批后可试]
    TRY[方法组 / action 试执行<br/>快照对比 + 审计]
  end
  subgraph V[批量验证]
    HARN[维护者工具<br/>scratch 副本逐组试跑]
  end
  subgraph A[A 级：已验证]
    TYPED[专用工具 / 白名单条目]
  end
  B --> C --> V --> A
  V -->|live_verified| REPORT[覆盖报告]
```

## 4. 分批计划

每批独立推送 main（本地门禁），每个新工具先写工具规格。负责人 2026-10-31 决定顺序改为“先可查、再可试、再验证、再白名单化”，并优先解决客户页差异。

### 4.1 第 1 批：可查（B 级，只读）
| 编号 | 工具（暂定名） | 内容 | 数据来源 |
|---|---|---|---|
| B1 | `eplan_api_search` / `eplan_api_get` | 按命名空间、类型、名称、说明搜索全部重载组；返回签名、文档摘要、排除原因、覆盖状态和到达它的工具 | 仓库内 `catalog/eplan-2026.0.3/`，Host 内读取，不需要 EPLAN |
| B2 | `eplan_object_find` / `eplan_object_get` | 68 种对象类型的通用查找与属性读取：按类型、页、名称查找，分页返回定位器与属性（ID、名称、索引、值，按语言） | Bridge，只读 |
| B3 | `eplan_action_catalog_search` / `eplan_action_catalog_get` | 官方 action（177 行）与本机界面 action（`MFTools.xml` 等运行时读取）；返回名称、菜单位置、界面文字、已知参数、级别与验证记录 | Host 读本机安装，不入库 |

### 4.2 第 2 批：可试（C 级，只在 scratch，逐次审批）
| 编号 | 工具 | 内容 |
|---|---|---|
| C1 | `eplan_api_try` | 只接受目录中登记、未被排除的方法组；参数只允许简单值与对象定位器；Bridge 中按目录生成的分发表执行（不按字符串反射）；执行前后业务快照与文件指纹对比；超时或模态对话框报 `unknown` 并暂停写入 |
| C2 | `eplan_action_try` | 界面 action 的 C 级执行，规则同 ADR-0030 |
| C3 | 对象属性写入白名单扩展 | 每个属性登记可写性、取值校验与读回；按批加入 `eplan_property_set` |

### 4.3 第 3 批：批量验证
| 编号 | 内容 |
|---|---|
| V1 | Large `BatchVerification`：仅在 `EPLANMCP_BATCH_VERIFY=1` 时运行，`PlanStepLiveSession` 确保只有 LargeWrite 副本打开；经 `TrialApprovals` 逐次回答审批。遍历 main 当前生成分发表的全部读取、属性/方法写入（含设置、句柄、数组、生命周期）及策略允许的无参数 action；记录结果、耗时、变更、系统消息和一步撤销。服务保留 Project target，工厂 Create 省略 target，Remove 保留 destructive 审批与备份；缺少句柄来源、文件或页面等参数上下文时记录 failed:needs_context，不猜测调用 |
| V2 | JSON 仅保存于 scratch；`eng/coverage import-verification <results.json>` 校验既有分母内的目录 ID、日期和版本，未知/已排除 ID 逐项报告并丢弃，按 ID 合并到 overlay 的 `verification`，保留人工审阅的其他条目；`generate` 更新分类统计、通过 API 组的 live_verified 和 `verified-whitelist.yaml` 晋级候选。未运行不标为通过或 env_blocked；未知、弹窗、超时立即停跑并保留写入暂停，撤销不恢复也停跑 |

### 4.4 第 4 批：先验证，再白名单化（A 级）

**负责人决定（2026-10-31）**：第 4 批以第 3 批证据为依据，按 ADR-0021 / ADR-0032 把通过项作为数据逐批晋级；条件为 `outcome=ok`，且只读未变更或 `undo_restores=true`。`catalog/mapping/verified-whitelist.yaml` 由 `generate` 生成，当前仅供审阅，不被运行器加载，不自动放宽安全策略。晋级仍须核对级别、参数、版本、事务和副作用，按 ADR 流程发布。

手写有类型工具只用于客户差异 D1–D6，以及确实需要规划、组合、校验或读回逻辑的功能；下表是能力分类，不再要求每个操作组单独写工具。
| 功能区 | 主要能力 |
|---|---|
| 编辑 | 删除对象、复制/粘贴/多重复制、旋转、镜像、比例缩放、组合与取消组合、对齐/分布、置顶/置底 |
| 插入 | 黑盒、设备连接点、结构盒、插头/插针与插头定义、屏蔽、连接定义点、母线连接点、分散式端子、路径功能文本、圆/弧/多段线/椭圆、尺寸标注、图片 |
| 设备 / PLC | PLC 编址与分配、插头导航、PLC 连接点显示属性 |
| 连接 | 电位与源目标、连接定义点、全部重新分配、对齐与格式化 |
| 工具 | 按表格模板生成报表、自动处理、结构标识符、图层、属性排列、更换、同步 |
| 主数据（并入 M9） | 部件新建/修改、表格与图框设计、宏创建 |
| 预规划、拓扑、修订、`EServices.Ged`、Pro Panel 3D / 安装板 | 按命名空间分组的专用工具，范围在第 3 批验证结果出来后细化 |

### 4.5 客户页差异（必须在重画前解决）
| # | 差异 | 归属 |
|---|---|---|
| D1 | W3 的 RD 芯：经母线节点的连接作为芯线 | 连接（第 4 批），需先 spike：EPLAN 在星形网络中如何分配芯线 |
| D2 | 同页同名中断点 I2/I5（`-HMT-13S1`） | 连接；需先验证 EPLAN 同页同名中断点的配对规则 |
| D3 | PLC 通道显示 `-KF1`、`&EFA1/1.2` 与插头 `-X10` | 设备 / PLC：PLC 连接点显示属性、插头定义 |
| D4 | 电缆文字竖排 | 编辑：属性文字旋转与单独移动 |
| D5 | 辅助触点 `-SUP1`（中断点生成需要的真实连接） | 已离线取消依赖：无对端设备时按图形验收；正常配对仍校验逻辑连接，单步撤销 Large 待运行 |
| D6 | 端子排定义文字 `-X6 = 操作控制盒` | 插入：端子排定义放置到指定位置 |

## 5. 安全与数据完整性
- B 级全部只读；对象属性值是项目数据，按"数据而非指令"处理，返回经 `ResultShaper` 清洗与预算控制。
- C 级只作用于 scratch 根目录下的项目，scratch 外 `POLICY_DENIED`，不提供绕过；每次审批，审批内容显示条目、参数原文、"未验证"标记；默认关闭，`config.json` 显式开启。
- C 级执行前后快照对比、超时与模态对话框检测、审计标记沿用 ADR-0010 / ADR-0030 的机制。
- A 级专用工具沿用写入流水线：审批、事务、读回、一步撤销。
- 方法组试执行的威胁与机制见[架构威胁模型](../architecture/architecture.md#13-威胁模型)。

### 5.1 已有 C1/C2 入口与证据边界

以下记录现有分发表和适配器的范围，签名清单以 [api-try.g.tsv](../../catalog/api-try.g.tsv) 为准；实机状态见[待验台账](../process/large-pending.md)和 [eplan_api_try](../tools/eplan_api_try.md)。

- **文件入口**：方向明确的字符串文件参数可解除 `external_path_parameter`，输入落在 Host `imports`、输出落在新建 `exports` 子目录。Host/Bridge 校验 UNC、路径穿越、保留名、重解析点和大小；输入 SHA-256/大小与目标目录身份绑定审批，执行前重算。输出清单在单撤销事务提交前核对大小/哈希。外部文件不随项目撤销（A56），失败或 `unknown` 可能留下文件；目录、打印/发送、主数据、设置及隐式目的地仍被排除。
- **静态和属性入口**：静态调用没有 `target`，仅生成目录公开类型的编译分支；可能写入的调用带同项目对象参数。`via` 来自公开 getter 的闭合路径表，深度最多 2；父定位器、参数和返回对象核对 scratch 项目，规划不跟随 getter，路径/图层选择器参与审批和指纹。ProjectSettings/方案当前只读，`settings_write` 没有写分支；生命周期、文件、网络、授权、应用/会话状态和主数据按各自排除表处理。
- **设置读取**：scratch Project 定位器绑定项目，USER/STATION/PROJECT 节点和叶设置逐段枚举，规划和执行时复核；`offset`/`limit` 与索引有界。方案绑定已枚举节点，不切换方案或写 LastUsed。设置 setter、修改方法及 COMPANY/INSTALL 根被排除。读取逐次审批，不创建撤销步；前后快照不覆盖 USER/STATION 设置库，因而不代表全局无副作用。`ref` 容器局限于封闭适配器。
- **生命周期入口**：`Create*`/`Remove*`/`Delete*`/`Erase*` 与公开构造工厂目前仅在 scratch 中逐次批准为 write，审批显示 creates/removes 和撤销未验证；移除采用 [ADR-0035](../adr/0035-destructive-approval-default-reject.md) 的默认拒绝及执行前备份。单撤销事务提交后记录 `undoExpected`，不是撤销实测。对象/资源清单前后独立读回，提交后重新查找创建定位器；读回失败为 `unknown` 并暂停。`Project.Remove`、transient 创建、销毁/初始化/会话锁和文件/主数据/设置仍被排除。
- **服务入口**：目录生成公开无参服务构造和编译调用，`IDisposable` 服务以 `using` 释放，借用对象不释放。Project 定位器锚定 scratch，对象参数和返回定位器核对同项目；无对象形参的入口还核对当前项目。路径、流、数据库、主数据、部件管理、设置/方案、打印机、URL、授权/退出、隐式外部目的地及自建事务按签名排除，理由记录在生成清单。其余按无副作用参数的读前缀或单撤销写事务分类。服务读取和写入撤销的 Large 用例已编写，原记录尚未运行。
- **C2 action**：未验证 action 可能越出项目、弹窗或启动外部程序。`eplan_action_try` 默认关闭且仅在 full 中，Host 校验同一目录行参数，Host/Bridge 同时检查随包禁止表和目标覆盖。执行时目标 scratch 项目单独打开且为当前项目，逐次审批标明 unverified，规划后和执行前复核目标；审计写 `unverified=true`。超时或模态窗口按 `unknown` 暂停项目和实例。禁止表不证明第三方 action 安全，文件指纹不覆盖用户/工作站设置，C 级结果不作为已验证证据。Small/Medium 有失败场景入口，Large 已编写，原记录待运行。

## 6. 备选方案
- 每个重载组一个 MCP 工具：被否决，工具数量超出模型可选范围；当前发现策略见[手册 MCP-34](../standards/engineering-handbook.html#mcp-catalog)。
- 只提供 C 级任意执行、不做专用工具：被否决，无法逐项读回核对，不满足"和原版一模一样"的验收。
- 先修客户页 6 处差异、再补 API：负责人 2026-10-30 否决，要求先系统补齐。

## 7. 测试与发布
- 每个工具：Small（规划、校验）、Medium（Host 流水线与假 Bridge）、Large（scratch 实机，含撤销）。
- 第 3 批之后每周更新覆盖报告；截图功能区对照表随批次更新。
- 当前里程碑状态、v0.5.0 发布门槛与排期统一见[路线图](../roadmap.md)。
