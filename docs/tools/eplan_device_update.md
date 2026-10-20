# 工具规格：`eplan_device_update`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_device_update` |
| 所属分组 / Profile | `device` / `core` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M3a 第 11 步） |

## 1. 这个工具解决什么任务
"把 `-KF1` 改成 `-KF10`""把 `=GAA+A1-FC1` 挪到 `=GAB1` 下"。代理给出设备的现有标识和新标识，工具把整个设备改名：主功能和全部辅助功能（包括其他页上的触点，即交叉引用）一起改，设备保持完整。这一版只改设备标识，设备属性在 M3b 加入（M3 设计 4.4）。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `tag` | string | 是 | — | 现有的设备标识。完整标识（如 `=GAA+A1-KF1`）总是唯一；短标识（如 `-KF1`）在多个结构下出现时返回 `AMBIGUOUS_NAME` |
| `new_tag` | string | 是 | — | 新标识：完整标识（如 `=GAA+A1-KF10`）；或以 `-` 开头的短标识（如 `-KF10`），表示保留设备现在的结构 |
| `dry_run` | boolean | 否 | false | 为 true 时只返回将要进行的修改，不请求审批，也不修改项目 |

- `tag` 的查找规则与 `eplan_device_get` 相同。
- `new_tag` 不能为空，不能含空白和控制字符，必须含有 `-` 开头的设备部分；与现有标识相同时返回 `INVALID_ARGUMENT`。只改大小写（`-kf1` → `-KF1`）是允许的。

## 3. 输出
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "device": {
    "tag": "=GAA+A1-KF10",
    "previousTag": "=GAA+A1-KF1",
    "visibleTag": "-KF10",
    "functions": 3,
    "pages": ["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2"]
  },
  "undo": "AI: eplan_device_update",
  "recovered": false,
  "hint": null
}
```
- `device.functions`：改名的功能数（主功能加辅助功能）。`device.pages`：这些功能所在的页，最多 20 个，按 EPLAN 的顺序列出。
- 提交后是读回的值；`dry_run` 时是将要得到的值，`visibleTag` 为 `null`。
- `undo`、`recovered`：同 `eplan_page_create`。
- 标识按项目数据处理，经过 ResultShaper。
- 一行摘要（只有计数，不回显项目名，架构 8.5）：
  - `Renamed a device and its 2 auxiliary functions; Ctrl+Z in EPLAN undoes this step.`
  - `Dry run: would rename a device and its 2 auxiliary functions. Nothing was changed.`
- `dry_run` 和取回结果时的 `functions`、`pages` 来自计划的 `details`（`functions`、`pageCount`、`pages`），审批表单的"共 N 个功能，分布在 M 页"也取自这里。

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `new_tag` 不合法，或与现有标识相同；`new_tag` 以 `-` 开头但拼不出现在的结构；设备有带后缀的功能（见第 5 节） | `not_started` | `metadata.violations` 列出全部问题。带后缀的情况："This version renames only devices whose functions all carry the device tag itself; '<tag>' has functions with their own suffixes (such as '<tag>:1')." |
| `INVALID_ARGUMENT` | 读回的标识与请求不同（项目结构对这类设备不用新标识中的某一部分，EPLAN 把它丢掉了），或者改名后仍有功能带着旧标识 | `rolled_back` | `metadata.requested`、`metadata.actual`；提示参照同类设备的标识（`eplan_device_list`） |
| `DEVICE_NOT_FOUND` / `AMBIGUOUS_NAME` | `tag` 找不到，或短标识对应多个设备 | `not_started` | 同 `eplan_device_get` |
| `ALREADY_EXISTS` | 新标识已被其他设备使用（不区分大小写） | `not_started` | "'<new_tag>' is already used by another device; giving this device the same tag would merge the two devices in EPLAN, which this tool does not do. Choose a free tag; eplan_device_list with tag_contains shows the tags in use." |
| `PLAN_STALE` | 规划之后，这个设备的功能发生了变化，或者新标识被占用了 | `not_started` | 提示重新调用 |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_create` | 同 `eplan_page_create` | 同 `eplan_page_create` |

## 5. 实现要点
- **Host**：走 `WritePipeline`，规划操作是 `device.update.plan`，执行操作是 `device.update.execute`。执行时限为默认的 60 秒。
- **改名的范围**（S9，api-notes A13）：
  - 只给主功能的 `Name` 赋值时，辅助功能保留旧标识，**从设备上脱离**；
  - 要把带旧标识的功能全部改成新标识。范围是：`Name` 与旧的完整标识完全相同（`Ordinal`）的全部功能，包括主功能、辅助功能，以及黑盒中同名的功能（A12）；
  - 主功能的 `SubFunctions` 必须都在这个范围内。如果有辅助功能的名称带后缀（如端子的 `+A1-FC2:1`，A11），这一版不改，返回 `INVALID_ARGUMENT`。带后缀的功能怎样改名，没有在实机上验证过。
- **新标识**：
  - 完整标识原样使用；
  - 以 `-` 开头时，先算出设备现在的结构：从 `Name` 末尾去掉 `-` 加 `FUNC_DEVICETAG_FULLNAME`（S6），再接上 `new_tag`。`Name` 不以它结尾时，要求给出完整标识。
- **规划**：
  - 找到设备和改名范围；
  - 检查新标识：全部功能中有不区分大小写同名、又不在改名范围内的，返回 `ALREADY_EXISTS`。EPLAN 自己不检查重名（A13）；
  - 一条 `update` 变更：`Property = "tag"`，表单显示为 `update device =GAA+A1-KF1: tag '=GAA+A1-KF1' -> '=GAA+A1-KF10'`；
  - 指纹包括两部分，排序后取 SHA-256：
    - 改名范围内每个功能的 `Name` 和会话内标识（`ToStringIdentifier`，A3）；
    - 带新标识的功能个数。

    EPLAN 重启后会话内标识会变，计划也随之失效，这正是想要的结果。
- **执行**：
  - 先重算指纹，不同就返回 `PLAN_STALE`；
  - 在 `TransactionScope` 中给范围内的每个功能的 `Name` 赋值新标识，然后读回：
    - 主功能的 `Name` 必须等于新标识（EPLAN 可能丢掉这类设备不用的结构部分，A13）；
    - 项目中不能再有带旧标识的功能；
  - 不满足就抛出，整体回滚；全部通过后 `Commit()`，再 `CloseUndo()`。
- **包装**：同 `eplan_page_create`。S9 验证过：改名后 `SafetyPoint.Rollback()` 能恢复主功能和全部辅助功能的标识。
- **没有验证过的影响**：改名对已有连接和部件引用的影响。由 Large 测试比较改名前后设备的连接数和部件。

## 6. 写操作专项
- **dry-run**：返回第 3 节的结果，`execution = not_started`。
- **审批**：项目在 scratch 之外时请求审批。表单显示旧标识和新标识；正文另注"共 N 个功能，分布在 M 页"，取自计划。
- **审计、回滚、各阶段失败时的 `execution`**：同 `eplan_page_create`。
- 不是幂等的：同样的调用再来一次，旧标识已经不存在，返回 `DEVICE_NOT_FOUND`。执行请求从不重发（ADR-0010）。

## 7. 测试
- **Small**：
  - 由短标识拼出完整标识；`new_tag` 的校验；
  - 改名范围的计算：同名、带后缀、`SubFunctions` 不在范围内；
  - 指纹；摘要；schema 快照；注解。
- **Medium**：在假 Bridge 上测试：
  - `dry_run`；`ALREADY_EXISTS`；`AMBIGUOUS_NAME`；`PLAN_STALE`；
  - 读回不一致或仍有旧标识时返回 `rolled_back`。
- **Large**（示例项目的 scratch 副本）：
  - 把 `=F+A2-KF1`（辅助功能在另一页，S9）改成 `-KF19`：`eplan_device_get` 读回后，主功能和两个辅助功能都是新标识，连接数和部件与改名前相同；
  - 改成已有的标识，返回 `ALREADY_EXISTS`；
  - 用 `-` 开头的短标识，结构保持不变；
  - 选一个辅助功能带后缀的设备（端子排），返回 `INVALID_ARGUMENT`。
- **Agent 评测**："把 scratch 项目中的 `=F+A2-KF1` 改名为 `=F+A2-KF19`"，作答后用 `eplan_device_get` 核对辅助功能也跟着改了名。
