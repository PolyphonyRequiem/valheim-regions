# Feature Specification: Valheim Region Name Overlay

**Feature Branch**: `[003-region-name-overlay]`  
**Created**: 2026-02-28  
**Status**: Draft  
**Input**: User description: "Show the player’s current region name in minimap and map UI, plus a one-time region discovery banner, using a pre-generated testing catalog of names."

## User Scenarios & Testing *(mandatory)*

<!--
  IMPORTANT: User stories should be PRIORITIZED as user journeys ordered by importance.
  Each user story/journey must be INDEPENDENTLY TESTABLE - meaning if you implement just ONE of them,
  you should still have a viable MVP (Minimum Viable Product) that delivers value.
  
  Assign priorities (P1, P2, P3, etc.) to each story, where P1 is the most critical.
  Think of each story as a standalone slice of functionality that can be:
  - Developed independently
  - Tested independently
  - Deployed independently
  - Demonstrated to users independently
-->

### User Story 1 - Minimap Region Visibility (Priority: P1)

As a player exploring the world, I can see my current region name at the bottom of the minimap so I always know which region I am in without opening additional tools.

**Why this priority**: This is the core value of the feature and must work for the feature to be useful.

**Independent Test**: Can be fully tested by moving across region boundaries with only minimap labeling enabled and confirming the shown name updates to the current region.

**Acceptance Scenarios**:

1. **Given** the player is in-game and the minimap is visible, **When** the player remains inside one region, **Then** the bottom minimap label shows that region’s test name.
2. **Given** the player crosses into a different region, **When** the position changes into the new region, **Then** the minimap label updates to the new region’s test name.
3. **Given** the minimap is not visible, **When** the player moves, **Then** no minimap region label is shown.

---

### User Story 2 - World Map Hover Region Context (Priority: P2)

As a player reviewing the full map, I can see the hovered location’s region name in the map UI so I can inspect region context before traveling.

**Why this priority**: This extends the core experience to planning/navigation and matches expected map inspection behavior.

**Independent Test**: Can be fully tested by opening the map and moving the cursor over explored terrain to confirm a region test name appears in the intended map UI area.

**Acceptance Scenarios**:

1. **Given** the full map is open and the cursor is over an explored position, **When** hover context is shown, **Then** the region test name for that hovered location is displayed in the top-left map area.
2. **Given** the full map is open and hover context changes to another region, **When** the cursor moves, **Then** the shown region test name updates to match the new hovered region.
3. **Given** the map is closed, **When** the player resumes gameplay, **Then** map-hover region text is not shown.

---

### User Story 3 - Region Discovery Feedback (Priority: P3)

As a player entering a region for the first time, I receive a discovery banner so new region entry is clearly signaled and memorable.

**Why this priority**: Discovery feedback improves usability but depends on region identification already working.

**Independent Test**: Can be fully tested by entering an undiscovered region, observing one banner, then leaving and re-entering to confirm no duplicate banner.

**Acceptance Scenarios**:

1. **Given** a player enters a region they have not discovered before, **When** entry is detected, **Then** one discovery banner appears with that region’s test name.
2. **Given** a player re-enters a previously discovered region, **When** entry is detected again, **Then** no new discovery banner is shown.
3. **Given** a player has discovered a set of regions and restarts the game, **When** they revisit those regions, **Then** discovery banners remain suppressed for previously discovered regions.

---

### Edge Cases

- Player stands exactly on a boundary between two regions; the displayed region must resolve deterministically and avoid rapid flickering.
- Current position or hovered position maps to no region (for example, out-of-bounds or unassigned area); no incorrect region name is shown.
- UI refresh timing differs between minimap and full map states; displayed region names remain consistent with active view context.
- Save data for discovered regions is missing or unreadable; system falls back safely and allows rediscovery without blocking gameplay.
- Existing biome-related map text is visible at the same time; region text must not suppress or overwrite biome text.
- The testing catalog contains exactly 500 names; when region count exceeds 500, displayed names remain deterministic and stable across sessions.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST determine the player’s current region from in-world position and provide a stable region identifier for display.
- **FR-002**: The system MUST show the current region test name at the bottom of the minimap while the minimap is visible.
- **FR-003**: The system MUST show the hovered location’s region test name in the top-left area of the full map screen.
- **FR-004**: The system MUST preserve existing biome display behavior while adding region display behavior.
- **FR-005**: The system MUST trigger one discovery banner when a player enters a region not yet discovered by that player.
- **FR-006**: The system MUST NOT trigger additional discovery banners for a region already discovered by the same player.
- **FR-007**: The system MUST persist discovered-region state per player across game sessions.
- **FR-008**: Region naming MUST be deterministic for the same world and region identity.
- **FR-009**: The system MUST continue functioning when region data cannot be resolved by omitting region text for that context instead of showing incorrect data.
- **FR-010**: The system MUST update displayed region information efficiently so normal gameplay responsiveness is preserved during movement and map hover.
- **FR-011**: The first release MUST use a fixed pre-generated testing catalog of 500 names as the visible region names.
- **FR-013**: The system MUST map each region identity to one catalog name deterministically, including when total regions exceed 500 names.
- **FR-012**: The specification MUST explicitly defer multiplayer authority and synchronization behavior to a later feature.

### Key Entities *(include if feature involves data)*

- **Region Identity**: A deterministic region record representing one logical region in a world, including a stable region ID and a deterministic display name.
- **Region Display Context**: A view-specific record describing where region text is rendered (minimap current position or full-map hover position).
- **Testing Name Catalog**: A fixed catalog of 500 pre-generated region names used for deterministic display during testing.
- **Discovery Record**: A per-player set of discovered region names (or stable region identities backing those names), used to gate banner display and persisted between sessions.

### Assumptions

- Players expect region text to appear only in the specified UI locations for this release.
- Region names for this feature are sourced from a fixed test catalog of 500 pre-generated names.
- Existing world/region generation data is available to support deterministic lookup.
- Local-first discovery behavior is acceptable until multiplayer rules are specified.

### Dependencies

- Availability of existing world-to-region mapping data generated from the current repository’s region pipeline.
- Access to the game UI update points needed to render minimap text, map-hover text, and discovery banners.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In validation play sessions, 100% of sampled player positions with valid region assignment display the correct region test name on the minimap when minimap mode is visible.
- **SC-002**: In validation play sessions, 100% of sampled explored hover positions on the full map display the correct region test name in the specified map UI area.
- **SC-003**: For a test set of at least 5 first-time region entries, discovery banners appear once per newly entered region and 0 additional times on re-entry.
- **SC-004**: Across repeated runs for the same world seed, region name assignment matches exactly for all sampled regions.
- **SC-005**: During movement and map-hover testing, region UI updates produce no user-observable degradation in normal gameplay responsiveness.

## Deferred Decisions

- Multiplayer authority model for region resolution and discovery ownership is intentionally deferred.
- Multiplayer synchronization and conflict resolution for discovered-region state is intentionally deferred.
