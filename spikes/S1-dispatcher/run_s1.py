"""Spike S1 driver: main-thread dispatch from a background thread inside EPLAN.

    third_party/eplan-rag-mcp/.venv/Scripts/python.exe spikes/S1-dispatcher/run_s1.py "Electric P8" [--revision r2]

Registers the spike add-in in the running EPLAN, opens a scratch copy of the sample project, runs each
background scenario (the action returns immediately; the report appears when the background thread is
done), then an undo sequence where writes come from the dispatcher instead of an action. Always closes
the project and unregisters the add-in. The "modal" scenario shows a message box in EPLAN for ~1 s and
closes it again. Remote Client access reuses covagashi/eplan-rag-mcp (MIT) from third_party/.
Outputs go to .local/spikes/s1/ (git-ignored).
"""
import json
import os
import shutil
import sqlite3
import subprocess
import sys
import time
from datetime import datetime

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
sys.path.insert(0, os.path.join(REPO, "third_party", "eplan-rag-mcp", "eplan-p8-mcp-server", "mcp_server"))
from eplan_connection import get_manager  # noqa: E402

REVISION = sys.argv[sys.argv.index("--revision") + 1] if "--revision" in sys.argv else ""
ASSEMBLY_TITLE = "EplanMcp.Spikes.S1" + REVISION
ACTION = "EplanMcpSpikeS1" + REVISION
ADDIN_DLL = os.path.join(REPO, "spikes", "S1-dispatcher", "bin", "Release", "net481", ASSEMBLY_TITLE + ".dll")
TEMPLATE = r"C:\Users\Public\EPLAN\Data\项目\Company name\EPLAN_Sample_Project"
SCRATCH_ROOT = os.path.join(REPO, ".local", "scratch")  # not %LOCALAPPDATA%: see spike S0 / ADR-0008
SCRATCH_PROJECT = os.path.join(SCRATCH_ROOT, "s1", "S1_Sample")
OUT_DIR = os.path.join(REPO, ".local", "spikes", "s1")
VARIANT_LOG = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith("--") else "Electric P8"
EPL_LOG = os.path.join(r"C:\Users\Public\EPLAN", VARIANT_LOG, "EplLog.eldb")

BACKGROUND_SCENARIOS = (sys.argv[sys.argv.index("--scenarios") + 1].split(",") if "--scenarios" in sys.argv
                        else ["probe", "idle", "busy", "exception", "rollback", "modal", "modalGuard"])
RUN_UNDO_SEQUENCE = "--scenarios" not in sys.argv or "--undo" in sys.argv
steps, results = [], {}


def step(name, func):
    t0 = time.time()
    try:
        result = func()
        ok = not (isinstance(result, dict) and result.get("success") is False)
    except Exception as exc:  # noqa: BLE001 - a spike records every failure
        result, ok = {"exception": f"{type(exc).__name__}: {exc}"}, False
    steps.append({"step": name, "ok": ok, "seconds": round(time.time() - t0, 2), "result": result})
    print(f"[{'ok ' if ok else 'ERR'}] {name} ({time.time() - t0:.1f}s)", flush=True)
    return ok, result


class EplanDied(RuntimeError):
    pass


def eplan_alive():
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq EPLAN.exe", "/NH"], capture_output=True, text=True).stdout
    return "eplan.exe" in out.lower()


def report_path(name):
    return os.path.join(OUT_DIR, f"s1-{name}.json")


def run_action(m, mode, name, extra=""):
    path = report_path(name)
    if os.path.exists(path):
        os.remove(path)
    if not eplan_alive():
        raise EplanDied(f"EPLAN is no longer running (before {mode} {name})")
    cmd = f'{ACTION} /Mode:{mode} /Report:"{path}" /ScratchRoot:"{SCRATCH_ROOT}" {extra}'.strip()
    step(f"{mode} {name}", lambda: m.execute_action(cmd))
    return path


def wait_for(path, timeout=40):
    deadline = time.time() + timeout
    while time.time() < deadline:
        if not eplan_alive():
            raise EplanDied(f"EPLAN terminated while waiting for {os.path.basename(path)}")
        if os.path.exists(path):
            time.sleep(0.2)  # the writer may still be flushing
            try:
                return json.load(open(path, encoding="utf-8"))
            except json.JSONDecodeError:
                pass
        time.sleep(0.2)
    return {"timeout": f"no report after {timeout}s"}


def copy_scratch():
    if os.path.exists(SCRATCH_PROJECT + ".edb"):
        shutil.rmtree(SCRATCH_PROJECT + ".edb")
    os.makedirs(os.path.dirname(SCRATCH_PROJECT), exist_ok=True)
    shutil.copy2(TEMPLATE + ".elk", SCRATCH_PROJECT + ".elk")
    shutil.copytree(TEMPLATE + ".edb", SCRATCH_PROJECT + ".edb")
    return {"project": SCRATCH_PROJECT + ".elk"}


def log_messages_since(started):
    con = sqlite3.connect("file:" + EPL_LOG + "?mode=ro", uri=True)
    sid = con.execute("select max(Id) from TSysSession_v100").fetchone()[0]
    rows = con.execute("select Time, ErrorLevel, Module, Description from TSysMsg_v100 where SessionId=? order by Id", (sid,)).fetchall()
    return [{"time": r[0], "level": r[1], "module": r[2], "text": r[3]} for r in rows if r[0] >= started]


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    started = datetime.now().strftime("%H:%M:%S")
    m = get_manager()
    ok, _ = step("connect", m.connect)
    if not ok:
        return
    step("copyScratchProject", copy_scratch)
    timeline = []
    try:
        # A crashed earlier run cannot unregister; clear any stale registration first (failure is fine).
        step("unregisterStale", lambda: m.execute_action(f'EplApiModuleAction /unregister:"{ASSEMBLY_TITLE}"', quiet_mode=True))
        step("registerAddIn", lambda: m.execute_action(f'EplApiModuleAction /register:"{ADDIN_DLL}"', quiet_mode=True))
        step("openScratchProject", lambda: m.execute_action(f'ProjectOpen /Project:"{SCRATCH_PROJECT}.elk"', quiet_mode=True))

        for scenario in BACKGROUND_SCENARIOS:
            path = run_action(m, "bg", scenario, f"/Scenario:{scenario} /Counter:10")
            results[scenario] = wait_for(path)
            print(f"    {scenario}: {'fatal' if 'fatal' in results[scenario] else 'timeout' if 'timeout' in results[scenario] else 'report ok'}")

        # Undo sequence with writes coming from the dispatcher (not from inside an action).
        def snapshot(label):
            state = wait_for(run_action(m, "check", "check"))
            timeline.append({"after": label, "spikePages": state.get("spikePages")})
            print(f"    after {label}: {state.get('spikePages')}")

        for baseline, ai, scenario in ((61, 62, "undoCommit"), (64, 65, "noUndo")) if RUN_UNDO_SEQUENCE else ():
            wait_for(run_action(m, "op", f"op{baseline}", f"/Counter:{baseline}"))
            snapshot(f"baseline {baseline} (action, with UndoStep)")
            results[f"{scenario}{ai}"] = wait_for(run_action(m, "bg", f"{scenario}{ai}", f"/Scenario:{scenario} /Counter:{ai}"))
            snapshot(f"dispatcher write {ai} ({scenario})")
            step("Ctrl+Z", lambda: m.execute_action("XEsUndoAction"))
            snapshot("Ctrl+Z #1")
            step("Ctrl+Z", lambda: m.execute_action("XEsUndoAction"))
            snapshot("Ctrl+Z #2")

        results["cleanup"] = wait_for(run_action(m, "cleanup", "cleanup"))
    except EplanDied as died:
        results["aborted"] = str(died)
        print("ABORTED:", died, flush=True)
    finally:
        if eplan_alive():
            step("closeProject", lambda: m.execute_action("XPrjActionProjectClose", quiet_mode=True))
            step("unregisterAddIn", lambda: m.execute_action(f'EplApiModuleAction /unregister:"{ASSEMBLY_TITLE}"', quiet_mode=True))
        else:
            print("EPLAN is not running: the add-in stays registered; unregister it after restarting EPLAN.", flush=True)

    try:
        log = log_messages_since(started)
    except sqlite3.Error as exc:
        log = [{"error": str(exc)}]
    out = {"results": results, "undoTimeline": timeline, "driverSteps": steps, "eplanLog": log}
    with open(os.path.join(OUT_DIR, "s1-run.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(out, f, ensure_ascii=False, indent=1, default=str)
    print("written:", os.path.join(OUT_DIR, "s1-run.json"))


if __name__ == "__main__":
    main()
