using System.IO;
using System.IO.Compression;

namespace WorldZones.Cli
{
    /// <summary>
    /// Minimal PNG writer for RGB24 data — no external image library needed.
    /// Ported from the Unity Editor tool's PngWriter.
    /// </summary>
    static class PngWriter
    {
        public static void Write(string path, int width, int height, byte[] rgb)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                // PNG signature
                bw.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

                // IHDR
                var ihdr = new byte[13];
                WriteInt32BE(ihdr, 0, width);
                WriteInt32BE(ihdr, 4, height);
                ihdr[8] = 8;  // bit depth
                ihdr[9] = 2;  // color type: RGB
                ihdr[10] = 0; // compression
                ihdr[11] = 0; // filter
                ihdr[12] = 0; // interlace
                WriteChunk(bw, "IHDR", ihdr);

                // IDAT — deflate raw scanlines with filter byte 0 (None) per row
                int rowBytes = width * 3 + 1; // +1 for filter byte
                byte[] rawData = new byte[height * rowBytes];
                for (int y = 0; y < height; y++)
                {
                    rawData[y * rowBytes] = 0; // filter: None
                    System.Buffer.BlockCopy(rgb, y * width * 3, rawData, y * rowBytes + 1, width * 3);
                }

                byte[] compressed;
                using (var ms = new MemoryStream())
                {
                    // zlib header
                    ms.WriteByte(0x78);
                    ms.WriteByte(0x01);
                    using (var ds = new DeflateStream(ms, CompressionLevel.Fastest, true))
                    {
                        ds.Write(rawData, 0, rawData.Length);
                    }
                    // zlib Adler-32 checksum
                    uint adler = Adler32(rawData);
                    ms.WriteByte((byte)(adler >> 24));
                    ms.WriteByte((byte)(adler >> 16));
                    ms.WriteByte((byte)(adler >> 8));
                    ms.WriteByte((byte)adler);
                    compressed = ms.ToArray();
                }
                WriteChunk(bw, "IDAT", compressed);

                // IEND
                WriteChunk(bw, "IEND", new byte[0]);
            }
        }

        static void WriteChunk(BinaryWriter bw, string type, byte[] data)
        {
            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            byte[] lenBytes = new byte[4];
            WriteInt32BE(lenBytes, 0, data.Length);
            bw.Write(lenBytes);
            bw.Write(typeBytes);
            bw.Write(data);

            // CRC32 over type + data
            byte[] crcInput = new byte[4 + data.Length];
            System.Buffer.BlockCopy(typeBytes, 0, crcInput, 0, 4);
            System.Buffer.BlockCopy(data, 0, crcInput, 4, data.Length);
            uint crc = Crc32(crcInput);
            byte[] crcBytes = new byte[4];
            WriteInt32BE(crcBytes, 0, (int)crc);
            bw.Write(crcBytes);
        }

        static void WriteInt32BE(byte[] buf, int offset, int value)
        {
            buf[offset]     = (byte)(value >> 24);
            buf[offset + 1] = (byte)(value >> 16);
            buf[offset + 2] = (byte)(value >> 8);
            buf[offset + 3] = (byte)value;
        }

        static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            for (int i = 0; i < data.Length; i++)
            {
                a = (a + data[i]) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        static uint[]? crcTable;
        static uint Crc32(byte[] data)
        {
            if (crcTable == null)
            {
                crcTable = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++)
                        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    crcTable[n] = c;
                }
            }
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < data.Length; i++)
                crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }
    }
}
