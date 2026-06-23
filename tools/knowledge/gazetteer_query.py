#!/usr/bin/env python3
"""
gazetteer_query — cross-source query over the region dataset's THREE sidecars, joined on regionKey:

  {seed}_gazetteer.json   — identity, name, biome, geometry        (source: computed / measured terrain)
  {seed}_locations.json   — bosses, traders, POI/dungeon counts    (source: real-db)
  {seed}_vegetation.json  — modeled ore / flora counts             (source: modeled, upper-bias)

This is the payoff of the three-source architecture: ask questions NO single sidecar can answer,
e.g. "named regions with a boss AND the most ore", "the richest copper region and its biome".

Usage:
  gazetteer_query.py <dir> <query> [args]

  richest [N]              — top N regions by modeled ore (resourceTotal). default 10
  boss-ore [N]             — regions that have a boss, ranked by ore (the marquee mining-near-boss list)
  ore <prefab>             — top regions for a specific ore prefab (e.g. silvervein, rock4_copper)
  region <regionKey>       — full joined dossier for one region
  bosses                   — every boss and the named region it sits in (+ that region's ore)
  traders                  — every trader region (Haldor/Hildir), with biome + ore
  biome <Biome> [N]        — richest-ore regions within a biome (Mountain, BlackForest, Swamp, Plains…)
  summary                  — dataset-wide totals across all three sources

`<dir>` holds the three {seed}_*.json sidecars (e.g. the gazetteer --output dir, named or not).
Joins are LEFT from the gazetteer: every region exists; locations/vegetation default to empty.
"""
import sys, os, json, glob

def _find(d, suffix):
    hits = glob.glob(os.path.join(d, f"*_{suffix}.json"))
    if not hits:
        raise SystemExit(f"no *_{suffix}.json in {d}")
    # prefer the _named gazetteer if present
    if suffix == "gazetteer":
        named = [h for h in hits if h.endswith("_gazetteer_named.json")]
        # _named is actually {seed}_gazetteer_named.json — but the plain one is canonical for fields
        plain = [h for h in hits if h.endswith("_gazetteer.json")]
        return (plain or named)[0]
    return hits[0]

def load(d):
    """Return {regionKey: joined_dict} left-joined from the gazetteer."""
    gaz = json.load(open(_find(d, "gazetteer")))
    # locations/vegetation are optional
    try: loc = json.load(open(_find(d, "locations")))["regions"]
    except SystemExit: loc = {}
    try: veg = json.load(open(_find(d, "vegetation")))["regions"]
    except SystemExit: veg = {}

    joined = {}
    for r in gaz["regions"]:
        k = r["regionKey"]
        l = loc.get(k, {})
        v = veg.get(k, {})
        joined[k] = {
            "regionKey": k,
            "name": r.get("name", k),
            "biome": r.get("dominantBiome", "?"),
            "areaKm2": r.get("areaKm2", 0),
            "centroid": r.get("centroidMeters", {}),
            "isCoastal": r.get("isCoastal", False),
            # locations
            "hasBoss": l.get("hasBoss", False),
            "bosses": l.get("bosses", []),
            "traderPresent": l.get("traderPresent", False),
            "totalPOIs": l.get("totalPOIs", 0),
            "poiCounts": l.get("counts", {}),
            # vegetation
            "resourceTotal": v.get("resourceTotal", 0),
            "floraTotal": v.get("floraTotal", 0),
            "ore": {kk: vv for kk, vv in v.get("byPrefab", {}).items()
                    if any(t in kk.lower() for t in ("copper","tin","silver","iron","obsidian","mudpile"))},
        }
    return joined

def _row(j):
    boss = ("★ " + ",".join(j["bosses"])) if j["hasBoss"] else ""
    trad = "⚒trader" if j["traderPresent"] else ""
    tags = " ".join(t for t in (boss, trad) if t)
    return (f"{j['regionKey']:11} {j['name'][:30]:30} {j['biome']:11} "
            f"ore={j['resourceTotal']:>5} POI={j['totalPOIs']:>4} {tags}")

def richest(J, n=10):
    print(f"=== top {n} regions by modeled ore (source=modeled, upper-bias) ===")
    for j in sorted(J.values(), key=lambda x:-x["resourceTotal"])[:n]:
        print(_row(j))

def boss_ore(J, n=10):
    print(f"=== boss regions, ranked by ore — the 'mine near the altar' list ===")
    bs = [j for j in J.values() if j["hasBoss"]]
    for j in sorted(bs, key=lambda x:-x["resourceTotal"])[:n]:
        ores = ", ".join(f"{k}={v}" for k,v in sorted(j["ore"].items(), key=lambda kv:-kv[1])[:4])
        print(f"{_row(j)}")
        if ores: print(f"            └ {ores}")

def ore_prefab(J, prefab, n=12):
    print(f"=== top regions for ore '{prefab}' ===")
    rows = [(j, j["ore"].get(prefab) or
             next((v for k,v in j["ore"].items() if prefab.lower() in k.lower()), 0))
            for j in J.values()]
    rows = [(j,c) for j,c in rows if c]
    for j,c in sorted(rows, key=lambda t:-t[1])[:n]:
        print(f"{j['regionKey']:11} {j['name'][:30]:30} {j['biome']:11} {prefab}={c:>5}")

def region(J, key):
    j = J.get(key)
    if not j: raise SystemExit(f"no region {key}")
    print(f"=== {j['name']}  ({key}) ===")
    c = j["centroid"]
    print(f"biome={j['biome']}  area={j['areaKm2']}km²  coastal={j['isCoastal']}  "
          f"centroid=({c.get('x',0):.0f},{c.get('z',0):.0f})m")
    print(f"bosses: {j['bosses'] or '—'}   trader: {'yes' if j['traderPresent'] else 'no'}")
    print(f"POIs: {j['totalPOIs']}  {j['poiCounts']}")
    print(f"ore (modeled): total={j['resourceTotal']}  {j['ore'] or '—'}")
    print(f"flora (modeled): {j['floraTotal']}")

def bosses(J):
    print("=== every boss → its named region (+ that region's ore) ===")
    bs = [j for j in J.values() if j["hasBoss"]]
    for j in sorted(bs, key=lambda x:x["regionKey"]):
        for b in j["bosses"]:
            print(f"{b:14} → {j['name'][:30]:30} ({j['regionKey']}, {j['biome']})  ore={j['resourceTotal']}")

def traders(J):
    print("=== trader regions ===")
    for j in sorted((x for x in J.values() if x["traderPresent"]), key=lambda x:-x["resourceTotal"]):
        print(_row(j))

def biome(J, name, n=10):
    print(f"=== richest-ore regions in biome '{name}' ===")
    rows = [j for j in J.values() if j["biome"].lower()==name.lower()]
    for j in sorted(rows, key=lambda x:-x["resourceTotal"])[:n]:
        print(_row(j))

def summary(J):
    nb = sum(1 for j in J.values() if j["hasBoss"])
    nt = sum(1 for j in J.values() if j["traderPresent"])
    ore = sum(j["resourceTotal"] for j in J.values())
    flora = sum(j["floraTotal"] for j in J.values())
    poi = sum(j["totalPOIs"] for j in J.values())
    withore = sum(1 for j in J.values() if j["resourceTotal"]>0)
    print("=== dataset summary (three sources joined on regionKey) ===")
    print(f"regions: {len(J)}")
    print(f"  with boss: {nb}   with trader: {nt}   with ore: {withore}")
    print(f"totals: {poi:,} POIs (real-db) · {ore:,} ore nodes + {flora:,} flora (modeled)")

def main():
    if len(sys.argv) < 3:
        print(__doc__); sys.exit(1)
    d, cmd = sys.argv[1], sys.argv[2]
    rest = sys.argv[3:]
    J = load(d)
    if cmd == "richest":    richest(J, int(rest[0]) if rest else 10)
    elif cmd == "boss-ore": boss_ore(J, int(rest[0]) if rest else 10)
    elif cmd == "ore":      ore_prefab(J, rest[0])
    elif cmd == "region":   region(J, rest[0])
    elif cmd == "bosses":   bosses(J)
    elif cmd == "traders":  traders(J)
    elif cmd == "biome":    biome(J, rest[0], int(rest[1]) if len(rest)>1 else 10)
    elif cmd == "summary":  summary(J)
    else: print(__doc__)

if __name__ == "__main__":
    main()
