# 项目交接

## GitHub Actions 引用

2026-11-03 核对：`.github/workflows/build.yml` 的 `actions/checkout`、`actions/setup-dotnet` 和 `.github/workflows/commit-rules.yml` 的 `actions/checkout` 仍使用 `@v4` 标签，完整提交 SHA 固定尚未完成。`.github/workflows/release.yml` 的 action 引用已经固定到完整 SHA；其余 workflow 的固定仍是待办。
