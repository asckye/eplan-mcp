[中文](zh-CN.md) | [English](en.md)

# EplanMcp English Getting Started Guide

This guide covers ZIP preview releases. Use the files actually available on [GitHub Releases](https://github.com/asckye/eplan-mcp/releases). If no ZIP is available yet, wait for a later release; see the repository README for building from source.

If EPLAN runs in a VM and the AI client runs on the host computer, see [VM deployment](#10-vm-deployment).

## 1. Prerequisites

- Windows 10/11 x64, EPLAN 2026 Electric P8 or Pro Panel, and an EPLAN licence that includes the API extension and allows unsigned add-ins. Installation has not yet been tested on 2024, 2025, or 2027, so those versions are outside this guide's getting-started environment.
- Install Claude Code or Claude Desktop and set up the client's own model service. Installing EplanMcp and connecting locally do not require internet access; the client may need internet access to connect to its model.
- One Windows user should use EplanMcp on each computer: EPLAN registration is stored per machine, while the program is installed per user. Do not register it again under another account.
- Keep the client's confirmation prompts for shell commands enabled. Scripts running as the same user can operate approval windows; never let an agent or script approve on the engineer's behalf.

## 2. Download and SHA-256 Verification

Download `EplanMcp-<version>.zip` and `SHA256SUMS.txt` from the same release. In PowerShell, go to the download directory and replace the version placeholder:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath '.\EplanMcp-<version>.zip'
```

Compare the output `Hash` character by character with the 64-digit hexadecimal value for **that filename** in the checksum file (letter case does not matter). If they differ, stop, do not run the file, and download it again from the release page. For an offline computer, download both files on a connected computer first, then transfer them together for verification.

After verification, right-click the ZIP → Properties → select **Unblock**, if shown, and Apply before extracting. **The 0.x programs are not code-signed.** If SmartScreen, enterprise policy, or Smart App Control blocks execution, contact your administrator under your organisation's rules; do not change system security settings. The add-in still requires the EPLAN licence described above.

## 3. Install and Register the EPLAN Add-in

Extract the entire ZIP, which contains one top-level folder, `EplanMcp-<version>`. Move that whole folder to a short, permanent location; you can rename it to `D:\EplanMcp`. Keep every file and subfolder together; do not copy only the EXE or launch it from an archive preview. This program folder is separate from the data root, `%USERPROFILE%\EplanMcp`. No administrator privileges or PATH changes are needed.

Close EPLAN and open **`D:\EplanMcp\workbench\EplanMcp.Workbench.exe`** (adjust the prefix for your location). On the status page, tick the detected variants you use, such as Electric P8 and Pro Panel, then choose "Register EPLAN add-in" and read each result. Keep the program folder at this location afterwards.

Registration starts EPLAN, waits for the add-in to load, then requests a normal shutdown of that instance. Handle and close any login or licence windows yourself; do not force the process to end. A system messages window that EPLAN shows at start-up is closed for you so that EPLAN can close, and its messages stay in EPLAN's system log. Only after registration succeeds should you start your usual EPLAN instance and open a project.

Use Workbench again to register another variant, or use these commands after closing EPLAN. Adjust the program path; subsequent commands reuse `$eplanMcp` in the same PowerShell window:

```powershell
$eplanMcp = 'D:\EplanMcp\eplanmcp.exe'
& $eplanMcp install-addin --variant 'Electric P8'
```

For Pro Panel, replace the variant name with `'Pro Panel'`; if you use both, pass `--variant` more than once. Omitting the variant registers all detected variants. The command may wait for two minutes. If it reports "not confirmed", check the EPLAN window left open before doing anything else; do not repeatedly register.

## 4. Run Diagnostics

```powershell
& $eplanMcp doctor
```

For the first run, you can use Workbench's status page directly; the command above is the CLI alternative. `OK` means normal, `INFO` provides information, `WARN` needs checking, and `FAIL` needs action. Use `doctor --format json` to save structured results.

## 5. Connect a Client

On Workbench's "AI Clients" page, select your client, preview the changes, then write them. Workbench calls Host's `client-config`, which uses the actual absolute path of the current `eplanmcp.exe`. The engineer performs these steps. Open Workbench from the extracted folder, or run CLI commands from PowerShell opened from Start; configuration commands refuse writes inside a Claude Desktop session's redirected MSIX environment.

### Claude Code: Local Plugin Recommended

First generate the Claude Code local marketplace on Workbench's client page, then follow its two commands. With the default data root, enter these in a Claude Code session; for a custom data root, use the actual path shown by Workbench:

```text
/plugin marketplace add ~/EplanMcp/claude-plugin
/plugin install eplan-mcp@eplanmcp-local
```

You can also use the `claude plugin marketplace add …` and `claude plugin install …` terminal commands shown by Workbench. The generated marketplace needs no content downloads and already contains the actual Host path. Do not install the template inside the program folder directly. Reopen the session afterwards.

**Choose either the plugin or `client-config claude-code` below.** Remove previous configuration and check project-level entries before installing the plugin. Claude Code gives priority to user or project configuration when entries duplicate a plugin server. Keep the generated `claude-plugin\` directory in the data root.

### Claude Desktop or Other Clients: client-config

Fully quit Claude Desktop (including the tray), preview the change, then write it:

```powershell
& $eplanMcp client-config claude-desktop --dry-run
& $eplanMcp client-config claude-desktop
```

The command detects the configuration location actually used by the MSIX desktop app. Start the client after writing. Use this route for the Claude Desktop chat interface; configure the Code tab as Claude Code to avoid adding the same server again. Sharing plugins with the Code tab still needs testing on a real machine.

For Claude Code without the plugin, replace the client name above with `claude-code`. If the plugin is installed, writing will be refused; first run `claude plugin marketplace remove eplanmcp-local` in a terminal. Other supported names are `codex`, `gemini-cli`, `qwen-code`, `kimi-code`, `codebuddy`, `opencode`, `qwen-agent`, `cursor`, and `vscode-copilot`.

Writing only changes the server entry named `eplan`. The original file is first backed up in the same directory as `<file>.bak_<UTC time>`. JSONC comments are lost when the file is rewritten; you can view them in the backup. Do not share the entire client configuration file, which may contain keys for other services.

For a client that can only load a few tools at a time, add `serve --profile discovery` to the server arguments: it publishes only `eplan_tool_search`, `eplan_tool_usage`, `eplan_tool_read` and `eplan_tool_write`; the model searches first and then calls, with the same safety tiers and approvals ([CLI reference](../reference/cli.md)). Claude Code loads tools on demand by itself and does not need this.

## 6. Your First Conversation

Open a project in EPLAN, then tell the client: "First call `eplan_session_status` to confirm the connection, then tell me how many pages this project has."

The normal sequence is `eplan_session_status` → `eplan_project_list` → `eplan_project_get`. If multiple EPLAN instances are running, confirm the target instance yourself first, then use `eplan_session_select` to select it; do not guess based only on the current foreground window. For detailed parameters, ask the model to call `eplan_tool_usage`.

If EPLAN is not running yet, ask the model to call `eplan_session_start` (Electric P8 by default, or Pro Panel). It returns once the add-in connects, within 3 minutes; it opens no project and refuses while another EPLAN runs. Starting EPLAN takes an EPLAN licence.

## 7. Your First Change and Approval

First use EPLAN's project copy feature to put a test copy in `%USERPROFILE%\EplanMcp\scratch\`, then open the copy in EPLAN; do not set a real project's directory as scratch. Ask the model to find the copy's full page name, then preview a single page description change using `eplan_page_update` with `dry_run: true`. Check it before executing on the copy. After this tool succeeds, you can undo it with one Ctrl+Z in EPLAN.

Ordinary project writes in scratch are allowed automatically. Real projects require approval for each write by default; `writesOutsideScratch` in `config.json` can also be set to deny them (`deny`). The engineer can turn off "Normal write manual approval" on Workbench's "Settings" page, in the status bar or on the approvals page (`allow`): ordinary writes to real projects then run without a question, and the change reaches running sessions at once. Nobody looks at each change while it is off, so use it only for a long run of small, undoable changes, and turn it back on afterwards. Destructive operations (such as deleting pages or renumbering) require approval even in scratch. A backup is made after approval; if the backup fails, the operation does not run. Writing to the system parts database affects all projects, requires approval and a backup even when initiated in scratch, and cannot be undone with Ctrl+Z. These two kinds, unverified writes and lifting a write suspension always ask, whatever that switch says.

Open `workbench\EplanMcp.Workbench.exe` in your program folder yourself, or enable startup at sign-in in its settings; Host will not start it for you. Ordinary project approvals go to Workbench first. Check the tray notification and "Approval queue"; the window will not take focus. If Workbench is unavailable, an EPLAN dialog is used instead. If the connection drops after delivery, the request is cancelled without opening a second dialog. System master data approvals still appear only in EPLAN.

Check the tool, instance, project, and change list, then approve or reject it yourself. By default, if nobody approves within 120 seconds, the request is denied and nothing runs. Only the engineer can edit safety settings in `config.json` in the data root; tools cannot change them. Do not turn off protection just to make a call succeed.

For compiling, reviewing and applying dependent changes, see the [plans guide](https://github.com/asckye/eplan-mcp/blob/main/docs/guides/plans.en.md), including task polling, initial backups, stale plans and v2 verification coverage.

## 8. Troubleshooting, Logs, and Audit

| Symptom or doctor item | What to do |
|---|---|
| Cannot connect to EPLAN; `eplan.running`, `bridge.pipe` | First ask the model to call `eplan_session_status` and read its hint. Confirm EPLAN is running and the variant in use is registered. Open a project if none is open. Explicitly select an instance if several are running. |
| `eplan.installation`, `bridge.handshake`, `host.version` | Check EPLAN 2026 and the Host/add-in versions. Restart EPLAN after upgrading; if they still do not match, close EPLAN and register again. Do not repeatedly unload and load the add-in in API management. |
| `bridge.licence` | Check the licence requirements for the API extension and unsigned add-ins; licence files or credentials are neither needed nor to be provided. |
| No approval appeared | Read-only calls, `dry_run`, no actual changes, and ordinary scratch writes do not require approval. For real projects, check the Workbench tray/queue and the EPLAN dialog. Read the returned denial or timeout reason carefully; never let the model answer for you. |
| `EPLAN_BUSY` | EplanMcp first closes EPLAN's system messages window by itself (set `"eplanWindowWatch": "report"` in `config.json` to only report it), and the error says what it closed or which window is in the way; handle any other window yourself, wait for the current operation to finish, then retry. |
| `config`, `data.directory`, `audit.directory` | Correct the configuration errors reported by doctor; confirm the data directory is writable. Writes are denied if the audit cannot be written; do not delete the audit to bypass this. A directory that has not been created may simply not have been used yet. |
| `suspension` or unknown result | First check in EPLAN whether the change has already happened. The next write will ask you to confirm through the approval channel. Do not simply retry a change whose result is unknown. |
| `exports`, `backups` | Check disk usage and remove files you no longer need yourself; backups are not deleted automatically. |
| `msix` | MSIX clients can connect to local tools. To write client configuration, use PowerShell opened from the Start menu and fully quit the desktop app. |
| `path.length` | Follow the hint to check for environment issues caused by an overly long user PATH. This software does not modify PATH, and you do not need to add app to it manually. |
| Administrator privileges required, failure when offline, or cannot use a virtual machine | Local installation requires no administrator privileges and is not tied to a computer. Distinguish Windows policy, EPLAN licensing, and model client network issues. Use a local connection if the AI client also runs in the VM; otherwise see [VM deployment](#10-vm-deployment). Only setting network permissions requires administrator consent; approval requirements stay the same. |
| Registration points to another user | Stop and let the engineer responsible for the computer coordinate. Detection has only partial coverage: it cannot reliably identify another user's add-in if it is unreadable or an older version does not report its path. |

The default data root is `%USERPROFILE%\EplanMcp\`, separate from the extracted program folder. `EPLANMCP_HOME` changes only the data location, not the program folder; use the location shown by doctor when troubleshooting.

- Add-in logs: `logs\bridge-*.log`; for startup failures, check `logs\bridge-startup-error.log`. Workbench logs are in `logs\workbench-*.log`. Host logs go to stderr and are collected by the client; a Host log file is not guaranteed.
- Audit: `audit\<date>.jsonl`. Run `& $eplanMcp audit verify` to verify the hash chain. This detects changes within the chain but cannot prove that an entire trailing sequence of records has not been deleted.
- `state\` stores registration records, client configuration records, and write suspension state; `scratch\` holds test projects, `exports\` holds exports, and `backups\` holds backups made before operations. Before reporting an issue, check logs, configuration, and project text and remove sensitive information; do not provide keys.

## 9. Upgrade and Uninstall

Before upgrading, wait for writes to finish and close **EPLAN, MCP clients, and Workbench** (exit from its tray menu). Verify, unblock, and extract the new ZIP, then replace the entire program folder at the same path, such as `D:\EplanMcp`, to avoid leaving old files. Do not force Host to end. Open the new Workbench, regenerate the local marketplace or update client configuration, then start EPLAN. Registration is retained if the path stays the same; register again if it changes. Downgrades are unsupported. All data is retained separately.

Before uninstalling, remove the Claude Code local marketplace yourself (this also uninstalls its plugins):

```powershell
claude plugin marketplace remove eplanmcp-local
```

If you used the configuration route, you can run `& $eplanMcp client-config --remove` before uninstalling to remove server entries previously written and recorded by the command. You can also specify a client, such as `client-config claude-desktop --remove`. Check any manually written project configuration yourself.

If you used a VM connection, remove `eplan-vm` on the host, then choose "Stop service" → "Remove network access" in the VM; see [Remove the VM connection](#remove-the-vm-connection).

Disable startup at sign-in in Workbench settings and remove client configuration as described above. Close EPLAN and clients, then choose "Unregister" in Workbench. It calls `uninstall-addin` for all recorded registrations and may start EPLAN; handle any windows yourself. After success, exit Workbench from the tray and delete the entire program folder. If unregistering fails, keep the files, resolve the issue, and retry. The CLI alternative is `& $eplanMcp uninstall-addin`.

Uninstallation retains all data in the data root; delete it yourself only after confirming you no longer need it. Moving to another computer requires no EplanMcp licence transfer; handle EPLAN's own licensing according to its rules.

## 10. VM deployment

### When to use it and where things run

Use this when EPLAN runs in a VM and the AI client runs on the host computer. Host (`eplanmcp.exe`), Bridge, approvals, audit, scratch projects, and exports all stay in the VM. Only MCP requests and results cross the network.

Clients must support Streamable HTTP: Claude Code, Codex, Gemini CLI, Qwen Code, Kimi Code CLI, CodeBuddy Code, OpenCode, Qwen Agent, Cursor, and VS Code Copilot. Claude Desktop cannot use this connection: its configuration file supports only local stdio servers, while remote connectors use HTTPS from Anthropic's backend and cannot reach this private VM HTTP address. Spike S19 tested the connection only with Claude Code; the other clients still need individual acceptance testing. Qwen Agent's configuration could not be verified against official documentation. Cross-machine connections and UAC still need testing on real machines.

**Use only a private network between the host and VM. HTTP is unencrypted and there is no key: project data travels in plaintext, and any program that can reach the port can call the tools. Access is limited only by the Windows Firewall rule created below; do not start the service when that rule is missing or was changed.** The examples use `192.168.56.2` for the VM and `192.168.56.1` for the host. Replace these with mutually reachable IPv4 addresses on your network; do not use `0.0.0.0` or loopback addresses.

### In the VM: start the service

1. Follow [Download and Verification](#2-download-and-sha-256-verification) and [Install and Register](#3-install-and-register-the-eplan-add-in) to extract the entire ZIP and register the add-in, then start EPLAN. Open Workbench → "MCP Server" and find "VM connection".
2. Select the VM's address in "VM IPv4". "Port" defaults to `8766`; enter the AI client's host computer address in "Host computer IPv4".
3. Choose "Allow network access" and have an administrator consent in the Windows UAC dialog. The inbound rule allows only the selected TCP port, this `eplanmcp.exe`, and the single host IPv4, on Domain and Private profiles only. **This rule is the only access control**: once it exists, check that the rule the page shows matches what you entered.
4. If several EPLAN instances are running, select the target in "EPLAN instance (PID)"; use "Refresh instances" if it is missing. Choose "Start service" and check for `Listening at http://<VM IPv4>:<port>/mcp. Keep the Workbench running.` Each service stays bound to one instance; it will not switch to another if that instance exits.

The service never starts automatically during installation or at sign-in. A service started by Workbench stops when Workbench exits. Closing the window only minimises it to the tray; exit from the tray to stop it.

CLI alternative (do not start a second service alongside Workbench's): first run these in ordinary PowerShell.

```powershell
$eplanMcp = 'D:\EplanMcp\eplanmcp.exe'
& $eplanMcp vm-instances --format json
& $eplanMcp vm-firewall show --port 8766 --host-address 192.168.56.1
```

Open a separate PowerShell window with "Run as administrator" to create the rule only (adjust the program path):

```powershell
& 'D:\EplanMcp\eplanmcp.exe' vm-firewall add --port 8766 --host-address 192.168.56.1
```

Return to the ordinary window to start the service. Omit `--process` for a single instance; with several, replace `1234` with the target PID from `vm-instances`. This command stays in the foreground; stop it with Ctrl+C.

```powershell
& $eplanMcp serve --http --address 192.168.56.2 --port 8766 --process 1234
```

### On the host computer: connect clients

1. Verify and extract **the entire ZIP of the same version**, then open `workbench\EplanMcp.Workbench.exe`. This copy is only for the configuration wizard; no EPLAN installation or add-in registration is needed.
2. Open "AI Clients" and select "VM ↔ host" at the top. Enter "VM address (IPv4)" and "Remote port".
3. Choose "Test connection" and check that it reports a connection, server version, and tool count. This tests the connection without calling EPLAN tools.
4. Tick the clients, choose "Preview and write", review the differences, then choose "Write configuration". The remote server entry is named `eplan-vm`. Read each card's result, then restart the clients. This mode writes remote client configuration; local plugin setup is covered in section 5.

CLI alternative: open PowerShell from Start and run these lines individually: test, preview, then write only after a successful test and review. Replace the client name with any supported name listed above.

```powershell
$eplanMcp = 'D:\EplanMcp\eplanmcp.exe'
$vmUrl = 'http://192.168.56.2:8766/mcp'
& $eplanMcp vm-test --url $vmUrl
& $eplanMcp client-config claude-code --vm --url $vmUrl --dry-run
& $eplanMcp client-config claude-code --vm --url $vmUrl
```

Restart the client after a successful write.

### Access control, paths, and approvals

- From 0.9.0 the VM connection no longer uses a shared key ([ADR-0062](../adr/0062-vm-http-without-shared-key.md)). The `eplan-vm` entries that 0.8.0 and earlier wrote into host client configurations carry the key in plaintext: after upgrading, run "Preview and write" once on the host and the entry is replaced by one without a key. Old configuration backups (`.bak_<time>`) still contain the old key; delete them yourself when you no longer need them.
- **Do not turn off "Normal write manual approval" while the VM service is running** unless you accept the consequence: with both in effect, a program that can reach the port can change real projects without anyone confirming.
- All paths given to tools are **VM paths**. Scratch projects, exports, backups, and import files are in the VM. Exported files are not copied to the host automatically; `inline` images can still return through MCP.
- Approvals appear in the VM's Workbench or EPLAN dialog, with `via network: <address>` beside the client name. The engineer reviews and answers them in the VM; network access does not relax approval or scratch rules.

### Troubleshoot the connection

Run the `vm-test` command above on the host and check its failure code. Add `--format json` to save structured results.

| Failure code | What to do |
|---|---|
| `unreachable` | Confirm the VM service is listening. Check the firewall rule, host source IPv4, VM IPv4, and port, and that the private network connects both machines. |
| `timed_out` | A request exceeded 10 seconds. Check whether the VM is paused, the service is responsive, or network packets are being lost or dropped by the firewall, then test again. |
| `forbidden` (403) | The request has an unapproved `Origin` header, often from a browser-based client. Have the engineer check `httpAllowedOrigins` in the VM's `config.json`, add only the actual trusted Origin, then restart the service. The list is empty by default; `vm-test` sends no Origin. |
| `protocol_error` | Check that the URL is `http://<IPv4>:<port>/mcp`. Check protocol and software versions; retry with the same ZIP version on both machines. A client of 0.8.0 or earlier that cannot reach a 0.9.0 service needs the same version on both sides. |

Run `& $eplanMcp doctor` **in the VM**: `http.service` lists services for this data root and their bound addresses/ports; `vm.firewall` reports whether the rule exists and its actual scope. Check the program path, TCP port, host address, and Domain/Private profiles. These checks do not change rules or replace a connection test from the host.

### Remove the VM connection

On the host, select clients in "VM ↔ host" mode, choose "Remove entry", confirm removal of `eplan-vm`, then restart the clients. Alternatively, run `& $eplanMcp client-config --vm --remove` to remove recorded remote entries; no address is needed. In the VM, choose "Stop service" → "Remove network access" (requires UAC consent).

For the CLI alternative, stop the foreground service in the VM with Ctrl+C, then remove the rule in administrator PowerShell. Use the same port, host IPv4, and program path used to create the rule.

```powershell
& 'D:\EplanMcp\eplanmcp.exe' vm-firewall remove --port 8766 --host-address 192.168.56.1
```

---
EplanMcp is an independent open-source project. It is not affiliated with, endorsed or sponsored by EPLAN GmbH & Co. KG. EPLAN, EPLAN Electric P8 and EPLAN Pro Panel are trademarks or registered trademarks of EPLAN GmbH & Co. KG.
