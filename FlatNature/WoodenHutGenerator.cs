using System;
using System.Collections.Generic;

namespace Game;

/// <summary>
/// Sparse deterministic wooden cottage generation and reusable manual placement.
/// Structure placement is confined to one chunk during world generation.
/// </summary>
internal static class WoodenHutGenerator
{
    private const int ChunkSize = 16;
    private const int HutSize = 9;
    private const int RegionSize = 96;
    private const int ChunksPerRegion = RegionSize / ChunkSize;
    private const int RegionSpawnChancePercent = 30;

    internal static bool GenerateChunk(TerrainChunk chunk, FlatNatureWorldContext context)
    {
        if (chunk?.Cells == null || context == null)
            return false;

        // One deterministic candidate per 96x96 region, not a roll in every chunk.
        int regionX = FloorDiv(chunk.Origin.X, RegionSize);
        int regionZ = FloorDiv(chunk.Origin.Y, RegionSize);
        uint hash = Hash(context.Seed, regionX, regionZ, 7319);
        if (hash % 100u >= RegionSpawnChancePercent)
            return false;

        int ownerChunkX = regionX * ChunksPerRegion + (int)((hash >> 8) % ChunksPerRegion);
        int ownerChunkZ = regionZ * ChunksPerRegion + (int)((hash >> 16) % ChunksPerRegion);
        if (FloorDiv(chunk.Origin.X, ChunkSize) != ownerChunkX ||
            FloorDiv(chunk.Origin.Y, ChunkSize) != ownerChunkZ)
            return false;

        if (!TryGetMaterials(out HutMaterials materials))
            return false;

        // Keep the full 9x9 house inside the owning 16x16 chunk.
        int startX = 3 + (int)((hash >> 24) % 5u);
        int startZ = 3 + (int)((Hash(context.Seed, regionX, regionZ, 7321) >> 8) % 5u);
        int groundY = FindFlatGround(chunk, startX, startZ, context.WaterIndex);
        if (groundY < 2 || groundY > 243 ||
            !CanPlace(chunk, startX, startZ, groundY, context.WaterIndex))
            return false;

        BuildHut(
            startX, groundY + 1, startZ, materials,
            (x, y, z, value) => PutChunk(chunk, x, y, z, value));

        Console.WriteLine("[FlatNature] Wooden cottage generated at " +
            (chunk.Origin.X + startX) + "," + groundY + "," + (chunk.Origin.Y + startZ));
        return true;
    }

    /// <summary>
    /// Places the same cottage from a ground-placed kit. The kit marks the center
    /// floor cell; the whole footprint is validated before any terrain is changed.
    /// </summary>
    internal static bool TryBuildManual(
        SubsystemTerrain terrain, int anchorX, int anchorY, int anchorZ,
        int waterIndex, int kitIndex)
    {
        if (terrain == null || anchorY < 2 || anchorY > 245 ||
            !TryGetMaterials(out HutMaterials materials))
            return false;

        int startX = anchorX - HutSize / 2;
        int startZ = anchorZ - HutSize / 2;
        int groundY = anchorY - 1;

        for (int x = startX; x < startX + HutSize; x++)
        for (int z = startZ; z < startZ + HutSize; z++)
        {
            int supportValue = terrain.Terrain.GetCellValue(x, groundY, z);
            int supportContents = Terrain.ExtractContents(supportValue);
            if (supportContents <= 0 || supportContents >= BlocksManager.Blocks.Length ||
                (waterIndex > 0 && supportContents == waterIndex))
                return false;

            Block supportBlock = BlocksManager.Blocks[supportContents];
            if (supportBlock == null || !supportBlock.IsCollidable_(supportValue))
                return false;

            for (int y = anchorY; y <= anchorY + 9; y++)
            {
                int value = terrain.Terrain.GetCellValue(x, y, z);
                int contents = Terrain.ExtractContents(value);
                bool isKit = x == anchorX && y == anchorY && z == anchorZ &&
                             contents == kitIndex;
                if (contents != 0 && !isKit)
                    return false;
            }
        }

        BuildHut(
            startX, anchorY, startZ, materials,
            (x, y, z, value) => PutWorld(terrain, x, y, z, value));
        return true;
    }

    private static void BuildHut(
        int sx, int floorY, int sz, HutMaterials m,
        Action<int, int, int, int> place)
    {
        int wallBottom = floorY + 1;
        int wallTop = floorY + 3;
        int roofBase = wallTop + 1;

        // Stone foundation border and warm plank floor.
        for (int x = 0; x < HutSize; x++)
        for (int z = 0; z < HutSize; z++)
        {
            bool border = x == 0 || x == HutSize - 1 || z == 0 || z == HutSize - 1;
            place(sx + x, floorY, sz + z, border ? m.StoneValue : m.PlankValue);
        }

        // Main walls with a real two-block wooden door and glass windows.
        for (int y = wallBottom; y <= wallTop; y++)
        {
            for (int p = 1; p < HutSize - 1; p++)
            {
                bool doorOpening = p == 4 && y <= wallBottom + 1;
                if (!doorOpening)
                {
                    int frontValue = (y == wallBottom + 1 && (p == 2 || p == 6))
                        ? m.GlassValue : m.PlankValue;
                    int rearValue = (y == wallBottom + 1 && (p == 2 || p == 6))
                        ? m.GlassValue : m.PlankValue;
                    place(sx + p, y, sz, frontValue);
                    place(sx + p, y, sz + HutSize - 1, rearValue);
                }

                int sideValue = (y == wallBottom + 1 && (p == 3 || p == 5))
                    ? m.GlassValue : m.PlankValue;
                place(sx, y, sz + p, sideValue);
                place(sx + HutSize - 1, y, sz + p, sideValue);
            }
        }

        // Log corner posts and horizontal timber beams.
        for (int y = wallBottom; y <= wallTop; y++)
        {
            place(sx, y, sz, m.LogValue);
            place(sx + 8, y, sz, m.LogValue);
            place(sx, y, sz + 8, m.LogValue);
            place(sx + 8, y, sz + 8, m.LogValue);
        }
        for (int p = 0; p < HutSize; p++)
        {
            place(sx + p, wallTop, sz, m.LogValue);
            place(sx + p, wallTop, sz + 8, m.LogValue);
        }

        // Gable triangles at the front and rear ends.
        for (int x = 1; x < HutSize - 1; x++)
        {
            int rise = 4 - Math.Abs(4 - x);
            for (int y = wallTop + 1; y < roofBase + rise; y++)
            {
                place(sx + x, y, sz, m.PlankValue);
                place(sx + x, y, sz + 8, m.PlankValue);
            }
        }

        // Actual two-cell wooden door. DoorBlock uses rotation data 0 (closed).
        if (m.DoorValue != 0)
        {
            place(sx + 4, wallBottom, sz, m.DoorValue);
            place(sx + 4, wallBottom + 1, sz, m.DoorValue);
        }

        // Thick stone gable roof, with ridge running from front to rear.
        for (int x = 0; x < HutSize; x++)
        {
            int rise = 4 - Math.Abs(4 - x);
            int roofY = roofBase + rise;
            for (int z = 0; z < HutSize; z++)
                place(sx + x, roofY, sz + z, m.RoofValue);
        }

        // A short stone chimney near the rear corner.
        for (int y = roofBase + 1; y <= roofBase + 4; y++)
            place(sx + 7, y, sz + 7, m.RoofValue);

        // Furnished interior: crafting table, working wood stove and wall torches.
        if (m.CraftingTableValue != 0)
            place(sx + 2, floorY + 1, sz + 2, m.CraftingTableValue);
        if (m.StoveValue != 0)
            place(sx + 6, floorY + 1, sz + 6, m.StoveValue);
        if (m.TorchWestValue != 0)
            place(sx + 1, wallBottom + 1, sz + 4, m.TorchWestValue);
        if (m.TorchEastValue != 0)
            place(sx + 7, wallBottom + 1, sz + 4, m.TorchEastValue);
    }

    private static bool TryGetMaterials(out HutMaterials m)
    {
        int plank = FindBlock("WoodPlanks", "Planks", "OakPlanks", "PinePlanks");
        int log = FindBlock("OakWood", "PineWood", "Wood");
        int stone = FindBlock("Cobblestone", "StoneBricks", "StoneBrick", "Stone");
        int glass = FindBlock("Glass", "GlassBlock");
        int door = FindBlockType("WoodenDoorBlock");
        if (door <= 0)
            door = FindBlockType("DoorBlock");
        int torch = FindBlockType("TorchBlock");
        int table = FindBlockType("CraftingTableBlock");
        int stove = FindBlockType("WoodStoveBlock");

        if (plank <= 0) plank = log;
        if (log <= 0) log = plank;
        if (stone <= 0) stone = plank;

        // The door is required for this design; don't silently build a doorless house.
        if (plank <= 0 || log <= 0 || stone <= 0 || door <= 0 || torch <= 0 || table <= 0)
        {
            m = default;
            return false;
        }

        int torchValue = Terrain.MakeBlockValue(torch);
        m = new HutMaterials
        {
            PlankValue = Terrain.MakeBlockValue(plank),
            LogValue = Terrain.MakeBlockValue(log),
            StoneValue = Terrain.MakeBlockValue(stone),
            RoofValue = Terrain.MakeBlockValue(stone),
            GlassValue = glass > 0 ? Terrain.MakeBlockValue(glass) : Terrain.MakeBlockValue(plank),
            DoorValue = Terrain.MakeBlockValue(door),
            TorchWestValue = Terrain.ReplaceData(torchValue, 1),
            TorchEastValue = Terrain.ReplaceData(torchValue, 3),
            CraftingTableValue = Terrain.MakeBlockValue(table),
            StoveValue = stove > 0 ? Terrain.MakeBlockValue(stove) : 0
        };
        return true;
    }

    private static int FindFlatGround(TerrainChunk chunk, int startX, int startZ, int waterIndex)
    {
        int ground = -1;
        for (int x = startX; x < startX + HutSize; x++)
        for (int z = startZ; z < startZ + HutSize; z++)
        {
            int y = chunk.CalculateTopmostCellHeight(x, z);
            while (y > 1 && Terrain.ExtractContents(chunk.GetCellValueFast(x, y, z)) == 0)
                y--;

            int value = chunk.GetCellValueFast(x, y, z);
            if (y < 2 || Terrain.ExtractContents(value) == waterIndex)
                return -1;
            if (ground < 0)
                ground = y;
            else if (y != ground)
                return -1;
        }
        return ground;
    }

    private static bool CanPlace(TerrainChunk chunk, int sx, int sz, int groundY, int waterIndex)
    {
        for (int x = sx; x < sx + HutSize; x++)
        for (int z = sz; z < sz + HutSize; z++)
        {
            int groundValue = chunk.GetCellValueFast(x, groundY, z);
            int contents = Terrain.ExtractContents(groundValue);
            if (contents <= 0 || contents >= BlocksManager.Blocks.Length ||
                (waterIndex > 0 && contents == waterIndex))
                return false;

            Block block = BlocksManager.Blocks[contents];
            if (block == null || !block.IsCollidable_(groundValue))
                return false;

            for (int y = groundY + 1; y <= groundY + 10; y++)
                if (Terrain.ExtractContents(chunk.GetCellValueFast(x, y, z)) != 0)
                    return false;
        }
        return true;
    }

    private static void PutChunk(TerrainChunk chunk, int x, int y, int z, int value)
    {
        if (x < 0 || x >= ChunkSize || z < 0 || z >= ChunkSize || y < 1 || y >= 255)
            return;
        if (chunk.GetCellValueFast(x, y, z) != value)
            chunk.SetCellValueFast(x, y, z, value);
    }

    private static void PutWorld(SubsystemTerrain terrain, int x, int y, int z, int value)
    {
        if (y < 1 || y >= 255 || value == 0)
            return;
        if (terrain.Terrain.GetCellValue(x, y, z) != value)
            terrain.ChangeCell(x, y, z, value);
    }

    private static int FindBlock(params string[] names)
    {
        foreach (string name in names)
        foreach (KeyValuePair<string, int> item in BlocksManager.BlockNameToIndex)
            if (item.Value > 0 && string.Equals(item.Key, name, StringComparison.OrdinalIgnoreCase))
                return item.Value;

        foreach (string name in names)
        foreach (KeyValuePair<string, int> item in BlocksManager.BlockNameToIndex)
            if (item.Value > 0 && item.Key.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                return item.Value;
        return 0;
    }

    private static int FindBlockType(string typeToken)
    {
        foreach (KeyValuePair<Type, int> item in BlocksManager.BlockTypeToIndex)
            if (item.Value > 0 && item.Key.Name.IndexOf(typeToken, StringComparison.OrdinalIgnoreCase) >= 0)
                return item.Value;
        return 0;
    }

    private static int FloorDiv(int value, int divisor)
    {
        int q = value / divisor;
        int r = value % divisor;
        return r < 0 ? q - 1 : q;
    }

    private static uint Hash(int seed, int x, int z, int salt)
    {
        unchecked
        {
            uint h = (uint)seed;
            h ^= (uint)x * 0x9E3779B9u;
            h = (h ^ (h >> 16)) * 0x85EBCA6Bu;
            h ^= (uint)z * 0xC2B2AE35u;
            h = (h ^ (h >> 13)) * 0x27D4EB2Fu;
            h ^= (uint)salt * 0x165667B1u;
            return h ^ (h >> 16);
        }
    }

    private struct HutMaterials
    {
        public int PlankValue;
        public int LogValue;
        public int StoneValue;
        public int RoofValue;
        public int GlassValue;
        public int DoorValue;
        public int TorchWestValue;
        public int TorchEastValue;
        public int CraftingTableValue;
        public int StoveValue;
    }
}
