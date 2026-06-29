using System;
using System.Collections.Generic;
using System.IO;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY SPIKE (2026-06-29) for DECISION "the refined RegionRing is authoritative bounds".
    /// Proves the ONE unproven thing: does a single coastal region's closed ring, refined to the
    /// contour (coast→sea-iso, land-seam→biome-flip) then smoothed LAST, stay WATERTIGHT
    /// (no self-intersection, winding preserved) on real Niflheim? Everything else the model needs
    /// (RegionRing/OuterRing, the snap march, the smoother primitives, RegionFillMaskBaker) already
    /// ships; this isolates the watertight risk and renders it 3-up for Daniel's felt judgment.
    ///
    /// Reimplements the snap march + a CLOSED despike/Chaikin inline ON PURPOSE: the shipped
    /// RegionBoundaryRefiner snap helpers are private, and the shipped PolylineSmoother is OPEN-curve
    /// (pins endpoints) — a ring needs closed smoothing or it kinks at the closure seam. That gap is
    /// itself a finding the spike surfaces for the real impl. Not wired into any shipped path.
    /// </summary>
    public static class RingSpike
    {
        // SegmentRefineOptions defaults, mirrored (Subdivisions handled by ring density; we snap verts).
        const double MaxDisplacement = 40.0;   // m — same bound the ink uses
        const double MarchStep = 4.0;          // m
        const double DespikeThreshold = 24.0;  // m
        const int ChaikinIterations = 2;

        enum EdgeKind { Coast, Seam, Unknown }

        public static int Run(string seed, string outDir)
        {
            Console.WriteLine($"=== Ring spike — seed '{seed}' (authoritative refined-ring watertight proof) ===");
            Directory.CreateDirectory(outDir);

            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false, UseFeatureAwareBorders = true,
                ComputeRegionInfo = true, Namer = new MultiSchemaRegionNamer(),
            });

            int[,] rid = world.RegionIdGrid;
            int min = world.Grid.MinIndex;
            int gh = rid.GetLength(0), gw = rid.GetLength(1);
            const double zone = 64.0;
            Func<double, double, int> regionIdAt = (wx, wz) =>
            {
                int gx = (int)Math.Round(wx / zone) - min;
                int gy = (int)Math.Round(wz / zone) - min;
                if (gx < 0 || gy < 0 || gx >= gw || gy >= gh) return -1;
                return rid[gy, gx];
            };

            RegionBoundaryGraph graph = world.BuildBoundaryGraph();
            var heightField = new HeightScalarField(sampler);      // IsoLevel = CoastIso (25 m)
            var biomeField = new BiomeCategoryField(sampler);

            // ── ALL-REGION WATERTIGHT SWEEP — the n=164 gate before any production refiner ─────────────
            SweepAllRegions(world, graph, regionIdAt, heightField, biomeField);

            // ── Pick ONE good coastal region: coastal, has neighbours, mid-sized, and its outer ring
            //    carries BOTH coast AND seam edges (so the spike exercises both snap fields) ──────────
            RegionInfo chosen = null; RegionRing chosenRing = null;
            int bestMix = -1;
            foreach (RegionInfo r in world.Regions)
            {
                if (!r.IsCoastal) continue;
                if (r.AreaZones < 18 || r.AreaZones > 240) continue;
                RegionRing ring = graph.OuterRing(r.RegionKey);
                if (ring == null || ring.Vertices.Count < 8) continue;
                var verts = ring.Vertices;
                int coast = 0, seam = 0;
                ClassifyEdges(verts, r.TransientId, regionIdAt, out coast, out seam, out _);
                int mix = Math.Min(coast, seam);          // want a real mix of both edge kinds
                if (mix > bestMix) { bestMix = mix; chosen = r; chosenRing = ring; }
            }
            if (chosen == null) { Console.Error.WriteLine("no suitable coastal region found"); return 1; }

            var raw = new List<WzVec2>(chosenRing.Vertices);
            int n = raw.Count;
            ClassifyEdges(raw, chosen.TransientId, regionIdAt, out int coastE, out int seamE, out int unkE);
            Console.WriteLine($"REGION: key={chosen.RegionKey} name=\"{chosen.Name}\" biome={chosen.DominantBiome} "
                            + $"areaZones={chosen.AreaZones} neighbours={chosen.NeighborKeys.Count}");
            Console.WriteLine($"RAW RING: {n} verts | edges: coast={coastE} seam={seamE} unknown={unkE}");

            // ── Refine: snap each vertex ONCE along its local normal to the iso its adjacent edges imply.
            //    Coast priority at junctions (coast is the dominant visible case; height field defined
            //    everywhere). Refinement is per-vertex on the CLOSED ring → no shared-corner double-snap. ─
            EdgeKind[] edgeKind = EdgeKinds(raw, chosen.TransientId, regionIdAt);
            var refined = new List<WzVec2>(n);
            int hugged = 0; double maxDisp = 0, sumDisp = 0;
            for (int i = 0; i < n; i++)
            {
                EdgeKind prev = edgeKind[(i - 1 + n) % n], cur = edgeKind[i];
                bool coast = prev == EdgeKind.Coast || cur == EdgeKind.Coast;
                bool seam = !coast && (prev == EdgeKind.Seam || cur == EdgeKind.Seam);

                WzVec2 a = raw[(i - 1 + n) % n], b = raw[(i + 1) % n];
                double tx = b.X - a.X, tz = b.Z - a.Z;
                double tl = Math.Sqrt(tx * tx + tz * tz);
                if (tl < 1e-9) { refined.Add(raw[i]); continue; }
                double nx = -tz / tl, nz = tx / tl;

                double s; bool snap;
                if (coast) snap = SnapIso(heightField, raw[i].X, raw[i].Z, nx, nz, out s);
                else if (seam) snap = SnapFlip(biomeField, raw[i].X, raw[i].Z, nx, nz, out s);
                else { s = 0; snap = false; }

                if (snap && Math.Abs(s) > 1e-6)
                {
                    refined.Add(new WzVec2(raw[i].X + s * nx, raw[i].Z + s * nz));
                    hugged++; double d = Math.Abs(s); sumDisp += d; if (d > maxDisp) maxDisp = d;
                }
                else refined.Add(raw[i]);
            }
            Console.WriteLine($"REFINED RING: {refined.Count} verts | hugged={hugged}/{n} "
                            + $"({100.0 * hugged / n:F0}%) | disp mean={(hugged > 0 ? sumDisp / hugged : 0):F1}m max={maxDisp:F1}m");

            // ── Smooth LAST — separable stage, on the CLOSED ring (despike then Chaikin) ──────────────
            var smoothed = ChaikinClosed(DespikeClosed(refined, DespikeThreshold), ChaikinIterations);
            Console.WriteLine($"SMOOTHED RING: {smoothed.Count} verts (closed despike+Chaikin×{ChaikinIterations})");

            // ── WATERTIGHT GATE: self-intersection + winding-preservation ─────────────────────────────
            double aRaw = SignedArea(raw), aRef = SignedArea(refined), aSm = SignedArea(smoothed);
            int siRef = SelfIntersections(refined), siSm = SelfIntersections(smoothed);
            bool windOk = Math.Sign(aRaw) == Math.Sign(aRef) && Math.Sign(aRaw) == Math.Sign(aSm);
            Console.WriteLine();
            Console.WriteLine("── WATERTIGHT GATE ─────────────────────────────────────────");
            Console.WriteLine($"  self-intersections: refined={siRef}  smoothed={siSm}   (PASS = 0)");
            Console.WriteLine($"  winding preserved (CCW outer kept): {(windOk ? "YES" : "NO ←FAIL")}");
            double km2Raw = Math.Abs(aRaw) / 1e6, km2Ref = Math.Abs(aRef) / 1e6, km2Sm = Math.Abs(aSm) / 1e6;
            Console.WriteLine($"  area km²: raw={km2Raw:F3} refined={km2Ref:F3} ({Pct(km2Ref, km2Raw)}) "
                            + $"smoothed={km2Sm:F3} ({Pct(km2Sm, km2Raw)})");
            WzVec2 cRaw = Centroid(raw), cRef = Centroid(refined), cSm = Centroid(smoothed);
            Console.WriteLine($"  centroid shift: raw→refined={Dist(cRaw, cRef):F1}m  raw→smoothed={Dist(cRaw, cSm):F1}m");
            bool PASS = siRef == 0 && siSm == 0 && windOk;
            Console.WriteLine($"  VERDICT: {(PASS ? "PASS — ring stays watertight, vector authority is viable" : "FAIL — see counts above")}");
            Console.WriteLine("────────────────────────────────────────────────────────────");

            // ── Render 3-up on a shared bbox: raw / refined / refined+smoothed, over the REAL coastline ─
            RenderPanels(outDir, sampler, regionIdAt, chosen, raw, refined, smoothed);
            Console.WriteLine($"  panels → {outDir}/ringspike_raw.png, ringspike_refined.png, ringspike_smoothed.png");
            return PASS ? 0 : 2;
        }

        // ── Refine (per-vertex snap) + smooth-last on a closed ring. Shared by sweep + single-region. ──
        static List<WzVec2> RefineAndSmooth(IReadOnlyList<WzVec2> raw, int label,
            Func<double, double, int> ridAt, IScalarField height, ICategoryField biome,
            out List<WzVec2> refinedOut, out int hugged)
        {
            int n = raw.Count;
            EdgeKind[] edgeKind = EdgeKinds(raw, label, ridAt);
            var refined = new List<WzVec2>(n);
            hugged = 0;
            for (int i = 0; i < n; i++)
            {
                EdgeKind prev = edgeKind[(i - 1 + n) % n], cur = edgeKind[i];
                bool coast = prev == EdgeKind.Coast || cur == EdgeKind.Coast;
                bool seam = !coast && (prev == EdgeKind.Seam || cur == EdgeKind.Seam);
                WzVec2 a = raw[(i - 1 + n) % n], b = raw[(i + 1) % n];
                double tx = b.X - a.X, tz = b.Z - a.Z;
                double tl = Math.Sqrt(tx * tx + tz * tz);
                if (tl < 1e-9) { refined.Add(raw[i]); continue; }
                double nx = -tz / tl, nz = tx / tl;
                double s; bool snap;
                if (coast) snap = SnapIso(height, raw[i].X, raw[i].Z, nx, nz, out s);
                else if (seam) snap = SnapFlip(biome, raw[i].X, raw[i].Z, nx, nz, out s);
                else { s = 0; snap = false; }
                if (snap && Math.Abs(s) > 1e-6) { refined.Add(new WzVec2(raw[i].X + s * nx, raw[i].Z + s * nz)); hugged++; }
                else refined.Add(raw[i]);
            }
            refinedOut = refined;
            return ChaikinClosed(DespikeClosed(refined, DespikeThreshold), ChaikinIterations);
        }

        // ── All-region watertight sweep: refine+smooth EVERY outer+hole ring, count failures ───────────
        static void SweepAllRegions(RegionWorld world, RegionBoundaryGraph graph,
            Func<double, double, int> ridAt, IScalarField height, ICategoryField biome)
        {
            int regions = 0, ringsTested = 0, refSelfInt = 0, smSelfInt = 0, windFlip = 0, ringFailRings = 0;
            double worstAreaDrift = 0; string worstAreaKey = null;
            int worstSI = 0; string worstSIKey = null;
            var failKeys = new List<string>();
            var failSizes = new List<int>(); var failAreas = new List<double>(); var passSizes = new List<int>();
            var failBiome = new Dictionary<BiomeType, int>(); var allBiome = new Dictionary<BiomeType, int>();

            foreach (RegionInfo r in world.Regions)
            {
                regions++;
                allBiome[r.DominantBiome] = allBiome.TryGetValue(r.DominantBiome, out var ab) ? ab + 1 : 1;
                foreach (RegionRing ring in graph.RingsFor(r.RegionKey))
                {
                    if (ring.Vertices.Count < 4) continue;
                    ringsTested++;
                    var raw = new List<WzVec2>(ring.Vertices);
                    var sm = RefineAndSmooth(raw, r.TransientId, ridAt, height, biome, out var refined, out _);
                    int siRef = SelfIntersections(refined), siSm = SelfIntersections(sm);
                    double aRaw = SignedArea(raw), aSm = SignedArea(sm);
                    bool wind = Math.Sign(aRaw) == Math.Sign(aSm) || Math.Abs(aRaw) < 1e-6;
                    bool ringBad = siRef > 0 || siSm > 0 || !wind;
                    if (siRef > 0) refSelfInt++;
                    if (siSm > 0) smSelfInt++;
                    if (!wind) windFlip++;
                    if (ringBad) { ringFailRings++; if (!failKeys.Contains(r.RegionKey)) failKeys.Add(r.RegionKey); failSizes.Add(raw.Count); failAreas.Add(Math.Abs(aRaw) / 1e6); failBiome[r.DominantBiome] = failBiome.TryGetValue(r.DominantBiome, out var fb) ? fb + 1 : 1; }
                    else { passSizes.Add(raw.Count); }
                    if (siSm > worstSI) { worstSI = siSm; worstSIKey = r.RegionKey; }
                    double drift = Math.Abs(aRaw) > 1e-6 ? Math.Abs((Math.Abs(aSm) - Math.Abs(aRaw)) / Math.Abs(aRaw)) : 0;
                    if (drift > worstAreaDrift) { worstAreaDrift = drift; worstAreaKey = r.RegionKey; }
                }
            }

            Console.WriteLine("══ ALL-REGION WATERTIGHT SWEEP (n=" + regions + " regions, " + ringsTested + " rings) ══");
            Console.WriteLine($"  rings with self-intersection: refined={refSelfInt}  smoothed={smSelfInt}");
            Console.WriteLine($"  rings with winding flip: {windFlip}");
            Console.WriteLine($"  FAILING RINGS: {ringFailRings}  (regions affected: {failKeys.Count})");
            if (failKeys.Count > 0)
                Console.WriteLine($"    keys: {string.Join(", ", failKeys.GetRange(0, Math.Min(12, failKeys.Count)))}{(failKeys.Count > 12 ? " …" : "")}");
            Console.WriteLine($"  worst smoothed self-int: {worstSI} (region {worstSIKey ?? "none"})");
            Console.WriteLine($"  worst area drift: {worstAreaDrift * 100:F1}% (region {worstAreaKey ?? "none"})");
            failSizes.Sort(); passSizes.Sort(); failAreas.Sort();
            double Med(List<int> l) => l.Count == 0 ? 0 : l[l.Count / 2];
            double MedD(List<double> l) => l.Count == 0 ? 0 : l[l.Count / 2];
            Console.WriteLine($"  FAIL ring vert-count: min={(failSizes.Count>0?failSizes[0]:0)} median={Med(failSizes):F0} max={(failSizes.Count>0?failSizes[failSizes.Count-1]:0)}  |  PASS median={Med(passSizes):F0}");
            Console.WriteLine($"  FAIL ring raw-area km²: min={(failAreas.Count>0?failAreas[0]:0):F3} median={MedD(failAreas):F3} max={(failAreas.Count>0?failAreas[failAreas.Count-1]:0):F3}");
            // Biome breakdown of failing regions vs baseline — tests the "fuzzy-shoreline biome" hypothesis.
            Console.WriteLine("  FAIL-by-biome (failRegions / totalRegions of that biome, % of biome failing):");
            foreach (var kv in new List<KeyValuePair<BiomeType,int>>(allBiome))
            {
                int f = failBiome.TryGetValue(kv.Key, out var fc) ? fc : 0;
                Console.WriteLine($"     {kv.Key,-12} {f}/{kv.Value}  ({(kv.Value>0?100.0*f/kv.Value:0):F0}% fail)");
            }
            Console.WriteLine($"  SWEEP VERDICT: {(ringFailRings == 0 ? "PASS — all rings watertight, production refiner is SAFE" : ringFailRings <= 3 ? "MOSTLY-PASS — a few fjords need a guard (listed above)" : "NEEDS-WORK — self-intersection guard required before production")}");
            Console.WriteLine("════════════════════════════════════════════════════════════");
            Console.WriteLine();
        }

        // ── Edge classification ──────────────────────────────────────────────────────────────────────
        // For ring edge i (verts[i]→verts[i+1]), sample the region grid 32 m to EACH side of the edge
        // midpoint; the side that is NOT this region is the exterior. Exterior <0 ⇒ coast; exterior is
        // another region ⇒ seam. Sampling both sides removes all winding-convention risk.
        static void ClassifyEdges(IReadOnlyList<WzVec2> v, int label, Func<double, double, int> ridAt,
                                  out int coast, out int seam, out int unknown)
        {
            coast = seam = unknown = 0;
            var k = EdgeKinds(v, label, ridAt);
            foreach (var e in k) { if (e == EdgeKind.Coast) coast++; else if (e == EdgeKind.Seam) seam++; else unknown++; }
        }

        static EdgeKind[] EdgeKinds(IReadOnlyList<WzVec2> v, int label, Func<double, double, int> ridAt)
        {
            int n = v.Count; var kinds = new EdgeKind[n];
            for (int i = 0; i < n; i++)
            {
                WzVec2 a = v[i], b = v[(i + 1) % n];
                double mx = (a.X + b.X) * 0.5, mz = (a.Z + b.Z) * 0.5;
                double dx = b.X - a.X, dz = b.Z - a.Z;
                double l = Math.Sqrt(dx * dx + dz * dz);
                if (l < 1e-9) { kinds[i] = EdgeKind.Unknown; continue; }
                double nx = -dz / l, nz = dx / l;        // perpendicular
                const double step = 32.0;                // into the adjacent zone centre
                int sideP = ridAt(mx + nx * step, mz + nz * step);
                int sideN = ridAt(mx - nx * step, mz - nz * step);
                int exterior;
                if (sideP == label && sideN != label) exterior = sideN;
                else if (sideN == label && sideP != label) exterior = sideP;
                else exterior = (sideP != label) ? sideP : sideN;   // fallback
                kinds[i] = exterior < 0 ? EdgeKind.Coast : (exterior != label ? EdgeKind.Seam : EdgeKind.Unknown);
            }
            return kinds;
        }

        // ── Snap march (faithful reimpl of RegionBoundaryRefiner.TrySnapToIso / TrySnapToBiomeFlip) ────
        static bool SnapIso(IScalarField f, double px, double pz, double nx, double nz, out double bestS)
        {
            bestS = 0; double iso = f.IsoLevel;
            double F(double s) => f.Sample(px + s * nx, pz + s * nz) - iso;
            double f0 = F(0); if (Math.Abs(f0) < 1e-9) return true;
            double best = double.MaxValue; bool found = false;
            foreach (int dir in new[] { 1, -1 })
            {
                double prevS = 0, prevF = f0;
                for (double step = MarchStep; step <= MaxDisplacement + 1e-9; step += MarchStep)
                {
                    double s = dir * step, fs = F(s);
                    if (Math.Sign(fs) != Math.Sign(prevF) && Math.Sign(fs) != 0)
                    { double sc = Bisect(F, prevS, s, prevF, fs); if (Math.Abs(sc) < best) { best = Math.Abs(sc); bestS = sc; found = true; } break; }
                    if (Math.Abs(fs) < 1e-9) { if (Math.Abs(s) < best) { best = Math.Abs(s); bestS = s; found = true; } break; }
                    prevS = s; prevF = fs;
                }
            }
            return found;
        }

        static bool SnapFlip(ICategoryField f, double px, double pz, double nx, double nz, out double bestS)
        {
            bestS = 0; int c0 = f.CategoryAt(px, pz);
            double best = double.MaxValue; bool found = false;
            foreach (int dir in new[] { 1, -1 })
            {
                int prevCat = c0; double prevS = 0;
                for (double step = MarchStep; step <= MaxDisplacement + 1e-9; step += MarchStep)
                {
                    double s = dir * step; int cat = f.CategoryAt(px + s * nx, pz + s * nz);
                    if (cat != prevCat)
                    {
                        double s0 = prevS, s1 = s;
                        for (int it = 0; it < 20; it++) { double mid = 0.5 * (s0 + s1); int cm = f.CategoryAt(px + mid * nx, pz + mid * nz); if (cm == c0) s0 = mid; else s1 = mid; }
                        double sc = 0.5 * (s0 + s1);
                        if (Math.Abs(sc) < best) { best = Math.Abs(sc); bestS = sc; found = true; }
                        break;
                    }
                    prevCat = cat; prevS = s;
                }
            }
            return found;
        }

        static double Bisect(Func<double, double> f, double s0, double s1, double f0, double f1)
        {
            for (int i = 0; i < 24; i++)
            { double mid = 0.5 * (s0 + s1), fm = f(mid); if (Math.Abs(fm) < 1e-9) return mid; if (Math.Sign(fm) == Math.Sign(f0)) { s0 = mid; f0 = fm; } else { s1 = mid; f1 = fm; } }
            return 0.5 * (s0 + s1);
        }

        // ── CLOSED smoothing (the shipped PolylineSmoother is open-curve; a ring needs these) ──────────
        static WzVec2 Lerp(WzVec2 a, WzVec2 b, double t) => new WzVec2(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);

        static List<WzVec2> DespikeClosed(IReadOnlyList<WzVec2> p, double maxDev)
        {
            int n = p.Count; var o = new List<WzVec2>(n); if (n < 3) { o.AddRange(p); return o; }
            double thr2 = maxDev * maxDev;
            for (int i = 0; i < n; i++)
            {
                WzVec2 m = Lerp(p[(i - 1 + n) % n], p[(i + 1) % n], 0.5);
                double dx = p[i].X - m.X, dz = p[i].Z - m.Z;
                o.Add(dx * dx + dz * dz > thr2 ? m : p[i]);
            }
            return o;
        }

        static List<WzVec2> ChaikinClosed(IReadOnlyList<WzVec2> p, int iterations)
        {
            var cur = new List<WzVec2>(p);
            for (int it = 0; it < iterations; it++)
            {
                int n = cur.Count; if (n < 3) break;
                var o = new List<WzVec2>(n * 2);
                for (int i = 0; i < n; i++) { WzVec2 a = cur[i], b = cur[(i + 1) % n]; o.Add(Lerp(a, b, 0.25)); o.Add(Lerp(a, b, 0.75)); }
                cur = o;
            }
            return cur;
        }

        // ── Geometry audits ───────────────────────────────────────────────────────────────────────────
        static double SignedArea(IReadOnlyList<WzVec2> v)
        { double s = 0; int n = v.Count; for (int i = 0; i < n; i++) { WzVec2 a = v[i], b = v[(i + 1) % n]; s += a.X * b.Z - b.X * a.Z; } return s / 2.0; }

        static WzVec2 Centroid(IReadOnlyList<WzVec2> v)
        {
            double a = 0, cx = 0, cz = 0; int n = v.Count;
            for (int i = 0; i < n; i++) { WzVec2 p = v[i], q = v[(i + 1) % n]; double cr = p.X * q.Z - q.X * p.Z; a += cr; cx += (p.X + q.X) * cr; cz += (p.Z + q.Z) * cr; }
            if (Math.Abs(a) < 1e-9) return v[0]; a *= 0.5; return new WzVec2(cx / (6 * a), cz / (6 * a));
        }

        static double Dist(WzVec2 a, WzVec2 b) { double dx = a.X - b.X, dz = a.Z - b.Z; return Math.Sqrt(dx * dx + dz * dz); }

        // Count proper intersections between non-adjacent edges of a closed ring.
        static int SelfIntersections(IReadOnlyList<WzVec2> v)
        {
            int n = v.Count, hits = 0;
            for (int i = 0; i < n; i++)
            {
                WzVec2 a1 = v[i], a2 = v[(i + 1) % n];
                for (int j = i + 1; j < n; j++)
                {
                    if (j == i) continue;
                    if ((i == 0 && j == n - 1) || j == i + 1) continue;   // skip shared-vertex neighbours + wrap
                    WzVec2 b1 = v[j], b2 = v[(j + 1) % n];
                    if (SegInt(a1, a2, b1, b2)) hits++;
                }
            }
            return hits;
        }

        static bool SegInt(WzVec2 p, WzVec2 p2, WzVec2 q, WzVec2 q2)
        {
            double d1 = Cross(q, q2, p), d2 = Cross(q, q2, p2), d3 = Cross(p, p2, q), d4 = Cross(p, p2, q2);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
            return false;
        }
        static double Cross(WzVec2 a, WzVec2 b, WzVec2 c) => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);

        static string Pct(double v, double baseV) => baseV > 1e-9 ? $"{(v / baseV - 1) * 100:+0.0;-0.0;0.0}%" : "n/a";

        // ── Render: 3 panels, shared bbox, real coastline backdrop + region fill + bright ring outline ──
        static void RenderPanels(string outDir, IWorldSampler sampler, Func<double, double, int> ridAt,
                                 RegionInfo region, List<WzVec2> raw, List<WzVec2> refined, List<WzVec2> smoothed)
        {
            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (var p in raw) { minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z); }
            const double pad = 96.0; minX -= pad; maxX += pad; minZ -= pad; maxZ += pad;
            double spanX = maxX - minX, spanZ = maxZ - minZ;
            const int target = 560;
            double scale = target / Math.Max(spanX, spanZ);
            int W = Math.Max(8, (int)(spanX * scale)), H = Math.Max(8, (int)(spanZ * scale));

            // Backdrop computed ONCE (shared bbox): real coastline + grey land.
            var biome = region.DominantBiome;
            var (wr, wg, wb) = BiomeRenderPalette.Wash(biome);
            byte[] backdrop = new byte[W * H * 3];
            for (int py = 0; py < H; py++)
            {
                double wz = maxZ - (py + 0.5) / scale;     // north up
                for (int px = 0; px < W; px++)
                {
                    double wx = minX + (px + 0.5) / scale;
                    double h = sampler.GetHeight((float)wx, (float)wz);
                    int o = (py * W + px) * 3;
                    if (h < HeightScalarField.CoastIso)
                    {
                        double depth = Math.Min(1.0, (HeightScalarField.CoastIso - h) / 60.0);
                        backdrop[o] = (byte)(18 + 10 * (1 - depth)); backdrop[o + 1] = (byte)(34 + 24 * (1 - depth)); backdrop[o + 2] = (byte)(70 + 60 * (1 - depth));
                    }
                    else { backdrop[o] = 60; backdrop[o + 1] = 60; backdrop[o + 2] = 60; }
                }
            }

            RenderOne(Path.Combine(outDir, "ringspike_raw.png"), W, H, backdrop, raw, minX, maxZ, scale, wr, wg, wb);
            RenderOne(Path.Combine(outDir, "ringspike_refined.png"), W, H, backdrop, refined, minX, maxZ, scale, wr, wg, wb);
            RenderOne(Path.Combine(outDir, "ringspike_smoothed.png"), W, H, backdrop, smoothed, minX, maxZ, scale, wr, wg, wb);
        }

        static void RenderOne(string path, int W, int H, byte[] backdrop, List<WzVec2> ring,
                              double minX, double maxZ, double scale, byte wr, byte wg, byte wb)
        {
            byte[] img = (byte[])backdrop.Clone();
            int n = ring.Count;
            // map world→pixel
            double PX(WzVec2 p) => (p.X - minX) * scale;
            double PY(WzVec2 p) => (maxZ - p.Z) * scale;

            // Scanline even-odd fill → blend region wash 55% over backdrop (fill stops AT the ring).
            for (int py = 0; py < H; py++)
            {
                double yz = maxZ - (py + 0.5) / scale;
                var xs = new List<double>();
                for (int i = 0; i < n; i++)
                {
                    WzVec2 a = ring[i], b = ring[(i + 1) % n];
                    double z0 = a.Z, z1 = b.Z;
                    if ((z0 <= yz && z1 > yz) || (z1 <= yz && z0 > yz))
                    {
                        double t = (yz - z0) / (z1 - z0);
                        double wx = a.X + t * (b.X - a.X);
                        xs.Add((wx - minX) * scale);
                    }
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int x0 = Math.Max(0, (int)Math.Ceiling(xs[k])), x1 = Math.Min(W - 1, (int)Math.Floor(xs[k + 1]));
                    for (int px = x0; px <= x1; px++)
                    {
                        int o = (py * W + px) * 3;
                        img[o] = (byte)((img[o] * 45 + wr * 55) / 100);
                        img[o + 1] = (byte)((img[o + 1] * 45 + wg * 55) / 100);
                        img[o + 2] = (byte)((img[o + 2] * 45 + wb * 55) / 100);
                    }
                }
            }

            // Bright ring outline.
            for (int i = 0; i < n; i++)
            {
                WzVec2 a = ring[i], b = ring[(i + 1) % n];
                DrawLine(img, W, H, (int)PX(a), (int)PY(a), (int)PX(b), (int)PY(b), 255, 240, 80);
            }
            // Vertex dots so the staircase vs hugged difference is legible.
            foreach (var p in ring) Dot(img, W, H, (int)PX(p), (int)PY(p), 255, 90, 90);

            PngWriter.Write(path, W, H, img);
        }

        static void DrawLine(byte[] img, int W, int H, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                if (x0 >= 0 && y0 >= 0 && x0 < W && y0 < H) { int o = (y0 * W + x0) * 3; img[o] = r; img[o + 1] = g; img[o + 2] = b; }
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        static void Dot(byte[] img, int W, int H, int cx, int cy, byte r, byte g, byte b)
        {
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            { int x = cx + dx, y = cy + dy; if (x >= 0 && y >= 0 && x < W && y < H) { int o = (y * W + x) * 3; img[o] = r; img[o + 1] = g; img[o + 2] = b; } }
        }
    }
}
