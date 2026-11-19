using System;
using System.Collections.Generic;

namespace Game;

/// <summary>
/// Deterministic, chunk-safe wooden hut generation for new Flat Nature worlds.
/// The same placement routine can later be called by the manual blueprint item.
/// </summary>
internal static class WoodenHutGenerator
{
    private const int ChunkSize = 16;
    private const int HutSize = 7;
    private const int HutChancePercent = 7;

    internal static void GenerateChunk(TerrainChunk chunk, FlatNatureWorldContext context)
    {
        if (chunk == null || chunk.Cells == null || context == null)
            return;

        int plankIndex = FindBlock("WoodPlanks", "Planks", "OakPlanks", "PinePlanks");
        int woodIndex = context.WoodIndex;
        int glassIndex = FindBlock("Glass", "GlassBlock");
        int logIndex = woodIndex;
        if (plankIndex <= 0)
            plankIndex = woodIndex;
        if (plankIndex <= 0 || logIndex <= 0)
            return;

        uint hash = Hash(context.Seed, chunk.Origin.X / ChunkSize, chunk.Origin.Y / ChunkSize, 7319);
        if (hash % 100u >= HutChancePercent)
            return;

        // Keep the complete 7x7 hut inside this chunk. No cross-chunk writes.
        int startX = 4;
        int startZ = 4;
        int groundY = FindFlatGround(chunk, startX, startZ, context.WaterIndex);
        if (groundY < 2 || groundY > 246)
            return;

        if (!CanPlace(chunk, startX, startZ, groundY, context.WaterIndex))
            return;

        BuildHut(chunk, startX, startZ, groundY, plankIndex, logIndex, glassIndex);

        chunk.ModificationCounter++;
        chunk.InvalidateSliceContentsHashes();
        Console.WriteLine("[FlatNature] Wooden hut generated at " +
            (chunk.Origin.X + startX) + "," + groundY + "," + (chunk.Origin.Y + startZ));
    }


    /// <summary>
    /// Builds a hut centered on a placed kit. The kit occupies the center floor cell
    /// and is replaced only after the complete 7x7 footprint has passed validation.
    /// Uses ChangeCell so lighting, chunk invalidation and block notifications run.
    /// </summary>
    internal static bool TryBuildManual(
        SubsystemTerrain terrain, int anchorX, int anchorY, int anchorZ,
        int waterIndex, int kitIndex)
    {
        if (terrain == null || anchorY < 2 || anchorY > 247)
            return false;

        int plankIndex = FindBlock("WoodPlanks", "Planks", "OakPlanks", "PinePlanks");
        int logIndex = FindBlock("OakWood", "PineWood", "WoodBlock", "Wood");
        int glassIndex = FindBlock("Glass", "GlassBlock");
        if (plankIndex <= 0)
            plankIndex = logIndex;
        if (logIndex <= 0)
            logIndex = plankIndex;
        if (plankIndex <= 0 || logIndex <= 0)
            return false;

        int sx = anchorX - 3;
        int sz = anchorZ - 3;
        int groundY = anchorY - 1;

        // Require a level, solid, non-water 7x7 footprint.
        for (int x = sx; x < sx + HutSize; x++)
        for (int z = sz; z < sz + HutSize; z++)
        {
            int supportValue = terrain.Terrain.GetCellValue(x, groundY, z);
            int supportContents = Terrain.ExtractContents(supportValue);
            if (supportContents <= 0 || supportContents >= BlocksManager.Blocks.Length ||
                (waterIndex > 0 && supportContents == waterIndex))
                return false;

            Block supportBlock = BlocksManager.Blocks[supportContents];
            if (supportBlock == null || !supportBlock.IsCollidable_(supportValue))
                return false;

            for (int y = anchorY; y <= anchorY + 7; y++)
            {
                int value = terrain.Terrain.GetCellValue(x, y, z);
                int contents = Terrain.ExtractContents(value);
                bool isKitCell = x == anchorX && y == anchorY && z == anchorZ &&
                                 contents == kitIndex;
                if (contents != 0 && !isKitCell)
                    return false;
            }
        }

        BuildHutInTerrain(terrain, sx, anchorY, sz, plankIndex, logIndex, glassIndex);
        return true;
    }

    private static void BuildHutInTerrain(
        SubsystemTerrain terrain, int sx, int floorY, int sz,
        int plankIndex, int logIndex, int glassIndex)
    {
        int wallBottom = floorY + 1;
        int wallTop = floorY + 3;
        int roofY = floorY + 4;

        for (int x = 0; x < HutSize; x++)
        for (int z = 0; z < HutSize; z++)
            PutWorld(terrain, sx + x, floorY, sz + z, plankIndex);

        for (int y = wallBottom; y <= wallTop; y++)
        for (int p = 0; p < HutSize; p++)
        {
            bool frontDoor = p == 3 && y <= wallBottom + 1;
            if (!frontDoor)
                PutWorld(terrain, sx + p, y, sz, plankIndex);
            PutWorld(terrain, sx + p, y, sz + HutSize - 1, plankIndex);
            PutWorld(terrain, sx, y, sz + p, plankIndex);
            PutWorld(terrain, sx + HutSize - 1, y, sz + p, plankIndex);
        }

        for (int y = wallBottom; y <= roofY; y++)
        {
            PutWorld(terrain, sx, y, sz, logIndex);
            PutWorld(terrain, sx + 6, y, sz, logIndex);
            PutWorld(terrain, sx, y, sz + 6, logIndex);
            PutWorld(terrain, sx + 6, y, sz + 6, logIndex);
        }

        if (glassIndex > 0)
        {
            for (int y = wallBottom + 1; y <= wallBottom + 2; y++)
            {
                PutWorld(terrain, sx, y, sz + 2, glassIndex);
                PutWorld(terrain, sx, y, sz + 4, glassIndex);
                PutWorld(terrain, sx + 6, y, sz + 2, glassIndex);
                PutWorld(terrain, sx + 6, y, sz + 4, glassIndex);
            }
        }

        for (int x = 0; x < HutSize; x++)
        {
            PutWorld(terrain, sx + x, roofY, sz, plankIndex);
            PutWorld(terrain, sx + x, roofY + 1, sz + 1, plankIndex);
            PutWorld(terrain, sx + x, roofY + 2, sz + 2, plankIndex);
            PutWorld(terrain, sx + x, roofY + 3, sz + 3, plankIndex);
            PutWorld(terrain, sx + x, roofY + 2, sz + 4, plankIndex);
            PutWorld(terrain, sx + x, roofY + 1, sz + 5, plankIndex);
            PutWorld(terrain, sx + x, roofY, sz + 6, plankIndex);
        }
    }

    private static void PutWorld(SubsystemTerrain terrain, int x, int y, int z, int contents)
    {
        if (y < 1 || y >= 255 || contents <= 0)
            return;

        int value = Terrain.MakeBlockValue(contents);
        if (terrain.Terrain.GetCellValue(x, y, z) != value)
            terrain.ChangeCell(x, y, z, value);
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

            if (y < 2 || (waterIndex > 0 && Terrain.ExtractContents(chunk.GetCellValueFast(x, y, z)) == waterIndex))
                return -1;
            if (ground < 0)
                ground = y;
            else if (Math.Abs(y - ground) > 0)
                return -1;
        }
        return ground;
    }

    private static bool CanPlace(TerrainChunk chunk, int startX, int startZ, int groundY, int waterIndex)
    {
        for (int x = startX; x < startX + HutSize; x++)
        for (int z = startZ; z < startZ + HutSize; z++)
        {
            int groundValue = chunk.GetCellValueFast(x, groundY, z);
            int groundContents = Terrain.ExtractContents(groundValue);
            if (groundContents <= 0 || groundContents >= BlocksManager.Blocks.Length ||
                (waterIndex > 0 && groundContents == waterIndex))
                return false;

            Block groundBlock = BlocksManager.Blocks[groundContents];
            if (groundBlock == null || !groundBlock.IsCollidable_(groundValue))
                return false;

            for (int y = groundY + 1; y <= groundY + 8; y++)
            {
                if (Terrain.ExtractContents(chunk.GetCellValueFast(x, y, z)) != 0)
                    return false;
            }
        }
        return true;
    }

    private static void BuildHut(
        TerrainChunk chunk, int sx, int sz, int groundY,
        int plankIndex, int logIndex, int glassIndex)
    {
        int floorY = groundY + 1;
        int wallBottom = floorY + 1;
        int wallTop = floorY + 3;
        int roofY = floorY + 4;

        // Floor.
        for (int x = 0; x < HutSize; x++)
        for (int z = 0; z < HutSize; z++)
            Put(chunk, sx + x, floorY, sz + z, plankIndex);

        // Four walls, leaving a two-block entrance in the front.
        for (int y = wallBottom; y <= wallTop; y++)
        for (int p = 0; p < HutSize; p++)
        {
            bool frontDoor = p == 3 && y <= wallBottom + 1;
            if (!frontDoor)
                Put(chunk, sx + p, y, sz, plankIndex);
            Put(chunk, sx + p, y, sz + HutSize - 1, plankIndex);
            Put(chunk, sx, y, sz + p, plankIndex);
            Put(chunk, sx + HutSize - 1, y, sz + p, plankIndex);
        }

        // Corner posts.
        for (int y = wallBottom; y <= roofY; y++)
        {
            Put(chunk, sx, y, sz, logIndex);
            Put(chunk, sx + 6, y, sz, logIndex);
            Put(chunk, sx, y, sz + 6, logIndex);
            Put(chunk, sx + 6, y, sz + 6, logIndex);
        }

        // Windows on both side walls.
        if (glassIndex > 0)
        {
            for (int y = wallBottom + 1; y <= wallBottom + 2; y++)
            {
                Put(chunk, sx, y, sz + 2, glassIndex);
                Put(chunk, sx, y, sz + 4, glassIndex);
                Put(chunk, sx + 6, y, sz + 2, glassIndex);
                Put(chunk, sx + 6, y, sz + 4, glassIndex);
            }
        }

        // Symmetric pitched roof across the full 7-block width.
        for (int x = 0; x < HutSize; x++)
        {
            Put(chunk, sx + x, roofY, sz, plankIndex);
            Put(chunk, sx + x, roofY + 1, sz + 1, plankIndex);
            Put(chunk, sx + x, roofY + 2, sz + 2, plankIndex);
            Put(chunk, sx + x, roofY + 3, sz + 3, plankIndex);
            Put(chunk, sx + x, roofY + 2, sz + 4, plankIndex);
            Put(chunk, sx + x, roofY + 1, sz + 5, plankIndex);
            Put(chunk, sx + x, roofY, sz + 6, plankIndex);
        }
    }

    private static void Put(TerrainChunk chunk, int x, int y, int z, int contents)
    {
        if (x < 0 || x >= ChunkSize || z < 0 || z >= ChunkSize || y < 1 || y >= 255)
            return;

        int value = Terrain.MakeBlockValue(contents);
        if (chunk.GetCellValueFast(x, y, z) != value)
            chunk.SetCellValueFast(x, y, z, value);
    }

    private static int FindBlock(params string[] tokens)
    {
        // Prefer more specific names before generic substrings such as "Wood".
        foreach (string token in tokens)
        foreach (KeyValuePair<string, int> item in BlocksManager.BlockNameToIndex)
        {
            if (item.Value > 0 &&
                string.Equals(item.Key, token, StringComparison.OrdinalIgnoreCase))
                return item.Value;
        }

        foreach (string token in tokens)
        foreach (KeyValuePair<string, int> item in BlocksManager.BlockNameToIndex)
        {
            if (item.Value > 0 &&
                item.Key.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                return item.Value;
        }

        return 0;
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
}
