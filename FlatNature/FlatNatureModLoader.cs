using System;
using System.Collections.Generic;
using System.Threading;
using GameEntitySystem;
using HarmonyLib;

namespace Game;

public sealed class FlatNatureModLoader : ModLoader
{
    private static Harmony s_harmony;
    private static FlatNatureWorldContext s_context;

    public override void __ModInitialize()
    {
        try
        {
            s_harmony = new Harmony("darksccorpionnn.FlatNature.WorldMarker");
            FlatNatureWorldMarkerPatch.Install(s_harmony);

            ModsManager.RegisterHook("OnProjectLoaded", this);
            ModsManager.RegisterHook("OnLoadingFinished", this);
            ModsManager.RegisterHook("OnProjectDisposed", this);
            ModsManager.RegisterHook("OnTerrainContentsGenerated", this);
            ModsManager.RegisterHook("OnBlockDug", this);

            Console.WriteLine("[FlatNature] Initialized. Only newly created Flat worlds with the marker are affected.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FlatNature] Initialization failed: " + ex);
        }
    }

    public override void OnProjectLoaded(Project project)
    {
        ActivateForProject(project);
    }

    public override void OnLoadingFinished(List<Action> actions)
    {
        try
        {
            ActivateForProject(GameManager.Project);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FlatNature] Could not verify the loaded world: " + ex);
        }
    }

    public override void OnProjectDisposed()
    {
        Interlocked.Exchange(ref s_context, null);
        Console.WriteLine("[FlatNature] World context cleared.");
    }


    public override void OnBlockDug(
        ComponentMiner componentMiner,
        BlockPlacementData digValue,
        int cellValue,
        ref int durabilityReduction,
        ref bool mute,
        ref int playerDataDugAdd)
    {
        TreeCapitator.TryChop(componentMiner, cellValue);
    }

    public override void OnTerrainContentsGenerated(TerrainChunk chunk)
    {
        FlatNatureWorldContext context = Volatile.Read(ref s_context);
        if (context == null || chunk == null)
            return;

        try
        {
            FlatNatureGenerator.GenerateChunk(chunk, context);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FlatNature] Chunk generation failed at " + chunk.Origin + ": " + ex);
        }
    }

    private static void ActivateForProject(Project project)
    {
        if (project == null)
        {
            Interlocked.Exchange(ref s_context, null);
            return;
        }

        try
        {
            SubsystemGameInfo gameInfo = project.FindSubsystem<SubsystemGameInfo>(false);
            SubsystemTerrain subsystemTerrain = project.FindSubsystem<SubsystemTerrain>(false);
            if (gameInfo == null || subsystemTerrain == null || gameInfo.WorldSettings == null)
            {
                Interlocked.Exchange(ref s_context, null);
                return;
            }

            WorldSettings settings = gameInfo.WorldSettings;
            bool isFlat = settings.TerrainGenerationMode == TerrainGenerationMode.FlatContinent ||
                          settings.TerrainGenerationMode == TerrainGenerationMode.FlatIsland;
            if (!isFlat || !(subsystemTerrain.TerrainContentsGenerator is TerrainContentsGeneratorFlat))
            {
                Interlocked.Exchange(ref s_context, null);
                return;
            }

            string markerPath = Engine.Storage.CombinePaths(
                new[] { gameInfo.DirectoryName ?? string.Empty, FlatNatureWorldMarkerPatch.MarkerFileName });
            if (!Engine.Storage.FileExists(markerPath))
            {
                Interlocked.Exchange(ref s_context, null);
                return;
            }

            FlatNatureWorldContext context = FlatNatureWorldContext.Create(gameInfo);
            Interlocked.Exchange(ref s_context, context);
            Console.WriteLine("[FlatNature] Active in newly created Flat world '" + gameInfo.DirectoryName + "'.");
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref s_context, null);
            Console.WriteLine("[FlatNature] World marker check failed; generation remains disabled: " + ex);
        }
    }
}