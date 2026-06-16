# WorldZones — Roadmap & Re-Orientation

> **Purpose:** get your understanding of the whole picture back to current after the
> dormancy + today's deep dive + the adversarial review. Read top to bottom; it's ordered
> from "what's true" → "what the review found" → "the vision" → "what to actually do."
> Authored 2026-06-15. Provisional where marked — **your call to lock; nothing here is
> decided on your behalf.**

---

## Part 1 — Where WorldZones actually is (ground truth, verified)

- **The engine is sound.** Builds clean (net472 + net8.0). **102/102 tests pass** (WorldGen 34, Regions 68). Worldgen is a faithful port of Valheim's (bit-exact was achieved historically, commit `7b34f19`).
- **It's been dormant since 2026-03-02** (last code commit = the inland-water merge). Four spec arcs shipped: 001 worldgen port → 002 region skeleton → 003 name overlay → 004 inland-water attribution.
- **The pipeline (locked in conversation today):**
  ```
  pre-gen intent → worldgen-guided → vanilla worldgen → POST-GEN derive regions → consumer meaning
                                                          ▲ ALWAYS RUNS = the MVP
  ```
- **The known weak layer:** region **borders are terrain-blind** — unweighted BFS flood-fill from random seeds, zero awareness of rivers/ridges/coastlines. A deliberate v0 placeholder (spec 002 deferred weighted Dijkstra) that *calcified* as specs 003/004 built on top of it.
- **Knowledge base now exists** at `docs/knowledge/` (map → worldgen → regions → contract-and-invariants) + a token-cheap code index (`tools/knowledge/wzq.py`, pivots to the Valheim decomp). This is how we reason about it with real grounding now.

---

## Part 2 — What the adversarial review found (the important part)

We ran a 3-lens panel (game-systems, architecture, vision). Two timed out mid-run on the big decomp; the vision lens finished and I ground-checked the two highest-value technical claims myself. **The headline reframe:**

> **We've been polishing the comfortable problem.** Terrain-blind borders are concrete, isolated, and fun to fix — which is exactly why they've absorbed our attention while three worse risks sat unattended.

### 🔴 START-OVER risk #1 — the correctness guard was deleted, right before the foundation churns
Commit `aea19e0` removed *every* test that checked the port against the **real Valheim map** (ground-truth PNG comparison, biome-pattern analysis, coordinate validation). The 102 green tests are **structural** (pipeline is self-consistent) — **none prove the map is actually correct.** "Bit-exact" is now a historical claim with no live regression test. And Valheim **1.0 churns the worldgen assemblies** — if Deep North shifts terrain and our port silently diverges, regions compute against terrain that doesn't match the ground players walk, and it fails **invisibly** (determinism still holds — every client is wrong *identically*). The deleted tests were the only thing that would catch this.

### 🔴 START-OVER risk #2 — region identity is secretly the seed's array index (I found this by grounding)
`ProtoRegionGenerator.cs:161` → `regionIdGrid[seed] = i`. **A region's ID is literally its seed's index in the seeds list.** The inland-water attributor, lookup service, naming, and `ProtoRegion` all key off it. So any new border algorithm that changes seed *count or order* — weighted Dijkstra, authored seeds, anything — **silently renumbers every region.** Names shift; any persisted claim points at the wrong land. The "swappable behind `GenerateLand`" thesis is clean for the grid *shape* but **leaks seed-ordering into region identity.** This is small to fix now, catastrophic after anything persists region IDs.

### 🔴 START-OVER risk #3 — the contract insures the wrong axis
The contract guarantees **`RegionId` is stable.** But the territorial vision attaches meaning to region **extent** — "the guild owns the land around the lake," "this map shows *this territory*." Those are claims about *where the border is* — and extent is exactly what the architecture lists as "freely swappable." **Stable integer ≠ stable territory.** The moment a consumer keys a claim off extent, the "swap borders later" door slams shut unless there's a border-moved-under-a-live-claim migration story. There isn't one. **This is your original "build on borders, have to start over" fear, in the one form the contract doesn't cover.**

### ✅ The good news (also grounded)
**Determinism-across-clients looks sound.** No unseeded `Random()`; the order-dependent steps (merge, component processing) are explicitly sorted with deterministic tie-breaks. The prior authors got the hardest correctness property *right*. So the substrate's core promise holds — it's the *validation of correctness* (#1) and the *identity model* (#2) that are soft, not the determinism.

---

## Part 3 — The vision (extracted)

The territorial vision (regions = places, claims, guilds, the ambition ladder L1→L3, the
IR / lift–lower idea, progression tiers) has moved to its own doc so this roadmap stays a
launch-scoped action plan:

- **[`vision/territorial-substrate.md`](vision/territorial-substrate.md)** — the molten,
  consumer-agnostic vision. **Provisional; your call to lock.**
- **[`partner-integrators/trailborne.md`](partner-integrators/trailborne.md)** — the
  Trailborne-specific integration considerations (the provider model + the two-repos
  persistence mismatch the review flagged).

The one piece that *stays* here because it's a launch risk, not vision: the territorial
direction attaches meaning to region **extent**, which the contract lists as swappable — so
"stable `RegionId`" doesn't protect a territory claim. See Part 2 risk #3 and
[`knowledge/contract-and-invariants.md`](knowledge/contract-and-invariants.md).

---

## Part 4 — The launch reality (mid-September, Valheim 1.0 / Deep North)

**The September path isn't currently a path — it deadlocks:**
border decisions gate on the ESP → the ESP can't build without a **client rig** → the Linux worker box isn't set up → repo's been dormant 3.5 months → the entire vision + knowledge base is **uncommitted working-tree files.**

**The honest cut list for an actual launch:**
- **SHIP:** L1 overlay with terrain-blind borders **as-is** (deterministic, correct identity — "arbitrary midline" is *fine* for flavor labels), **restored ground-truth validation**, Thunderstore packaging, in-game validation on a real client.
- **CUT from launch (post-September):** the border rewrite (Dijkstra/hybrid), L2 seed-selection, the IR, ESP-as-product, the territorial/provider integration.

---

## Part 5 — Decisions pending (yours)

1. **The four border decisions** (these pin the border *requirements*, all still open):
   - Is a border gameplay-load-bearing or cosmetic?
   - Tier-coherent or tier-spanning regions?
   - Granularity — guild-holding-sized or progression-chapter-sized?
   - Read-only (L1–2) or authored (L3)?
2. **Worker box = Linux** — you confirmed Linux for the worker server. Windows stays the playtest/ESP-walking rig; git is the bridge.
3. **What to commit** — the knowledge base, contract doc, and this roadmap are untracked. Committing them is itself risk-reduction (a dead box shouldn't evaporate the plan).

---

## Part 6 — Recommended path (MY priority order — reversible, argue with it)

1. **Restore the deleted ground-truth validation FIRST.** Before any border work. It's the launch gate *and* the guard against 1.0 breaking us silently. (Was buried; the review promoted it to #1.)
2. **Decouple `RegionId` from seed index.** Small now, catastrophic after persistence. Do it before any consumer depends on region IDs.
3. **Commit the docs** (knowledge base + contract + this roadmap). Cheap, high-value insurance.
4. **Stand up the Linux worker box** — the single gating dependency that unblocks ESP → border judgment → in-game validation → packaging. It's the long pole, not a side quest.
5. **ESP (ground-projected lines) → walk the borders → make the four border decisions** against reality, not imagination.
6. **Then** border rewrite / L2 / IR / territorial integration — all post-launch, on the now-solid foundation.

---

## Open questions to resolve (not urgent, but live)

- Re-run the two timed-out review lenses (game-systems "does 'regions' even fit how Valheim is played" + the full architecture swap-point trace)? Lighter scope so they finish.
- Biome-variants wildcard (1.0) — unverified; could change the classification layer. Wait for the real spec.
- Does the territorial vision want regions to be **biome-coherent** or **tier-spanning**? (Border decision #2, but it's the one with the most downstream pull.)
