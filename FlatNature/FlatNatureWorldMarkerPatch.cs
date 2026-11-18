using System;
using System.Reflection;
using Engine;
using HarmonyLib;

namespace Game;

internal static class FlatNatureWorldMarkerPatch
{
    internal const string MarkerFileName = "FlatNature.created";
    private const string MarkerContents = "FlatNature world marker v1";
    [ThreadStatic]
    private static string s_pendingDirectoryName;

    internal static void Install(Harmony harmony)
    {
        MethodInfo getUnusedDirectory = AccessTools.Method(
            typeof(WorldsManager), nameof(WorldsManager.GetUnusedWorldDirectoryName));
        MethodInfo createWorld = AccessTools.Method(
            typeof(WorldsManager), nameof(WorldsManager.CreateWorld),
            new[] { typeof(WorldSettings) });

        MethodInfo capturePostfix = AccessTools.Method(
            typeof(FlatNatureWorldMarkerPatch), nameof(CaptureDirectoryPostfix));
        MethodInfo markPostfix = AccessTools.Method(
            typeof(FlatNatureWorldMarkerPatch), nameof(CreateWorldPostfix));

        if (getUnusedDirectory == null || createWorld == null ||
            capturePostfix == null || markPostfix == null)
            throw new InvalidOperationException("Could not locate Survivalcraft world creation methods.");

        harmony.Patch(getUnusedDirectory, postfix: new HarmonyMethod(capturePostfix));
        harmony.Patch(createWorld, postfix: new HarmonyMethod(markPostfix));
    }

    private static void CaptureDirectoryPostfix(ref string __result)
    {
        s_pendingDirectoryName = __result;
    }

    private static void CreateWorldPostfix(WorldSettings worldSettings)
    {
        string directoryName = s_pendingDirectoryName;
        s_pendingDirectoryName = null;

        if (string.IsNullOrEmpty(directoryName) || worldSettings == null)
            return;

        bool isFlat = worldSettings.TerrainGenerationMode == TerrainGenerationMode.FlatContinent ||
                      worldSettings.TerrainGenerationMode == TerrainGenerationMode.FlatIsland;
        if (!isFlat)
            return;

        try
        {
            string markerPath = Storage.CombinePaths(new[] { directoryName, MarkerFileName });
            Storage.WriteAllText(markerPath, MarkerContents);
            Log.Information("[FlatNature] Marked newly created Flat world: " + directoryName);
        }
        catch (Exception ex)
        {
            Log.Error("[FlatNature] Could not mark the newly created Flat world: " + ex);
        }
    }
}