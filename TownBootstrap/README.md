# Town Bootstrap - Two-Storey Village Generator

## Target

- Survivalcraft: 2.4.0.0
- Survivalcraft API: 1.9.5.1
- .NET: 10
- Package: `TownBootstrap.scmod`

## Version 0.3.0

This is the first procedural village-generation stage. After the player entity loads, it searches 30-60 blocks from the player's initial loaded position for a clear, nearly flat 41x41 area.

The village layout contains:

- Six two-storey timber-and-stone houses around a central square.
- Stone foundations and lower courses, timber framing, plank walls and pitched roofs.
- Ground-floor and upper-floor interiors, with carpet areas on both levels.
- Chests, a work corner, indoor torches and a two-block doorway where the wooden-door block is available.
- Glass windows, an internal stair run and a special stone chimney on the blacksmith house.
- A cobblestone well/fountain, market stall, connecting paths, lamp posts and a small fenced garden plot.

## Safety and limitations

- The generator searches for a flat, clear site before making changes.
- Every placement checks that the target cell is air. Existing blocks are not intentionally replaced.
- The block types are resolved through the API block registry; optional decorations are skipped if their block class cannot be resolved.
- A four-block granite feature in the central well is used as a nearby duplicate-generation marker.
- This version generates the buildings and props only. Villager NPCs, schedules, trading and work AI are not included yet.
- The site search uses the player's first loaded position as its reference. It may skip generation if no sufficiently flat and clear 41x41 area is found nearby.

## Build

```bash
dotnet restore TownBootstrap/TownBootstrap.csproj --configfile nuget.config
dotnet build TownBootstrap/TownBootstrap.csproj --configuration Release
```

The build target creates `TownBootstrap.scmod` above the output folder. The package must contain:

- `TownBootstrap.dll`
- `modinfo.json`
- `Assets/TownBootstrapDatabase.xdb`

## Android installation

Replace the previous TownBootstrap test package with the new `.scmod` in:

```
/storage/emulated/0/download/Survivalcraft/Mods/
```

Keep any separate villager mod installed. Restart the API client, create or open a test world, and check the surrounding area for the generated village. A newly created world in a suitable flat area is the best first test.
