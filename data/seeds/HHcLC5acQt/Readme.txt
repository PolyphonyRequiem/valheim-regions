Thanks for trying out the "All Data" download option. Feel free to drop me a line if you run into trouble. Check out the about page for my contact details.
-wd40bomber7

This zip file will contain all the data the generator makes available to the user sampled at a high resolution. However, this data requires some programming expertise to make use of. I (may) have provided some basic/incomplete examples in a few languages under the example directories.

Zip Format:
	data/tiles/<tile files>
	data/locations.json
	data/map.json

Tile files:
Tile files are square samples of the map. Think of them as if someone drew a grid on the Valheim map and then turned each 'cell' into a tile. The tiles are similar to 'pictures' of the map, except instead of red, green and blue forming a pixel, they are instead a combination of all the useful map data at that point.

The tiles are individually compressed with gzip and must be uncompressed before they can be read. Once read they are binary files which are just a list of this C style struct:
struct MapSample
{
	uint16_t biome;
	float height;
	float forestFactor;
}

There is no padding of any of the elements or between any of the structs. For the number of rows/columns in each tile see data/maps.json.
  biome is defined like this:
        None = 0,
        Meadows = 1,
        Swamp = 2,
        Mountain = 4,
        BlackForest = 8,
        Plains = 16,
        AshLands = 32,
        DeepNorth = 64,
        Ocean = 256,
        Mistlands = 512,

  height is the height in valheim distance of the world. This is not exactly the same as the heightmap height. Valheim does smoothing between adjacent biomes of heightmap height which this number does not account for.
  forestFactor is a float used to determine whether or not a biome will have forest in this area. (For example, if meadow/plains will have forest or not in a given spot). 


data/locations.json file:
  This file is a straight forward list of every location in the valheim map that has been identified by the map generator. Of particular note is "pseudo" locations in which the generator works to identify not only the presence of a location (e.g. a fuling camp) but the contents of that location (e.g. a beehive, fuling totems, a maypole, etc.). To get an idea of the structure I suggest just browsing the file format in a text editor (with JSON formatter) of your choice.


data/map.json file:
  This file describes the rest of the files and is of particular importance since it defines the size of the tiles. It contains a single json structure which should look like this. I've added comments here to explain the purpose of every field.


{
  // This is the seed the generated file targeted
  "WorldSeed": "Ba77EWy08c",
  // This is the world version the generator targeted
  "WorldVersion": "0.150.3",

  // This is the number of "pixels" per side on a tile in this data folder (the tiles are square)
  "TileRowCount": 1024,
  // This is the Valheim world size traversed by a single tile. (In this case each tile represents 6000x6000 valheim distance units). Since this is 6000 that means each sample is 6000 / 1024 = 5.8 valheim units apart. Valheim heightmaps are 1 unit apart so you would need to sample at a higher resolution to get closer to that heightmap size.
  "TileSize": 6000.0,
  // This is the number of tiles the world is across (so in this case the world is 4 tiles across, for a total of 4x4 = 16 tiles)
  "TileSideCount": 4,
  // This is the total width of the world sampled by the generator. It is equal to tile size * tile side count
  "WorldWidth": 24000.0,
  
  // This is the version of the http://valheim-map.world/ software used to generate this download
  "GeneratorVersion": "2.8",
  // This is the version of unity backing valheim-map.world at the time of generation
  "GeneratorUnityVersion": "2019.4.20f1"
}