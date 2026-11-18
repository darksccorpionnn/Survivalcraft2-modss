# Flat Nature

Independent Survivalcraft 2 API mod for newly created Flat worlds only.

## Features
- Sparse green rolling hills and medium-height green mountains.
- Separate high mountain clusters with snowy caps when the game has a snow block.
- Large lakes with solid beds and filled water columns.
- Clustered tree groups with open plains between them.
- Coal, copper, iron and gold ore veins when those ore blocks are available.
- No cave generation and no underground air pockets. The vanilla Flat generator's empty Pass 3 and Pass 4 are left intact; the mod adds solid terrain, filled lakes, trees and ores.

## New-world-only safety gate
When the game creates a FlatContinent or FlatIsland world, the mod writes a FlatNature.created marker into that new world directory. The terrain hook checks both the Flat generator and this marker before making changes. Existing saves are not marked and are ignored.

## Android installation
Copy FlatNature.scmod into:
 /storage/emulated/0/Download/Survivalcraft/Mods/

The package is assembled by the GitHub Actions workflow in this repository. In-game world generation still needs a test on the Android client before this can be called verified.
