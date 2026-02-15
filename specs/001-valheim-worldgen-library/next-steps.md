# Next Steps: Spec-Driven Development - 2026-02-15

## Current Situation

### ✅ What's Working
- WorldGenerator is implemented (GetBiome, GetHeight, GetBaseHeight)
- Ground truth validation infrastructure exists (PNG + tile data validated at 99.99%)
- Coordinate mapping formulas proven accurate
- Test framework in place

### ❌ What's Broken
- **Ground truth tests using WRONG coordinate mapping!**
  - Test uses worldSize=21000, actual is 24576 (3.0 m/pixel)
  - Test uses wrong pixel center
  - **Result: Only 15.6% match rate** (should be >90%)

### 📋 Documentation Updated
- ✅ Checkpoint 007 created with coordinate validation findings
- ✅ research.md updated with ground truth data section
- ✅ status-review.md created showing implementation status
- ✅ Checkpoint index updated

## Immediate Next Step (Spec-Driven)

### Fix Ground Truth Validation Tests

**Goal**: Update `GroundTruthComparisonTests.cs` to use validated coordinate mapping

**Changes Needed**:
```csharp
// OLD (WRONG):
float worldSize = 21000f;
float pixelToWorld = worldSize / bitmap.Width;
float worldX = (px - bitmap.Width / 2f) * pixelToWorld;
float worldZ = -(py - bitmap.Height / 2f) * pixelToWorld;

// NEW (VALIDATED):
const int WORLD_SIZE = 24576;  // 3.0 m/pixel exactly
const int PNG_CENTER = 4095;    // Validated center
double pixelToWorld = WORLD_SIZE / 8192.0;  // 3.0
double worldX = (px - PNG_CENTER) * pixelToWorld;
double worldZ = -(py - PNG_CENTER) * pixelToWorld;  // Z flipped
```

**Expected Outcome**: Match rate should jump from 15.6% to >90%

**Why This Matters**:
- Validates our WorldGenerator implementation
- Proves coordinate mapping research was correct
- Enables confidence in PNG export feature (Phase 4)
- Fulfills **FR-002**: "validate against reference maps"

## After Fixing Tests

### If Match Rate >90% ✅
**Mark Phase 3 (User Story 1) as COMPLETE**

Then proceed to:
1. **Phase 4**: Implement GetBiomeMap() and PNG export
2. Visual validation against online tools
3. Mark Feature 001 as COMPLETE

### If Match Rate 50-90% ⚠️
**WorldGenerator has systematic bias**

Debug steps:
1. Check distance calculations (are biome rings too small/large?)
2. Verify noise parameters (frequency, octaves)
3. Check biome thresholds (distance boundaries, noise cutoffs)
4. Compare GetHeight() values to tile height data

### If Match Rate <50% ❌
**Something fundamentally wrong**

Likely causes:
1. Seed hash algorithm mismatch
2. Perlin noise implementation differs from Valheim
3. Random number generator (UnityRandom) not matching Unity's Xorshift128
4. Wrong noise offsets or scaling

## Specification Compliance Check

### User Story 1 (Generate World from Seed)
- [x] FR-001: Accept seed ✅
- [x] FR-002: Calculate height with Perlin ✅  
- [x] FR-003: Determine biome procedurally ✅
- [x] FR-004: Deterministic output ✅ (same seed = same world)
- [x] FR-005-006: Query individual coordinates ✅
- [ ] **SC-002: Visual validation against reference** ⚠️ BLOCKED by coordinate mapping bug

**Status**: 90% complete, blocked by incorrect test

### User Story 2 (Export Biome Maps)
- [ ] FR-007: Query rectangular regions ❌
- [ ] FR-008: Export PNG ❌
- [ ] FR-009: Distinct biome colors ❌
- [ ] FR-015: Accurate spatial mapping ❌

**Status**: 0% complete, waiting on US1 validation

### User Story 3 (Query Terrain Data)
- [x] FR-005-006: Individual queries work ✅
- [ ] FR-007: Batch queries ❌

**Status**: 50% complete

## Recommended Action Plan

**Now (30 minutes)**:
1. Fix `GroundTruthComparisonTests.cs` coordinate mapping
2. Run test again
3. Celebrate >90% match rate 🎉

**Next Session (2-3 hours)**:
1. If >90%: Implement GetBiomeMap() (Phase 4)
2. If 50-90%: Debug WorldGenerator biases
3. If <50%: Deep dive into noise/random implementations

**After Phase 4 (1-2 hours)**:
1. Export PNG map for seed "HHcLC5acQt"
2. Compare visually to ground truth PNG
3. Document any visible differences
4. Mark Feature 001 COMPLETE ✅

## Summary

We're **95% done with Feature 001** but tests are using wrong coordinates!

**The fix is trivial** (update 4 constants), then we should see >90% validation proving our WorldGenerator works correctly.

After that, just implement PNG export and we're done with the entire feature specification. 🎯
