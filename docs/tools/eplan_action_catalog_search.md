# 工具规格：`eplan_action_catalog_search`

| 项 | 内容 |
|---|---|
| 分组 / Profile | action_catalog / full |
| 级别 / 状态 | Read / Implemented |
| MCP 注解 | readOnly=true、destructive=false、idempotent=true、openWorld=false |

用于发现官方 action 用法与本机界面 action；目录不授予执行权限，不调用 EPLAN 或 Bridge。

输入：`text`（名称或界面文字包含，忽略大小写，1–500 字符）、`tier`（A / C-candidate / excluded）、`source`（official / ui），过滤取交集；`limit` 默认 20、范围 1–100；`cursor` 使用返回的 PageCursor。

输出：`catalogVersion`、`installationVersion`、`gaps`、`items`、`total`、`nextCursor`、`truncated`、`hint`。每项包含名称、来源、TYPE、位置、界面文字、已知参数、级别、排除原因、白名单条目及验证引用、来源引用。官方 177 行保持独立；A 仅匹配已发布白名单的 action + TYPE，候选不代表可执行。

官方数据嵌入 Host，来源为 actions.json 与 api-actions 表的一一对应行；本机目录沿用 Host 安装发现得到的 Bin，仅扫描相邻 Cfg/Bin 内 MFTools、Ribbon、Menu XML。多安装无法选择、缺失、损坏、未知结构、版本不一致、未存储界面文字均返回缺口；不推断资源 ID 对应的文字，不持久化或分发本机数据。XML 禁止 DTD 和外部实体，并限制文件数、深度及大小。

`INVALID_ARGUMENT`：一次报告全部过滤、分页错误；移除旧游标重新查询。结果预算沿用 API 目录分页，数据清洗后返回。

测试：Small 验证官方关联、分级、过滤和游标；测试自行写入合成 XML 覆盖解析及缺失安装；Host 协议测试检查注册与只读注解；Large 只读安装断言 UI action 超过 900，白名单条目为 A，本批不运行。评测场景：查询图层导出的 TYPE 和白名单证据后结束，不执行 action。
