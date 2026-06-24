using System;
using System.Linq;
using WorldZones.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// CONSUMER PROOF — this whole file uses only the WorldZones.Runtime surface.
// It is exactly what a second mod (or Trailborne) would write to consume regions.
// ─────────────────────────────────────────────────────────────────────────────

string seed = args.Length > 0 ? args[0] : "ForTheWort"; // Niflheim's worldgen seed

Console.WriteLine($"=== WorldZones consumer demo · seed '{seed}' ===\n");

// 1. The ENTIRE bootstrap a consumer writes: one sampler + one Build call.
var world = WorldZonesRuntime.Build(
    PortWorldSampler.FromSeed(seed),
    new RegionBuildOptions { IncludeInlandWater = true });

Console.WriteLine($"regions: {world.Regions.Count}   worldId: {world.WorldId}\n");

// 2. Browse shape — the rich, named region model.
Console.WriteLine("── 12 largest regions (the in-process gazetteer) ──");
Console.WriteLine($"{"name",-30} {"biome",-12} {"km²",6}  {"relief",6}  coast  nbrs");
foreach (var r in world.Regions.OrderByDescending(r => r.AreaZones).Take(12))
{
    Console.WriteLine($"{r.Name,-30} {r.DominantBiome,-12} {r.AreaKm2,6:F1}  {r.Relief,5:F0}m  {(r.IsCoastal ? "  ~  " : "     ")}  {r.NeighborKeys.Count,3}");
}

// 3. Point-query shape — "what region is the player standing in?"
//    Sampled at real region centroids so they resolve to land, not arbitrary ocean points.
Console.WriteLine("\n── point queries at real region centroids ──");
foreach (var pick in world.Regions.OrderByDescending(r => r.AreaZones).Take(4))
{
    var here = world.RegionAt(pick.CentroidX, pick.CentroidZ);
    Console.WriteLine($"  ({pick.CentroidX,7:F0},{pick.CentroidZ,7:F0}) -> {(here is null ? "(unassigned/ocean)" : $"{here.Name}  [{here.DominantBiome}]")}");
}

// 4. Show the naming REACH — a sample across biomes + the rare earned landmarks.
Console.WriteLine("\n── naming reach: a sample across biomes ──");
foreach (var grp in world.Regions.GroupBy(r => r.DominantBiome).OrderBy(g => g.Key.ToString()))
{
    var sample = grp.OrderByDescending(r => r.AreaZones).First();
    Console.WriteLine($"  {grp.Key,-12}  {sample.Name}");
}

int uniqueNames = world.Regions.Select(r => r.Name).Distinct(StringComparer.Ordinal).Count();
Console.WriteLine($"\nunique names: {uniqueNames}/{world.Regions.Count}  (collisions resolved deterministically)");
Console.WriteLine("\n=== consumer needed ONE reference (WorldZones.Runtime) and ONE Build call. ===");
