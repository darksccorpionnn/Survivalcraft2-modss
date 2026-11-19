# Flat Nature + Wood Stove + Blue Torch

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

## Blue torch (0.2.13)
- Adds a separate **Mavi Meşale** block; the vanilla torch and Iron Lantern are not modified.
- Retains the native wall and floor placement variants.
- Uses the vanilla wooden torch mesh but removes its static orange flame mesh on the new block.
- Uses a dedicated blue-tinted FireParticleSystem subclass for animated fire particles.
- Emits the native scalar light amount 14/15. This is standard game lighting, not RGB-colored environmental lighting.
- Crafting: one coal chunk above one stick produces two Blue Torches.
- The blue flame, placement support and particle cleanup still require an in-game check on the Android Survivalcraft API client.

## New-world-only terrain safety gate
When the game creates a FlatContinent or FlatIsland world, the mod writes a FlatNature.created marker into that new world directory. The terrain hook checks both the Flat generator and this marker before making terrain changes. Existing saves are not marked and are ignored.

## Android installation
Copy the generated `FlatNature.scmod` into:
`/storage/emulated/0/Download/Survivalcraft/Mods/`

GitHub Actions builds the DLL and packages the mod. A successful build is not the same as in-game verification.


## Wooden structures (0.2.20)
- New Flat Nature chunks can receive a deterministic 7×7 wooden hut when a clear, level footprint is found.
- The hut generator keeps its footprint inside a single 16×16 chunk, preventing partial structures at chunk edges.
- The **Ahşap Kulübe Kiti** block can also be crafted and placed manually. It builds a centered hut only when the full 7×7 footprint is level, solid, dry and clear.
- Manual placement uses `SubsystemTerrain.ChangeCell` so terrain lighting and chunk geometry are refreshed.
- Automatic huts are generated only in new Flat Nature worlds protected by the existing world marker. The manual kit can be used in any world where the mod is loaded.
- A build success is not an in-game test; verify automatic placement in a newly created Flat Nature world and manual placement in a clear area.
