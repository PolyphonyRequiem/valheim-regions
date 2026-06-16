#!/usr/bin/env python3
"""WorldZones source index — mirrors the SBPR decomp-index convention.

Parses every .cs under valheim-regions/src by brace tracking, emitting one row per
(type, member) with: asm | line | type_path | type_kind | member_kind | signature
where member_kind in {field,prop,method,ctor,event,nested_type,enum_value,type_decl}.

Writes (under docs/knowledge/index/):
  members.tsv         — flat table
  by_type/<Type>.txt  — one file per type, just that type's members
  methods.json        — {method_name: [{type,file,line,kind,sig}, ...]} for quick xref

This is the WorldZones sibling of /home/polyphonyrequiem/valheim/sbpr-corpus/scripts/build_index.py
(which indexes the 142k-line Valheim decomp). Same schema, so q.py works against both.
Not a full Roslyn parse — heuristic brace-tracking, but good enough for
"where is GetBaseHeight / what members does ProtoRegionGenerator have" queries at ~0 tokens.
"""
import os, re, json, collections, glob

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC_DIR = os.path.join(REPO, "src")
OUT = os.path.join(REPO, "docs", "knowledge", "index")
os.makedirs(os.path.join(OUT, "by_type"), exist_ok=True)

TYPE_RE = re.compile(r'^(?P<indent>\s*)(?:\[[^\]]+\]\s*)*(public|internal|private|protected)?\s*(static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+|unsafe\s+)*(?P<kind>class|struct|enum|interface|delegate)\s+(?P<name>[A-Z_][A-Za-z0-9_]*)\b')
MEMBER_RE = re.compile(r'^(?P<indent>\s*)(?:\[[^\]]+\]\s*)*((public|internal|private|protected|static|virtual|override|abstract|sealed|readonly|const|extern|new|unsafe|async|partial)\s+)+(?P<rest>[A-Za-z0-9_<>\[\]\.,\? \t]+?\s+)?(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?P<after>[\(\{;=])')
ENUM_VAL_RE = re.compile(r'^\s*(?P<name>[A-Z_][A-Za-z0-9_]*)\s*(=\s*[^,}]+)?\s*[,}]')

def walk_braces(lines):
    depth = 0
    in_block_comment = False
    for i, raw in enumerate(lines, 1):
        line = raw.rstrip("\n")
        s = line
        if in_block_comment:
            end = s.find("*/")
            if end == -1:
                yield i, line, depth, depth
                continue
            s = s[end+2:]
            in_block_comment = False
        s2 = re.sub(r'//.*$', '', s)
        s2 = re.sub(r'"(\\.|[^"\\])*"', '""', s2)
        s2 = re.sub(r"'(\\.|[^'\\])*'", "''", s2)
        if "/*" in s2:
            in_block_comment = "*/" not in s2[s2.find("/*"):]
            s2 = s2.split("/*", 1)[0]
        before = depth
        depth += s2.count("{") - s2.count("}")
        yield i, line, before, depth

def parse(path, label):
    with open(path) as fh:
        lines = fh.readlines()
    type_stack = []
    members = []
    for i, line, before, after in walk_braces(lines):
        while type_stack and after <= type_stack[-1][2]:
            type_stack.pop()
        m = TYPE_RE.match(line)
        if m:
            name = m.group("name"); kind = m.group("kind")
            type_stack.append((name, kind, before))
            members.append((label, i, "::".join(t[0] for t in type_stack), kind, "type_decl", line.strip()[:200]))
            continue
        if not type_stack:
            continue
        cur = type_stack[-1]
        cur_path = "::".join(t[0] for t in type_stack)
        if cur[1] == "enum":
            em = ENUM_VAL_RE.match(line)
            if em:
                members.append((label, i, cur_path, "enum", "enum_value", em.group("name")))
            continue
        if before != cur[2] + 1:
            continue
        mm = MEMBER_RE.match(line)
        if mm:
            name = mm.group("name"); after_ch = mm.group("after")
            if after_ch == "(":
                kind = "ctor" if name == cur[0] else "method"
            elif after_ch == "{":
                kind = "prop"
            else:
                kind = "field"
            members.append((label, i, cur_path, cur[1], kind, line.strip()[:240]))
    return members

def main():
    all_members = []
    files = sorted(glob.glob(os.path.join(SRC_DIR, "**", "*.cs"), recursive=True))
    files = [f for f in files if "/obj/" not in f and "/bin/" not in f and "AssemblyInfo" not in f and "AssemblyAttributes" not in f]
    for src in files:
        label = os.path.relpath(src, SRC_DIR).replace(".cs", "")
        all_members.extend(parse(src, label))
    with open(os.path.join(OUT, "members.tsv"), "w") as fh:
        fh.write("asm\tline\ttype_path\ttype_kind\tmember_kind\tsignature\n")
        for r in all_members:
            fh.write("\t".join(str(x).replace("\t", " ").replace("\n", " ") for x in r) + "\n")
    by_type = collections.defaultdict(list)
    for r in all_members:
        by_type[r[2]].append(r)
    for tp, rows in by_type.items():
        safe = re.sub(r'[^A-Za-z0-9_:.-]+', '_', tp)[:200]
        with open(os.path.join(OUT, "by_type", safe + ".txt"), "w") as fh:
            for r in rows:
                fh.write(f"{r[0]}:{r[1]}\t{r[4]:<10}\t{r[5]}\n")
    methods = collections.defaultdict(list)
    for asm, ln, tp, tk, mk, sig in all_members:
        if mk in ("method", "ctor", "prop"):
            mn = re.search(r'\b([A-Za-z_][A-Za-z0-9_]*)\s*[\({]', sig)
            if mn:
                methods[mn.group(1)].append({"type": tp, "asm": asm, "line": ln, "kind": mk, "sig": sig})
    with open(os.path.join(OUT, "methods.json"), "w") as fh:
        json.dump(methods, fh, indent=1)
    types = set(r[2] for r in all_members if r[4] == "type_decl")
    method_count = sum(1 for r in all_members if r[4] == "method")
    print(f"[wz-index] files={len(files)} types={len(types)} members={len(all_members)} methods={method_count} unique_method_names={len(methods)}")
    smoke = ["GetBiome", "GetBaseHeight", "GetHeight", "GenerateLand", "Classify", "Attribute", "PlaceSeeds", "MergeTinyRegions", "ResolveCurrent", "WorldToZoneCoord"]
    print("\n[smoke] does each key method resolve?")
    for s in smoke:
        hits = methods.get(s, [])
        print(f"  {'OK' if hits else 'XX'} {s}: {len(hits)} hits" + (f" - {hits[0]['type']} @ {hits[0]['asm']}:{hits[0]['line']}" if hits else ""))

if __name__ == "__main__":
    main()
