using System;
using System.Reflection;
using Engine;
using HarmonyLib;

namespace Game;

internal static class FlatNatureWorldMarkerPatch
{
    internal const string MarkerFileName = "FlatNature.created";
    private const string MarkerContents = "FlatNature world marker v2";

    internal static void Install(Harmony harmony)
    {
        // CreateWorld returns WorldInfo. Use its DirectoryName directly instead
        // of relying on a thread-static value captured from another method.
        MethodInfo createWorld = AccessTools.Method(
            typeof(WorldsManager),
            nameof(WorldsManager.CreateWorld),
            new[] { typeof(WorldSettings) });

        MethodInfo markPostfix = AccessTools.Method(
            typeof(FlatNatureWorldMarkerPatch),
            nameof(CreateWorldPostfix));

        if (createWorld == null || markPostfix == null)
            throw new InvalidOperationException("Could not locate WorldsManager.CreateWorld(WorldSettings).");

        harmony.Patch(createWorld, postfix: new HarmonyMethod(markPostfix));
        Console.WriteLine("[FlatNature] World creation marker patch installed: " + createWorld);
    }

    private static void CreateWorldPostfix(WorldSettings __0, WorldInfo __result)
    {
        if (__0 == null || __result == null || string.IsNullOrEmpty(__result.DirectoryName))
        {
            Console.WriteLine("[FlatNature] CreateWorld returned no world info; marker not written.");
            return;
        }

        bool isFlat = __0.TerrainGenerationMode == TerrainGenerationMode.FlatContinent ||
                      __0.TerrainGenerationMode == TerrainGenerationMode.FlatIsland;
        Console.WriteLine("[FlatNature] CreateWorld postfix: mode=" + __0.TerrainGenerationMode +
            ", directory=" + __result.DirectoryName + ", flat=" + isFlat);

        if (!isFlat)
            return;

        try
        {
            string markerPath = Storage.CombinePaths(new[] { __result.DirectoryName, MarkerFileName });
            Storage.WriteAllText(markerPath, MarkerContents);
            bool exists = Storage.FileExists(markerPath);
            Console.WriteLine("[FlatNature] Marker write " + (exists ? "verified" : "FAILED") +
                ": " + markerPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FlatNature] Could not mark newly created Flat world: " + ex);
        }
    }
}
