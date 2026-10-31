"""Static validation of the V6 notebook (nothing is executed): JSON structure, Python syntax per cell, undefined names in execution order
(pyflakes over the concatenated code cells), forbidden patterns, hard-coded machine paths, unguarded writes, imports, section coverage.
Usage: python validate_notebook.py <notebook.ipynb> [report.json]"""
import ast, io, json, re, sys, importlib.util
import nbformat
from pyflakes import api as pf, reporter as pr

nb_path = sys.argv[1]; out_path = sys.argv[2] if len(sys.argv) > 2 else None
nb = nbformat.read(nb_path, as_version=4); nbformat.validate(nb)
rep = dict(notebook=nb_path, cells=len(nb.cells), code=sum(c.cell_type == "code" for c in nb.cells), markdown=sum(c.cell_type == "markdown" for c in nb.cells), problems=[], warnings=[])
P = lambda m: rep["problems"].append(m); W = lambda m: rep["warnings"].append(m)

# 1. syntax per cell + outputs must be empty (no fabricated results)
code_cells = [(i, c.source) for i, c in enumerate(nb.cells) if c.cell_type == "code"]
for i, c in enumerate(nb.cells):
    if c.cell_type == "code":
        try: ast.parse(c.source)
        except SyntaxError as e: P(f"cell {i}: SyntaxError {e.msg} line {e.lineno}")
        if c.get("outputs"): P(f"cell {i}: has stored outputs (the notebook must ship without results)")
        if c.get("execution_count"): P(f"cell {i}: has an execution count")

# 2. pyflakes on the concatenation (module-level execution order); map lines back to cells
lines, owner = [], []
for i, src in code_cells:
    for ln in src.split("\n"): lines.append(ln); owner.append(i)
    lines.append(""); owner.append(i)
code = "\n".join(lines)
buf_o, buf_e = io.StringIO(), io.StringIO()
pf.check(code, "notebook", pr.Reporter(buf_o, buf_e))
for m in buf_o.getvalue().splitlines():
    mm = re.match(r"notebook:(\d+):\d+:? (.*)", m)
    if not mm: continue
    ln, text = int(mm.group(1)), mm.group(2)
    if "imported but unused" in text or "assigned to but never used" in text or "redefinition of unused" in text or "f-string is missing placeholders" in text or "unable to detect undefined names" in text or "may be undefined, or defined from star" in text: continue
    if "undefined name" in text: P(f"cell {owner[ln - 1]} line~{ln}: {text}")
    else: W(f"cell {owner[ln - 1]}: {text}")

# 3. forbidden / suspicious patterns in code
for i, src in code_cells:
    for pat, why in [(r"\bTODO\b|\bFIXME\b|NotImplementedError|raise NotImplemented", "unfinished marker"), (r"/home/bautista|C:\\\\Users|C:/Users/Bautista|/mnt/c/Users/Bautista", "hard-coded machine path"),
                     (r"except\s*:\s*\n\s*pass", "bare except: pass (silent failure)"), (r"except Exception:\s*\n\s*pass", "except Exception: pass (silent failure)")]:
        if re.search(pat, src): P(f"cell {i}: {why}")
    for ln, line in enumerate(src.split("\n")):
        if re.search(r"^\s*pass\s*$", line) and "except" not in src.split("\n")[max(ln - 1, 0)]: W(f"cell {i} line {ln + 1}: bare 'pass'")
# silent exception swallowing: 'except ...: pass' on one line
for i, src in code_cells:
    if re.search(r"except[^\n]*:\s*pass\b", src): W(f"cell {i}: one-line 'except ...: pass' (check it is a documented, harmless case)")

# 4. writes must go through the guarded helpers
WRITE = re.compile(r"(open\([^)]*['\"][wa]b?['\"]|\.to_csv\(|\.save\(|np\.save\(|np\.savez|cv2\.imwrite\(|\.savefig\(|shutil\.copy|os\.remove|\.write\()")
for i, src in code_cells:
    for ln, line in enumerate(src.split("\n")):
        if WRITE.search(line) and not re.search(r"safe_path|export_path|write_text|write_json|write_csv|def |#.*(read|temp)|tempfile|StringIO|buf\.write|f\.write\(\"\\n\"\.join\(buf\)|w\) as f|AUDIT_PATH|DET_PATH|\.write\(json\.dumps\(rec\)|\.write\(\"\\n\"", line):
            W(f"cell {i} line {ln + 1}: write not visibly guarded: {line.strip()[:110]}")

# 5. imports resolvable
mods = set()
for i, src in code_cells:
    for n in ast.walk(ast.parse(src)):
        if isinstance(n, ast.Import): mods |= {a.name.split(".")[0] for a in n.names}
        if isinstance(n, ast.ImportFrom) and n.module and n.level == 0: mods.add(n.module.split(".")[0])
rep["imports"] = sorted(mods)
rep["unresolvable_imports_in_this_interpreter"] = [m for m in sorted(mods) if importlib.util.find_spec(m) is None]

# 6. markdown must not claim results
claims = re.compile(r"(achiev|improv|outperform|increas)\w*\s+(accuracy|f1|map|auc|recall|precision)|\b(accuracy|f1|recall|precision|auc)\s+(of|=|:)\s*0?\.\d{2}|\b\d{2}(\.\d)?\s*%\s+(accuracy|recall|precision)", re.I)
for i, c in enumerate(nb.cells):
    if c.cell_type == "markdown":
        for m in claims.finditer(c.source):
            W(f"markdown cell {i}: possible result claim -> '{m.group(0)}' (verify it is a V5/audit fact, not a V6 result)")

# 7. required sections present
need = ["Research introduction", "baselines", "Environment", "GPU", "Dependency", "Reproducib", "path configuration", "inventory", "integrity", "Duplicate", "Label", "Multilabel", "region annotation", "preprocessing",
        "parity", "V5 baseline inference", "confusion", "Hard-example", "review queue", "global MobileNetV2", "Global classifier training", "fine-tuning", "YOLO", "Ensemble", "ROI extraction", "ROI training", "ROI MobileNetV2",
        "feature aggregation", "Fusion", "Calibration", "Stage 1", "Stage 2", "verification", "final", "abstention", "OOD", "Classification evaluation", "localisation", "ablation", "Robustness", "Grad-CAM", "learning curves",
        "Confusion", "ROC", "calibration", "Risk", "V5 versus V6", "ONNX", "Keras-versus-ONNX", "metadata", "audit", "unit", "Chapter IV", "artifact summary"]
alltext = "\n".join(c.source for c in nb.cells if c.cell_type == "markdown").lower() + "\n".join(src for _, src in code_cells).lower()
rep["missing_section_keywords"] = [k for k in need if k.lower() not in alltext]

# 8. order of key definitions (cell index) for the readiness report
KEY = ["CFG =", "def safe_path", "DF =", "def build_groups", "SPLIT_PATH", "def load_annotations", "def preprocess_global", "V5_SESS =", "def mine_hard_examples", "class MaskedBCE", "def run_phase", "DETECTORS =",
       "def detect_condition", "def build_roi_candidates", "def build_fusion", "def crossfit_fusion", "def verification_cycle", "def decide", "def assess_image", "open_test_once", "def build_deployment_metadata", "class TestV6"]
rep["definition_order"] = {k: next((i for i, s in code_cells if k in s), None) for k in KEY}
order = [v for v in rep["definition_order"].values() if v is not None]
if order != sorted(order): P("key definitions are not in execution order: " + json.dumps(rep["definition_order"]))
if any(v is None for v in rep["definition_order"].values()): P("missing key definitions: " + str([k for k, v in rep["definition_order"].items() if v is None]))

rep["ok"] = not rep["problems"]
print(json.dumps({k: rep[k] for k in ["cells", "code", "markdown", "ok"]}, indent=1)); print("PROBLEMS:", len(rep["problems"]), "WARNINGS:", len(rep["warnings"]))
for p in rep["problems"][:60]: print("  PROBLEM", p)
for w in rep["warnings"][:50]: print("  warn", w)
print("unresolvable imports here:", rep["unresolvable_imports_in_this_interpreter"], "| missing section keywords:", rep["missing_section_keywords"])
if out_path: json.dump(rep, open(out_path, "w"), indent=1)
sys.exit(0 if rep["ok"] else 1)
