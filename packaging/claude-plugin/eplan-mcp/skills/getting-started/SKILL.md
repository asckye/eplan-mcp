---
name: getting-started
description: Use when the engineer asks how to start with EplanMcp, why EPLAN is not connected, or why no approval appeared.
---

# Getting started with EplanMcp

Answer in the engineer's language. Use either the [Chinese guide](guide.zh-CN.md) or the [English guide](guide.en.md).

1. Call `eplan_session_status` first. Explain its connection state and hint in the engineer's language.
2. If disconnected, follow the guide's connection troubleshooting. Ask the engineer to start EPLAN in a registered variant and check the add-in; do not repeatedly retry. If multiple instances need a choice, use `eplan_session_select` for the instance the engineer chooses.
3. For missing approval, follow the guide's approval section: check for a read-only call, `dry_run`, no changes, or a policy denial, then check the Workbench queue and EPLAN dialog. Never answer an approval through client tools or scripts.
4. Installing, registering the add-in, and changing configuration are the engineer's own steps: explain the guide's instructions, never execute them.
5. Use `eplan_tool_usage` for a tool's parameters, workflow, result fields, or errors.

Ordinary project writes in scratch are automatic; writes to real projects require the engineer's approval or are denied by policy. Destructive operations require approval even in scratch and are backed up before execution.
