"""GATED DRY RUN (no training, no evaluation): executes the notebook's code cells in order inside a SCRATCH workspace to prove that the pipeline
imports, reads the real manifest/models, builds the frozen split/templates, runs the V5 baseline inference and the YOLO engine on a small preview,
and that every guarded stage stops with a clear message when annotations are missing. It stops at the first uncaught exception.
Usage:  LUMYVUE_V6_WORKSPACE=/tmp/lumyvue_dryrun LUMYVUE_HEADLESS=1 python dry_run.py <notebook.ipynb> [last_cell_index]"""
import json, sys, time, traceback, io, contextlib
nb = json.load(open(sys.argv[1], encoding="utf-8")); last = int(sys.argv[2]) if len(sys.argv) > 2 and sys.argv[2].isdigit() else 10 ** 9
extra = sys.argv[sys.argv.index("--extra") + 1] if "--extra" in sys.argv else None
ns = {"__name__": "__main__"}
t_all = time.time(); status = []
for i, c in enumerate(nb["cells"]):
    if c["cell_type"] != "code" or i > last: continue
    src = "".join(c["source"]); t0 = time.time()
    print(f"\n######## CELL {i}: {src.strip().splitlines()[0][:90] if src.strip() else ''}", flush=True)
    try:
        exec(compile(src, f"<cell {i}>", "exec"), ns)
        status.append((i, "ok", round(time.time() - t0, 1)))
    except BaseException as e:
        status.append((i, type(e).__name__, round(time.time() - t0, 1)))
        print(f"\n!!!! STOPPED at cell {i} with {type(e).__name__}: {str(e)[:700]}", flush=True)
        traceback.print_exc(limit=6)
        break
if extra and all(st[1] == "ok" for st in status):
    print("\n######## EXTRA forward-only smoke checks:", extra, flush=True)
    exec(compile(open(extra, encoding="utf-8").read(), extra, "exec"), ns)
print("\nSUMMARY:", json.dumps(status[-8:]), "| total seconds:", round(time.time() - t_all))
