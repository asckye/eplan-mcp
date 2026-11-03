# 工具规格：`eplan_pack_install`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_pack_install` |
| 所属分组 / Profile | `pack` / `full` |
| 安全级别 | Write（本地数据） |
| MCP 注解 | readOnlyHint=false，destructiveHint=false，idempotentHint=true，openWorldHint=false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
从已列出的插件库安装精确插件版本，不要求代理提供路径。安装不启用插件、不改变项目绑定。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| library | string | 是 | — | `eplan_pack_library_list` 返回的精确库名 |
| plugin | string | 是 | — | 精确插件 ID |
| version | string | 是 | — | 精确 SemVer 版本 |

## 3. 输出
`installed` 含 id/version/digest/outcome；outcome 为 installed 或 already_installed。`warnings` 报告不可读来源；此时 installed 为 null。摘要说明安装或未更改。

## 4. 错误
INVALID_ARGUMENT：修正名称/版本，不接受路径。RESOURCE_NOT_FOUND：重新列库选择条目。AMBIGUOUS_NAME：请工程师消除同名库。
PACK_INVALID：归档或索引摘要不一致。ALREADY_EXISTS：同版本内容不同，请作者发布新版本。APPROVAL_DENIED/APPROVAL_EXPIRED：未安装。

## 5. 实现要点
归档先有界复制至数据目录 staging，沿用 PackArchive/PackVerifier/PackStore；要求 PackDigest 等于索引摘要。无 EPLAN API 调用；支持取消。

## 6. 写操作专项
签名限定三个选择参数，不提供 dry_run。程序目录 builtin 免审批；其它来源沿用 pack-import 审批，加库名、来源类型和目录。相同版本及摘要无需审批。
`pack_change` 记录 started/committed/failed 和库名、类型；安装版本不可变，失败清理 staging。没有项目写入，沿用 pack-import 的无 execution 契约。

## 7. 测试
Small 校验参数；Medium 临时库验证 builtin、审批/拒绝、摘要、幂等和更新；不运行 EPLAN。
可运行任务：先列库，取一个条目的 library/id/version 调用本工具，再用 `eplan_pack_get` 核对已安装版本与摘要。
