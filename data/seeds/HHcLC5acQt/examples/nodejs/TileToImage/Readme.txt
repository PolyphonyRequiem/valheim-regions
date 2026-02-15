This is a simple example that reads all the tiles and converts them to individual images representing the land in each tile.
It then generates an html file which stitches all the images together.

This example requires nodejs v12+ installed and can be run like this:

cd examples/nodejs/TileToImage
npm install
node server.js <PathToDataFolder>