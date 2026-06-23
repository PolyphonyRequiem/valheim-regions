#!/usr/bin/env python3
"""Parse Valheim ZoneSystem m_vegetation -> VegetationConfig catalogue JSON.
Runs on Prime where the AssetRipper export lives. Resolves m_prefab GUID -> real
prefab name via the export's .meta index for canonical naming.
"""
import re, json, os, sys, glob

EXPORT = "/tmp/valheim_export/ExportedProject"
ZS = f"{EXPORT}/Assets/Systems/_ZoneSystem.prefab"

# Heightmap.Biome bitmask (from decomp) -> name, for readability
BIOME_BITS = {
    1: "Meadows", 2: "Swamp", 4: "Mountain", 8: "BlackForest",
    16: "Plains", 32: "AshLands", 64: "DeepNorth", 256: "Ocean",
    512: "Mistlands", 0x100000: "Other",
}
def biome_names(mask):
    return [n for b, n in BIOME_BITS.items() if mask & b]

# Resource prefabs = ore/mineable. Matched case-insensitively on name token.
RESOURCE_TOKENS = ("copper", "tin", "silvervein", "silver", "mudpile", "obsidian",
                   "rock_meteorite", "flametal", "ironvein", "iron", "minerock")
def is_resource(name):
    n = name.lower()
    return any(t.lower() in n for t in RESOURCE_TOKENS)

def build_guid_index():
    """guid -> asset basename (prefab name), from every .meta in the export."""
    idx = {}
    for meta in glob.glob(f"{EXPORT}/Assets/**/*.prefab.meta", recursive=True):
        try:
            with open(meta, "r", errors="replace") as f:
                head = f.read(400)
            m = re.search(r"guid:\s*([0-9a-f]{32})", head)
            if m:
                base = os.path.basename(meta)[:-len(".prefab.meta")]
                idx[m.group(1)] = base
        except OSError:
            pass
    return idx

FIELD_RE = {
    "m_name": str, "m_enable": int, "m_min": float, "m_max": float,
    "m_forcePlacement": int, "m_biome": int, "m_minAltitude": float,
    "m_maxAltitude": float, "m_groupSizeMin": int, "m_groupSizeMax": int,
    "m_groupRadius": float, "m_minDistanceFromCenter": float,
    "m_maxDistanceFromCenter": float, "m_inForest": int,
}

def parse():
    guid_idx = build_guid_index()
    with open(ZS, "r", errors="replace") as f:
        lines = f.readlines()
    # find m_vegetation: block start
    start = next(i for i, l in enumerate(lines) if re.match(r"\s*m_vegetation:\s*$", l))
    entries = []
    cur = None
    cur_guid = None
    for l in lines[start+1:]:
        # new entry starts with "- m_name:"
        m = re.match(r"\s*-\s*m_name:\s*(.*)$", l)
        if m:
            if cur is not None:
                entries.append((cur, cur_guid))
            cur = {"m_name": m.group(1).strip()}
            cur_guid = None
            continue
        if cur is None:
            continue
        # end of list: dedent to a non-entry key at base indent
        if re.match(r"\s{0,2}m_[A-Za-z]", l) and "m_vegetation" not in l and not l.startswith("   "):
            break
        mp = re.match(r"\s*m_prefab:.*guid:\s*([0-9a-f]{32})", l)
        if mp:
            cur_guid = mp.group(1)
            continue
        mm = re.match(r"\s*(m_[A-Za-z]+):\s*(.*)$", l)
        if mm and mm.group(1) in FIELD_RE:
            k, v = mm.group(1), mm.group(2).strip()
            try:
                cur[k] = FIELD_RE[k](v)
            except ValueError:
                cur[k] = v
    if cur is not None:
        entries.append((cur, cur_guid))

    catalogue = []
    for e, guid in entries:
        prefab = guid_idx.get(guid or "", "") or e.get("m_name", "")
        catalogue.append({
            "PrefabName": prefab,
            "VegName": e.get("m_name", ""),
            "Enable": bool(e.get("m_enable", 1)),
            "BiomeMask": int(e.get("m_biome", 0)),
            "Biomes": biome_names(int(e.get("m_biome", 0))),
            "Min": float(e.get("m_min", 0)),
            "Max": float(e.get("m_max", 10)),
            "GroupSizeMin": int(e.get("m_groupSizeMin", 1)),
            "GroupSizeMax": int(e.get("m_groupSizeMax", 1)),
            "GroupRadius": float(e.get("m_groupRadius", 0)),
            "MinAltitude": float(e.get("m_minAltitude", -1000)),
            "MaxAltitude": float(e.get("m_maxAltitude", 1000)),
            "MinDistanceFromCenter": float(e.get("m_minDistanceFromCenter", 0)),
            "MaxDistanceFromCenter": float(e.get("m_maxDistanceFromCenter", 0)),
            "ForcePlacement": bool(e.get("m_forcePlacement", 0)),
            "InForest": bool(e.get("m_inForest", 0)),
            "IsResource": is_resource(prefab or e.get("m_name", "")),
        })
    return catalogue

if __name__ == "__main__":
    cat = parse()
    out = sys.argv[1] if len(sys.argv) > 1 else "/tmp/valheim_vegetation_catalogue.json"
    payload = {
        "provenance": {
            "source": "assetripper-export",
            "tool": "AssetRipper 1.3.14",
            "asset": "valheim_Data/_ZoneSystem.prefab m_vegetation",
            "schemaVersion": 1,
        },
        "count": len(cat),
        "configs": cat,
    }
    with open(out, "w") as f:
        json.dump(payload, f, indent=2)
    res = [c for c in cat if c["IsResource"]]
    print(f"parsed {len(cat)} vegetation configs ({len(res)} resource/ore)")
    print("--- resource configs ---")
    for c in res:
        print(f"  {c['PrefabName'] or c['VegName']:24} min={c['Min']:>4g} max={c['Max']:>4g} "
              f"grp={c['GroupSizeMin']}-{c['GroupSizeMax']} biomes={c['Biomes']} "
              f"alt={c['MinAltitude']:g}..{c['MaxAltitude']:g}")
    print(f"wrote {out}")
