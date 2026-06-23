# Region naming bench (PROVISIONAL)

> Lab bench for the multi-schema, data-informed region naming design. **Not the production namer** —
> it's a Python enrichment layer over a gazetteer JSON, used to iterate on naming schemes at zero cost
> before porting the locked design into the C# `RegionGuidNameService`.
>
> Design doc: [`docs/design/region-naming.md`](../../docs/design/region-naming.md).
> Status: provisional — tone / roster / register-mix not locked.

## Files

- `name_schemes.py` — the schema registry: 13 schema families across 5 registers (terrain, people/
  settlement, faux-lore, memorial, cardinal) + rare superlatives. Data-driven schema selection from
  gazetteer-derived region traits. Deterministic on `regionKey`.
- `enrich_gazetteer.py` — applies the schemes over a gazetteer JSON with a deterministic **uniqueness
  pass** (re-roll on collision). Emits `*_named.json`, `*_named.tsv`, and a readable `*_regions_named.txt`.

## Usage

```bash
# 1. produce a gazetteer (from repo root)
dotnet run --project src/WorldZones.Cli -f net8.0 -- gazetteer --seed ForTheWort --output /tmp/out --inland-water

# 2. enrich it with multi-schema names
python3 tools/naming/enrich_gazetteer.py /tmp/out/ForTheWort_gazetteer.json

# 3. read the map
less /tmp/out/ForTheWort_regions_named.txt
```

## Preview the scheme spread without writing files

```bash
python3 tools/naming/name_schemes.py /tmp/out/ForTheWort_gazetteer.json
```

Prints a per-biome sample, the rare superlative landmarks, and the schema distribution over all regions.

## Tuning dials (all in `name_schemes.py`)

- **Rosters** — `PEOPLE`, `CREATURES`, `MYTHIC`, `EVENTS`. Deeper = fewer uniqueness re-rolls.
- **Weights** — the `SCHEMAS` table's `weight(r,t)` lambdas; data biases (remote/dangerous/hospitable…)
  add weight to fitting schemas.
- **Vocabulary** — `BIOME_DESC`, `BIOME_ADJ`, `BIOME_PREFIX`, suffix pools.

When the design locks, this bench is the spec for the C# port (see the design doc's "The port" section).
The bench output (`*_named.*`) is **gitignored** — it's derived and seed-specific.
