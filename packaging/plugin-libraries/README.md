# 内置插件源

`builtin/<id>/pack.json` 是构建输入，保留固定来源日期与插件元数据；`resources` 由构建器扫描旁边的 KnowledgeDocument 文件生成，不手写摘要。

Host 构建与发布复用 `PluginLibraryBuilder`、`PackWriter`、`PackArchive`，将 `library.json` 和 `.eplanpack` 写入输出的 `plugin-libraries/builtin/`。同一份源产生相同的 PackDigest 和归档字节；仓库不保存二进制归档。

`iec-base` 的标准编号只作适用标签，不宣称标准符合性。P1 仅提供 A83 已验证的端子排定义 `TDEF`/0；中断点默认值尚未核实，留给维护者后续补充。
