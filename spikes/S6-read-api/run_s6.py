"""Spike S6 driver: register the spike add-in in the running EPLAN, open a scratch copy of the sample
project, run the read probes twice (cold and warm) and a check run, then always close and unregister.

Remote Client access reuses the connection layer of covagashi/eplan-rag-mcp (MIT), checked out
locally under third_party/ (not part of this repository). Run with that checkout's venv:

    third_party/eplan-rag-mcp/.venv/Scripts/python.exe spikes/S6-read-api/run_s6.py ["Electric P8"]

The argument names the variant EPLAN runs in (for its log). Outputs go to .local/spikes/s6/ (raw,
machine-specific; git-ignored).
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

# EPLAN cannot unload an add-in assembly (api-notes A8), so a rebuild tested in the same EPLAN session gets a
# revision suffix: build with -p:AssemblyName=EplanMcp.Spikes.S6<rev> and pass --revision <rev> (as in S2).
REVISION = sys.argv[sys.argv.index("--revision") + 1] if "--revision" in sys.argv else ""
ASSEMBLY_TITLE = "EplanMcp.Spikes.S6" + REVISION
ACTION = "EplanMcpSpikeS6" + REVISION
ADDIN_DLL = os.path.join(REPO, "spikes", "S6-read-api", "bin", "Release", "net481", ASSEMBLY_TITLE + ".dll")
TEMPLATE = r"C:\Users\Public\EPLAN\Data\项目\Company name\EPLAN_Sample_Project"
# Not under %LOCALAPPDATA% (MSIX redirection, api-notes A6); .local/ is git-ignored.
SCRATCH_ROOT = os.path.join(REPO, ".local", "scratch")
SCRATCH_PROJECT = os.path.join(SCRATCH_ROOT, "s6", "S6_Sample")
OUT_DIR = os.path.join(REPO, ".local", "spikes", "s6")
VARIANT_LOG = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith("--") else "Electric P8"
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


def snapshot():
    """Size and modification time of every file of the scratch project, to see what reading changes."""
    files = {}
    for base in (SCRATCH_PROJECT + ".elk", SCRATCH_PROJECT + ".edb"):
        paths = [base] if os.path.isfile(base) else [os.path.join(d, f) for d, _, fs in os.walk(base) for f in fs]
        for p in paths:
            st = os.stat(p)
            files[os.path.relpath(p, SCRATCH_ROOT)] = (st.st_size, st.st_mtime_ns)
    return files


def diff(before, after):
    changed = sorted(k for k in after if k in before and after[k] != before[k])
    return {
        "changed": changed[:50],
        "changedCount": len(changed),
        "added": sorted(set(after) - set(before))[:50],
        "removed": sorted(set(before) - set(after))[:50],
    }


def log_messages_since(started):
    con = sqlite3.connect("file:" + EPL_LOG + "?mode=ro", uri=True)
    sid = con.execute("select max(Id) from TSysSession_v100").fetchone()[0]
    rows = con.execute(
        "select Time, ErrorLevel, Module, Description from TSysMsg_v100 where SessionId=? order by Id",
        (sid,),
    ).fetchall()
    return [{"time": r[0], "level": r[1], "module": r[2], "text": r[3]} for r in rows if r[0] >= started]


def run_action(mode, report):
    return lambda: get_manager().execute_action(f'{ACTION} /Mode:{mode} /Report:"{report}" /ScratchRoot:"{SCRATCH_ROOT}"')


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    started = datetime.now().strftime("%H:%M:%S")
    reports = {name: os.path.join(OUT_DIR, f"s6-{name}.json") for name in ("noop", "readCold", "readWarm", "sysmsg", "sysmsgAll", "sysmsgAfterException", "sysmsgReuse", "checkRun", "close")}
    for path in reports.values():
        if os.path.exists(path):
            os.remove(path)

    m = get_manager()
    ok, _ = step("connect", m.connect)
    if not ok:
        return
    files = {}
    try:
        step("registerAddIn", lambda: m.execute_action(f'EplApiModuleAction /register:"{ADDIN_DLL}"', quiet_mode=True))
        # A copy left open by an earlier run must be closed before its files are replaced.
        step("closeLeftoverScratchProject", run_action("close", os.path.join(OUT_DIR, "s6-closeLeftover.json")))
        ok, _ = step("copyScratchProject", copy_scratch)
        if not ok:
            return
        step("openScratchProject", lambda: m.execute_action(f'ProjectOpen /Project:"{SCRATCH_PROJECT}.elk"', quiet_mode=True))
        files["afterOpen"] = snapshot()
        # Opening may write the project's user data a little later; wait, so a change seen after the
        # reads can be told apart from one that opening causes anyway.
        time.sleep(10)
        files["afterIdle"] = snapshot()
        step("noop", run_action("noop", reports["noop"]))
        files["afterNoop"] = snapshot()
        step("readCold", run_action("read", reports["readCold"]))
        step("readWarm", run_action("read", reports["readWarm"]))
        files["afterReads"] = snapshot()
        step("sysmsg", run_action("sysmsg", reports["sysmsg"]))
        step("sysmsgAll", run_action("sysmsgall", reports["sysmsgAll"]))
        step("sysmsgAfterException", run_action("sysmsgafterexception", reports["sysmsgAfterException"]))
        step("sysmsgReuse", run_action("sysmsgreuse", reports["sysmsgReuse"]))
        step("checkRun", run_action("checkrun", reports["checkRun"]))
        files["afterCheck"] = snapshot()
    finally:
        # Close only the scratch copy, found by path inside the action. XPrjActionProjectClose closes the
        # selected project, which in the first run was the engineer's own project, not the copy.
        step("closeScratchProject", run_action("close", reports["close"]))
        step("unregisterAddIn", lambda: m.execute_action(f'EplApiModuleAction /unregister:"{ASSEMBLY_TITLE}"', quiet_mode=True))

    out = {
        "driverSteps": steps,
        "reports": {k: (json.load(open(v, encoding="utf-8")) if os.path.exists(v) else None) for k, v in reports.items()},
        "filesChangedWhileIdle": diff(files["afterOpen"], files["afterIdle"]) if "afterIdle" in files else None,
        "filesChangedByNoop": diff(files["afterIdle"], files["afterNoop"]) if "afterNoop" in files else None,
        "filesChangedByReads": diff(files["afterNoop"], files["afterReads"]) if "afterReads" in files else None,
        "filesChangedByCheck": diff(files["afterReads"], files["afterCheck"]) if "afterCheck" in files else None,
        "eplanLog": log_messages_since(started),
    }
    with open(os.path.join(OUT_DIR, "s6-run.json"), "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1, default=str)
    print("reports:", {k: v is not None for k, v in out["reports"].items()}, "| EPLAN log entries:", len(out["eplanLog"]))
    print("written:", os.path.join(OUT_DIR, "s6-run.json"))


if __name__ == "__main__":
    main()
