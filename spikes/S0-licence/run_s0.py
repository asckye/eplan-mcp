"""Spike S0 driver: register the unsigned spike add-in in the running EPLAN, run it against a
scratch copy of a >5-page project, then always unregister and close.

Remote Client access reuses the connection layer of covagashi/eplan-rag-mcp (MIT), checked out
locally under third_party/ (not part of this repository). Run with that checkout's venv:

    third_party/eplan-rag-mcp/.venv/Scripts/python.exe spikes/S0-licence/run_s0.py

Outputs go to .local/spikes/s0/ (raw, machine-specific; git-ignored).
"""
import json
import os
import shutil
import sqlite3
import sys
import time
from datetime import datetime

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CONNECTION_LAYER = os.path.join(REPO, "third_party", "eplan-rag-mcp", "eplan-p8-mcp-server", "mcp_server")
sys.path.insert(0, CONNECTION_LAYER)
from eplan_connection import get_manager  # noqa: E402

ADDIN_DLL = os.path.join(REPO, "spikes", "S0-licence", "bin", "Release", "net481", "EplanMcp.Spikes.S0.dll")
ASSEMBLY_TITLE = "EplanMcp.Spikes.S0"
TEMPLATE = r"C:\Users\Public\EPLAN\Data\项目\Company name\EPLAN_Sample_Project"
# Not under %LOCALAPPDATA%: when this script runs inside an MSIX-packaged app (e.g. Claude Desktop),
# AppData writes are redirected to the package store and EPLAN cannot see them. .local/ is git-ignored.
SCRATCH_ROOT = os.path.join(REPO, ".local", "scratch")
SCRATCH_PROJECT = os.path.join(SCRATCH_ROOT, "s0", "S0_Sample")
OUT_DIR = os.path.join(REPO, ".local", "spikes", "s0")
VARIANT_LOG = sys.argv[1] if len(sys.argv) > 1 else "Electric P8"
EPL_LOG = os.path.join(r"C:\Users\Public\EPLAN", VARIANT_LOG, "EplLog.eldb")

steps = []


def step(name, func):
    t0 = time.time()
    try:
        result = func()
        ok = not (isinstance(result, dict) and result.get("success") is False)
    except Exception as exc:  # noqa: BLE001 - a spike records every failure
        result, ok = {"exception": f"{type(exc).__name__}: {exc}"}, False
    steps.append({"step": name, "ok": ok, "seconds": round(time.time() - t0, 2), "result": result})
    print(f"[{'ok ' if ok else 'ERR'}] {name}: {json.dumps(result, ensure_ascii=False, default=str)[:300]}", flush=True)
    return ok, result


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
    rows = con.execute(
        "select Time, ErrorLevel, Module, Description from TSysMsg_v100 where SessionId=? order by Id",
        (sid,),
    ).fetchall()
    return [
        {"time": r[0], "level": r[1], "module": r[2], "text": r[3]}
        for r in rows
        if r[0] >= started
    ]


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    started = datetime.now().strftime("%H:%M:%S")
    report_path = os.path.join(OUT_DIR, "s0-report.json")
    if os.path.exists(report_path):
        os.remove(report_path)

    m = get_manager()
    ok, _ = step("connect", m.connect)
    if not ok:
        return
    step("copyScratchProject", copy_scratch)
    try:
        step("registerAddIn", lambda: m.execute_action(f'EplApiModuleAction /register:"{ADDIN_DLL}"', quiet_mode=True))
        step("openScratchProject", lambda: m.execute_action(f'ProjectOpen /Project:"{SCRATCH_PROJECT}.elk"', quiet_mode=True))
        step("runSpikeAction", lambda: m.execute_action(
            f'EplanMcpSpikeS0 /Report:"{report_path}" /ScratchRoot:"{SCRATCH_ROOT}" /Write:1'))
    finally:
        step("closeProject", lambda: m.execute_action("XPrjActionProjectClose", quiet_mode=True))
        step("unregisterAddIn", lambda: m.execute_action(f'EplApiModuleAction /unregister:"{ASSEMBLY_TITLE}"', quiet_mode=True))

    spike_report = json.load(open(report_path, encoding="utf-8")) if os.path.exists(report_path) else None
    messages = log_messages_since(started)
    out = {"driverSteps": steps, "spikeReport": spike_report, "eplanLog": messages}
    with open(os.path.join(OUT_DIR, "s0-run.json"), "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1, default=str)
    print(f"spike report: {'present' if spike_report else 'MISSING'}; EPLAN log entries: {len(messages)}")
    print("written:", os.path.join(OUT_DIR, "s0-run.json"))


if __name__ == "__main__":
    main()
