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
    internal int PineWoodIndex { get; private set; }
    internal int PineLeavesIndex { get; private set; }
    internal int TallSpruceWoodIndex { get; private set; }
    internal int TallSpruceLeavesIndex { get; private set; }
    internal int TallSpruceSaplingIndex { get; private set; }
    internal int LeavesIndex { get; private set; }
    internal List<int> TreeWoodIndices { get; } = new();
    internal List<int> TreeLeavesIndices { get; } = new();
    internal List<int> OreIndices { get; } = new();

    internal static FlatNatureWorldContext Create(SubsystemGameInfo gameInfo)
    {
        var context = new FlatNatureWorldContext
        {
            Seed = gameInfo.WorldSeed,
            TerrainLevel = gameInfo.WorldSettings.TerrainLevel,
            WaterIndex = gameInfo.WorldSettings.TerrainOceanBlockIndex
        };

        // Flat Nature uses granite as the continuous underground base rock.
        context.RockIndex = FindBlockByName(new[] { "Granite" });
        if (context.RockIndex == 0)
            context.RockIndex = FindBlockContaining("granite", "");
        if (context.RockIndex == 0)
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
            context.SandIndex = FindBlockByType("SandBlock");
        if (context.SandIndex == 0)
            context.SandIndex = context.DirtIndex;

        // Match "Tall Spruce Sapling" regardless of spaces or suffixes.
        context.TallSpruceWoodIndex = FindBlockNameNormalized("TallSpruceWood");
        if (context.TallSpruceWoodIndex == 0) context.TallSpruceWoodIndex = FindBlockTypeNormalized("TallSpruceWood");
        context.TallSpruceLeavesIndex = FindBlockNameNormalized("TallSpruceLeaves");
        if (context.TallSpruceLeavesIndex == 0) context.TallSpruceLeavesIndex = FindBlockTypeNormalized("TallSpruceLeaves");
        context.TallSpruceSaplingIndex = FindBlockNameNormalized("TallSpruceSapling");
        if (context.TallSpruceSaplingIndex == 0) context.TallSpruceSaplingIndex = FindBlockTypeNormalized("TallSpruceSapling");
        Console.WriteLine("[FlatNature] Tall Spruce sapling resolved index=" + context.TallSpruceSaplingIndex);

        context.PineWoodIndex = context.TallSpruceWoodIndex > 0
            ? context.TallSpruceWoodIndex : FindBlockContaining("Pine", "Wood");
        context.PineLeavesIndex = context.TallSpruceLeavesIndex > 0
            ? context.TallSpruceLeavesIndex : FindBlockContaining("Pine", "Leaves");

        context.WoodIndex = context.TallSpruceWoodIndex > 0 ? context.TallSpruceWoodIndex : (context.PineWoodIndex > 0 ? context.PineWoodIndex : FindBlockByType("WoodBlock"));
        if (context.WoodIndex == 0)
            context.WoodIndex = FindBlockByName(new[] { "OakWood", "Wood" });

        context.LeavesIndex = context.TallSpruceLeavesIndex > 0 ? context.TallSpruceLeavesIndex : (context.PineLeavesIndex > 0 ? context.PineLeavesIndex : FindBlockByType("LeavesBlock"));
        if (context.LeavesIndex == 0)
            context.LeavesIndex = FindBlockByName(new[] { "OakLeaves", "Leaves" });

        // Register actual tree species when the game exposes them as separate blocks.
        AddTreeSpecies(context, "Oak");
        AddTreeSpecies(context, "Pine");
        AddTreeSpecies(context, "Birch");
        AddTreeSpecies(context, "Acacia");
        AddTreeSpecies(context, "Spruce");
        if (context.TreeWoodIndices.Count == 0 && context.WoodIndex > 0)
        {
            context.TreeWoodIndices.Add(context.WoodIndex);
            context.TreeLeavesIndices.Add(context.LeavesIndex);
        }

        AddOre(context, "Coal");
        AddOre(context, "Copper");
        AddOre(context, "Iron");
        AddOre(context, "Gold");
        AddOre(context, "Diamond");
        AddOre(context, "Sulphur");
        AddOre(context, "Saltpeter");
        LogOreCandidates();

        Console.WriteLine("[FlatNature] Block lookup: rock=" + context.RockIndex +
            ", dirt=" + context.DirtIndex + ", grass=" + context.GrassIndex +
            ", snow=" + context.SnowIndex + ", water=" + context.WaterIndex +
            ", wood=" + context.WoodIndex + ", leaves=" + context.LeavesIndex +
             ", pineWood=" + context.PineWoodIndex + ", pineLeaves=" + context.PineLeavesIndex +
            ", tallSpruceWood=" + context.TallSpruceWoodIndex + ", tallSpruceLeaves=" + context.TallSpruceLeavesIndex +
            ", tallSpruceSapling=" + context.TallSpruceSaplingIndex +
            ", tallSpruceWood=" + context.TallSpruceWoodIndex + ", tallSpruceLeaves=" + context.TallSpruceLeavesIndex +
            ", ores=" + context.OreIndices.Count);

        return context;
    }

    private static void AddTreeSpecies(FlatNatureWorldContext context, string species)
    {
        int wood = FindBlockContaining(species, "wood");
        int leaves = FindBlockContaining(species, "leaves");
        if (wood > 0 && leaves > 0 &&
            !context.TreeWoodIndices.Contains(wood))
        {
            context.TreeWoodIndices.Add(wood);
            context.TreeLeavesIndices.Add(leaves);
            Console.WriteLine("[FlatNature] Tree species registered: " + species);
        }
    }

    private static void AddOre(FlatNatureWorldContext context, string mineral)
    {
        int index = FindBlockContaining(mineral, "ore");
        if (index == 0)
            index = FindBlockTypeContaining(mineral, "ore");
        if (index > 0 && !context.OreIndices.Contains(index))
        {
            context.OreIndices.Add(index);
            Console.WriteLine("[FlatNature] Ore registered: " + mineral + " = " + index);
        }
        else
        {
            Console.WriteLine("[FlatNature] Ore not found in block registry: " + mineral);
        }
    }

    private static int FindBlockTypeContaining(string firstToken, string secondToken)
    {
        foreach (KeyValuePair<Type, int> item in BlocksManager.BlockTypeToIndex)
        {
            string name = item.Key.Name;
            if (name.IndexOf(firstToken, StringComparison.OrdinalIgnoreCase) >= 0 &&
                name.IndexOf(secondToken, StringComparison.OrdinalIgnoreCase) >= 0 &&
                item.Value > 0)
                return item.Value;
        }
        return 0;
    }

    private static void LogOreCandidates()
    {
        foreach (KeyValuePair<string, int> item in BlocksManager.BlockNameToIndex)
        {
            if (item.Key.IndexOf("ore", StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.Key.IndexOf("granite", StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.Key.IndexOf("spruce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.Key.IndexOf("sapling", StringComparison.OrdinalIgnoreCase) >= 0)
                Console.WriteLine("[FlatNature] Registry candidate: " + item.Key + " = " + item.Value);
        }
        foreach (KeyValuePair<Type, int> item in BlocksManager.BlockTypeToIndex)
        {
            if (item.Key.Name.IndexOf("ore", StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.Key.Name.IndexOf("granite", StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.Key.Name.IndexOf("spruce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.Key.Name.IndexOf("sapling", StringComparison.OrdinalIgnoreCase) >= 0)
                Console.WriteLine("[FlatNature] Type candidate: " + item.Key.FullName + " = " + item.Value);
        }
    }

    private static string NormalizeBlockToken(string value)
    {
        var chars = new List<char>();
        foreach (char ch in value)
            if (char.IsLetterOrDigit(ch)) chars.Add(char.ToLowerInvariant(ch));
        return new string(chars.ToArray());
    }

    private static int FindBlockNameNormalized(string token)
    {
        string wanted = NormalizeBlockToken(token);
        foreach (KeyValuePair<string, int> item in BlocksManager.BlockNameToIndex)
            if (NormalizeBlockToken(item.Key).Contains(wanted) && item.Value > 0) return item.Value;
        return 0;
    }

    private static int FindBlockTypeNormalized(string token)
    {
        string wanted = NormalizeBlockToken(token);
        foreach (KeyValuePair<Type, int> item in BlocksManager.BlockTypeToIndex)
            if (NormalizeBlockToken(item.Key.Name).Contains(wanted) && item.Value > 0) return item.Value;
        return 0;
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
                (string.IsNullOrEmpty(secondToken) ||
                 item.Key.IndexOf(secondToken, StringComparison.OrdinalIgnoreCase) >= 0) &&
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