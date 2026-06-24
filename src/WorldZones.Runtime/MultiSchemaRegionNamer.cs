using System;
using System.Collections.Generic;
using System.Globalization;
using WorldZones.Regions;
using WorldZones.WorldGen;
using Vector2i = WorldZones.Regions.Vector2i;

namespace WorldZones.Runtime
{
    /// <summary>
    /// Optional real-world location signal for a region (boss seats, traders, dungeon density),
    /// sourced from a Valheim <c>.db</c> save join. When supplied, it unlocks the location-driven
    /// naming schemas (boss-seat / trader-hold / dungeon-haunt). All-absent is the graceful default:
    /// those schemas simply carry zero weight and never fire, exactly like the Python bench with no
    /// sidecar attached.
    /// </summary>
    public sealed class RegionLocationSignal
    {
        public bool HasBoss { get; set; }
        public string Boss { get; set; }
        public bool TraderPresent { get; set; }
        public int DungeonCount { get; set; }
        public int TotalPois { get; set; }
    }

    /// <summary>
    /// The default <see cref="IRegionNamer"/>: a deterministic, data-driven multi-schema namer that
    /// gives each region a name telling one of several KINDS of story — terrain, people/settlement,
    /// faux-lore (figures &amp; events), memorial, cardinal, minted, or a rare earned superlative.
    /// Most names are invented world-native lore, not literal terrain descriptors; the region's data
    /// biases WHICH kind of story its name tells (remote+dangerous → folly/grave; big+central+
    /// hospitable → hold/jarldom; coastal → landing; the single highest region → "the Roof of the
    /// World"). Deterministic on <c>RegionKey</c>, so a name survives seed-list churn.
    ///
    /// <para>
    /// This is a direct port of the locked-pending Python design bench (<c>tools/naming/
    /// name_schemes.py</c> + the uniqueness pass in <c>enrich_gazetteer.py</c>). The hash, per-schema
    /// salts, weights, and rosters match the bench so C# output equals the bench output for the same
    /// world. Status: PROVISIONAL (rosters / register-mix / weights not locked) — which is exactly
    /// why it sits behind <see cref="IRegionNamer"/>: tuning is a swap behind the seam.
    /// </para>
    /// </summary>
    public sealed class MultiSchemaRegionNamer : IRegionNamer
    {
        // Optional per-region location signal, keyed by RegionKey. Null/absent ⇒ location schemas off.
        private readonly IReadOnlyDictionary<string, RegionLocationSignal> locationByKey;

        public MultiSchemaRegionNamer(IReadOnlyDictionary<string, RegionLocationSignal> locationByKey = null)
        {
            this.locationByKey = locationByKey;
        }

        // ── rosters (fictitious faux-Norse) ──────────────────────────────────
        private static readonly string[] People =
        {
            "Halla","Ulfr","Bjorn","Sigrun","Thrandr","Astrid","Gunnar","Hildr","Ivar","Thora",
            "Gudrun","Leif","Knut","Frida","Olvir","Ingrid","Hakon","Vigdis","Sigurd","Brand",
            "Yrsa","Orm","Dagny","Steinarr","Solveig","Torvald","Ragnhild","Eyvind","Bera","Hrolf",
            "Asgeir","Borghild","Dyri","Eldgrim","Frosti","Geirmund","Halldis","Ketil","Liv","Mundi",
            "Njal","Oddny","Ragnar","Saerun","Tofa","Unnr","Vali","Yngvar","Aslaug","Birger",
            "Drifa","Egil","Folki","Groa","Hroar","Isgerd","Jorund","Kari","Lifa","Mord"
        };
        private static readonly string[] Creatures =
        {
            "Wolf","Raven","Boar","Elk","Bear","Serpent","Drake","Stag","Hawk","Lynx","Adder","Crow",
            "Fox","Owl","Wyrm","Hound","Falcon","Viper","Tern","Marten","Ermine","Heron","Asp","Shrike"
        };
        private static readonly string[] Mythic =
        {
            "the Grey Wanderer","the Drowned King","the Ash-Mother","the Hooded One","the Twin Jarls",
            "the Last Skald","the Pale Rider","the Hunter in Mist","the Bone Singer","the Frost Widow",
            "the Nameless Jarl","the Weeping Seer","the Iron Hermit","the Salt-Witch","the Old Ferryman",
            "the Thrice-Burned","the Hollow Saint","the Wending Crone"
        };
        private static readonly string[] Events =
        {
            "the Sundering","the Long Winter","the Reaving","the Burning","the Silence","the Drowning",
            "the Severing","Last Light","the Hunger Years","the Red Tide","the Breaking","the Stillness",
            "the Hollowing","the Calling","the Witherfrost","the Embering"
        };

        // ── terrain vocab (for the terrain-flavored minority) ────────────────
        private static readonly Dictionary<string, string[]> BiomeDesc = new Dictionary<string, string[]>
        {
            ["Mountain"] = new[]{"Highlands","Heights","Crags","Peaks"},
            ["Swamp"] = new[]{"Marshlands","Mire","Fens","Boglands"},
            ["Plains"] = new[]{"Plains","Flats","Steppe","Expanse"},
            ["Meadows"] = new[]{"Vale","Downs","Greens","Meadows"},
            ["BlackForest"] = new[]{"Woods","Pinewoods","Thicket","Wilds"},
            ["Mistlands"] = new[]{"Mistlands","Shroud","Veil","Gloaming"},
            ["DeepNorth"] = new[]{"Frostlands","Tundra","Winterreach","Hoarfrost"},
            ["AshLands"] = new[]{"Cinderlands","Emberwastes","Scorchlands","Ashfall"},
        };
        private static readonly Dictionary<string, string[]> BiomeAdj = new Dictionary<string, string[]>
        {
            ["Mountain"] = new[]{"Towering","Stone","Wind-scoured","High"},
            ["Swamp"] = new[]{"Sunken","Drowned","Rotting","Foul"},
            ["Plains"] = new[]{"Golden","Windswept","Open","Wide"},
            ["Meadows"] = new[]{"Verdant","Gentle","Sunlit","Green"},
            ["BlackForest"] = new[]{"Shadowed","Tangled","Pine-dark","Deep"},
            ["Mistlands"] = new[]{"Misted","Veiled","Shrouded","Dim"},
            ["DeepNorth"] = new[]{"Frostbound","Hoar","Frozen","Pale"},
            ["AshLands"] = new[]{"Charred","Ember","Scorched","Blackened"},
        };
        private static readonly Dictionary<string, string[]> BiomePrefix = new Dictionary<string, string[]>
        {
            ["Mountain"] = new[]{"Berg","Stein","Haug"},
            ["Swamp"] = new[]{"Myrk","Saur","Kjarr"},
            ["Plains"] = new[]{"Slet","Vidda","Eng"},
            ["Meadows"] = new[]{"Grön","Eng","Vang"},
            ["BlackForest"] = new[]{"Skog","Furu","Myrk"},
            ["Mistlands"] = new[]{"Mistr","Niflr","Dvergr"},
            ["DeepNorth"] = new[]{"Frost","Snae","Vetr"},
            ["AshLands"] = new[]{"Eld","Aska","Brenn"},
        };
        private static readonly string[] CoastSuf = {"vik","fjord","havn","sund","nes","kyst"};
        private static readonly string[] LandSuf = {"fell","dalr","holt","skog","voll","berg","mark"};
        private static readonly string[] SettleSuf = {"hold","watch","garth","stead","by","thorp","gard"};
        private static readonly string[] Grave = {"Barrow","Cairn","Howe","Grave","Mound"};

        // invented seat-names per boss, deterministic (location-driven schema)
        private static readonly Dictionary<string, string[]> BossEpithet = new Dictionary<string, string[]>
        {
            ["Eikthyr"] = new[]{"the Antlered Seat","Eikthyr's Heath","the Stag-King's Ground"},
            ["The Elder"] = new[]{"the Elder's Roots","the Greatwood Seat","where the Elder stands"},
            ["Bonemass"] = new[]{"the Rotten Throne","Bonemass's Mire","the Sunken Seat"},
            ["Moder"] = new[]{"the Dragon's Roost","Moder's Peak","the Winged Seat"},
            ["Yagluth"] = new[]{"the Ashen Throne","Yagluth's Reach","the Fallen King's Ground"},
            ["Queen"] = new[]{"the Hive Throne","the Queen's Gloaming","the Sealed Seat"},
            ["Fader"] = new[]{"the Cinder Throne","Fader's End"},
        };

        // ── hash (mirrors the Python bench h(s,salt), itself mirroring RegionGuidNameService mixing) ──
        private static uint H(string s, uint salt)
        {
            uint x = unchecked(salt * 0x9E3779B1u);
            for (int i = 0; i < s.Length; i++)
            {
                x = unchecked(((x << 5) + x) ^ s[i]);
            }
            x ^= x >> 16; x = unchecked(x * 0x7FEB352Du);
            x ^= x >> 15; x = unchecked(x * 0x846CA68Bu);
            x ^= x >> 16;
            return x;
        }

        private static string Pick(string[] pool, string key, uint salt) => pool[H(key, salt) % (uint)pool.Length];

        private static string BiomeName(BiomeType b)
        {
            switch (b)
            {
                case BiomeType.Meadows: return "Meadows";
                case BiomeType.Swamp: return "Swamp";
                case BiomeType.Mountain: return "Mountain";
                case BiomeType.BlackForest: return "BlackForest";
                case BiomeType.Plains: return "Plains";
                case BiomeType.AshLands: return "AshLands";
                case BiomeType.DeepNorth: return "DeepNorth";
                case BiomeType.Mistlands: return "Mistlands";
                case BiomeType.Ocean: return "Ocean";
                default: return "None";
            }
        }

        // ── cardinal direction + radial distance from world centre ──
        private static (string card, double dist) Cardinal(float cx, float cz)
        {
            double ang = Math.Atan2(cz, cx) * 180.0 / Math.PI;
            // (name, lo, hi) — first matching half-open [lo,hi) wins; Vest wraps the ±180 seam.
            var table = new (string n, double a, double b)[]
            {
                ("Aust", -22.5, 22.5), ("Nordaust", 22.5, 67.5), ("Nord", 67.5, 112.5),
                ("Nordvest", 112.5, 157.5), ("Vest", 157.5, 180.1), ("Vest", -180, -157.5),
                ("Sudvest", -157.5, -112.5), ("Sud", -112.5, -67.5), ("Sudaust", -67.5, -22.5),
            };
            string nm = "Nord";
            foreach (var (n, a, b) in table)
            {
                if (a <= ang && ang < b) { nm = n; break; }
            }
            return (nm, Math.Sqrt((double)cx * cx + (double)cz * cz));
        }

        // ── derived region "character" used for schema biasing ──
        private struct Traits
        {
            public string Biome;
            public float Relief, Peak;
            public int Nbr, Area;
            public double Dist;
            public bool Remote, Hospitable, Dangerous, Rugged, Big, Small, Coastal;
            public bool HasBoss, Trader, DungeonDense, PoiRich;
            public string Boss;
            public string Sup; // reserved superlative name for this region, if any
        }

        private Traits BuildTraits(RegionInfo r)
        {
            string b = BiomeName(r.DominantBiome);
            var (_, dist) = Cardinal(r.CentroidX, r.CentroidZ);

            RegionLocationSignal loc = null;
            this.locationByKey?.TryGetValue(r.RegionKey, out loc);

            var t = new Traits
            {
                Biome = b,
                Relief = r.Relief,
                Peak = r.MaxElevation,
                Nbr = r.NeighborKeys.Count,
                Area = r.AreaZones,
                Dist = dist,
                Hospitable = b == "Meadows" || b == "Plains" || b == "BlackForest",
                Dangerous = b == "Swamp" || b == "Mistlands" || b == "AshLands" || b == "DeepNorth",
                Big = r.AreaZones >= 300,
                Small = r.AreaZones <= 120,
                Coastal = r.IsCoastal,
                HasBoss = loc?.HasBoss ?? false,
                Boss = loc?.Boss,
                Trader = loc?.TraderPresent ?? false,
                DungeonDense = (loc?.DungeonCount ?? 0) >= 20,
                PoiRich = (loc?.TotalPois ?? 0) >= 120,
            };
            t.Remote = t.Dist > 6500 || t.Nbr <= 1;
            t.Rugged = t.Relief >= 200 || b == "Mountain";
            return t;
        }

        private string Descriptor(RegionInfo r)
        {
            string b = BiomeName(r.DominantBiome);
            if (b != "Mountain" && r.Relief >= 220) return Pick(new[]{"Highlands","Heights","Crags"}, r.RegionKey, 7);
            return Pick(BiomeDesc.TryGetValue(b, out var d) ? d : new[]{"Coast","Shores","Reach"}, r.RegionKey, 7);
        }

        // context passed to schema makers
        private sealed class Ctx
        {
            public string Desc, Card, Superlative, BaseName;
            public Traits T;
        }

        // ── schema makers (port of s_* in name_schemes.py) ──
        private static string SBare(RegionInfo r, Ctx c) => c.BaseName;
        private static string STerrPost(RegionInfo r, Ctx c) => $"the {c.BaseName} {c.Desc}";
        private static string STerrOf(RegionInfo r, Ctx c) => $"the {c.Desc} of {c.BaseName}";
        private static string SDescriptive(RegionInfo r, Ctx c)
        {
            string b = c.T.Biome;
            return $"the {Pick(BiomeAdj.TryGetValue(b, out var adj) ? adj : new[]{"Wild"}, r.RegionKey, 11)} " +
                   $"{Pick(BiomeDesc.TryGetValue(b, out var d) ? d : new[]{"Reach"}, r.RegionKey, 12)}";
        }
        private static string SCardinal(RegionInfo r, Ctx c) =>
            c.T.Dist > 7500 ? $"the Far {c.Card}" : $"{c.Card}{Pick(new[]{"mark","reach","land","heim"}, r.RegionKey, 13)}";
        private static string SMinted(RegionInfo r, Ctx c)
        {
            string b = c.T.Biome;
            string pre = Pick(BiomePrefix.TryGetValue(b, out var p) ? p : new[]{"Vild"}, r.RegionKey, 14);
            return pre + Pick(r.IsCoastal ? CoastSuf : LandSuf, r.RegionKey, 15);
        }
        private static string SPerson(RegionInfo r, Ctx c)
        {
            string p = Pick(People, r.RegionKey, 20);
            string thing = Pick(new[]{"Rest","Reach","Landing","Holding","Stand","Crossing","Folly","Ward"}, r.RegionKey, 21);
            return $"{p}'s {thing}";
        }
        private static string SSettlement(RegionInfo r, Ctx c)
        {
            string p = Pick(People, r.RegionKey, 22);
            string suf = Pick(SettleSuf, r.RegionKey, 24);
            string col = Pick(new[]{"Grey","East","North","Stone","Black","High","Wind"}, r.RegionKey, 25);
            switch ((int)(H(r.RegionKey, 23) % 4))
            {
                case 0: return $"the Hold of {p}";
                case 1: return $"the Jarldom of {p}";
                case 2: return $"{p}{suf}";
                default: return $"{col}{suf}";
            }
        }
        private static string SCreature(RegionInfo r, Ctx c)
        {
            string cr = Pick(Creatures, r.RegionKey, 30);
            string suf = Pick(new[]{"moor","fell","mere","wood","crag","fen"}, r.RegionKey, 32);
            string place = Pick(new[]{"Wallow","Roost","Den","Run","Grave","Hollow"}, r.RegionKey, 33);
            switch ((int)(H(r.RegionKey, 31) % 3))
            {
                case 0: return $"{cr}{suf}";
                case 1: return $"the {cr}'s {place}";
                default: return $"{cr}moor";
            }
        }
        private static string SMemorial(RegionInfo r, Ctx c)
        {
            string who = Pick(People, r.RegionKey, 40);
            string g = Pick(Grave, r.RegionKey, 41);
            string suf = Pick(new[]{"howe","barrow","cairn"}, r.RegionKey, 42);
            var forms = new[]{ $"{who}'s {g}", $"the {g} of {who}", $"{who}{suf}" };
            return forms[H(r.RegionKey, 43) % (uint)forms.Length];
        }
        private static string SLoreFigure(RegionInfo r, Ctx c)
        {
            string fig = Pick(Mythic, r.RegionKey, 50);
            var forms = new[]{ $"{fig}'s Rest", $"where {fig} fell", $"the Hall of {fig}", $"{fig}'s Folly" };
            return forms[H(r.RegionKey, 51) % (uint)forms.Length];
        }
        private static string SLoreEvent(RegionInfo r, Ctx c)
        {
            string ev = Pick(Events, r.RegionKey, 60);
            var forms = new[]{ ev, $"the Land of {ev}", $"{c.BaseName}, after {ev}" };
            return forms[H(r.RegionKey, 61) % (uint)forms.Length];
        }
        private static string SSuperlative(RegionInfo r, Ctx c) => c.Superlative;
        private static string SBossSeat(RegionInfo r, Ctx c)
        {
            var pool = (c.T.Boss != null && BossEpithet.TryGetValue(c.T.Boss, out var p)) ? p : new[]{"the Warded Seat","the Old Throne"};
            return Pick(pool, r.RegionKey, 70);
        }
        private static string STraderHold(RegionInfo r, Ctx c)
        {
            string p = Pick(People, r.RegionKey, 72);
            var forms = new[]{ $"{p}'s Market", $"the Trade-Hold of {p}", $"{p}'s Crossing", "the Merchant's Rest" };
            return forms[H(r.RegionKey, 73) % (uint)forms.Length];
        }
        private static string SDungeonHaunt(RegionInfo r, Ctx c) =>
            Pick(new[]{"the Crypt-Lands","the Hollow Hills","the Barrow-Reach","the Tomb-Fields","the Restless Ground"}, r.RegionKey, 74);

        // (key, make, weight, fits) — mirrors the SCHEMAS table
        private struct Schema
        {
            public string Key;
            public Func<RegionInfo, Ctx, string> Make;
            public Func<RegionInfo, Traits, int> Weight;
            public Func<RegionInfo, Traits, bool> Fits;
        }

        private static readonly Schema[] Schemas =
        {
            new Schema { Key="bare",          Make=SBare,        Weight=(r,t)=>26, Fits=(r,t)=>true },
            new Schema { Key="terrain-post",  Make=STerrPost,    Weight=(r,t)=>10, Fits=(r,t)=>true },
            new Schema { Key="terrain-of",    Make=STerrOf,      Weight=(r,t)=>6,  Fits=(r,t)=>true },
            new Schema { Key="descriptive",   Make=SDescriptive, Weight=(r,t)=>9 + (t.Dangerous?6:0), Fits=(r,t)=>BiomeAdj.ContainsKey(t.Biome) },
            new Schema { Key="cardinal",      Make=SCardinal,    Weight=(r,t)=>6 + (t.Remote?8:0), Fits=(r,t)=>true },
            new Schema { Key="minted",        Make=SMinted,      Weight=(r,t)=>8,  Fits=(r,t)=>BiomePrefix.ContainsKey(t.Biome) },
            new Schema { Key="person",        Make=SPerson,      Weight=(r,t)=>12 + (t.Hospitable?8:0), Fits=(r,t)=>true },
            new Schema { Key="settlement",    Make=SSettlement,  Weight=(r,t)=>6 + ((t.Big && t.Hospitable)?14:0) + (t.Trader?10:0), Fits=(r,t)=>true },
            new Schema { Key="creature",      Make=SCreature,    Weight=(r,t)=>9 + ((t.Dangerous || t.Biome=="BlackForest")?6:0), Fits=(r,t)=>true },
            new Schema { Key="memorial",      Make=SMemorial,    Weight=(r,t)=>5 + (t.Dangerous?10:0), Fits=(r,t)=>true },
            new Schema { Key="lore-figure",   Make=SLoreFigure,  Weight=(r,t)=>3 + ((t.Remote && t.Dangerous)?14:0), Fits=(r,t)=>true },
            new Schema { Key="lore-event",    Make=SLoreEvent,   Weight=(r,t)=>2 + ((t.Remote && t.Small)?10:0), Fits=(r,t)=>true },
            new Schema { Key="boss-seat",     Make=SBossSeat,    Weight=(r,t)=>t.HasBoss?55:0, Fits=(r,t)=>t.HasBoss },
            new Schema { Key="trader-hold",   Make=STraderHold,  Weight=(r,t)=>t.Trader?16:0, Fits=(r,t)=>t.Trader },
            new Schema { Key="dungeon-haunt", Make=SDungeonHaunt,Weight=(r,t)=>t.DungeonDense?18:0, Fits=(r,t)=>t.DungeonDense },
            new Schema { Key="superlative",   Make=SSuperlative, Weight=(r,t)=>80, Fits=(r,t)=>t.Sup != null },
        };

        private Dictionary<string, string> BuildSuperlatives(IReadOnlyList<RegionInfo> regions)
        {
            var sup = new Dictionary<string, string>();
            if (regions.Count == 0) return sup;

            RegionInfo peak = regions[0], big = regions[0], iso = regions[0];
            foreach (var r in regions)
            {
                if (r.MaxElevation > peak.MaxElevation) peak = r;
                if (r.AreaZones > big.AreaZones) big = r;
                // most isolated: fewest neighbours, ties broken by larger area (matches the bench's
                // min over (nbr, -area))
                if (r.NeighborKeys.Count < iso.NeighborKeys.Count ||
                    (r.NeighborKeys.Count == iso.NeighborKeys.Count && r.AreaZones > iso.AreaZones)) iso = r;
            }

            sup[peak.RegionKey] = Pick(new[]{"the Spire","Himinbjorg","the Roof of the World"}, peak.RegionKey, 21);
            if (!sup.ContainsKey(big.RegionKey)) sup[big.RegionKey] = $"Greater {BaseNameOf(big)}";
            if (iso.NeighborKeys.Count <= 1 && !sup.ContainsKey(iso.RegionKey))
                sup[iso.RegionKey] = Pick(new[]{"the Lonely Isle","Utgard","the Sundered Land"}, iso.RegionKey, 22);
            return sup;
        }

        // Per-world base (catalog) name = the legacy deterministic stem the faux-lore schemas build on.
        private string worldIdForBase;
        private string BaseNameOf(RegionInfo r) => RegionGuidNameService.CreateDeterministicName(this.worldIdForBase, r.RegionKey);

        private (string name, string schema) NameRegion(RegionInfo r, Dictionary<string, string> supmap)
        {
            Traits t = BuildTraits(r);
            supmap.TryGetValue(r.RegionKey, out var sup);
            t.Sup = sup;
            var c = new Ctx { Desc = Descriptor(r), Card = Cardinal(r.CentroidX, r.CentroidZ).card, Superlative = sup, BaseName = BaseNameOf(r), T = t };

            long total = 0;
            foreach (var s in Schemas) if (s.Fits(r, t)) total += s.Weight(r, t);
            if (total <= 0) return (BaseNameOf(r), "bare");

            long roll = H(r.RegionKey, 777) % (uint)total;
            long acc = 0;
            foreach (var s in Schemas)
            {
                if (!s.Fits(r, t)) continue;
                acc += s.Weight(r, t);
                if (roll < acc) return (s.Make(r, c), s.Key);
            }
            return (BaseNameOf(r), "bare");
        }

        /// <inheritdoc />
        public void NameAll(string worldId, IReadOnlyList<RegionInfo> regions)
        {
            if (string.IsNullOrWhiteSpace(worldId)) throw new ArgumentException("worldId must not be null or empty", nameof(worldId));
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            if (regions.Count == 0) return;

            this.worldIdForBase = worldId;
            var supmap = BuildSuperlatives(regions);

            // Deterministic uniqueness pass (mirrors enrich_gazetteer.py):
            //   1) reserve superlative names first,
            //   2) then regions in RegionKey order, re-rolling on collision by perturbing the hash key.
            var used = new HashSet<string>(StringComparer.Ordinal);
            var supKeys = new HashSet<string>(supmap.Keys, StringComparer.Ordinal);
            foreach (var kv in supmap) used.Add(kv.Value);

            var ordered = new List<RegionInfo>(regions);
            ordered.Sort((a, b) => string.CompareOrdinal(a.RegionKey, b.RegionKey));

            foreach (var r in ordered)
            {
                string nm;
                if (supKeys.Contains(r.RegionKey))
                {
                    nm = supmap[r.RegionKey];
                }
                else
                {
                    nm = null;
                    for (int attempt = 0; attempt < 60; attempt++)
                    {
                        string cand = NameWithSalt(r, attempt, supmap);
                        if (!used.Contains(cand)) { nm = cand; break; }
                    }
                    if (nm == null)
                    {
                        // exhausted — disambiguate by cardinal direction
                        string baseName = NameRegion(r, supmap).name;
                        nm = $"{baseName} ({Cardinal(r.CentroidX, r.CentroidZ).card})";
                    }
                }
                used.Add(nm);
                r.Name = nm;
            }
        }

        // attempt 0 = unperturbed; attempt k>0 perturbs the RegionKey with a "#k" salt suffix so a
        // colliding name re-rolls deterministically (identical to the Python uniqueness pass).
        private string NameWithSalt(RegionInfo r, int attempt, Dictionary<string, string> supmap)
        {
            if (attempt == 0) return NameRegion(r, supmap).name;

            var perturbed = new RegionInfo
            {
                RegionKey = $"{r.RegionKey}#{attempt.ToString(CultureInfo.InvariantCulture)}",
                TransientId = r.TransientId,
                IdentityCoord = r.IdentityCoord,
                SeedZone = r.SeedZone,
                CentroidX = r.CentroidX, CentroidZ = r.CentroidZ,
                MinZoneX = r.MinZoneX, MinZoneZ = r.MinZoneZ, MaxZoneX = r.MaxZoneX, MaxZoneZ = r.MaxZoneZ,
                AreaZones = r.AreaZones, LandZones = r.LandZones, InlandWaterZones = r.InlandWaterZones,
                AreaKm2 = r.AreaKm2, IsCoastal = r.IsCoastal, DominantBiome = r.DominantBiome,
                BiomeComposition = r.BiomeComposition,
                MinElevation = r.MinElevation, MeanElevation = r.MeanElevation, MaxElevation = r.MaxElevation,
                HighestPeakX = r.HighestPeakX, HighestPeakZ = r.HighestPeakZ,
                NeighborKeys = r.NeighborKeys,
            };
            return NameRegion(perturbed, supmap).name;
        }
    }
}
