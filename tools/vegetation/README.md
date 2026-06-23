# Vegetation catalogue extraction (Wall 2 unblock)

Extracts Valheim's **real** `ZoneVegetation` placement configs (the `m_vegetation` list on
`ZoneSystem`) from a **client** install — the ore/flora min/max/biome/altitude/group data that does
**not** exist on a headless box. This is the one-time, client-gated extraction that turns
`VegetationModel` from a scaffold (empty catalogue, honest zero) into a real estimator.

See `docs/design/vegetation-resource-model.md` → "Wall 2" for why this data is otherwise unreachable.

## Output

`data/valheim_vegetation_catalogue.json` — 98 configs (8 ore/resource, 90 flora), schema:

```jsonc
{
  "provenance": {"source": "assetripper-export", "tool": "AssetRipper 1.3.14",
                 "asset": "valheim_Data/_ZoneSystem.prefab m_vegetation", "schemaVersion": 1},
  "count": 98,
  "configs": [
    {"PrefabName": "rock4_copper", "Biomes": ["BlackForest"], "BiomeMask": 8,
     "Min": 0, "Max": 1, "GroupSizeMin": 1, "GroupSizeMax": 1, "GroupRadius": 0,
     "MinAltitude": 4, "MaxAltitude": 1000, "IsResource": true, ...}
  ]
}
```

Validation sanity (matches known Valheim ore distribution): `silvervein` → Mountain alt≥120 m;
`rock4_copper` → BlackForest alt≥4 m; `MineRock_Tin` → BlackForest shoreline (alt −0.6..1.5);
`Pickable_BogIronOre` → Swamp; `MineRock_Obsidian` → Mountain alt≥100 m.

## How to regenerate (requires a Valheim CLIENT install — not the dedicated server)

The dedicated server has **no** asset bundles; you need the real client (`valheim_Data/resources.assets`
+ `Managed/assembly_valheim.dll`). On RequiemSoul (15 GB) this OOMs — run it on **Requiem Prime U**
(62 GB) via ssh-as-command. AssetRipper loads ~6 GB of asset graph and exports a 6.6 GB Unity project.

```bash
# 1. AssetRipper 1.3.14 (headless web-API build), on the box with the client install
cd ~/tools/assetripper
#   launch detached (setsid + </dev/null is REQUIRED — a trailing & dies on SSH channel teardown):
setsid bash -c './AssetRipper.GUI.Free --headless --port 5610 >/tmp/ar_srv.log 2>&1' </dev/null
#   wait ~6s, confirm: ss -ltn | grep 5610  ->  LISTEN 127.0.0.1:5610

# 2. load the client's valheim_Data folder (HTTP 302 = success; ~20s, ~6 GB RAM)
curl -fsS -X POST http://127.0.0.1:5610/LoadFolder \
  --data-urlencode "Path=$HOME/.steam/steam/steamapps/common/Valheim/valheim_Data"

# 3. export the full Unity project (decompiles MonoBehaviours to YAML; ~90s, 6.6 GB out)
curl -fsS -X POST http://127.0.0.1:5610/Export/UnityProject \
  --data-urlencode "Path=/tmp/valheim_export"

# 4. parse _ZoneSystem.prefab's m_vegetation -> catalogue JSON
python3 tools/vegetation/parse_vegetation.py data/valheim_vegetation_catalogue.json
```

The config catalogue lives in `ExportedProject/Assets/Systems/_ZoneSystem.prefab` under `m_vegetation:`.
`parse_vegetation.py` resolves each entry's `m_prefab` GUID to a real prefab name via the export's
`.prefab.meta` index, maps `m_biome` (raw Valheim bitmask) to biome names, and flags ore prefabs.

## Consuming the catalogue

`dotnet run --project src/WorldZones.Cli -f net8.0 -- gazetteer --seed <SEED> --output <dir> \
    --inland-water --vegetation data/valheim_vegetation_catalogue.json`

emits `{seed}_vegetation.json` — per-region modeled ore/flora counts, keyed by `regionKey` to join the
core gazetteer + location sidecars. Every value is `source: modeled` (upper-bias: headless can't apply
the mesh/physics rejection filters — see the design doc's documented over-count caveat).
