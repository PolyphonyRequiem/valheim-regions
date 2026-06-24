#!/usr/bin/env python3
"""Parse Valheim ZoneLocation configs -> catalogue JSON.
Runs on Prime where the AssetRipper export lives. Mirrors parse_vegetation.py.

Locations live in TWO places (see SetupLocations, decomp 96603 — it aggregates LocationLists
sorted by m_sortOrder, then iterates the union):
  1. _ZoneSystem.prefab            m_locations  (the base game locations)
  2. Systems/LocationLists/*.prefab  m_locations  (Mistlands, Ashlands, Hildir, MountainCaves, cp1)
Both have the IDENTICAL m_locations block shape. We parse all of them in m_sortOrder order
(the game's registration order) so the catalogue matches the game's global zone-occupancy resolution.
"""
import re, json, os, sys, glob

EXPORT = "/tmp/valheim_export/ExportedProject"
ZS = f"{EXPORT}/Assets/Systems/_ZoneSystem.prefab"
LOCLISTS = f"{EXPORT}/Assets/Systems/LocationLists"

BIOME_BITS = {1:"Meadows",2:"Swamp",4:"Mountain",8:"BlackForest",16:"Plains",
              32:"AshLands",64:"DeepNorth",256:"Ocean",512:"Mistlands",0x100000:"Other"}
BIOMEAREA_BITS = {1:"Edge",2:"Median",4:"Everything"}  # Heightmap.BiomeArea
def names(mask, bits): return [n for b,n in bits.items() if mask & b]

FIELD_RE = {
    "m_enable": int, "m_prefabName": str, "m_biome": int, "m_biomeArea": int,
    "m_quantity": int, "m_prioritized": int, "m_centerFirst": int, "m_unique": int,
    "m_minDistanceFromSimilar": float, "m_maxDistanceFromSimilar": float,
    "m_randomRotation": int, "m_slopeRotation": int, "m_snapToWater": int,
    "m_interiorRadius": float, "m_exteriorRadius": float,
    "m_minTerrainDelta": float, "m_maxTerrainDelta": float,
    "m_minimumVegetation": float, "m_maximumVegetation": float,
    "m_surroundCheckVegetation": int, "m_surroundCheckDistance": float,
    "m_surroundCheckLayers": int, "m_surroundBetterThanAverage": float,
    "m_inForest": int, "m_forestTresholdMin": float, "m_forestTresholdMax": float,
    "m_minDistanceFromCenter": float, "m_maxDistanceFromCenter": float,
    "m_minDistance": float, "m_maxDistance": float, "m_clearArea": int,
    "m_minAltitude": float, "m_maxAltitude": float,
    "m_group": str, "m_groupMax": str, "m_iconAlways": int, "m_iconPlaced": int,
}


def parse_m_locations(path):
    """Extract the m_locations entries from one prefab's YAML. Returns a list of raw field dicts.
    Returns [] if the file has no m_locations block."""
    with open(path, "r", errors="replace") as f:
        lines = f.readlines()
    try:
        start = next(i for i, l in enumerate(lines) if re.match(r"\s*m_locations:\s*$", l))
    except StopIteration:
        return []
    entries, cur = [], None
    for l in lines[start+1:]:
        m = re.match(r"\s*-\s*m_name:\s*(.*)$", l)
        if m:
            if cur is not None: entries.append(cur)
            cur = {"m_name": m.group(1).strip()}
            continue
        if cur is None: continue
        # end of m_locations: a base-indent key that isn't part of an entry (entries indent deeper).
        if re.match(r"\s{0,2}m_[A-Za-z]", l) and not l.startswith("   "):
            if re.match(r"  m_[A-Za-z]", l) and not re.match(r"    ", l):
                break
        mm = re.match(r"\s*(m_[A-Za-z]+):\s*(.*)$", l)
        if mm and mm.group(1) in FIELD_RE:
            k, v = mm.group(1), mm.group(2).strip()
            try: cur[k] = FIELD_RE[k](v)
            except ValueError: cur[k] = v
    if cur is not None: entries.append(cur)
    return entries


def get_sort_order(path):
    """m_sortOrder of a LocationList prefab (governs registration order). Default 0."""
    with open(path, "r", errors="replace") as f:
        for l in f:
            m = re.match(r"\s*m_sortOrder:\s*(-?\d+)", l)
            if m: return int(m.group(1))
    return 0


def to_config(e, source):
    bm = int(e.get("m_biome", 0) or 0)
    ba = int(e.get("m_biomeArea", 0) or 0)
    return {
        "PrefabName": e.get("m_prefabName", "") or e.get("m_name", ""),
        "Enable": bool(e.get("m_enable", 1)),
        "BiomeMask": bm, "Biomes": names(bm, BIOME_BITS),
        "BiomeAreaMask": ba, "BiomeAreas": names(ba, BIOMEAREA_BITS),
        "Quantity": int(e.get("m_quantity", 0) or 0),
        "Prioritized": bool(e.get("m_prioritized", 0)),
        "CenterFirst": bool(e.get("m_centerFirst", 0)),
        "Unique": bool(e.get("m_unique", 0)),
        "Group": e.get("m_group", "") or "",
        "GroupMax": e.get("m_groupMax", "") or "",
        "MinDistanceFromSimilar": float(e.get("m_minDistanceFromSimilar", 0) or 0),
        "MaxDistanceFromSimilar": float(e.get("m_maxDistanceFromSimilar", 0) or 0),
        "ExteriorRadius": float(e.get("m_exteriorRadius", 0) or 0),
        "InteriorRadius": float(e.get("m_interiorRadius", 0) or 0),
        "MinTerrainDelta": float(e.get("m_minTerrainDelta", 0) or 0),
        "MaxTerrainDelta": float(e.get("m_maxTerrainDelta", 10) or 10),
        "MinAltitude": float(e.get("m_minAltitude", -1000) or -1000),
        "MaxAltitude": float(e.get("m_maxAltitude", 1000) or 1000),
        "MinimumVegetation": float(e.get("m_minimumVegetation", 0) or 0),
        "MaximumVegetation": float(e.get("m_maximumVegetation", 1) or 1),
        "SurroundCheckVegetation": bool(e.get("m_surroundCheckVegetation", 0)),
        "SurroundCheckDistance": float(e.get("m_surroundCheckDistance", 0) or 0),
        "SurroundCheckLayers": int(e.get("m_surroundCheckLayers", 0) or 0),
        "SurroundBetterThanAverage": float(e.get("m_surroundBetterThanAverage", 0) or 0),
        "InForest": bool(e.get("m_inForest", 0)),
        "ForestTresholdMin": float(e.get("m_forestTresholdMin", 0) or 0),
        "ForestTresholdMax": float(e.get("m_forestTresholdMax", 0) or 0),
        "MinDistanceFromCenter": float(e.get("m_minDistanceFromCenter", 0) or 0),
        "MaxDistanceFromCenter": float(e.get("m_maxDistanceFromCenter", 0) or 0),
        "MinDistance": float(e.get("m_minDistance", 0) or 0),
        "MaxDistance": float(e.get("m_maxDistance", 0) or 0),
        "SnapToWater": bool(e.get("m_snapToWater", 0)),
        "ClearArea": bool(e.get("m_clearArea", 0)),
        "Source": source,
    }


def build_catalogue():
    """Aggregate _ZoneSystem.prefab + every LocationList, in m_sortOrder order (game registration order).
    Within a tie, files are ordered by name for determinism. Returns (catalogue, source_summary)."""
    cat = []
    sources = []

    # 1. base _ZoneSystem (sortOrder 0 conceptually — it is the root list).
    base = parse_m_locations(ZS)
    cat += [to_config(e, "_ZoneSystem") for e in base]
    sources.append(("_ZoneSystem", 0, len(base)))

    # 2. LocationLists, sorted by (m_sortOrder, filename) to match SetupLocations' Sort(sortOrder).
    lists = sorted(glob.glob(f"{LOCLISTS}/*.prefab"),
                   key=lambda p: (get_sort_order(p), os.path.basename(p)))
    for p in lists:
        entries = parse_m_locations(p)
        name = os.path.basename(p)[:-len(".prefab")]
        cat += [to_config(e, name) for e in entries]
        sources.append((name, get_sort_order(p), len(entries)))

    return cat, sources


if __name__ == "__main__":
    cat, sources = build_catalogue()
    out = sys.argv[1] if len(sys.argv) > 1 else "/tmp/valheim_locations_catalogue.json"
    payload = {"provenance": {"source": "assetripper-export", "tool": "AssetRipper 1.3.14",
               "asset": "_ZoneSystem.prefab + Systems/LocationLists/*.prefab (m_locations), m_sortOrder order",
               "schemaVersion": 2},
               "count": len(cat), "locations": cat}
    with open(out, "w") as f: json.dump(payload, f, indent=2)

    enabled = [c for c in cat if c["Enable"] and c["Quantity"] > 0]
    uniques = [c for c in enabled if c["Unique"]]
    print(f"parsed {len(cat)} ZoneLocation configs ({len(enabled)} enabled w/ quantity>0) from {len(sources)} sources")
    print("--- sources (name, sortOrder, #locations) ---")
    for name, so, n in sources:
        print(f"  {name:28} sortOrder={so} locations={n}")
    print(f"--- enabled uniques (potential-location types) = {len(uniques)} ---")
    for c in uniques:
        print(f"  {c['PrefabName']:28} q={c['Quantity']} biomes={c['Biomes']}")
    print(f"--- total quantity to place (enabled) = {sum(c['Quantity'] for c in enabled)} ---")
    print(f"--- top enabled configs ---")
    for c in sorted(enabled, key=lambda x: -x['Quantity'])[:12]:
        print(f"  {c['PrefabName']:28} q={c['Quantity']:>4} uniq={int(c['Unique'])} "
              f"biomes={c['Biomes']} src={c['Source']}")
    print(f"wrote {out}")
