using System.Collections.Generic;
using Xunit;
using WorldZones.Regions;

namespace WorldZones.Regions.Tests
{
    /// <summary>
    /// Guards the biome-aware SEEDING lever (the <see cref="SeedingField"/> seam in
    /// <see cref="ProtoRegionGenerator.GenerateLand"/>). Two load-bearing properties:
    /// <list type="number">
    ///   <item>a null field, or a field with zero aggressiveness / zero placement bias, is
    ///   BYTE-IDENTICAL to the legacy area-only seeding (the gated/additive regression guard);</item>
    ///   <item>a positive aggressiveness over a diverse component ADDS seeds → more, smaller regions
    ///   (the lever does something); and the result is deterministic.</item>
    /// </list>
    /// Regions stays biome-blind: these tests feed it an OPAQUE field of numbers, never a biome.
    /// See docs/design/region-borders.md ("the SEEDING lever").
    /// </summary>
    public class SeedingFieldTests
    {
        private static ZoneGrid LandGrid(float radius)
        {
            var grid = new ZoneGrid(radius);
            foreach (var c in grid.AllCoords()) grid[c] = DepthClass.Land;
            return grid;
        }

        private static List<LandComponent> Land(ZoneGrid grid) => ComponentLabeler.LabelLand(grid, out _);

        // A uniform field at a constant weight across the whole grid.
        private static SeedingField UniformField(ZoneGrid grid, double weight, double aggr, double bias = 0.0)
        {
            int size = grid.Size;
            var w = new double[size, size];
            for (int gy = 0; gy < size; gy++)
                for (int gx = 0; gx < size; gx++)
                    w[gy, gx] = weight;
            return new SeedingField(w, aggr, bias);
        }

        [Fact]
        public void NullField_IsByteIdenticalToLegacySeeding()
        {
            var grid = LandGrid(640f); // 21×21 = 441 zones, one component
            var land = Land(grid);

            var legacy = ProtoRegionGenerator.GenerateLand(
                grid, land, targetZonesPerRegion: 50, seedRng: 1234,
                out int[,] gridA, out List<Vector2i> seedsA);

            var withNull = ProtoRegionGenerator.GenerateLand(
                grid, land, targetZonesPerRegion: 50, seedRng: 1234,
                out int[,] gridB, out List<Vector2i> seedsB, seedingField: null);

            Assert.Equal(seedsA.Count, seedsB.Count);
            for (int i = 0; i < seedsA.Count; i++) Assert.Equal(seedsA[i], seedsB[i]);
            AssertGridsEqual(gridA, gridB);
        }

        [Fact]
        public void ZeroAggressiveness_IsByteIdenticalToLegacySeeding()
        {
            // A non-null field that does nothing (aggr 0, bias 0) must not perturb anything — proves the
            // guard is on the SCALARS, not merely on the null reference.
            var grid = LandGrid(640f);
            var land = Land(grid);

            var legacy = ProtoRegionGenerator.GenerateLand(
                grid, land, targetZonesPerRegion: 50, seedRng: 99,
                out int[,] gridA, out List<Vector2i> seedsA);

            var inert = ProtoRegionGenerator.GenerateLand(
                grid, land, targetZonesPerRegion: 50, seedRng: 99,
                out int[,] gridB, out List<Vector2i> seedsB,
                seedingField: UniformField(grid, weight: 1.0, aggr: 0.0, bias: 0.0));

            Assert.Equal(seedsA.Count, seedsB.Count);
            for (int i = 0; i < seedsA.Count; i++) Assert.Equal(seedsA[i], seedsB[i]);
            AssertGridsEqual(gridA, gridB);
        }

        [Fact]
        public void PositiveAggressiveness_OverDiverseComponent_AddsSeeds()
        {
            // A component that reads as maximally diverse (weight 1 everywhere) at aggressiveness 1.0
            // should roughly DOUBLE the seed budget (1 + 1·1 = 2×) vs the legacy area-only budget.
            var grid = LandGrid(640f);
            var land = Land(grid);

            ProtoRegionGenerator.GenerateLand(
                grid, land, targetZonesPerRegion: 50, seedRng: 7,
                out _, out List<Vector2i> legacySeeds);

            ProtoRegionGenerator.GenerateLand(
                grid, land, targetZonesPerRegion: 50, seedRng: 7,
                out _, out List<Vector2i> moreSeeds,
                seedingField: UniformField(grid, weight: 1.0, aggr: 1.0));

            Assert.True(moreSeeds.Count > legacySeeds.Count,
                $"diverse component should get more seeds ({moreSeeds.Count} vs {legacySeeds.Count})");
            // ~2× budget (441/50 = 8 legacy → ~16 with the lever); allow slack for rounding/merge.
            Assert.True(moreSeeds.Count >= legacySeeds.Count + legacySeeds.Count / 2,
                $"expected close to double the seeds, got {moreSeeds.Count} from {legacySeeds.Count}");
        }

        [Fact]
        public void SeedingLever_IsDeterministic()
        {
            var grid = LandGrid(640f);
            var land = Land(grid);

            ProtoRegionGenerator.GenerateLand(
                grid, land, targetZonesPerRegion: 50, seedRng: 555,
                out int[,] gridA, out List<Vector2i> seedsA,
                seedingField: UniformField(grid, weight: 0.7, aggr: 2.0, bias: 0.5));
            ProtoRegionGenerator.GenerateLand(
                grid, land, targetZonesPerRegion: 50, seedRng: 555,
                out int[,] gridB, out List<Vector2i> seedsB,
                seedingField: UniformField(grid, weight: 0.7, aggr: 2.0, bias: 0.5));

            Assert.Equal(seedsA.Count, seedsB.Count);
            for (int i = 0; i < seedsA.Count; i++) Assert.Equal(seedsA[i], seedsB[i]);
            AssertGridsEqual(gridA, gridB);
        }

        [Fact]
        public void Weight_IsClampedToUnitInterval()
        {
            var grid = LandGrid(192f);
            var f = UniformField(grid, weight: 5.0, aggr: 1.0); // over-range
            Assert.Equal(1.0, f.Weight(0, 0));
            var f2 = UniformField(grid, weight: -3.0, aggr: 1.0);
            Assert.Equal(0.0, f2.Weight(0, 0));
        }

        [Fact]
        public void PlacementBias_IsClampedBelowOne()
        {
            var grid = LandGrid(192f);
            var f = new SeedingField(new double[grid.Size, grid.Size], aggressiveness: 1.0, placementBias: 5.0);
            Assert.True(f.PlacementBias < 1.0 && f.PlacementBias > 0.0);
        }

        private static void AssertGridsEqual(int[,] a, int[,] b)
        {
            Assert.Equal(a.GetLength(0), b.GetLength(0));
            Assert.Equal(a.GetLength(1), b.GetLength(1));
            for (int y = 0; y < a.GetLength(0); y++)
                for (int x = 0; x < a.GetLength(1); x++)
                    Assert.True(a[y, x] == b[y, x], $"grid mismatch at ({x},{y}): {a[y, x]} != {b[y, x]}");
        }
    }
}
