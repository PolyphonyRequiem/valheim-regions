#!/usr/bin/env python3
"""
Location sidecar builder — join decoded .db locations to gazetteer regions.

Reads:
  {seed}_locations_raw.json   (from decode_locations.py — real prefabName + x,z + placed)
  {seed}_gazetteer_grid.bin   (from the gazetteer CLI — per-zone regionId + id->RegionKey)
Emits:
  {seed}_locations.json       sidecar keyed by regionKey: per-category counts, boss/trader flags,
                              total POIs, notable list. source = "real-db".

Each location (x,z world metres) -> zone -> regionId -> RegionKey via the grid. Locations on
unassigned/ocean zones (regionId<0) are bucketed as "unregioned".
"""
import json, struct, sys
from collections import defaultdict, Counter

def load_grid(path):
    d = open(path,"rb").read(); o=0
    assert d[o:o+4]==b"WZGR"; o+=4
    ver,=struct.unpack_from("<i",d,o); o+=4
    minIndex,size,zoneSize=struct.unpack_from("<iii",d,o); o+=12
    rc,=struct.unpack_from("<i",d,o); o+=4
    id2key={}
    for _ in range(rc):
        rid,=struct.unpack_from("<i",d,o); o+=4
        klen,=struct.unpack_from("<i",d,o); o+=4
        id2key[rid]=d[o:o+klen].decode("utf-8"); o+=klen
    region=[[-1]*size for _ in range(size)]
    for gy in range(size):
        for gx in range(size):
            rid,=struct.unpack_from("<i",d,o); o+=4
            o+=8  # biome(2)+pad(2)+height(4)
            region[gy][gx]=rid
    return dict(minIndex=minIndex,size=size,zoneSize=zoneSize,id2key=id2key,region=region)

# prefab name -> category + flags. Buckets the ~hundreds of prefab variants into player-meaningful types.
def categorize(prefab):
    p = prefab.lower()
    boss = {"eikthyrnir":"Eikthyr","gdking":"The Elder","bonemass":"Bonemass","dragonqueen":"Moder",
            "goblinking":"Yagluth","fader":"Fader","mistlands_dvergrbossentrance1":"Queen"}
    for k,v in boss.items():
        if p == k: return "boss", v
    if "starttemple" in p: return "spawn", None
    if "vendor" in p or "hildir" in p or "haldor" in p: return "trader", None
    if "crypt" in p or "sunkencrypt" in p: return "crypt", None
    if "cave" in p or "frostcave" in p: return "cave", None
    if "troll" in p: return "trollcave", None
    if "ruin" in p or "stonehouse" in p or "stonetower" in p or "woodhouse" in p: return "ruin", None
    if "runestone" in p: return "runestone", None
    if "dolmen" in p or "grave" in p or "barrow" in p: return "grave", None
    if "tarpit" in p: return "tarpit", None
    if "drakenest" in p: return "drakenest", None
    if "goblincamp" in p or "goblin" in p: return "fulingcamp", None
    if "dvergrtown" in p or "dvergr" in p: return "dvergr", None
    if "giant" in p or "infestedtree" in p or "statue" in p or "viaduct" in p or "rockspire" in p \
       or "roadpost" in p or "spawner" in p or "nest" in p or "charredstone" in p or "camp" in p:
        return "misc", None
    return "other", None

def main():
    seed = sys.argv[1] if len(sys.argv)>1 else "ForTheWort"
    raw_path = sys.argv[2] if len(sys.argv)>2 else "/tmp/gazetteer/niflheim_locations_raw.json"
    grid_path = sys.argv[3] if len(sys.argv)>3 else f"/tmp/gazetteer/{seed}_gazetteer_grid.bin"
    out_path = sys.argv[4] if len(sys.argv)>4 else f"/tmp/gazetteer/{seed}_locations.json"

    raw = json.load(open(raw_path))
    g = load_grid(grid_path)
    size, zs, minIndex, region, id2key = g["size"], g["zoneSize"], g["minIndex"], g["region"], g["id2key"]

    per_region = defaultdict(lambda: {"counts":Counter(), "bosses":set(), "trader":False, "total":0})
    unregioned = 0
    for loc in raw["locations"]:
        x, z = loc["x"], loc["z"]
        # world metres -> zone index (zone = round(world/64)); grid index = zone - minIndex
        zx = round(x/zs) - minIndex
        zy = round(z/zs) - minIndex
        rid = region[zy][zx] if (0<=zx<size and 0<=zy<size) else -1
        if rid < 0:
            unregioned += 1; continue
        key = id2key.get(rid)
        if key is None:
            unregioned += 1; continue
        cat, boss = categorize(loc["prefab"])
        rec = per_region[key]
        rec["counts"][cat] += 1
        rec["total"] += 1
        if cat == "boss" and boss: rec["bosses"].add(boss)
        if cat == "trader": rec["trader"] = True

    out = {"provenance": {"seed": seed, "source": "real-db", "worldVersion": raw["worldVersion"],
                          "schemaVersion": 1, "note": "Location instances decoded from the world .db ZoneSystem block, joined to regions by position. Real ground-truth, not modeled."},
           "unregionedLocations": unregioned,
           "regions": {}}
    for key, rec in sorted(per_region.items()):
        out["regions"][key] = {
            "totalPOIs": rec["total"],
            "counts": dict(rec["counts"].most_common()),
            "hasBoss": len(rec["bosses"])>0,
            "bosses": sorted(rec["bosses"]),
            "traderPresent": rec["trader"],
        }
    json.dump(out, open(out_path,"w"), indent=2)

    # report
    tot = sum(r["totalPOIs"] for r in out["regions"].values())
    print(f"joined {tot:,} locations across {len(out['regions'])} regions  ·  {unregioned:,} unregioned (ocean/islet)")
    boss_regions = [(k,v) for k,v in out["regions"].items() if v["hasBoss"]]
    print(f"\nboss regions: {len(boss_regions)}")
    for k,v in boss_regions:
        print(f"  {k:12} {', '.join(v['bosses'])}")
    # richest POI regions
    print("\ntop 8 regions by POI count:")
    for k,v in sorted(out["regions"].items(), key=lambda kv:-kv[1]["totalPOIs"])[:8]:
        top = ", ".join(f"{c}:{n}" for c,n in list(v["counts"].items())[:4])
        print(f"  {k:12} {v['totalPOIs']:3} POIs  [{top}]")
    print(f"\nwrote {out_path}")

if __name__=="__main__": main()
