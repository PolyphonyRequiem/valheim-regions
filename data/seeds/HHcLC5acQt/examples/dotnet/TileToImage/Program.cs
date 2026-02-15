using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json.Linq;

namespace TileToImage
{
    class Program
    {
        static int Main(string[] args)
        {
            // First locate the map.json file
            string basePath;
            if (args.Length == 0)
            {
                basePath = "";
            }
            else
            {
                basePath = args[0];
            }

            string mapDescriptor = Path.Combine(basePath, "map.json");
            if (!File.Exists(mapDescriptor))
            {
                Console.WriteLine("You must specify the data folder as an argument, or run this from the data folder.");
                Console.WriteLine("e.g. dotnet run Program.cs path\\to\\data\\folder");
                return -1;
            }

            // Read the map.json file to see how many tiles across this map is
            var mapObject = JObject.Parse(File.ReadAllText(mapDescriptor));
            int tileSideCount = mapObject["TileSideCount"].Value<int>();
            int tileRowCount = mapObject["TileRowCount"].Value<int>();

            Console.WriteLine("Converting tiles...");
            for (int x = 0; x < tileSideCount; x++)
            {
                for (int z = 0; z < tileSideCount; z++ )
                {
                    string tileName = Path.Combine(basePath, "tiles", $"{x:d2}-{z:d2}.bin.gz");
                    Console.WriteLine($"\t{tileName}");

                    // This is the file that represents the tile (its a gzip file)
                    using (FileStream tileFile = File.OpenRead(tileName))
                    // Extract the GZIP stream to get the raw decompressed data
                    using (GZipStream gzipFile = new GZipStream(tileFile, CompressionMode.Decompress))
                    // We need a place to put that tile
                    using (MemoryStream uncompressedData = new MemoryStream())
                    // Finally we want an image to draw the tile into
                    using (Bitmap bmp = new Bitmap(tileRowCount, tileRowCount))
                    {
                        Console.WriteLine($"\t\tDecompressing...");
                        gzipFile.CopyTo(uncompressedData);
                        byte[] bytes = uncompressedData.GetBuffer();
                        Console.WriteLine($"\t\tWriting to image...");

                        // Now we have an uncompressed copy. Lets read it 'pixel' for 'pixel'
                        for (int tx = 0; tx < tileRowCount; tx++) {
                            for (int tz = 0; tz < tileRowCount; tz++) {
                                int offset = (tx * tileRowCount + tz) * 10; // 10 is the total length of one 'pixel' See the readme for more info

                                int biome = BitConverter.ToUInt16(bytes, 0 + offset);
                                float height = BitConverter.ToSingle(bytes, 2 + offset);
                                float forestFactor = BitConverter.ToSingle(bytes, 6 + offset);

                                // The storage format acts like images do and stores 0,0 in the top left
                                // however Valheim draws its map with 0,0 being in the bottom left, so if
                                // we want to draw the map the way folks are used to seeing it, we have to
                                // flip the vertical axis (Z);
                                int pixelX = tx;
                                int pixelZ = (tileRowCount - tz - 1);

                                if (height < 32)
                                    bmp.SetPixel(pixelX, pixelZ, Color.Blue);
                                else
                                    bmp.SetPixel(pixelX, pixelZ, Color.Green);
                            }
                        }

                        // Convert the tiles in-place to png files
                        Console.WriteLine($"\t\tSaving image...");
                        bmp.Save(Path.Combine(basePath, "tiles", $"{x:d2}-{z:d2}.png"), ImageFormat.Png);
                    }
                }
            }

            // Spit out an incredibly basic html file that just puts all the images into a table
            string htmlFile = Path.Combine(basePath, "land.html");
            StringBuilder html = new StringBuilder();
            html.AppendLine("<html><body><table>");

            // For the same reason we described in our building we have to flip the order of the tiles
            for (int z = tileSideCount- 1; z >= 0; z-- )
            {
                html.AppendLine("\t<tr>");
                for (int x = 0; x < tileSideCount; x++ )
                {
                    html.AppendLine($"\t\t<td><img src=\"tiles/{x:d2}-{z:d2}.png\"></td>");
                }
                html.AppendLine("\t</tr>");
            }
            html.AppendLine("</table></body></html>");
            File.WriteAllText(htmlFile, html.ToString());
            
            Console.WriteLine($"Done! Open {htmlFile} in a web browser to see the stitched together image.");
            return 0; // Return success
        }
    }
}
