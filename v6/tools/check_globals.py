"""Finds names that (a) are assigned at module level in MORE THAN ONE cell and (b) are read as free/global variables inside functions or lambdas.
Such names can be silently clobbered by a later cell (a real bug class found by the dry run: a loop variable `S` overwrote an image-size constant).
Usage: python check_globals.py notebook.ipynb"""
import ast, sys, json, collections
nb = json.load(open(sys.argv[1], encoding="utf-8"))
assigned = collections.defaultdict(set); free_reads = collections.defaultdict(set)


def targets(t):
    if isinstance(t, ast.Name): yield t.id
    elif isinstance(t, (ast.Tuple, ast.List)):
        for e in t.elts: yield from targets(e)
    elif isinstance(t, ast.Starred): yield from targets(t.value)


class Mod(ast.NodeVisitor):
    def __init__(self, cell): self.cell = cell
    def visit_Assign(self, n):
        for t in n.targets:
            for nm in targets(t): assigned[nm].add(self.cell)
        self.generic_visit(n)
    def visit_AnnAssign(self, n):
        for nm in targets(n.target): assigned[nm].add(self.cell)
        self.generic_visit(n)
    def visit_AugAssign(self, n):
        for nm in targets(n.target): assigned[nm].add(self.cell)
    def visit_For(self, n):
        for nm in targets(n.target): assigned[nm].add(self.cell)
        for b in n.body + n.orelse: self.visit(b)
    def visit_With(self, n):
        for it in n.items:
            if it.optional_vars is not None:
                for nm in targets(it.optional_vars): assigned[nm].add(self.cell)
        for b in n.body: self.visit(b)
    def visit_FunctionDef(self, n): assigned[n.name].add(self.cell); self.func(n)
    visit_AsyncFunctionDef = visit_FunctionDef
    def visit_ClassDef(self, n): assigned[n.name].add(self.cell)
    def visit_Lambda(self, n): self.func(n)
    def visit_Import(self, n):
        for a in n.names: assigned[(a.asname or a.name).split(".")[0]].add(self.cell)
    visit_ImportFrom = visit_Import
    def func(self, n):
        local = set()
        args = n.args
        for a in args.args + args.kwonlyargs + args.posonlyargs: local.add(a.arg)
        if args.vararg: local.add(args.vararg.arg)
        if args.kwarg: local.add(args.kwarg.arg)
        for sub in ast.walk(n):
            if isinstance(sub, ast.Name) and isinstance(sub.ctx, (ast.Store, ast.Del)): local.add(sub.id)
            if isinstance(sub, (ast.FunctionDef, ast.ClassDef)) and sub is not n: local.add(sub.name)
            if isinstance(sub, ast.ExceptHandler) and sub.name: local.add(sub.name)
            if isinstance(sub, ast.arg): local.add(sub.arg)
        for sub in ast.walk(n):
            if isinstance(sub, ast.Name) and isinstance(sub.ctx, ast.Load) and sub.id not in local: free_reads[sub.id].add(self.cell)


for i, c in enumerate(nb["cells"]):
    if c["cell_type"] == "code":
        Mod(i).visit(ast.parse("".join(c["source"])))
bad = {n: (sorted(cells), sorted(free_reads[n])) for n, cells in assigned.items() if len(cells) > 1 and n in free_reads and not n.startswith("__")}
SAFE = {"display", "np", "pd", "plt", "cv2", "tf", "keras", "os", "json", "re", "math", "time", "datetime", "hashlib", "glob", "collections"}
for n, (a, r) in sorted(bad.items()):
    if n in SAFE: continue
    print(f"{n:24s} assigned in cells {a}  read as a global in functions of cells {r[:8]}")
print("names flagged:", len([n for n in bad if n not in SAFE]))
