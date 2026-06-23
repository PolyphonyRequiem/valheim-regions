#!/usr/bin/env python3
"""
Region naming — MULTI-SCHEMA prototype v2 (provisional design bench, not the production namer).

A LARGE registry of naming SCHEMA FAMILIES. Names tell different KINDS of story:
  terrain · character/settlement · lore/legend · memorial · cardinal · minted · superlative.
Most schemas are NOT terrain descriptors — they invent faux-lore: people who lived/ruled/died there,
creatures, events. The gazette data's job is SCHEMA SELECTION: it biases which kind of story a
region's name tells (remote+dangerous → folly/grave; big+central+hospitable → hold/jarldom;
high+rugged → spire/heights; coastal → landing/watch). Deterministic on regionKey → survives churn.

Each schema: (key, make(r,ctx), weight(r,ctx), fits(r,ctx)). Eligible schemas are weighted by a
DATA-DRIVEN weight, then one is picked by hash(regionKey). This is a lab bench; lock the set, then port.
"""
import json, sys, math
from collections import Counter

def h(s, salt=0):
    x=(salt*0x9E3779B1)&0xFFFFFFFF
    for ch in str(s):
        x=((x<<5)+x)^ord(ch); x&=0xFFFFFFFF
    x^=x>>16; x=(x*0x7FEB352D)&0xFFFFFFFF
    x^=x>>15; x=(x*0x846CA68B)&0xFFFFFFFF
    x^=x>>16
    return x&0xFFFFFFFF
def pick(pool,key,salt): return pool[h(key,salt)%len(pool)]

# ── rosters (fictitious faux-Norse) ──────────────────────────────────
PEOPLE = ["Halla","Ulfr","Bjorn","Sigrun","Thrandr","Astrid","Gunnar","Hildr","Ivar","Thora",
          "Gudrun","Leif","Knut","Frida","Olvir","Ingrid","Hakon","Vigdis","Sigurd","Brand",
          "Yrsa","Orm","Dagny","Steinarr","Solveig","Torvald","Ragnhild","Eyvind","Bera","Hrolf"]
CREATURES = ["Wolf","Raven","Boar","Elk","Bear","Serpent","Drake","Stag","Hawk","Lynx","Adder","Crow"]
# faux mythic figures / titles (invented, not the real pantheon, to read as world-native lore)
MYTHIC = ["the Grey Wanderer","the Drowned King","the Ash-Mother","the Hooded One","the Twin Jarls",
          "the Last Skald","the Pale Rider","the Hunter in Mist"]
EVENTS = ["the Sundering","the Long Winter","the Reaving","the Burning","the Silence","the Drowning",
          "the Severing","Last Light"]

# ── terrain vocab (for the terrain-flavored minority) ────────────────
BIOME_DESC={"Mountain":["Highlands","Heights","Crags","Peaks"],"Swamp":["Marshlands","Mire","Fens","Boglands"],
  "Plains":["Plains","Flats","Steppe","Expanse"],"Meadows":["Vale","Downs","Greens","Meadows"],
  "BlackForest":["Woods","Pinewoods","Thicket","Wilds"],"Mistlands":["Mistlands","Shroud","Veil","Gloaming"],
  "DeepNorth":["Frostlands","Tundra","Winterreach","Hoarfrost"],"AshLands":["Cinderlands","Emberwastes","Scorchlands","Ashfall"]}
BIOME_ADJ={"Mountain":["Towering","Stone","Wind-scoured","High"],"Swamp":["Sunken","Drowned","Rotting","Foul"],
  "Plains":["Golden","Windswept","Open","Wide"],"Meadows":["Verdant","Gentle","Sunlit","Green"],
  "BlackForest":["Shadowed","Tangled","Pine-dark","Deep"],"Mistlands":["Misted","Veiled","Shrouded","Dim"],
  "DeepNorth":["Frostbound","Hoar","Frozen","Pale"],"AshLands":["Charred","Ember","Scorched","Blackened"]}
BIOME_PREFIX={"Mountain":["Berg","Stein","Haug"],"Swamp":["Myrk","Saur","Kjarr"],"Plains":["Slet","Vidda","Eng"],
  "Meadows":["Grön","Eng","Vang"],"BlackForest":["Skog","Furu","Myrk"],"Mistlands":["Mistr","Niflr","Dvergr"],
  "DeepNorth":["Frost","Snae","Vetr"],"AshLands":["Eld","Aska","Brenn"]}
COAST_SUF=["vik","fjord","havn","sund","nes","kyst"]; LAND_SUF=["fell","dalr","holt","skog","voll","berg","mark"]
SETTLE_SUF=["hold","watch","garth","stead","by","thorp","gard"]; GRAVE=["Barrow","Cairn","Howe","Grave","Mound"]

def cardinal(r):
    x,z=r["centroidMeters"]["x"],r["centroidMeters"]["z"]; ang=math.degrees(math.atan2(z,x))
    table=[("Aust",-22.5,22.5),("Nordaust",22.5,67.5),("Nord",67.5,112.5),("Nordvest",112.5,157.5),
           ("Vest",157.5,180.1),("Vest",-180,-157.5),("Sudvest",-157.5,-112.5),("Sud",-112.5,-67.5),("Sudaust",-67.5,-22.5)]
    nm="Nord"
    for n,a,b in table:
        if a<=ang<b: nm=n; break
    return nm, math.hypot(x,z)

# ── derived region "character" flags used for scheme biasing ─────────
def traits(r):
    b=r["dominantBiome"]; relief=r["elevationMeters"]["relief"]; peak=r["highestPeakMeters"]["height"]
    nbr=len(r["neighborKeys"]); area=r["areaZones"]; _,dist=cardinal(r)
    return dict(biome=b, relief=relief, peak=peak, nbr=nbr, area=area, dist=dist,
        remote = dist>6500 or nbr<=1,
        hospitable = b in ("Meadows","Plains","BlackForest"),
        dangerous = b in ("Swamp","Mistlands","AshLands","DeepNorth"),
        rugged = relief>=200 or b=="Mountain",
        big = area>=300, small = area<=120, coastal = r["isCoastal"])

# ── schema makers ────────────────────────────────────────────────────
def s_bare(r,c):        return r["name"]
def s_terr_post(r,c):   return f"the {r['name']} {c['desc']}"
def s_terr_of(r,c):     return f"the {c['desc']} of {r['name']}"
def s_descriptive(r,c):
    b=r["dominantBiome"]; return f"the {pick(BIOME_ADJ.get(b,['Wild']),r['regionKey'],11)} {pick(BIOME_DESC.get(b,['Reach']),r['regionKey'],12)}"
def s_cardinal(r,c):
    return f"the Far {c['card']}" if c['t']['dist']>7500 else f"{c['card']}{pick(['mark','reach','land','heim'],r['regionKey'],13)}"
def s_minted(r,c):
    b=r["dominantBiome"]; pre=pick(BIOME_PREFIX.get(b,["Vild"]),r["regionKey"],14)
    return pre+pick(COAST_SUF if r["isCoastal"] else LAND_SUF,r["regionKey"],15)
def s_person_possessive(r,c):
    p=pick(PEOPLE,r["regionKey"],20); thing=pick(["Rest","Reach","Landing","Holding","Stand","Crossing","Folly","Ward"],r["regionKey"],21)
    return f"{p}'s {thing}"
def s_settlement(r,c):
    p=pick(PEOPLE,r["regionKey"],22)
    form=pick(["the Hold of {p}","the Jarldom of {p}","{p}{suf}","{col}{suf}"],r["regionKey"],23)
    return form.format(p=p, suf=pick(SETTLE_SUF,r["regionKey"],24), col=pick(["Grey","East","North","Stone","Black","High","Wind"],r["regionKey"],25))
def s_creature(r,c):
    cr=pick(CREATURES,r["regionKey"],30)
    form=pick(["{cr}{suf}","the {cr}'s {place}","{cr}moor"],r["regionKey"],31)
    return form.format(cr=cr, suf=pick(["moor","fell","mere","wood","crag","fen"],r["regionKey"],32),
                       place=pick(["Wallow","Roost","Den","Run","Grave","Hollow"],r["regionKey"],33))
def s_memorial(r,c):
    who=pick(PEOPLE,r["regionKey"],40); g=pick(GRAVE,r["regionKey"],41)
    return pick([f"{who}'s {g}", f"the {g} of {who}", f"{who}{pick(['howe','barrow','cairn'],r['regionKey'],42)}"],r["regionKey"],43)
def s_lore_figure(r,c):
    fig=pick(MYTHIC,r["regionKey"],50)
    return pick([f"{fig}'s Rest", f"where {fig} fell", f"the Hall of {fig}", f"{fig}'s Folly"],r["regionKey"],51)
def s_lore_event(r,c):
    ev=pick(EVENTS,r["regionKey"],60)
    return pick([f"{ev}", f"the Land of {ev}", f"{r['name']}, after {ev}"],r["regionKey"],61)
def s_superlative(r,c): return c["superlative"]

# (key, make, weight(r,t), fits)
SCHEMAS=[
 ("bare",          s_bare,            lambda r,t: 26, lambda r,t: True),
 ("terrain-post",  s_terr_post,       lambda r,t: 10, lambda r,t: True),
 ("terrain-of",    s_terr_of,         lambda r,t: 6,  lambda r,t: True),
 ("descriptive",   s_descriptive,     lambda r,t: 9 + (6 if t['dangerous'] else 0), lambda r,t: r["dominantBiome"] in BIOME_ADJ),
 ("cardinal",      s_cardinal,        lambda r,t: 6 + (8 if t['remote'] else 0), lambda r,t: True),
 ("minted",        s_minted,          lambda r,t: 8,  lambda r,t: r["dominantBiome"] in BIOME_PREFIX),
 ("person",        s_person_possessive,lambda r,t: 12 + (8 if t['hospitable'] else 0), lambda r,t: True),
 ("settlement",    s_settlement,      lambda r,t: 6 + (14 if (t['big'] and t['hospitable']) else 0), lambda r,t: True),
 ("creature",      s_creature,        lambda r,t: 9 + (6 if (t['dangerous'] or t['biome']=='BlackForest') else 0), lambda r,t: True),
 ("memorial",      s_memorial,        lambda r,t: 5 + (10 if t['dangerous'] else 0), lambda r,t: True),
 ("lore-figure",   s_lore_figure,     lambda r,t: 3 + (14 if (t['remote'] and t['dangerous']) else 0), lambda r,t: True),
 ("lore-event",    s_lore_event,      lambda r,t: 2 + (10 if (t['remote'] and t['small']) else 0), lambda r,t: True),
 ("superlative",   s_superlative,     lambda r,t: 80, lambda r,t: c_has_sup(r,t)),
]
def c_has_sup(r,t): return t.get("_sup") is not None

def descriptor(r):
    b=r["dominantBiome"]; relief=r["elevationMeters"]["relief"]
    if b!="Mountain" and relief>=220: return pick(["Highlands","Heights","Crags"],r["regionKey"],7)
    return pick(BIOME_DESC.get(b,["Coast","Shores","Reach"]),r["regionKey"],7)

def build_superlatives(R):
    sup={}
    peak=max(R,key=lambda r:r["highestPeakMeters"]["height"])
    sup[peak["regionKey"]]=pick(["the Spire","Himinbjorg","the Roof of the World"],peak["regionKey"],21)
    big=max(R,key=lambda r:r["areaZones"])
    sup.setdefault(big["regionKey"],f"Greater {big['name']}")
    iso=min(R,key=lambda r:(len(r["neighborKeys"]),-r["areaZones"]))
    if len(iso["neighborKeys"])<=1: sup.setdefault(iso["regionKey"],pick(["the Lonely Isle","Utgard","the Sundered Land"],iso["regionKey"],22))
    return sup

def name_region(r, supmap):
    t=traits(r); t["_sup"]=supmap.get(r["regionKey"])
    c={"desc":descriptor(r),"card":cardinal(r)[0],"superlative":t["_sup"],"t":t}
    elig=[(k,mk,w(r,t)) for k,mk,w,fits in SCHEMAS if fits(r,t)]
    total=sum(w for _,_,w in elig); roll=h(r["regionKey"],777)%total; acc=0
    for k,mk,w in elig:
        acc+=w
        if roll<acc: return mk(r,c),k
    return r["name"],"bare"

def main():
    path=sys.argv[1] if len(sys.argv)>1 else "ForTheWort_gazetteer.json"
    R=json.load(open(path))["regions"]; supmap=build_superlatives(R)
    by={}
    for r in R: by.setdefault(r["dominantBiome"],[]).append(r)
    sample=[]
    for b,rs in sorted(by.items()):
        rs.sort(key=lambda x:-x["areaZones"]); sample+=rs[:3]
    print(f"=== MULTI-SCHEMA naming v2 · {len(R)} regions · {len(SCHEMAS)} schema families ===\n")
    rows=[(r["dominantBiome"],r["name"],*name_region(r,supmap)[::1]) for r in sorted(sample,key=lambda x:x["dominantBiome"])]
    rows=[(b,base,nm,sc) for (b,base,(nm,sc)) in ((r[0],r[1],name_region(r2,supmap)) for r,r2 in zip(rows,sorted(sample,key=lambda x:x["dominantBiome"])))]
    w1=max(len(x[0]) for x in rows);w2=max(len(x[1]) for x in rows);w3=max(len(x[2]) for x in rows)
    print(f"{'biome':<{w1}}  {'base':<{w2}}  {'→ name':<{w3}}  schema")
    print("-"*(w1+w2+w3+18))
    for b,base,nm,sc in rows: print(f"{b:<{w1}}  {base:<{w2}}  {nm:<{w3}}  {sc}")
    print(f"\n=== rare landmark (superlative) regions ===")
    for k,v in supmap.items():
        r=next(x for x in R if x["regionKey"]==k)
        print(f"  {v:<22} was '{r['name']}' ({r['dominantBiome']}, peak {int(r['highestPeakMeters']['height'])}m, {len(r['neighborKeys'])} nbr)")
    print(f"\n=== schema distribution over all {len(R)} regions ===")
    for sc,n in Counter(name_region(r,supmap)[1] for r in R).most_common():
        print(f"  {n:4d} ({n/len(R):4.0%})  {sc}")

if __name__=="__main__": main()
