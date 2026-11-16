using System;
using System.Collections.Generic;

namespace Game;

/// <summary>
/// Standalone starter loader for the nine villager character templates.
/// No Living Towns legacy assembly or custom villager controller is required.
/// </summary>
public sealed class KoylulerModuModLoader : ModLoader
{
    private const string LogPrefix = "[KoylulerModu] ";

    public override void __ModInitialize()
    {
        ModsManager.RegisterHook("InitializeCreatureTypes", this);
        ModsManager.RegisterHook("OnLoadingFinished", this);
        Log.Information(LogPrefix + "Initialized. Nine standalone villager templates are registered in the mod database.");
    }

    public override void InitializeCreatureTypes(
        SubsystemCreatureSpawn spawn,
        List<SubsystemCreatureSpawn.CreatureType> creatureTypes)
    {
        if (spawn == null || creatureTypes == null)
            return;

        SubsystemTerrain terrain = spawn.Project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
        int added = 0;

        AddVillagerType(spawn, creatureTypes, terrain, "KoylulerModu_MaleVillager", ref added);
        AddVillagerType(spawn, creatureTypes, terrain, "KoylulerModu_FemaleVillager", ref added);
        AddVillagerType(spawn, creatureTypes, terrain, "KoylulerModu_Farmer", ref added);
        AddVillagerType(spawn, creatureTypes, terrain, "KoylulerModu_Blacksmith", ref added);
        AddVillagerType(spawn, creatureTypes, terrain, "KoylulerModu_Merchant", ref added);
        AddVillagerType(spawn, creatureTypes, terrain, "KoylulerModu_Healer", ref added);
        AddVillagerType(spawn, creatureTypes, terrain, "KoylulerModu_Guard", ref added);
        AddVillagerType(spawn, creatureTypes, terrain, "KoylulerModu_Hunter", ref added);
        AddVillagerType(spawn, creatureTypes, terrain, "KoylulerModu_Shepherd", ref added);

        Log.Information(LogPrefix + "Registered " + added + " villager surface-spawn types.");
    }

    private static void AddVillagerType(
        SubsystemCreatureSpawn spawn,
        List<SubsystemCreatureSpawn.CreatureType> creatureTypes,
        SubsystemTerrain terrain,
        string templateName,
        ref int added)
    {
        string spawnTypeName = templateName + "_Spawn";
        foreach (SubsystemCreatureSpawn.CreatureType existing in creatureTypes)
        {
            if (existing != null && existing.Name == spawnTypeName)
                return;
        }

        var type = new SubsystemCreatureSpawn.CreatureType(
            spawnTypeName,
            SpawnLocationType.Surface,
            randomSpawn: true,
            constantSpawn: false);

        type.SpawnSuitabilityFunction = (spawnType, point) =>
        {
            if (point.Y <= 1 || point.Y >= 120)
                return 0f;

            int ground = Terrain.ExtractContents(
                terrain.Terrain.GetCellValue(point.X, point.Y - 1, point.Z));
            if (ground != GrassBlock.Index && ground != DirtBlock.Index)
                return 0f;

            int space = Terrain.ExtractContents(
                terrain.Terrain.GetCellValue(point.X, point.Y, point.Z));
            if (space != 0)
                return 0f;

            // Keep the first character-only build intentionally low density.
            // Village-specific placement replaces this test spawn in a later stage.
            return 0.004f;
        };

        type.SpawnFunction = (spawnType, point) =>
            spawn.SpawnCreatures(spawnType, templateName, point, 1).Count;

        creatureTypes.Add(type);
        added++;
    }

    public override void OnLoadingFinished(List<Action> actions)
    {
        Log.Information(LogPrefix + "World loading finished. Villagers currently use only native movement/wandering; jobs, homes, schedules and trade are not enabled yet.");
    }
}