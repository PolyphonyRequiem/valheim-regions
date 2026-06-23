using System;
using System.IO;
using WorldZones.WorldGen;

// Border-explorer exporter: sample the REAL port over a world window at fine resolution and dump
// height + biome to a flat binary for offline feature/cost-field experiments. Driven entirely by the
// verified WorldGenerator (no synthetic terrain). Resolution defaults to 8m (sub-64m-zone) so we can
// see the sub-zone structure the border algorithm would actually work with.
//
// Output binary (little-endian):
//   char[4] "WZBX"; int32 version=1
//   float32 originX, originZ      (world meters, SW corner of window)
//   float32 step                  (meters between samples)
//   int32   nx, nz                (samples per axis)
//   then nx*nz records row-major (z-major, x-minor):
//        float32 height; uint16 biome; uint16 pad
//
// Usage: export_patch <seed> <originX> <originZ> <step> <nx> <nz> <out.bin>
class ExportPatch
{
    static void Main(string[] args)
    {
        if (args.Length < 7)
        {
            Console.Error.WriteLine("usage: export_patch <seed> <originX> <originZ> <step> <nx> <nz> <out.bin>");
            Environment.Exit(1);
        }
        string seed = args[0];
        float ox = float.Parse(args[1]);
        float oz = float.Parse(args[2]);
        float step = float.Parse(args[3]);
        int nx = int.Parse(args[4]);
        int nz = int.Parse(args[5]);
        string outPath = args[6];

        var gen = new WorldGenerator(seed);
        using var bw = new BinaryWriter(File.Create(outPath));
        bw.Write(new char[] { 'W', 'Z', 'B', 'X' });
        bw.Write(1);
        bw.Write(ox); bw.Write(oz); bw.Write(step);
        bw.Write(nx); bw.Write(nz);

        for (int jz = 0; jz < nz; jz++)
        {
            float wz = oz + jz * step;
            for (int ix = 0; ix < nx; ix++)
            {
                float wx = ox + ix * step;
                float h = gen.GetHeight(wx, wz);
                int b = (int)gen.GetBiome(wx, wz);
                bw.Write(h);
                bw.Write((ushort)b);
                bw.Write((ushort)0);
            }
        }
        Console.WriteLine($"exported {nx}x{nz} @ {step}m from ({ox},{oz}) seed={seed} -> {outPath} ({new FileInfo(outPath).Length} bytes)");
    }
}
