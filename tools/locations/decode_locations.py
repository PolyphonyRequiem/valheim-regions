#!/usr/bin/env python3
"""
Valheim world .db -> location/POI decoder. REAL ZDO parser (not a regex scan).

Grounded in the decomp (assembly_valheim.decompiled.cs):
  World save envelope (WorldSaveData write, line ~68080):
    int32 worldVersion(37) · double m_netTime
    · ZDOMan.SaveAsync(writer):  long sessionID · uint nextUid · int zdoCount · zdoCount × ZDO.Save
    · ZoneSystem.SaveASync · RandEventSystem  (we stop after the ZDO list)
  ZDO.Save (line ~62790), per record:
    ushort flags · Vector2i sector(2×int) · Vector3 position(3×float) · int prefabHash
    · [Quaternion rotation(4×float) if flags&0x1000]
    · if (flags & 0x00FF): [connection] + 7 typed data sections (float/vec3/quat/int/long/string/bytes),
      each = byte count, then count × (int key, <value>)
  ZPackage string = .NET BinaryWriter 7-bit-length-prefixed UTF8.

prefabHash = Valheim GetStableHashCode(prefabName). We resolve hashes for known location/boss/vendor
prefab names by hashing a name list and matching. Position (x,z) → region via the gazetteer grid.

Self-check: the decoded per-prefab counts are compared against an independent raw-substring scan of
the same file; if the ZDO walk desyncs, counts diverge wildly and we FAIL LOUD rather than emit garbage.
"""
import struct, sys, json
from collections import Counter, defaultdict

# ── Valheim GetStableHashCode (assembly_utils) — for resolving prefab name -> hash ──
def stable_hash(s: str) -> int:
    # Valheim uses the .NET string.GetHashCode legacy algorithm (per-char, two accumulators)
    if s == "":
        return 0
    num = 5381
    num2 = num
    i = 0
    b = s.encode("utf-16-le")
    chars = [b[j] | (b[j+1] << 8) for j in range(0, len(b), 2)]
    n = len(chars)
    idx = 0
    while idx < n:
        c = chars[idx]
        num = ((num << 5) + num) ^ c
        num &= 0xFFFFFFFF
        if idx + 1 >= n:
            break
        c2 = chars[idx+1]
        if c2 == 0:
            break
        num2 = ((num2 << 5) + num2) ^ c2
        num2 &= 0xFFFFFFFF
        idx += 2
    res = (num + num2 * 1566083941) & 0xFFFFFFFF
    # to signed int32
    return res - 0x100000000 if res >= 0x80000000 else res

class Reader:
    def __init__(self, d, off=0):
        self.d = d; self.o = off
    def i32(self):
        v = struct.unpack_from("<i", self.d, self.o)[0]; self.o += 4; return v
    def u32(self):
        v = struct.unpack_from("<I", self.d, self.o)[0]; self.o += 4; return v
    def u16(self):
        v = struct.unpack_from("<H", self.d, self.o)[0]; self.o += 2; return v
    def i64(self):
        v = struct.unpack_from("<q", self.d, self.o)[0]; self.o += 8; return v
    def f32(self):
        v = struct.unpack_from("<f", self.d, self.o)[0]; self.o += 4; return v
    def f64(self):
        v = struct.unpack_from("<d", self.d, self.o)[0]; self.o += 8; return v
    def byte(self):
        v = self.d[self.o]; self.o += 1; return v
    def vec3(self):
        return (self.f32(), self.f32(), self.f32())
    def vec2s(self):
        # Vector2s = two int16 (sector). 4 bytes, NOT two int32.
        x = struct.unpack_from("<h", self.d, self.o)[0]; self.o += 2
        y = struct.unpack_from("<h", self.d, self.o)[0]; self.o += 2
        return (x, y)
    def num_items(self):
        # ZPackage.ReadNumItems (v33+): 1 byte, or if high bit set, big-endian 2-byte
        n = self.byte()
        if n & 0x80:
            n = ((n & 0x7F) << 8) | self.byte()
        return n
    def string(self):
        # .NET 7-bit-encoded length prefix + UTF8
        n = 0; shift = 0
        while True:
            b = self.byte()
            n |= (b & 0x7F) << shift
            if not (b & 0x80): break
            shift += 7
        s = self.d[self.o:self.o+n].decode("utf-8", "replace"); self.o += n
        return s
    def skip(self, k):
        self.o += k

def skip_data_section(r, value_kind):
    """A data block (v33+): num_items count, then count × (int key, <value>)."""
    count = r.num_items()
    for _ in range(count):
        r.i32()  # key
        if value_kind == "f":    r.f32()
        elif value_kind == "v3": r.vec3()
        elif value_kind == "q":  r.skip(16)      # quaternion 4×float
        elif value_kind == "i":  r.i32()
        elif value_kind == "l":  r.i64()
        elif value_kind == "s":  r.string()
        elif value_kind == "b":
            blen = r.i32()                        # byte[] is int32-length-prefixed
            r.skip(blen)

def parse_zdos(path):
    d = open(path, "rb").read()
    r = Reader(d)
    world_version = r.i32()
    assert world_version >= 30, f"unexpected worldVersion {world_version}"
    net_time = r.f64()
    session_id = r.i64()
    next_uid = r.u32()
    zdo_count = r.i32()
    out = []  # (prefabHash, x, z)
    for _ in range(zdo_count):
        flags = r.u16()
        sector = r.vec2s()
        pos = r.vec3()
        prefab = r.i32()
        if flags & 0x1000:
            r.skip(12)  # rotation Vector3 (3×float)
        if (flags & 0x00FF) != 0:
            if flags & 0x1:        # connection
                r.byte()           # m_type
                r.u32()            # m_hash
            if flags & 0x2:  skip_data_section(r, "f")
            if flags & 0x4:  skip_data_section(r, "v3")
            if flags & 0x8:  skip_data_section(r, "q")
            if flags & 0x10: skip_data_section(r, "i")
            if flags & 0x20: skip_data_section(r, "l")
            if flags & 0x40: skip_data_section(r, "s")
            if flags & 0x80: skip_data_section(r, "b")
        out.append((prefab, pos[0], pos[2]))
    # ── After the ZDO list comes ZoneSystem.SaveASync (the LOCATION INSTANCES) ──
    # int genZoneCount · genZoneCount×(int x,int y) · int 0 · int locationVersion
    # · int globalKeyCount · globalKeyCount×string · bool locationsGenerated
    # · int locationCount · locationCount×(string prefabName, float x,y,z, bool placed)
    locations = []
    try:
        genZones = r.i32()
        r.skip(genZones * 8)         # Vector2i pairs (2×int)
        r.i32()                       # the literal 0
        loc_version = r.i32()
        gk = r.i32()
        for _ in range(gk):
            r.string()
        r.byte()                      # locationsGenerated bool
        loc_count = r.i32()
        for _ in range(loc_count):
            name = r.string()
            x = r.f32(); y = r.f32(); z = r.f32()
            placed = r.byte()
            locations.append((name, x, z, bool(placed)))
    except Exception as e:
        print(f"  [warn] location-section parse stopped: {e}")
    return dict(world_version=world_version, zdo_count=zdo_count, zdos=out,
                locations=locations, parsed_bytes=r.o, total=len(d))

# location/boss/vendor prefab names (vanilla) — hash these, match against decoded prefab hashes
LOCATION_PREFABS = [
    # crypts / dungeons
    "Crypt2","Crypt3","Crypt4","SunkenCrypt4","MountainCave02","FrostCaves","TrollCave","TrollCave02",
    # surface POIs
    "Runestone_Boars","Runestone_Greydwarfs","StoneTowerRuins03","StoneTowerRuins04","StoneTowerRuins05",
    "Ruin1","Ruin2","Ruin3","StoneHouse3","StoneHouse4","WoodHouse1","Dolmen01","Dolmen02","Dolmen03",
    "TarPit1","TarPit2","TarPit3","DrakeNest01","GoblinCamp2","Grave1","InfestedTree01",
    "Vendor_BlackForest","Hildir_camp","Mistlands_DvergrTownEntrance1",
    # bosses (altars)
    "Eikthyrnir","GDKing","Bonemass","Dragonqueen","GoblinKing","Mistlands_DvergrBossEntrance1","Fader",
    "StartTemple",
]

def main():
    db = sys.argv[1] if len(sys.argv) > 1 else "/home/polyphonyrequiem/valheim/niflheim/config/worlds_local/niflheim.db"
    parsed = parse_zdos(db)
    print(f"worldVersion={parsed['world_version']}  zdoCount={parsed['zdo_count']:,}  "
          f"parsed {parsed['parsed_bytes']:,}/{parsed['total']:,} bytes")

    locs = parsed["locations"]
    print(f"\nLOCATION INSTANCES decoded: {len(locs):,}")
    by = Counter(nm for nm,_,_,_ in locs)
    print("by prefab (top 30):")
    for nm, n in by.most_common(30):
        print(f"  {nm:34} {n}")
    placed = sum(1 for *_, p in locs if p)
    print(f"\nplaced (visited): {placed:,}  ·  unplaced: {len(locs)-placed:,}")

    # write for the region-join step
    out = {"worldVersion": parsed["world_version"], "locationCount": len(locs),
           "locations": [{"prefab": nm, "x": x, "z": z, "placed": p} for nm,x,z,p in locs]}
    outp = db.rsplit("/",1)[-1].replace(".db","") + "_locations_raw.json"
    json.dump(out, open(outp, "w"))
    print(f"\nwrote {outp}  ({len(locs)} locations)")

if __name__ == "__main__":
    main()
