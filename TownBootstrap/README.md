# Town Bootstrap - Two-Storey Village Generator

## Target and version

- Version: **0.6.4**
- Survivalcraft: 2.4.0.0
- Survivalcraft API: 1.9.5.1
- .NET: 10
- Package: `TownBootstrap.scmod`

## Generation

- The world is divided into 256×256-block grid cells. The generator allows one town per cell and attempts generation around the player's loaded position the first time that cell is visited.
- It retries when the nearby terrain does not yet provide a suitable site and uses a town marker to avoid duplicating an existing town in the same grid cell.
- A Village Egg can also be placed to request generation near that position. The egg is consumed when generation succeeds and restored when no suitable site is found.
- A town contains six two-storey houses around a central square, connecting roads, and town lights.
- The block palette uses spruce wood, planks, cobblestone, framed glass, wooden stairs, wooden doors, and wicker lamps. Required blocks are resolved from the game registry; the generator does not silently substitute unrelated blocks when required palette blocks are unavailable.

## Safety and limitations

- The generator searches for a suitable, nearly level site before building.
- Block placement checks the target terrain cells; unsuitable or occupied sites can cause an attempt to be skipped and retried.
- This package generates buildings and props. Villager NPC schedules, jobs, sleeping, and work AI are provided by the separate **Living Towns** mod.
- Build verification checks that the compiled assembly, version, database registration, Village Egg block registration, CSV block registry entry, and texture are included. It does not replace testing in an actual Android world.

## Build

From the repository root:

```bash
dotnet restore TownBootstrap/TownBootstrap.csproj --configfile nuget.config
dotnet build TownBootstrap/TownBootstrap.csproj --configuration Release
```

The build target creates `TownBootstrap.scmod` in `TownBootstrap/bin/Release/`. The package includes the compiled DLL, `modinfo.json`, the database, the block registry CSV, and the Village Egg texture.

## Installation

Install `TownBootstrap.scmod` and `LivingTowns-0.4.44-Full.scmod` as two separate mod files in the Survivalcraft API client's Mods folder. Do not merge the two packages into one `.scmod`. Restart the API client after replacing the files.
