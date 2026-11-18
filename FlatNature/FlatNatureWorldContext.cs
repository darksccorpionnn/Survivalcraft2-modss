using System;
using System.Collections.Generic;

namespace Game;

internal sealed class FlatNatureWorldContext
{
    internal int Seed { get; private set; }
    internal int TerrainLevel { get; private set; }
    internal int WaterIndex { get; private set; }
    internal int RockIndex { get; private set; }
    internal int DirtIndex { get; private set; }
    internal int GrassIndex { get; private set; }
    internal int SnowIndex { get; private set; }
    internal int SandIndex { get; private set; }
    internal int WoodIndex { get; private set; }
    internal int LeavesIndex { get; private set; }
    internal List<int> OreIndices { get; } = new();

    internal static FlatNatureWorldContext Create(SubsystemGameInfo gameInfo)
    {
        var context = new FlatNatureWorldContext
        {
            Seed = gameInfo.WorldSeed,
            TerrainLevel = gameInfo.WorldSettings.TerrainLevel,
            WaterIndex = gameInfo.WorldSettings.TerrainOceanBlockIndex
        };

        context.RockIndex = FindBlockByName(new[] { "Stone", "Rock" });
        if (context.RockIndex == 0)
            context.RockIndex = FindBlockByType("StoneBlock");
        if (context.RockIndex == 0)
            context.RockIndex = gameInfo.WorldSettings.TerrainBlockIndex;

        context.DirtIndex = FindBlockByName(new[] { "Dirt", "Soil" });
        if (context.DirtIndex == 0)
            context.DirtIndex = FindBlockByType("DirtBlock");
        if (context.DirtIndex == 0)
            context.DirtIndex = context.RockIndex;

        context.GrassIndex = FindBlockByName(new[] { "Grass", "GrassBlock" });
        if (context.GrassIndex == 0)
            context.GrassIndex = FindBlockByType("GrassBlock");
        if (context.GrassIndex == 0)
            context.GrassIndex = context.DirtIndex;

        context.SnowIndex = FindBlockByName(new[] { "Snow", "SnowBlock", "Snowpack" });
        if (context.SnowIndex == 0)
            context.SnowIndex = FindBlockByType("SnowBlock");

        context.SandIndex = FindBlockByName(new[] { "Sand", "SandBlock" });
        if (context.SandIndex == 0)
            context.SandIndex = context.DirtIndex;

        context.WoodIndex = FindBlockByType("WoodBlock");
        if (context.WoodIndex == 0)
            context.WoodIndex = FindBlockByName(new[] { "OakWood", "Wood" });

        context.LeavesIndex = FindBlockByType("LeavesBlock");
        if (context.LeavesIndex == 0)
            context.LeavesIndex = FindBlockByName(new[] { "OakLeaves", "Leaves" });

        AddOre(context, "Coal");
        AddOre(context, "Copper");
        AddOre(context, "Iron");
        AddOre(context, "Gold");

        Console.WriteLine("[FlatNature] Block lookup: rock=" + context.RockIndex +
            ", dirt=" + context.DirtIndex + ", grass=" + context.GrassIndex +
            ", snow=" + context.SnowIndex + ", water=" + context.WaterIndex +
            ", wood=" + context.WoodIndex + ", leaves=" + context.LeavesIndex +
            ", ores=" + context.OreIndices.Count);

        return context;
    }

    private static void AddOre(FlatNatureWorldContext context, string mineral)
    {
        int index = FindBlockContaining(mineral, "ore");
        if (index > 0 && !context.OreIndices.Contains(index))
            context.OreIndices.Add(index);
    }

    private static int FindBlockByName(IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            if (BlocksManager.BlockNameToIndex.TryGetValue(name, out int index) && index > 0)
                return index;
        }
        return 0;
    }

    private static int FindBlockContaining(string firstToken, string secondToken)
    {
        foreach (KeyValuePair<string, int> item in BlocksManager.BlockNameToIndex)
        {
            if (item.Key.IndexOf(firstToken, StringComparison.OrdinalIgnoreCase) >= 0 &&
                item.Key.IndexOf(secondToken, StringComparison.OrdinalIgnoreCase) >= 0 &&
                item.Value > 0)
                return item.Value;
        }
        return 0;
    }

    private static int FindBlockByType(string typeToken)
    {
        foreach (KeyValuePair<Type, int> item in BlocksManager.BlockTypeToIndex)
        {
            if (item.Key.Name.IndexOf(typeToken, StringComparison.OrdinalIgnoreCase) >= 0 &&
                item.Value > 0)
                return item.Value;
        }
        return 0;
    }
}