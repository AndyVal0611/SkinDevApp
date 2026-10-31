"""Assemble the V6 notebook from notebook_src/*.cellsrc ('#@@ md' / '#@@ code' separators). Pure assembly: nothing is executed.
Usage: python build_notebook.py [output.ipynb]"""
import glob, os, sys, hashlib
import nbformat
from nbformat.v4 import new_notebook, new_markdown_cell, new_code_cell

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "notebook_src")
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "LUMYVUE_V6_Hybrid_Three_YOLO_MobileNetV2_Training.ipynb")


def parse(path):
    cells, kind, buf = [], None, []
    for line in open(path, encoding="utf-8").read().split("\n"):
        if line.startswith("#@@ "):
            if kind: cells.append((kind, "\n".join(buf).strip("\n")))
            kind, buf = line[4:].strip(), []
        else:
            buf.append(line)
    if kind: cells.append((kind, "\n".join(buf).strip("\n")))
    return cells


def build():
    nb = new_notebook()
    for f in sorted(glob.glob(os.path.join(SRC, "*.cellsrc"))):
        for kind, text in parse(f):
            assert kind in ("md", "code"), (f, kind)
            if not text.strip(): continue
            c = new_markdown_cell(text) if kind == "md" else new_code_cell(text)
            c.metadata["source_file"] = os.path.basename(f)
            nb.cells.append(c)
    for i, c in enumerate(nb.cells):
        c["id"] = hashlib.md5(f"{i}:{c.source[:80]}".encode()).hexdigest()[:12]
    nb.metadata["kernelspec"] = {"display_name": "Python 3 (ipykernel)", "language": "python", "name": "python3"}
    nb.metadata["language_info"] = {"name": "python"}
    nb.metadata["lumyvue"] = {"notebook": "V6 hybrid three-detector", "executed_by_author": False, "contains_results": False}
    nbformat.validate(nb)
    return nb


if __name__ == "__main__":
    nb = build()
    nbformat.write(nb, OUT)
    n_code = sum(c.cell_type == "code" for c in nb.cells)
    print(f"wrote {OUT}: {len(nb.cells)} cells ({n_code} code, {len(nb.cells) - n_code} markdown)")
