using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    /// <summary>
    /// Ground-truth biome validation against an INDEPENDENT oracle.
    ///
    /// The fixture (tests/.../fixtures/biome_oracle_HHcLC5acQt.bin.gz) is decoded from a
    /// valheim-map.world "All Data" export for seed HHcLC5acQt (Valheim 0.221.4). That site runs
    /// its own reconstruction of Valheim worldgen, so agreement with our port is genuine
    /// cross-validation — not a self-comparison. This is the regression guard the structural
    /// tests cannot provide: it proves the map we compute matches the world players actually walk.
    ///
    /// WHY THE THRESHOLD IS NOT 100% (measured, not hand-waved):
    /// A biome map is a categorical function with razor-thin boundaries. The oracle stores biomes
    /// on ITS sample grid (~5.86 m); we evaluate at the EXACT query coordinate. At a biome edge the
    /// two grids can land on opposite sides of the line by ~1 m — both correct, sampled differently.
    /// Empirically every mismatch resolves within 2 m (the port reproduces the oracle's biome a
    /// hair away). So we assert two things:
    ///   (1) overall match >= 99%   — the headline correctness gate, and
    ///   (2) ~0 mismatches survive a 2 m nudge — the sharp drift detector. A real regression
    ///       (e.g. Valheim 1.0 shifting worldgen) produces SOLID disagreement that a 2 m move
    ///       cannot explain away; sampling-seam noise always can.
    /// Baseline at authoring: 99.84% match, 0 of ~49 mismatches unresolved within 2 m.
    /// </summary>
    public class GroundTruthBiomeValidationTests
    {
        private const string Seed = "HHcLC5acQt";
        private const double MinMatchFraction = 0.99;        // (1) headline gate
        private const int MaxUnresolvedMismatches = 5;       // (2) drift detector (slack for grid edges)
        private const float ResolveRadiusMeters = 2.0f;

        private readonly ITestOutputHelper output;
        public GroundTruthBiomeValidationTests(ITestOutputHelper output) => this.output = output;

        private static string FixturePath()
        {
            // walk up from the test bin dir to the project, then into fixtures/
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "fixtures", "biome_oracle_" + Seed + ".bin.gz");
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            throw new FileNotFoundException("oracle fixture not found by walking up from " + AppContext.BaseDirectory);
        }

        private static List<(float wx, float wz, int biome)> LoadOracle()
        {
            using var fs = File.OpenRead(FixturePath());
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            gz.CopyTo(ms);
            ms.Position = 0;
            using var br = new BinaryReader(ms);
            int n = br.ReadInt32();
            var recs = new List<(float, float, int)>(n);
            for (int i = 0; i < n; i++)
                recs.Add((br.ReadSingle(), br.ReadSingle(), br.ReadUInt16()));
            return recs;
        }

        [Fact]
        public void PortBiomes_MatchIndependentOracle_AboveThreshold()
        {
            var oracle = LoadOracle();
            Assert.True(oracle.Count > 10000, $"fixture suspiciously small: {oracle.Count} points");

            var gen = new WorldGenerator(Seed);
            int match = 0;
            var mismatches = new List<(float wx, float wz, int oracle)>();
            foreach (var (wx, wz, ob) in oracle)
            {
                if ((int)gen.GetBiome(wx, wz) == ob) match++;
                else mismatches.Add((wx, wz, ob));
            }

            double frac = (double)match / oracle.Count;

            // (2) how many mismatches are NOT explained by sub-2m sampling-seam jitter?
            int unresolved = 0;
            foreach (var (wx, wz, ob) in mismatches)
            {
                bool near = false;
                for (int k = 0; k < 16 && !near; k++)
                {
                    double a = 2 * Math.PI * k / 16;
                    float qx = wx + (float)(ResolveRadiusMeters * Math.Cos(a));
                    float qz = wz + (float)(ResolveRadiusMeters * Math.Sin(a));
                    if ((int)gen.GetBiome(qx, qz) == ob) near = true;
                }
                if (!near) unresolved++;
            }

            output.WriteLine($"seed={Seed}  points={oracle.Count}  match={match} ({frac:P3})");
            output.WriteLine($"mismatches={mismatches.Count}  unresolved within {ResolveRadiusMeters}m={unresolved}");

            Assert.True(frac >= MinMatchFraction,
                $"biome match {frac:P2} below floor {MinMatchFraction:P0} — port has diverged from real Valheim worldgen");
            Assert.True(unresolved <= MaxUnresolvedMismatches,
                $"{unresolved} mismatches are real (survive a {ResolveRadiusMeters}m nudge) — that's worldgen drift, not sampling-seam noise");
        }
    }
}
