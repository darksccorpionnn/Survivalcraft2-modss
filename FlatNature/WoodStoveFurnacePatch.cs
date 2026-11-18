using System;
using System.Reflection;
using Engine;
using GameEntitySystem;
using HarmonyLib;

namespace Game;

/// <summary>
/// Keeps the wood stove as a heater, not a smelting furnace.
/// Only the native fuel slot accepts fuel; recipe lookup is disabled.
/// </summary>
internal static class WoodStoveFurnacePatch
{
    internal static void Install(Harmony harmony)
    {
        MethodInfo findRecipe = AccessTools.Method(
            typeof(ComponentFurnace),
            nameof(ComponentFurnace.FindSmeltingRecipe),
            new[] { typeof(float) });
        MethodInfo slotCapacity = AccessTools.Method(
            typeof(ComponentFurnace),
            nameof(ComponentFurnace.GetSlotCapacity),
            new[] { typeof(int), typeof(int) });

        if (findRecipe == null || slotCapacity == null)
            throw new InvalidOperationException("[FlatNature] Could not locate native furnace recipe/slot methods.");

        harmony.Patch(
            findRecipe,
            prefix: new HarmonyMethod(
                AccessTools.Method(typeof(WoodStoveFurnacePatch), nameof(FindSmeltingRecipePrefix))));
        harmony.Patch(
            slotCapacity,
            prefix: new HarmonyMethod(
                AccessTools.Method(typeof(WoodStoveFurnacePatch), nameof(GetSlotCapacityPrefix))));

        Log.Information("[FlatNature] Stove heating-only inventory patch installed.");
    }

    private static bool FindSmeltingRecipePrefix(
        ComponentFurnace __instance,
        float heatLevel,
        ref CraftingRecipe __result)
    {
        if (!IsWoodStove(__instance))
            return true;

        __result = null;
        return false;
    }

    private static bool GetSlotCapacityPrefix(
        ComponentFurnace __instance,
        int slotIndex,
        int value,
        ref int __result)
    {
        if (!IsWoodStove(__instance))
            return true;

        // ComponentFurnace places fuel after its input slots. For the stove,
        // all input/output/remains slots are disabled.
        if (slotIndex != __instance.FuelSlotIndex)
        {
            __result = 0;
            return false;
        }

        // Empty-slot queries must retain the native capacity. A non-empty
        // candidate is accepted only when the game identifies it as fuel.
        if (value != 0)
        {
            int contents = Terrain.ExtractContents(value);
            if (contents <= 0 ||
                contents >= BlocksManager.Blocks.Length ||
                BlocksManager.Blocks[contents].GetFuelHeatLevel(value) <= 0f)
            {
                __result = 0;
                return false;
            }
        }

        return true;
    }

    private static bool IsWoodStove(ComponentFurnace furnace)
    {
        ComponentBlockEntity blockEntity = furnace?.m_componentBlockEntity;
        if (blockEntity == null)
            return false;

        int stoveIndex = BlocksManager.GetBlockIndex<WoodStoveBlock>();
        if (stoveIndex <= 0)
            return false;

        Point3 cell = blockEntity.Coordinates;
        Project project = GameManager.Project;
        SubsystemTerrain terrain = project?.FindSubsystem<SubsystemTerrain>(false);
        if (terrain == null)
            return false;

        int value = terrain.Terrain.GetCellValue(cell.X, cell.Y, cell.Z);
        return Terrain.ExtractContents(value) == stoveIndex;
    }
}
