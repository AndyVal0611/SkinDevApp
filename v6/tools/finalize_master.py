"""Source maintenance only. No notebook execution, training or dataset writes."""
from pathlib import Path
root=Path(__file__).resolve().parents[1]
def read(n):return (root/'notebook_src'/n).read_text(encoding='utf-8')
def write(n,s):(root/'notebook_src'/n).write_text(s,encoding='utf-8')
def replace(n,a,b):
    s=read(n);assert a in s,(n,a[:80]);write(n,s.replace(a,b))
n='05_global_cnn.cellsrc';s=read(n);start=s.index('    P = phase_paths',s.index('def run_phase('));end=s.index('\n\n\n#@@ md',start)
print(s[s.index('def run_phase('):start])
