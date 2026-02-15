const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const Jimp = require('jimp');

// First locate the map.json file
let basePath = "";
if (process.argv.length >= 3) {
    basePath = process.argv[2];
}

let mapDescriptor = path.join(basePath, "map.json");
if (!fs.existsSync(mapDescriptor)) {
    console.log("You must specify the data folder as an argument, or run this from the data folder.");
    console.log("e.g. node server.js path\\to\\data\\folder");
    process.exit(-1);
}

// Wrap Jimp's constructor in a promise
function CreateImage(width, height) {
    return new Promise((resolve, reject) => {
        new Jimp(width, height, (err, img) => {
            if (err) {
                reject(err);
            }
            else {
                resolve(img);
            }
        });
    });
}

async function ConvertTiles() {
    // Read the map.json file to see how many tiles across this map is
    let mapObject = JSON.parse(fs.readFileSync(mapDescriptor));
    let tileSideCount = mapObject.TileSideCount;
    let tileRowCount = mapObject.TileRowCount;

    console.log("Reading tiles");

    for (let x = 0; x < tileSideCount; x++) {
        for (let z = 0; z < tileSideCount; z++) {
            let tileName = path.join(basePath, "tiles", `${String(x).padStart(2, '0')}-${String(z).padStart(2, '0')}.bin.gz`);
            console.log(`\t${tileName}`);

            // Read the file
            let gzippedData = fs.readFileSync(tileName);

            // Make a new image
            let image = await CreateImage(tileRowCount, tileRowCount);

            // Decompress the data
            console.log(`\t\tDecompressing...`);
            let rawData = zlib.gunzipSync(gzippedData);

            console.log(`\t\tWriting to image...`);
            for (let tx = 0; tx < tileRowCount; tx++) {
                for (let tz = 0; tz < tileRowCount; tz++) {
                    let offset = (tx * tileRowCount + tz) * 10; // 10 is the total length of one 'pixel' See the readme for more info

                    let biome = rawData.readInt16LE(offset + 0);
                    let height = rawData.readFloatLE(offset + 2);
                    let forestFactor = rawData.readFloatLE(offset + 6);
                    
                    // The storage format acts like images do and stores 0,0 in the top left
                    // however Valheim draws its map with 0,0 being in the bottom left, so if
                    // we want to draw the map the way folks are used to seeing it, we have to
                    // flip the vertical axis (Z);
                    let pixelX = tx;
                    let pixelZ = (tileRowCount - tz - 1);
                        
                    if (height < 32)
                        image.setPixelColor(0x0000FFFF, pixelX, pixelZ);
                    else
                        image.setPixelColor(0x00FF00FF, pixelX, pixelZ);
                }
            }

            console.log(`\t\tSaving image...`);
            await image.write(path.join(basePath, "tiles", `${String(x).padStart(2, '0')}-${String(z).padStart(2, '0')}.png`));
        }
    }

    // Spit out an incredibly basic html file that just puts all the images into a table
    let htmlFilePath = path.join(basePath, "land.html");
    let html = "";
    html += "<html><body><table>\r\n";
    // For the same reason we described in our building we have to flip the order of the tiles
    for (let z = tileSideCount- 1; z >= 0; z-- )
    {
        html += "\t<tr>\r\n";
        for (let x = 0; x < tileSideCount; x++ )
        {
            html += `\t\t<td><img src=\"tiles/${String(x).padStart(2, '0')}-${String(z).padStart(2, '0')}.png\"></td>\r\n`;
        }
        html += "\t</tr>\r\n";
    }
    html += "</table></body></html>";
    fs.writeFileSync(htmlFilePath, html);

    console.log(`Done! Open ${htmlFilePath} in a web browser to see the stitched together image.`);
}


ConvertTiles();