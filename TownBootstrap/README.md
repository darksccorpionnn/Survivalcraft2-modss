# Town Bootstrap Test Mod

This is the first smoke-test project for the planned Survivalcraft 2 town-generation mod.

## Target

- Game version: Survivalcraft 2.4.0.0
- API package: SurvivalcraftAPI.Survivalcraft 1.9.5.1
- Target framework: .NET 10
- Package: `TownBootstrap.scmod`

## What this first build does

The mod loader writes two diagnostic entries:

- `[TownBootstrap] Mod initialized`
- `[TownBootstrap] OnLoadingFinished reached`

This verifies that the assembly is packaged and that the API recognizes the mod loader. It does not generate a town yet.

## Build

Install the .NET 10 SDK, then run from the repository root:

```bash
dotnet restore TownBootstrap/TownBootstrap.csproj --configfile nuget.config
dotnet build TownBootstrap/TownBootstrap.csproj --configuration Release
```

The build target creates:

```
TownBootstrap/bin/Release/TownBootstrap.scmod
```

GitHub Actions runs the same build after pushes to `main` and stores the package as the `TownBootstrap-scmod` workflow artifact.

## Android installation

Copy the built `.scmod` file to the Survivalcraft API mod folder:

```
/storage/emulated/0/download/Survivalcraft/Mods/
```

Restart the API client and check the mod list. This project still requires an in-game smoke test before it can be called confirmed working.
