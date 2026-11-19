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
        int groundY = FindFlatGround(chunk, startX, startZ);
        if (groundY < 2 || groundY > 235)
            return;

        if (!CanPlace(chunk, startX, startZ, groundY))
            return;

        BuildHut(chunk, startX, startZ, groundY, plankIndex, logIndex, glassIndex);

        chunk.ModificationCounter++;
        chunk.InvalidateSliceContentsHashes();
        Console.WriteLine("[FlatNature] Wooden hut generated at " +
            (chunk.Origin.X + startX) + "," + groundY + "," + (chunk.Origin.Y + startZ));
    }

    private static int FindFlatGround(TerrainChunk chunk, int startX, int startZ)
    {
        int ground = -1;
        for (int x = startX; x < startX + HutSize; x++)
        for (int z = startZ; z < startZ + HutSize; z++)
        {
            int y = chunk.CalculateTopmostCellHeight(x, z);
            while (y > 1 && Terrain.ExtractContents(chunk.GetCellValueFast(x, y, z)) == 0)
                y--;

            if (y < 2)
                return -1;
            if (ground < 0)
                ground = y;
            else if (Math.Abs(y - ground) > 0)
                return -1;
        }
        return ground;
    }

    private static bool CanPlace(TerrainChunk chunk, int startX, int startZ, int groundY)
    {
        for (int x = startX; x < startX + HutSize; x++)
        for (int z = startZ; z < startZ + HutSize; z++)
        {
            int groundContents = Terrain.ExtractContents(chunk.GetCellValueFast(x, groundY, z));
            if (groundContents == 0 || groundContents == 18) // never build over air or water
                return false;

            for (int y = groundY + 1; y <= groundY + 6; y++)
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
            bool frontDoor = p >= 3 && p <= 3 && y <= wallBottom + 1;
            if (!frontDoor)
            {
                Put(chunk, sx + p, y, sz, plankIndex);
                Put(chunk, sx + p, y, sz + HutSize - 1, plankIndex);
                Put(chunk, sx, y, sz + p, plankIndex);
                Put(chunk, sx + HutSize - 1, y, sz + p, plankIndex);
            }
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

        // Simple pitched roof with overhang-free stepped profile.
        for (int x = 0; x < HutSize; x++)
        {
            Put(chunk, sx + x, roofY, sz + 1, plankIndex);
            Put(chunk, sx + x, roofY + 1, sz + 2, plankIndex);
            Put(chunk, sx + x, roofY + 1, sz + 3, plankIndex);
            Put(chunk, sx + x, roofY + 1, sz + 4, plankIndex);
            Put(chunk, sx + x, roofY, sz + 5, plankIndex);
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
        foreach (KeyValuePair<string, int> item in BlocksManager.BlockNameToIndex)
        {
            foreach (string token in tokens)
            {
                if (item.Key.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    item.Value > 0)
                    return item.Value;
            }
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
