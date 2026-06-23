# Region naming — multi-schema, data-informed

> **Status:** PROVISIONAL design bench, 2026-06-23. Daniel likes the feel ("looking really good");
> tone / roster / register-mix are **not locked**. Currently implemented as a Python *enrichment
> layer* over the gazetteer (`tools/naming/`), NOT in the C# namer. This doc captures the design so it
> survives, and specifies the port. **Do not mark locked until Daniel says so.**

## The problem with the current namer

`RegionGuidNameService.CreateDeterministicName` hashes `(worldId, regionKey)` into a flat **500-name
catalogue** (`RegionNameCatalog` = 20 Norse geographic suffixes × 25 stems). Every region gets the
**same shape**: a bare proper noun (Nordadal, Eiksund, Myrkholt). Across 162 regions a map reads
*flat* — no texture, no story, no sense of place. The names also throw away signal that's already
present: the catalogue's suffixes (`-fell`=mountain, `-vik`/`-fjord`=coast, `-holt`/`-skog`=wood,
`-myr`=marsh) encode terrain, but the random-index pick ignores it.

## The design: schemas tell different KINDS of story, data picks which

Names shouldn't just *describe terrain* — they should tell stories: who lived/ruled/died there,
what happened, what haunts it. The key move: **the gazetteer data biases which kind of story a
region's name tells, not the words themselves.**

Two independent levers:
- **Lever A — base name:** (future) match catalogue suffix to terrain so a mountain region draws a
  `-fell` stem, not a `-vik`. *Not yet implemented; parked.*
- **Lever B — scheme:** wrap/replace the base via one of many **schema families**, chosen by region
  character. *This is what the bench implements.*

### Schema families (13 in the bench, 5 registers)

| Register | Schemas | Example (real Niflheim) |
|---|---|---|
| terrain | terrain-post, terrain-of, descriptive, minted | "the Dim Veil", "Mistrhavn" |
| people / settlement | person, settlement | "Halla's Crossing", "the Jarldom of Knut" |
| faux-lore | lore-figure, lore-event | "where the Pale Rider fell", "the Land of Last Light" |
| memorial | memorial | "Eyvindcairn", "the Mound of Sigrun" |
| spatial | cardinal | "the Far Nord", "Nordvestreach" |
| **rare** | superlative | "Himinbjorg" (the world's highest peak) |
| anchor | bare | "Eiksund" (unchanged catalogue name) |

### Data-driven schema selection (the mechanism)

Each region gets **traits** derived from the gazetteer: `remote` (far from centre or ≤1 neighbour),
`dangerous` (Swamp/Mistlands/AshLands/DeepNorth), `hospitable` (Meadows/Plains/BlackForest), `rugged`
(high relief), `big`/`small`, `coastal`. Each schema declares a `fits()` gate and a **data-driven
weight**:

- remote + dangerous → spikes **lore-figure / memorial** ("where X fell", a barrow)
- big + hospitable → spikes **settlement / jarldom** (a seat of power)
- dangerous / forest → spikes **creature** ("the Serpent's Roost", "Drakemoor")
- remote → spikes **cardinal** ("the Far Nord")
- the world's literal extremes (highest peak, largest area, only-island) → **superlative**, one each

Among eligible schemas, one is chosen by `hash(regionKey)` weighted — fully deterministic, survives
seed churn exactly like the base namer.

### Superlatives — the map's landmarks

Three regions get singular names *earned by the actual data*, which is why they feel special:
- highest peak in the world → "Himinbjorg" / "the Spire" / "the Roof of the World"
- largest region → "Greater {name}"
- the one region with 0 neighbours (a true island) → "the Sundered Land" / "Utgard"

### Uniqueness pass

Shallow rosters collide (8 dupes / 162 in the first pass — two "the Reaving", etc.). A deterministic
re-roll perturbs the hash key (`regionKey#1`, `#2`, …) until unique, superlatives reserved first.
Result: **162 / 162 unique.** A schema marked `*` was re-rolled.

### Healthy distribution (162 regions, observed)

~21% bare (anchors the map so it's not all purple prose), then a long tail — person 12%, lore-figure
9%, cardinal 9%, creature 9%, terrain-post 7%, memorial 7%, descriptive 7%, settlement 6%, lore-event
5%, minted 4%, terrain-of 2%, superlative <1%. No single scheme dominates.

## Open design decisions (Daniel's to lock)

1. **Register mix** — happy with ~21% bare + heavy people/lore lean? Or lore rarer/more-earned,
   terrain more common?
2. **Roster depth** — bench has 30 people / 12 creatures / 8 mythic / 8 events. Deeper pools = fewer
   re-rolls. Grow them?
3. **Tone** — faux-Norse to match Valheim. Keep, or stretch (darker / whimsical / more Skyrim-hold)?
4. **Lever A** — implement terrain-matched base names too, or is Lever B enough?

## The port (once locked)

The bench (`tools/naming/name_schemes.py` + `enrich_gazetteer.py`) is a **lab bench**, not production.
On lock:
1. Port the schema registry + rosters + trait derivation into `RegionGuidNameService` (or a new
   `RegionNamer` consuming a `ProtoRegion` + its gazetteer-derived traits).
2. Keep determinism on `(worldId, regionKey)`; the C# `MixRegionKey` already provides the hash.
3. Carry the uniqueness pass (needs whole-world context — name in a second pass after all regions
   exist, since collision detection is global).
4. Preserve `baseName` (catalogue stem) + `nameSchema` in output for debuggability.

Until then, names are applied as a reversible enrichment layer over the gazetteer JSON so the whole
162-name map can be read and tuned without touching the engine.
