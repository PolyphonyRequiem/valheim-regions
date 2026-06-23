#!/usr/bin/env python3
"""Enrich gazette with multi-schema names + UNIQUENESS pass (PROVISIONAL layer, not C# namer).
Deterministic collision resolution: superlatives reserved first, then regions in regionKey order;
a colliding name re-rolls by perturbing the hash key (#1,#2,...) until unique. Stable across runs."""
import json, sys, importlib.util
from collections import Counter, defaultdict

import os
_HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("ns", os.path.join(_HERE, "name_schemes.py"))
ns = importlib.util.module_from_spec(spec); spec.loader.exec_module(ns)

SRC = sys.argv[1] if len(sys.argv) > 1 else "ForTheWort_gazetteer.json"
LOC = sys.argv[2] if len(sys.argv) > 2 else None   # optional {seed}_locations.json sidecar
d = json.load(open(SRC)); R = d["regions"]

# Attach real-db location signal to each region (if a sidecar is provided) so location-driven
# naming schemas (boss-seat, trader-hold, dungeon-haunt) can fire. Graceful if absent.
if LOC and os.path.exists(LOC):
    loc = json.load(open(LOC)).get("regions", {})
    n_attached = 0
    for r in R:
        if r["regionKey"] in loc:
            r["_loc"] = loc[r["regionKey"]]; n_attached += 1
    print(f"attached location sidecar to {n_attached}/{len(R)} regions")

supmap = ns.build_superlatives(R)

def name_with_salt(r, attempt):
    if attempt == 0:
        return ns.name_region(r, supmap)
    r2 = dict(r); r2["regionKey"] = f'{r["regionKey"]}#{attempt}'
    nm, sc = ns.name_region(r2, supmap)
    return nm, (sc + "*")        # mark re-rolled

used = set()
# 1) reserve superlatives (fixed, already unique)
sup_keys = set(supmap)
for r in R:
    if r["regionKey"] in sup_keys:
        used.add(supmap[r["regionKey"]])
# 2) everyone else, regionKey order, re-roll on collision
collisions = 0
for r in sorted(R, key=lambda x: x["regionKey"]):
    if r["regionKey"] in sup_keys:
        nm, sc = supmap[r["regionKey"]], "superlative"
    else:
        nm = sc = None
        for attempt in range(60):
            cand, csc = name_with_salt(r, attempt)
            if cand not in used:
                nm, sc = cand, csc; 
                if attempt: collisions += 1
                break
        if nm is None:  # exhausted — disambiguate by cardinal
            base, _ = ns.name_region(r, supmap)
            nm = f'{base} ({ns.cardinal(r)[0]})'; sc = "disambig"
    used.add(nm)
    r["baseName"] = r["name"]; r["name"] = nm; r["nameSchema"] = sc
    r.pop("_loc", None)   # strip internal location-signal scratch (don't leak into output)

d["provenance"]["naming"] = {
    "status": "PROVISIONAL — design bench, not locked; enrichment layer over gazette",
    "schemaFamilies": 13, "uniquenessPass": True, "rerolledForUniqueness": collisions,
    "note": "name=multi-schema final; baseName=catalog stem; nameSchema=family that fired (*=re-rolled for uniqueness). Deterministic on regionKey.",
}

out_json = SRC.replace(".json", "_named.json")
json.dump(d, open(out_json, "w"), indent=2)

import csv
out_tsv = SRC.replace(".json", "_named.tsv")
cols = ["regionKey","name","nameSchema","baseName","dominantBiome","areaZones","landZones",
        "inlandWaterZones","areaKm2","isCoastal","reliefM","meanElevM","peakM","neighborCount","centroidX","centroidZ"]
with open(out_tsv,"w",newline="") as f:
    w=csv.writer(f,delimiter="\t"); w.writerow(cols)
    for r in sorted(R,key=lambda x:-x["areaZones"]):
        w.writerow([r["regionKey"],r["name"],r["nameSchema"],r["baseName"],r["dominantBiome"],r["areaZones"],
            r["landZones"],r["inlandWaterZones"],r["areaKm2"],1 if r["isCoastal"] else 0,
            round(r["elevationMeters"]["relief"],1),round(r["elevationMeters"]["mean"],1),
            round(r["highestPeakMeters"]["height"],1),len(r["neighborKeys"]),
            round(r["centroidMeters"]["x"]),round(r["centroidMeters"]["z"])])

out_txt = SRC.replace(".json","").replace("_gazetteer","") + "_regions_named.txt"
by=defaultdict(list)
for r in R: by[r["dominantBiome"]].append(r)
L=[f"NIFLHEIM  ·  seed ForTheWort  ·  {len(R)} regions  ·  PROVISIONAL multi-schema names","="*72]
L.append("\nLANDMARKS (rare — earned by the actual data):")
for k,v in supmap.items():
    r=next(x for x in R if x["regionKey"]==k)
    L.append(f"  ★ {v:<22}  {r['dominantBiome']}, peak {int(r['highestPeakMeters']['height'])}m, {len(r['neighborKeys'])} neighbors")
for biome in sorted(by):
    rs=sorted(by[biome],key=lambda x:-x["areaZones"])
    L.append(f"\n── {biome}  ({len(rs)} regions) "+"─"*(40-len(biome)))
    for r in rs:
        coast="~" if r["isCoastal"] else " "
        L.append(f"  {coast} {r['name']:<32} [{r['nameSchema']:<12}] {r['areaZones']:>3}z  relief {int(r['elevationMeters']['relief'])}m  ({r['baseName']})")
L+=["\n"+"="*72,"SCHEMA DISTRIBUTION:"]
for sc,n in Counter(r["nameSchema"].rstrip("*") for r in R).most_common():
    L.append(f"  {n:4d} ({n/len(R):4.0%})  {sc}")
open(out_txt,"w").write("\n".join(L)+"\n")

names=[r["name"] for r in R]
print(f"regions: {len(R)} | unique: {len(set(names))} | dupes: {len(names)-len(set(names))} | re-rolled: {collisions}")
print("wrote _named.json, _named.tsv, regions_named.txt")