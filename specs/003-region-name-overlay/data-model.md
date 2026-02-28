# Data Model: Valheim Region Name Overlay

**Phase 1 Output** | **Date**: 2026-02-28  
**Purpose**: Define core entities, relationships, validations, and state transitions for region overlay behavior.

---

## 1) RegionKey

**Purpose**: Stable identifier for a region within a world.

**Fields**:
- `worldId: string` - deterministic world identity (seed-derived or runtime world UID).
- `regionId: int` - stable region index/id from region assignment.

**Validation Rules**:
- `worldId` MUST be non-empty.
- `regionId` MUST be non-negative.

**Invariants**:
- `(worldId, regionId)` uniquely identifies one logical region.

---

## 2) RegionName

**Purpose**: Region display identity used by UI in v1.

**Fields**:
- `regionKey: RegionKey`
- `guidText: string` - deterministic GUID format string.

**Validation Rules**:
- `guidText` MUST parse as GUID.
- `guidText` MUST be deterministic for identical `regionKey`.

**State**:
- Immutable after creation.

---

## 3) RegionLookupContext

**Purpose**: Runtime request context for resolving a region from position.

**Fields**:
- `worldPositionX: float`
- `worldPositionZ: float`
- `viewMode: enum { MinimapCurrent, FullMapHover }`

**Validation Rules**:
- Coordinates MUST be finite values.
- `viewMode` MUST be one of defined values.

---

## 4) RegionLookupResult

**Purpose**: Output of position-to-region resolution.

**Fields**:
- `hasRegion: bool`
- `regionKey: RegionKey?`
- `regionName: RegionName?`
- `resolutionReason: enum { Resolved, Unassigned, OutOfBounds, DataUnavailable }`

**Validation Rules**:
- If `hasRegion = true`, `regionKey` and `regionName` MUST be non-null.
- If `hasRegion = false`, `resolutionReason` MUST NOT be `Resolved`.

---

## 5) DiscoveryState

**Purpose**: Persistent per-player record of discovered regions.

**Fields**:
- `playerId: string`
- `worldId: string`
- `discoveredRegionGuids: Set<string>`
- `lastUpdatedUtc: DateTime`

**Validation Rules**:
- `playerId` and `worldId` MUST be non-empty.
- All entries in `discoveredRegionGuids` MUST parse as GUID strings.
- Duplicate GUID entries are not allowed.

**State Transitions**:
- `Empty -> Discovered(guid)` on first entry into region.
- `Discovered(guid) -> Discovered(guid)` on re-entry (no banner).
- Persisted and reloaded unchanged across sessions unless file reset/corruption recovery occurs.

---

## 6) OverlayRenderState

**Purpose**: Current UI text state for minimap/map overlays.

**Fields**:
- `minimapVisible: bool`
- `fullMapVisible: bool`
- `currentRegionGuidText: string?`
- `hoverRegionGuidText: string?`
- `lastRenderedRegionKey: RegionKey?`

**Validation Rules**:
- `currentRegionGuidText` and `hoverRegionGuidText` MUST be null when corresponding view is hidden.
- Rendered text MUST match latest `RegionLookupResult` for active context.

---

## 7) WorldDataProviderContract

**Purpose**: Abstraction boundary that allows region logic to consume either hand-spun worldgen (CLI) or Valheim runtime worldgen (mod).

**Fields/Capabilities**:
- World identity retrieval (`worldId`).
- Position-to-zone conversion.
- Zone classification input required by region generation/lookup.

**Validation Rules**:
- Provider MUST produce deterministic outputs for identical world and position inputs.
- Provider MUST return explicit failure state when data is unavailable.

---

## Relationships

- `RegionLookupContext -> RegionLookupResult` via region resolver.
- `RegionLookupResult.regionKey -> RegionName` via deterministic naming function.
- `RegionName.guidText` feeds both `OverlayRenderState` and `DiscoveryState`.
- `DiscoveryState` gates whether discovery banner is emitted for a resolved region.

---

## Derived Rules

1. Discovery banner emits only when:
   - `hasRegion = true`, and
   - `regionName.guidText` not present in `DiscoveryState.discoveredRegionGuids`.
2. Minimap label renders only when minimap is visible and `hasRegion = true`.
3. Full-map hover label renders only when full map is visible, hover context is valid, and `hasRegion = true`.
4. Missing region data never renders placeholder fake IDs; UI remains blank for that context.
