#!/usr/bin/env python3
"""Quick CLI to query the WorldZones source index (sibling of sbpr-corpus/scripts/q.py).

Usage:
  wzq.py method <name>     — find all methods named <name> across WorldZones src
  wzq.py member <regex>    — search member signatures
  wzq.py type <Type>       — list all members of a type (fuzzy if no exact match)
  wzq.py field <regex>     — fields only
  wzq.py xref <name>       — find call sites of a method (greps the src tree)
  wzq.py decomp <name>     — pivot to the Valheim DECOMP index for the same name
                             (delegates to sbpr-corpus/scripts/q.py — the port's source of truth)

The point: WorldZones ports Valheim's worldgen. Use `method`/`type` to navigate the PORT,
`decomp` to jump to the ORIGINAL it was ported from, at ~0 tokens either way.
"""
import sys, os, json, re, subprocess

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
INDEX = os.path.join(REPO, "docs", "knowledge", "index")
SRC = os.path.join(REPO, "src")
SBPR_Q = "/home/polyphonyrequiem/valheim/sbpr-corpus/scripts/q.py"

def method(name):
    path = os.path.join(INDEX, "methods.json")
    if not os.path.exists(path):
        print("index missing — run tools/knowledge/build_index.py"); return
    m = json.load(open(path))
    for h in m.get(name, []):
        print(f"{h['type']:40} {h['asm']:45} L{h['line']:>5}  {h['kind']:6}  {h['sig'][:120]}")

def member(pat):
    rx = re.compile(pat, re.I)
    with open(os.path.join(INDEX, "members.tsv")) as fh:
        next(fh)
        for line in fh:
            p = line.rstrip("\n").split("\t")
            if len(p) < 6: continue
            if rx.search(p[5]):
                print(f"{p[2]:40} {p[0]:45} L{p[1]:>5}  {p[4]:10}  {p[5][:120]}")

def type_(name):
    safe = re.sub(r'[^A-Za-z0-9_:.-]+', '_', name)[:200]
    path = os.path.join(INDEX, "by_type", safe + ".txt")
    if os.path.exists(path):
        print(open(path).read()); return
    for f in sorted(os.listdir(os.path.join(INDEX, "by_type"))):
        if name.lower() in f.lower():
            print(f"--- {f} ---"); print(open(os.path.join(INDEX, "by_type", f)).read()[:4000]); print()

def field(pat):
    rx = re.compile(pat, re.I)
    with open(os.path.join(INDEX, "members.tsv")) as fh:
        next(fh)
        for line in fh:
            p = line.rstrip("\n").split("\t")
            if len(p) < 6 or p[4] != "field": continue
            if rx.search(p[5]):
                print(f"{p[2]:40} {p[0]:45} L{p[1]:>5}  {p[5][:120]}")

def xref(name):
    rx = re.escape(name) + r'\s*\('
    r = subprocess.run(["rg", "-n", "--no-heading", "-t", "cs", "--max-count", "60", rx, SRC],
                       capture_output=True, text=True)
    for line in r.stdout.splitlines()[:80]:
        print(line.replace(SRC + "/", ""))

def decomp(name):
    if not os.path.exists(SBPR_Q):
        print(f"decomp index tool not found at {SBPR_Q}"); return
    print(f"=== pivoting to Valheim decomp for '{name}' ===")
    subprocess.run(["python3", SBPR_Q, "method", name])

if __name__ == "__main__":
    if len(sys.argv) < 3:
        print(__doc__); sys.exit(1)
    cmd, arg = sys.argv[1], sys.argv[2]
    {"method": method, "member": member, "type": type_, "field": field,
     "xref": xref, "decomp": decomp}.get(cmd, lambda a: print(__doc__))(arg)
