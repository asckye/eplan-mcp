"""Spike S2, undo-stack sequence: simulates "engineer edits, AI edits, engineer presses Ctrl+Z" with
separate action calls, to learn which write pattern keeps EPLAN's undo history usable.

    third_party/eplan-rag-mcp/.venv/Scripts/python.exe spikes/S2-transactions/run_s2_undo.py "Electric P8" --revision r3

Each op is one committed page creation, wrapped as /Undo says:
  outside  = UndoStep around a SafetyPoint (the pattern that blocks rollback)
  insideSp = UndoStep inside the SafetyPoint
  tx       = UndoStep around a Transaction
  none     = no UndoStep at all
"""
import json
import os

import run_s2 as base  # shares constants (REVISION, paths) and helpers with the probe driver

SEQUENCE = [
    ("op", 61, "outside", "baseline: an earlier change in the history"),
    ("op", 62, "insideSp", "AI change, UndoStep inside SafetyPoint"),
    ("undo", None, None, "Ctrl+Z #1: expect 62 gone, 61 kept"),
    ("op", 63, "tx", "AI change, UndoStep around Transaction"),
    ("undo", None, None, "Ctrl+Z #2: expect 63 gone, 61 kept"),
    ("undo", None, None, "Ctrl+Z #3: expect 61 gone (history before the AI changes still usable)"),
    ("op", 64, "outside", "baseline 2"),
    ("op", 65, "none", "change WITHOUT an UndoStep"),
    ("undo", None, None, "Ctrl+Z #4: what does it undo after a change without UndoStep?"),
    ("undo", None, None, "Ctrl+Z #5: is baseline 2 still undoable?"),
]


def main():
    os.makedirs(base.OUT_DIR, exist_ok=True)
    m = base.get_manager()
    ok, _ = base.step("connect", m.connect)
    if not ok:
        return
    base.step("copyScratchProject", base.copy_scratch)
    timeline = []
    report = os.path.join(base.OUT_DIR, "s2-undo-op.json")
    try:
        base.step("registerAddIn", lambda: m.execute_action(f'EplApiModuleAction /register:"{base.ADDIN_DLL}"', quiet_mode=True))
        base.step("openScratchProject", lambda: m.execute_action(f'ProjectOpen /Project:"{base.SCRATCH_PROJECT}.elk"', quiet_mode=True))
        for kind, counter, undo, note in SEQUENCE:
            if kind == "op":
                base.step(f"op {counter} {undo}", lambda c=counter, u=undo: m.execute_action(
                    f'{base.ACTION} /Mode:op /Counter:{c} /Undo:{u} /Report:"{report}" /ScratchRoot:"{base.SCRATCH_ROOT}"'))
            else:
                base.step("Ctrl+Z", lambda: m.execute_action("XEsUndoAction"))
                base.step("check", lambda: m.execute_action(
                    f'{base.ACTION} /Mode:check /Report:"{report}" /ScratchRoot:"{base.SCRATCH_ROOT}"'))
            state = json.load(open(report, encoding="utf-8")) if os.path.exists(report) else {}
            pages = state.get("spikePages")
            timeline.append({"action": kind, "counter": counter, "undo": undo, "note": note, "spikePagesAfter": pages})
            print(f"  -> {note}: pages now {pages}", flush=True)
    finally:
        base.step("closeProject", lambda: m.execute_action("XPrjActionProjectClose", quiet_mode=True))
        base.step("unregisterAddIn", lambda: m.execute_action(f'EplApiModuleAction /unregister:"{base.ASSEMBLY_TITLE}"', quiet_mode=True))

    with open(os.path.join(base.OUT_DIR, "s2-undo-sequence.json"), "w", encoding="utf-8") as f:
        json.dump({"timeline": timeline, "driverSteps": base.steps}, f, ensure_ascii=False, indent=1, default=str)


if __name__ == "__main__":
    main()
