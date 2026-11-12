# Town Bootstrap - Block Placement Test

This stage verifies that the Survivalcraft API mod can safely place blocks in a loaded world.

## Target

- Survivalcraft: 2.4.0.0
- Survivalcraft API: 1.9.5.1
- .NET: 10
- Package: `TownBootstrap.scmod`

## What version 0.2.0 does

When the player entity loads into a world, the mod looks a few blocks diagonally from the player and attempts to place a small 3x3 stone pad with a two-block-high center marker.

- It uses `SubsystemTerrain.ChangeCell` so the game receives normal terrain-change notifications.
- It only places blocks into air cells; it does not replace existing blocks.
- It checks for the marker before placing, to avoid stacking another copy when the same area is revisited.
- If no surface is found, it skips the placement and records a diagnostic message.

This is a block-placement test, not the finished village generator.

## Build

```bash
dotnet restore TownBootstrap/TownBootstrap.csproj --configfile nuget.config
dotnet build TownBootstrap/TownBootstrap.csproj --configuration Release
```

The build target creates `TownBootstrap.scmod` one directory above the output folder. The package must contain:

- `TownBootstrap.dll`
- `modinfo.json`
- `Assets/TownBootstrapDatabase.xdb`

## Android installation

Replace the previous TownBootstrap test package with the new `.scmod` file in:

```
/storage/emulated/0/download/Survivalcraft/Mods/
```

Keep any separate villager mod installed. Then restart the API client, open a test world, and look a few blocks diagonally from the player for the small stone pad and marker.
