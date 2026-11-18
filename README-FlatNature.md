# Flat Nature + Wood Stove

Independent Survivalcraft 2 API mod for newly created Flat worlds only.

## Existing world-generation features
- Sparse green rolling hills and medium-height green mountains.
- Separate high mountain clusters with snowy caps when the game has a snow block.
- Large lakes with solid beds and filled water columns.
- Clustered tree groups with open plains between them.
- Coal, copper, iron and gold ore veins when those ore blocks are available.
- No cave generation and no underground air pockets.

## Wood stove
- Adds a placeable **Odun Sobası** block.
- Reuses the native furnace inventory and interface.
- Has a fuel-only slot: input, output and by-product slots are disabled.
- Does not smelt ores or run crafting recipes.
- Consumes one fuel item at a time and keeps it burning for its normal fuel duration, even with no smelting recipe.
- Shows the game's native fire particles and an added rising smoke trail.
- Sets block heat and emitted light while burning; both return to zero when fuel runs out.
- Can be refuelled through the fuel slot.

The stove uses the static GLB model at `Assets/Models/WoodStove/WoodStove.glb`. If the model is not present, the block temporarily falls back to the vanilla furnace mesh while the code build remains testable.

## New-world-only terrain safety gate
When the game creates a FlatContinent or FlatIsland world, the mod writes a FlatNature.created marker into that new world directory. The terrain hook checks both the Flat generator and this marker before making terrain changes. Existing saves are not marked and are ignored.

## Android installation
Copy the generated `FlatNature.scmod` into:
`/storage/emulated/0/Download/Survivalcraft/Mods/`

GitHub Actions builds the DLL and packages the mod. The build is not an in-game verification; the stove still needs a test on the Android Survivalcraft API client.
