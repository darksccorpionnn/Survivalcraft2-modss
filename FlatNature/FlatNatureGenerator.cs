using System;

namespace Game;

internal static class FlatNatureGenerator
{
    private const int ChunkSize = 16;
    private const int ChunkHeight = 256;
    private const int MountainGrid = 190;
    private const int LakeGrid = 230;
    private const int ForestGrid = 150;
    private const int TreeGrid = 8;

    internal static void GenerateChunk(TerrainChunk chunk, FlatNatureWorldContext context)
    {
        if (chunk.Cells == null || context.RockIndex <= 0)
            return;

        int[,] surface = new int[ChunkSize, ChunkSize];
        bool[,] land = new bool[ChunkSize, ChunkSize];
        bool changed = false;

        // Normalize all underground land into continuous solid rock with a shallow soil cap.
        // We never carve caves or hollow underground spaces.
        for (int x = 0; x < ChunkSize; x++)
        {
            for (int z = 0; z < ChunkSize; z++)
            {
                int y = FindSurface(chunk, x, z);
                surface[x, z] = y;
                if (y < 5)
                    continue;

                int topContents = Terrain.ExtractContents(chunk.GetCellValueFast(x, y, z));
                if (topContents == 0 || (context.WaterIndex > 0 && topContents == context.WaterIndex))
                    continue;

                land[x, z] = true;
                NormalizeStrata(chunk, x, z, y, context, ref changed);
            }
        }

        // Lake basins are broad, shallow-to-deep depressions with solid beds and filled water.
        for (int x = 0; x < ChunkSize; x++)
        {
            for (int z = 0; z < ChunkSize; z++)
            {
                if (!land[x, z])
                    continue;

                int worldX = chunk.Origin.X + x;
                int worldZ = chunk.Origin.Y + z;
                if (GetMountain(context.Seed, worldX, worldZ).Height > 7)
                    continue;

                float lakeFactor = GetLakeFactor(context.Seed, worldX, worldZ);
                if (lakeFactor <= 0f || context.WaterIndex <= 0)
                    continue;

                ApplyLake(chunk, x, z, surface[x, z], lakeFactor, context, ref changed);
                land[x, z] = false;
            }
        }

        // Meandering rivers are generated as shallow, solid-bottom channels.
        for (int x = 0; x < ChunkSize; x++)
        {
            for (int z = 0; z < ChunkSize; z++)
            {
                if (!land[x, z] || context.WaterIndex <= 0)
                    continue;

                int worldX = chunk.Origin.X + x;
                int worldZ = chunk.Origin.Y + z;
                if (GetLakeFactor(context.Seed, worldX, worldZ) > 0f)
                    continue;

                float riverFactor = GetRiverFactor(context.Seed, worldX, worldZ);
                if (riverFactor <= 0f)
                    continue;

                ApplyRiver(chunk, x, z, surface[x, z], riverFactor, context, ref changed);
                land[x, z] = false;
            }
        }

        // Two separate feature profiles: green medium-height hills and tall snowy peaks.
        for (int x = 0; x < ChunkSize; x++)
        {
            for (int z = 0; z < ChunkSize; z++)
            {
                if (!land[x, z])
                    continue;

                int worldX = chunk.Origin.X + x;
                int worldZ = chunk.Origin.Y + z;
                MountainInfo mountain = GetMountain(context.Seed, worldX, worldZ);
                if (mountain.Height <= 0)
                    continue;

                int baseSurface = surface[x, z];
                int targetSurface = Math.Min(ChunkHeight - 4, baseSurface + mountain.Height);
                AddMountainColumn(chunk, x, z, baseSurface, targetSurface, mountain.Snowy, context, ref changed);
                surface[x, z] = targetSurface;
            }
        }

        // Local sandy biomes: broad dry patches with sand surface and subsurface.
        GenerateSandBiomes(chunk, surface, land, context, ref changed);

        // Ores only replace solid granite cells below the soil cap.
        GenerateOreVeins(chunk, context, ref changed);

        // Trees are grouped into woodland patches, leaving large open plain areas.
        GenerateForestTrees(chunk, surface, land, context, ref changed);

        // Deterministic village layout replaces sparse individual cottage spawns.
        if (VillageGenerator.GenerateChunk(chunk, context))
            changed = true;

        if (changed)
        {
            chunk.ModificationCounter++;
            chunk.InvalidateSliceContentsHashes();
        }
    }

    private static int FindSurface(TerrainChunk chunk, int x, int z)
    {
        int y = chunk.CalculateTopmostCellHeight(x, z);
        if (y >= ChunkHeight)
            y = ChunkHeight - 1;
        if (y < 1)
            y = 1;

        while (y > 1 && Terrain.ExtractContents(chunk.GetCellValueFast(x, y, z)) == 0)
            y--;
        return y;
    }

    private static void NormalizeStrata(
        TerrainChunk chunk, int x, int z, int surfaceY,
        FlatNatureWorldContext context, ref bool changed)
    {
        int rockValue = Terrain.MakeBlockValue(context.RockIndex);
        int dirtValue = Terrain.MakeBlockValue(context.DirtIndex);
        int grassValue = Terrain.MakeBlockValue(context.GrassIndex);

        for (int y = 1; y < surfaceY - 3; y++)
        {
            int contents = Terrain.ExtractContents(chunk.GetCellValueFast(x, y, z));
            if (contents != 0 && contents != context.WaterIndex && contents != context.RockIndex)
                SetCell(chunk, x, y, z, rockValue, ref changed);
        }

        for (int y = Math.Max(1, surfaceY - 3); y < surfaceY; y++)
        {
            int contents = Terrain.ExtractContents(chunk.GetCellValueFast(x, y, z));
            if (contents != 0 && contents != context.WaterIndex && contents != context.DirtIndex)
                SetCell(chunk, x, y, z, dirtValue, ref changed);
        }

        if (context.GrassIndex > 0)
            SetCell(chunk, x, surfaceY, z, grassValue, ref changed);
    }

    private static void ApplyRiver(
        TerrainChunk chunk, int x, int z, int surfaceY, float factor,
        FlatNatureWorldContext context, ref bool changed)
    {
        int waterY = Math.Max(2, surfaceY - 1);
        int floorY = Math.Max(1, waterY - 10);
        SetCell(chunk, x, floorY, z, Terrain.MakeBlockValue(context.SandIndex), ref changed);
        for (int y = floorY + 1; y <= waterY; y++)
            SetCell(chunk, x, y, z, Terrain.MakeBlockValue(context.WaterIndex), ref changed);
        for (int y = waterY + 1; y <= surfaceY; y++)
            SetCell(chunk, x, y, z, 0, ref changed);
    }

    private static void ApplyLake(
        TerrainChunk chunk, int x, int z, int surfaceY, float factor,
        FlatNatureWorldContext context, ref bool changed)
    {
        int waterY = Math.Max(11, surfaceY - 2);
        int floorY = Math.Max(1, waterY - 10);
        int floorBlock = context.SandIndex;

        // Keep the lake bottom solid, then fill every water cell up to the lake surface.
        SetCell(chunk, x, floorY, z, Terrain.MakeBlockValue(floorBlock), ref changed);
        for (int y = floorY + 1; y <= waterY; y++)
            SetCell(chunk, x, y, z, Terrain.MakeBlockValue(context.WaterIndex), ref changed);

        // This is only the normal open space above the water surface, never an underground void.
        for (int y = waterY + 1; y <= surfaceY; y++)
            SetCell(chunk, x, y, z, 0, ref changed);
    }

    private static void AddMountainColumn(
        TerrainChunk chunk, int x, int z, int baseSurface, int targetSurface,
        bool snowy, FlatNatureWorldContext context, ref bool changed)
    {
        int rockValue = Terrain.MakeBlockValue(context.RockIndex);
        int dirtValue = Terrain.MakeBlockValue(context.DirtIndex);
        int grassValue = Terrain.MakeBlockValue(context.GrassIndex);

        for (int y = baseSurface + 1; y <= targetSurface; y++)
            SetCell(chunk, x, y, z, rockValue, ref changed);

        if (snowy && context.SnowIndex > 0 && targetSurface >= baseSurface + 28)
        {
            int snowStart = Math.Max(baseSurface + 1, targetSurface - 2);
            for (int y = snowStart; y <= targetSurface; y++)
                SetCell(chunk, x, y, z, Terrain.MakeBlockValue(context.SnowIndex), ref changed);
        }
        else
        {
            // Keep the visible mountain crown soil/grass, not exposed stone.
            for (int y = Math.Max(baseSurface + 1, targetSurface - 5); y < targetSurface; y++)
                SetCell(chunk, x, y, z, dirtValue, ref changed);
            SetCell(chunk, x, targetSurface, z, grassValue, ref changed);
        }
    }

    private static void GenerateSandBiomes(
        TerrainChunk chunk, int[,] surface, bool[,] land,
        FlatNatureWorldContext context, ref bool changed)
    {
        if (context.SandIndex <= 0)
            return;

        for (int x = 0; x < ChunkSize; x++)
        for (int z = 0; z < ChunkSize; z++)
        {
            if (!land[x, z])
                continue;
            int wx = chunk.Origin.X + x;
            int wz = chunk.Origin.Y + z;
            int gx = FloorDiv(wx, 300);
            int gz = FloorDiv(wz, 300);
            bool desert = false;
            for (int ox = -1; ox <= 1 && !desert; ox++)
            for (int oz = -1; oz <= 1 && !desert; oz++)
            {
                uint h = Hash(context.Seed, gx + ox, gz + oz, 4101);
                if (h % 100u >= 32u)
                    continue;
                int cx = (gx + ox) * 300 + 40 + (int)((h >> 8) % 220u);
                int cz = (gz + oz) * 300 + 40 + (int)((h >> 16) % 220u);
                float radius = 65f + ((h >> 24) % 55u);
                float dx = wx - cx, dz = wz - cz;
                if (dx * dx + dz * dz < radius * radius)
                    desert = true;
            }
            if (!desert || GetMountain(context.Seed, wx, wz).Height > 8)
                continue;
            int top = surface[x, z];
            for (int y = Math.Max(1, top - 3); y <= top; y++)
                SetCell(chunk, x, y, z, Terrain.MakeBlockValue(context.SandIndex), ref changed);
            land[x, z] = false;
        }
    }

    private static void GenerateOreVeins(TerrainChunk chunk, FlatNatureWorldContext context, ref bool changed)
    {
        if (context.OreIndices.Count == 0 || context.TerrainLevel < 12)
            return;

        int maxY = Math.Min(72, context.TerrainLevel - 6);
        if (maxY < 8)
            return;

        for (int ore = 0; ore < context.OreIndices.Count; ore++)
        {
            uint h = Hash(context.Seed, chunk.Origin.X, chunk.Origin.Y, 700 + ore * 37);
            if (h % 100u >= 92u)
                continue;

            int centerX = 2 + (int)((h >> 8) % 12u);
            int centerZ = 2 + (int)((h >> 16) % 12u);
            int centerY = 4 + (int)((h >> 24) % (uint)Math.Max(1, maxY - 3));
            int length = 16 + (int)((h >> 4) % 11u);
            int direction = (int)((h >> 12) % 3u);
            int oreValue = Terrain.MakeBlockValue(context.OreIndices[ore]);

            for (int step = 0; step < length; step++)
            {
                int cx = centerX + (direction == 0 ? step / 2 : 0);
                int cy = centerY + (direction == 1 ? step / 2 : (step % 3 == 0 ? 1 : 0));
                int cz = centerZ + (direction == 2 ? step / 2 : 0);

                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) > 2)
                        continue;

                    int x = cx + dx;
                    int y = cy + dy;
                    int z = cz + dz;
                    if (x < 0 || x >= ChunkSize || z < 0 || z >= ChunkSize || y < 1 || y >= ChunkHeight)
                        continue;

                    if (Terrain.ExtractContents(chunk.GetCellValueFast(x, y, z)) == context.RockIndex)
                        SetCell(chunk, x, y, z, oreValue, ref changed);
                }
            }
        }
    }

    private static void GenerateForestTrees(
        TerrainChunk chunk, int[,] surface, bool[,] land,
        FlatNatureWorldContext context, ref bool changed)
    {
        if (context.WoodIndex <= 0 || context.LeavesIndex <= 0)
            return;

        int minTreeCellX = FloorDiv(chunk.Origin.X, TreeGrid) - 1;
        int maxTreeCellX = FloorDiv(chunk.Origin.X + ChunkSize - 1, TreeGrid) + 1;
        int minTreeCellZ = FloorDiv(chunk.Origin.Y, TreeGrid) - 1;
        int maxTreeCellZ = FloorDiv(chunk.Origin.Y + ChunkSize - 1, TreeGrid) + 1;

        for (int gx = minTreeCellX; gx <= maxTreeCellX; gx++)
        {
            for (int gz = minTreeCellZ; gz <= maxTreeCellZ; gz++)
            {
                uint h = Hash(context.Seed, gx, gz, 901);
                if (h % 100u >= 72u)
                    continue;

                int worldX = gx * TreeGrid + 2 + (int)((h >> 8) % 7u);
                int worldZ = gz * TreeGrid + 2 + (int)((h >> 16) % 7u);
                int localX = worldX - chunk.Origin.X;
                int localZ = worldZ - chunk.Origin.Y;
                if (localX < 1 || localX > 14 || localZ < 1 || localZ > 14)
                    continue;

                MountainInfo treeMountain = GetMountain(context.Seed, worldX, worldZ);
                if (GetForestFactor(context.Seed, worldX, worldZ) <= 0.015f ||
                    (treeMountain.Snowy && treeMountain.Height > 25) ||
                    GetLakeFactor(context.Seed, worldX, worldZ) > 0f ||
                    !land[localX, localZ])
                    continue;

                int groundY = surface[localX, localZ];
                if (groundY < 2 || groundY > ChunkHeight - 10)
                    continue;

                int treeHeight = 14 + (int)((h >> 20) % 7u);
                int saplingY = groundY + 1;
                int groundContents = Terrain.ExtractContents(chunk.GetCellValueFast(localX, groundY, localZ));
                int aboveContents = Terrain.ExtractContents(chunk.GetCellValueFast(localX, saplingY, localZ));
                if ((groundContents == context.GrassIndex || groundContents == context.DirtIndex) && aboveContents == 0)
                {
                    BuildSpruceTree(chunk, localX, groundY, localZ, treeHeight, context, ref changed);
                }
            }
        }
    }

    // Tall, broad, tiered evergreen matching the reference conifer silhouette.
    private static void BuildSpruceTree(
        TerrainChunk chunk, int x, int groundY, int z, int height,
        FlatNatureWorldContext context, ref bool changed)
    {
        int woodValue = Terrain.MakeBlockValue(context.TallSpruceWoodIndex > 0 ? context.TallSpruceWoodIndex : (context.TallSpruceWoodIndex > 0 ? context.TallSpruceWoodIndex : (context.PineWoodIndex > 0 ? context.PineWoodIndex : context.WoodIndex)));
        int leavesValue = Terrain.MakeBlockValue(context.TallSpruceLeavesIndex > 0 ? context.TallSpruceLeavesIndex : (context.TallSpruceLeavesIndex > 0 ? context.TallSpruceLeavesIndex : (context.PineLeavesIndex > 0 ? context.PineLeavesIndex : context.LeavesIndex)));

        // Keep a clearly visible, thick central trunk below the crown.
        for (int y = groundY + 1; y <= groundY + height; y++)
            SetCell(chunk, x, y, z, woodValue, ref changed);

        // The crown starts around one third of the way up the tree.
        // Overlapping irregular bough tiers create the full, layered pine shape.
        int crownStart = groundY + height / 3;
        int crownHeight = groundY + height;
        for (int y = crownStart; y <= crownHeight; y++)
        {
            float t = (y - crownStart) / (float)Math.Max(1, crownHeight - crownStart);
            int radius = Math.Max(1, (int)MathF.Round(4f * (1f - t) + 1f));
            int tierOffset = (y - crownStart) % 4;
            if (tierOffset == 3 && radius > 1)
                radius--;

            for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                float distance = MathF.Sqrt(dx * dx + dz * dz);
                float edgeNoise = ((Hash(context.Seed, x + dx, z + dz, y * 13) >> 5) & 3u) * 0.18f;
                if (distance > radius + edgeNoise)
                    continue;

                int lx = x + dx;
                int lz = z + dz;
                if (lx < 0 || lx >= ChunkSize || lz < 0 || lz >= ChunkSize ||
                    y < 1 || y >= ChunkHeight)
                    continue;

                // Preserve the trunk through the middle of the foliage.
                if (dx == 0 && dz == 0)
                    continue;

                if (Terrain.ExtractContents(chunk.GetCellValueFast(lx, y, lz)) == 0)
                    SetCell(chunk, lx, y, lz, leavesValue, ref changed);
            }
        }

        // A compact pointed top finishes the conical crown.
        for (int y = crownHeight - 2; y <= crownHeight + 1 && y < ChunkHeight; y++)
        {
            int radius = y >= crownHeight ? 0 : (y == crownHeight - 1 ? 1 : 2);
            for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                int lx = x + dx;
                int lz = z + dz;
                if (lx < 0 || lx >= ChunkSize || lz < 0 || lz >= ChunkSize)
                    continue;
                if (dx == 0 && dz == 0)
                    continue;
                if (Terrain.ExtractContents(chunk.GetCellValueFast(lx, y, lz)) == 0)
                    SetCell(chunk, lx, y, lz, leavesValue, ref changed);
            }
        }
    }

    private static void SetCell(TerrainChunk chunk, int x, int y, int z, int value, ref bool changed)
    {
        if (y < 0 || y >= ChunkHeight || chunk.GetCellValueFast(x, y, z) == value)
            return;

        chunk.SetCellValueFast(x, y, z, value);
        changed = true;
    }

    private static MountainInfo GetMountain(int seed, int x, int z)
    {
        int gx = FloorDiv(x, MountainGrid);
        int gz = FloorDiv(z, MountainGrid);
        float bestHeight = 0f;
        bool bestSnowy = false;

        for (int ox = -1; ox <= 1; ox++)
        {
            for (int oz = -1; oz <= 1; oz++)
            {
                int cellX = gx + ox;
                int cellZ = gz + oz;
                uint h = Hash(seed, cellX, cellZ, 1001);
                if (h % 100u >= 52u)
                    continue;

                int centerX = cellX * MountainGrid + 70 + (int)((h >> 8) % 200u);
                int centerZ = cellZ * MountainGrid + 70 + (int)((h >> 16) % 200u);
                float radius = 78f + ((h >> 24) % 65u);
                bool snowy = ((h >> 5) % 3u) == 0u;
                float peak = snowy ? 48f + ((h >> 12) % 42u) : 24f + ((h >> 12) % 30u);

                float dx = x - centerX;
                float dz = z - centerZ;
                float distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance >= radius)
                    continue;

                float u = 1f - distance / radius;
                float rough = (ValueNoise(x, z, 17, seed, 1002) - 0.5f) * 5f * u;
                float height = peak * MathF.Pow(u, 1.28f) + rough;
                if (height > bestHeight)
                {
                    bestHeight = height;
                    bestSnowy = snowy;
                }
            }
        }

        return new MountainInfo((int)MathF.Round(Math.Max(0f, bestHeight)), bestSnowy);
    }

    private static float GetLakeFactor(int seed, int x, int z)
    {
        int gx = FloorDiv(x, LakeGrid);
        int gz = FloorDiv(z, LakeGrid);
        float best = 0f;

        for (int ox = -1; ox <= 1; ox++)
        for (int oz = -1; oz <= 1; oz++)
        {
            int cellX = gx + ox;
            int cellZ = gz + oz;
            uint h = Hash(seed, cellX, cellZ, 2001);
            if (h % 100u >= 24u)
                continue;

            int centerX = cellX * LakeGrid + 65 + (int)((h >> 8) % 290u);
            int centerZ = cellZ * LakeGrid + 65 + (int)((h >> 16) % 290u);
            float radius = 48f + ((h >> 24) % 34u);
            float dx = x - centerX;
            float dz = z - centerZ;
            float distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance >= radius)
                continue;

            float factor = 1f - distance / radius;
            if (factor > best)
                best = factor;
        }

        return best;
    }

    private static float GetRiverFactor(int seed, int x, int z)
    {
        const int grid = 280;
        int gx = FloorDiv(x, grid);
        int gz = FloorDiv(z, grid);
        float best = 0f;

        for (int ox = -1; ox <= 1; ox++)
        for (int oz = -1; oz <= 1; oz++)
        {
            int cellX = gx + ox;
            int cellZ = gz + oz;
            uint h = Hash(seed, cellX, cellZ, 2501);
            if (h % 100u >= 42u)
                continue;

            int centerX = cellX * grid + 40 + (int)((h >> 8) % 200u);
            int centerZ = cellZ * grid + 40 + (int)((h >> 16) % 200u);
            bool horizontal = (h & 1u) == 0u;
            float along = horizontal ? Math.Abs(x - centerX) : Math.Abs(z - centerZ);
            if (along > 175f)
                continue;

            float wave = MathF.Sin(along / 62f + Unit(h) * 6.28f) * 17f;
            float offset = horizontal ? (z - centerZ - wave) : (x - centerX - wave);
            float width = 3.5f + ((h >> 24) % 4u);
            float distance = Math.Abs(offset);
            if (distance >= width)
                continue;

            float factor = 1f - distance / width;
            if (factor > best)
                best = factor;
        }
        return best;
    }

    private static float GetForestFactor(int seed, int x, int z)
    {
        int gx = FloorDiv(x, ForestGrid);
        int gz = FloorDiv(z, ForestGrid);
        float best = 0f;

        for (int ox = -1; ox <= 1; ox++)
        for (int oz = -1; oz <= 1; oz++)
        {
            int cellX = gx + ox;
            int cellZ = gz + oz;
            uint h = Hash(seed, cellX, cellZ, 3001);
            if (h % 100u >= 48u)
                continue;

            int centerX = cellX * ForestGrid + 35 + (int)((h >> 8) % 130u);
            int centerZ = cellZ * ForestGrid + 35 + (int)((h >> 16) % 130u);
            float radius = 42f + ((h >> 24) % 36u);
            float dx = x - centerX;
            float dz = z - centerZ;
            float distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance >= radius)
                continue;

            float factor = 1f - distance / radius;
            if (factor > best)
                best = factor;
        }

        return best;
    }

    private static float ValueNoise(int x, int z, int scale, int seed, int salt)
    {
        int gx = FloorDiv(x, scale);
        int gz = FloorDiv(z, scale);
        float tx = (x - gx * scale) / (float)scale;
        float tz = (z - gz * scale) / (float)scale;
        tx = tx * tx * (3f - 2f * tx);
        tz = tz * tz * (3f - 2f * tz);

        float a = Unit(Hash(seed, gx, gz, salt));
        float b = Unit(Hash(seed, gx + 1, gz, salt));
        float c = Unit(Hash(seed, gx, gz + 1, salt));
        float d = Unit(Hash(seed, gx + 1, gz + 1, salt));
        float top = a + (b - a) * tx;
        float bottom = c + (d - c) * tx;
        return top + (bottom - top) * tz;
    }

    private static float Unit(uint value) => (value & 0x00FFFFFFu) / 16777215f;

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
            h ^= h >> 16;
            return h;
        }
    }

    private static int FloorDiv(int value, int divisor)
    {
        int q = value / divisor;
        int r = value % divisor;
        return r < 0 ? q - 1 : q;
    }

    private readonly struct MountainInfo
    {
        internal readonly int Height;
        internal readonly bool Snowy;

        internal MountainInfo(int height, bool snowy)
        {
            Height = height;
            Snowy = snowy;
        }
    }
}