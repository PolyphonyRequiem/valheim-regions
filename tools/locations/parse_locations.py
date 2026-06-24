#!/usr/bin/env python3
"""Parse Valheim ZoneLocation configs -> catalogue JSON.
Runs on Prime where the AssetRipper export lives. Mirrors parse_vegetation.py.

Locations live in SEVERAL serialized assets (see SetupLocations, decomp 96603 — it aggregates
LocationLists sorted by m_sortOrder, then iterates the union):
  1. _ZoneSystem.prefab            m_locations  (the base game locations)
  2. _GameMain.prefab              m_locations  (dev/combat locations: CombatRuin01, BigRockClearing,
                                                 BogWitch_Camp, the Dev* ring/range/garden — these have
                                                 an EMPTY m_prefabName in the serialized asset; the game
                                                 fills it at runtime from m_prefab's SoftReference)
  3. Systems/LocationLists/*.prefab  m_locations  (Mistlands, Ashlands, Hildir, MountainCaves, cp1)
All have the IDENTICAL m_locations block shape. We parse all of them in m_sortOrder order (the game's
registration order) so the catalogue matches the game's global zone-occupancy resolution.

🔴 EMPTY m_prefabName: some entries (notably in _GameMain) reference the prefab ONLY by
m_prefab.m_assetID (a 128-bit SoftReference hash), with m_prefabName left blank — it's populated at
runtime via m_prefab.Name. We resolve those via the SoftRef manifest's "asset ID -> path" map
(StreamingAssets/SoftRef/manifest). assetID hex = the four m_assetID words (v3,v2,v1,v0) packed
big-endian uint32 each, verified against StoneCircle's manifest id (5331b07f...). Without this, 8
entries (incl. CombatRuin01/BigRockClearing/BogWitch_Camp) silently drop — they did, until 2026-06-24.
"""
import re, json, os, sys, glob, struct

EXPORT = "/tmp/valheim_export/ExportedProject"
ZS = f"{EXPORT}/Assets/Systems/_ZoneSystem.prefab"
GAMEMAIN = f"{EXPORT}/Assets/Systems/_GameMain.prefab"
LOCLISTS = f"{EXPORT}/Assets/Systems/LocationLists"
# SoftRef manifest (assetID -> prefab path), on the live install. Used to resolve empty-name entries.
SOFTREF_MANIFEST = os.path.expanduser(
    "~/.steam/debian-installation/steamapps/common/Valheim/valheim_Data/StreamingAssets/SoftRef/manifest")

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


def load_assetid_index(manifest_path=SOFTREF_MANIFEST):
    """assetID hex -> prefab name, from the SoftRef manifest. The manifest is a binary blob with
    embedded ASCII; we scan for 'asset ID: <hex>' then the following 'path in bundle: .../Name.prefab'.
    Returns {} if the manifest is absent (e.g. running off-box) — empty-name entries then stay unresolved."""
    idx = {}
    if not os.path.exists(manifest_path):
        return idx
    with open(manifest_path, "rb") as f:
        data = f.read()
    text = data.decode("latin-1", errors="replace")
    cur_id = None
    for m in re.finditer(r"asset ID:\s*([0-9a-f]{32})|path in bundle:\s*(\S+)", text):
        if m.group(1):
            cur_id = m.group(1)
        elif m.group(2) and cur_id:
            name = os.path.basename(m.group(2))
            if name.endswith(".prefab"):
                name = name[:-len(".prefab")]
            idx[cur_id] = name
            cur_id = None
    return idx


def assetid_hex(v3, v2, v1, v0):
    """The four m_assetID words -> the manifest's 32-char hex id. Big-endian uint32 each, v3 first.
    Verified against StoneCircle (v3=1395765375.. -> 5331b07f2f41b914bb3b638c751ff578).
    Returns None for the all-zero id: disabled/empty-prefab entries carry a null assetID, and MANY
    distinct locations share it — it is NOT an identity and must never be used for dedup or resolution."""
    if (v3, v2, v1, v0) == (0, 0, 0, 0):
        return None
    return b"".join(struct.pack(">I", w & 0xFFFFFFFF) for w in (v3, v2, v1, v0)).hex()


def parse_m_locations(path):
    """Extract the m_locations entries from one prefab's YAML. Returns a list of raw field dicts.
    Captures m_prefabName AND the m_assetID tuple (for empty-name resolution). Returns [] if no block."""
    with open(path, "r", errors="replace") as f:
        lines = f.readlines()
    try:
        start = next(i for i, l in enumerate(lines) if re.match(r"\s*m_locations:\s*$", l))
    except StopIteration:
        return []
    entries, cur = [], None
    pending_assetid = False   # we just saw 'm_assetID:' and the next 4 v-lines are its words
    aid = {}
    for l in lines[start+1:]:
        m = re.match(r"\s*-\s*m_name:\s*(.*)$", l)
        if m:
            if cur is not None: entries.append(cur)
            cur = {"m_name": m.group(1).strip()}
            pending_assetid = False; aid = {}
            continue
        if cur is None: continue
        # end of m_locations: a base-indent key that isn't part of an entry (entries indent deeper).
        if re.match(r"\s{0,2}m_[A-Za-z]", l) and not l.startswith("   "):
            if re.match(r"  m_[A-Za-z]", l) and not re.match(r"    ", l):
                break
        # capture the assetID words (v3..v0) following an m_assetID: line
        if re.match(r"\s*m_assetID:\s*$", l):
            pending_assetid = True; aid = {}; continue
        if pending_assetid:
            vm = re.match(r"\s*(v[0-3]):\s*(\d+)", l)
            if vm:
                aid[vm.group(1)] = int(vm.group(2))
                if len(aid) == 4:
                    cur["_assetid_hex"] = assetid_hex(aid["v3"], aid["v2"], aid["v1"], aid["v0"])
                    pending_assetid = False
                continue
            else:
                pending_assetid = False
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


def to_config(e, source, aid_index):
    bm = int(e.get("m_biome", 0) or 0)
    ba = int(e.get("m_biomeArea", 0) or 0)
    # Resolve the prefab name. The m_prefab SoftReference (m_assetID) is AUTHORITATIVE — it is what the
    # game resolves m_prefab.Name from at runtime. The serialized m_prefabName is a cache that is
    # occasionally STALE (verified 2026-06-24: 3 of 260 entries mismatch — a Hildir_camp-labelled entry
    # is really BogWitch_Camp, two DevHouse4 are really DevHouse5/DevDressingRoom). So: prefer the
    # assetID->manifest name; fall back to m_prefabName / m_name only when the assetID can't resolve
    # (null id, or off-box with no manifest).
    hx = e.get("_assetid_hex")
    name = (aid_index.get(hx) if hx else None) or e.get("m_prefabName", "") or e.get("m_name", "")
    return {
        "PrefabName": name,
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
    """Aggregate _ZoneSystem.prefab + _GameMain.prefab + every LocationList, in m_sortOrder order (game
    registration order). Empty-name entries are resolved via the SoftRef manifest assetID index. Within
    a sortOrder tie, files are ordered by name for determinism. Returns (catalogue, source_summary)."""
    aid_index = load_assetid_index()
    cat = []
    sources = []
    seen = set()   # identity keys already added — _GameMain embeds the SAME ZoneSystem (most of its
                   # entries duplicate _ZoneSystem); dedupe by assetID, or by content when the assetID
                   # is null (disabled/empty-prefab entries all share the all-zero id, so it can't key).

    def identity(e):
        hx = e.get("_assetid_hex")
        if hx:
            return ("aid", hx)
        # null assetID -> key on the content that defines the ZoneLocation
        return ("content", e.get("m_prefabName", "") or e.get("m_name", ""),
                int(e.get("m_biome", 0) or 0), int(e.get("m_quantity", 0) or 0),
                int(e.get("m_enable", 0) or 0))

    # 1. base root assets (sortOrder 0): _ZoneSystem then _GameMain. _GameMain carries the dev/combat
    #    locations (CombatRuin01, BigRockClearing, the Dev* ring/range/garden) — empty-name, assetID-
    #    resolved — but ALSO re-embeds the _ZoneSystem entries, so we dedupe.
    for path, label in ((ZS, "_ZoneSystem"), (GAMEMAIN, "_GameMain")):
        if not os.path.exists(path):
            continue
        kept = 0
        for e in parse_m_locations(path):
            key = identity(e)
            if key in seen:
                continue
            seen.add(key)
            cat.append(to_config(e, label, aid_index))
            kept += 1
        sources.append((label, 0, kept))

    # 2. LocationLists, sorted by (m_sortOrder, filename) to match SetupLocations' Sort(sortOrder).
    lists = sorted(glob.glob(f"{LOCLISTS}/*.prefab"),
                   key=lambda p: (get_sort_order(p), os.path.basename(p)))
    for p in lists:
        name = os.path.basename(p)[:-len(".prefab")]
        kept = 0
        for e in parse_m_locations(p):
            key = identity(e)
            if key in seen:
                continue
            seen.add(key)
            cat.append(to_config(e, name, aid_index))
            kept += 1
        sources.append((name, get_sort_order(p), kept))

    return cat, sources


if __name__ == "__main__":
    cat, sources = build_catalogue()
    out = sys.argv[1] if len(sys.argv) > 1 else "/tmp/valheim_locations_catalogue.json"
    payload = {"provenance": {"source": "assetripper-export", "tool": "AssetRipper 1.3.14",
               "asset": "_ZoneSystem.prefab + _GameMain.prefab + Systems/LocationLists/*.prefab "
                        "(m_locations), m_sortOrder order; empty-name entries resolved via SoftRef manifest",
               "schemaVersion": 3},
               "count": len(cat), "locations": cat}
    with open(out, "w") as f: json.dump(payload, f, indent=2)

    enabled = [c for c in cat if c["Enable"] and c["Quantity"] > 0]
    uniques = [c for c in enabled if c["Unique"]]
    unnamed = [c for c in cat if not c["PrefabName"]]
    print(f"parsed {len(cat)} ZoneLocation configs ({len(enabled)} enabled w/ quantity>0) from {len(sources)} sources")
    print("--- sources (name, sortOrder, #locations) ---")
    for name, so, n in sources:
        print(f"  {name:28} sortOrder={so} locations={n}")
    if unnamed:
        print(f"⚠️  {len(unnamed)} entries STILL have no resolvable prefab name (assetID not in manifest):")
        for c in unnamed[:10]:
            print(f"     src={c['Source']} q={c['Quantity']} biomes={c['Biomes']}")
    else:
        print("✓ every entry has a resolved prefab name (0 unresolved)")
    print(f"--- enabled uniques (potential-location types) = {len(uniques)} ---")
    for c in uniques:
        print(f"  {c['PrefabName']:28} q={c['Quantity']} biomes={c['Biomes']}")
    print(f"--- total quantity to place (enabled) = {sum(c['Quantity'] for c in enabled)} ---")
    print(f"--- top enabled configs ---")
    for c in sorted(enabled, key=lambda x: -x['Quantity'])[:12]:
        print(f"  {c['PrefabName']:28} q={c['Quantity']:>4} uniq={int(c['Unique'])} "
              f"biomes={c['Biomes']} src={c['Source']}")
    print(f"wrote {out}")
