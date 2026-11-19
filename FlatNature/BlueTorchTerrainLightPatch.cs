using System;
using System.Collections.Generic;
using System.Reflection;
using Engine;
using HarmonyLib;

namespace Game;

/// <summary>
/// Adds a local blue cast to terrain cube faces near registered blue torches.
/// The world's stored light remains scalar; this colors nearby rendered surfaces
/// so the blue flame appears to illuminate walls and floors.
/// </summary>
internal static class BlueTorchTerrainLightPatch
{
    private const float LightRadius = 8.0f;
    private const float MaximumTintStrength = 0.96f;

    private static readonly object s_sync = new();
    private static SubsystemTerrain s_terrain;
    private static readonly Dictionary<Point3, Vector3> s_sourcesByCell = new();
    private static volatile Vector3[] s_sourceSnapshot = Array.Empty<Vector3>();

    internal static void Install(Harmony harmony)
    {
        MethodInfo target = AccessTools.Method(
            typeof(BlockGeometryGenerator),
            nameof(BlockGeometryGenerator.GenerateCubeVertices),
            new[]
            {
                typeof(Block),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(Color),
                typeof(TerrainGeometrySubset[])
            });

        if (target == null)
            throw new InvalidOperationException("[FlatNature] BlockGeometryGenerator.GenerateCubeVertices was not found.");

        harmony.Patch(
            target,
            prefix: new HarmonyMethod(
                AccessTools.Method(typeof(BlueTorchTerrainLightPatch), nameof(GenerateCubeVerticesPrefix))),
            postfix: new HarmonyMethod(
                AccessTools.Method(typeof(BlueTorchTerrainLightPatch), nameof(GenerateCubeVerticesPostfix))));

        Log.Information("[FlatNature] Local blue terrain-light tint patch installed.");
    }

    internal static void Register(Point3 cell, Vector3 sourcePosition, SubsystemTerrain terrain)
    {
        lock (s_sync)
        {
            s_terrain = terrain;
            s_sourcesByCell[cell] = sourcePosition;
            RebuildSnapshot();
        }

        RefreshGeometryNear(sourcePosition);
    }

    internal static void Unregister(Point3 cell, SubsystemTerrain terrain)
    {
        Vector3? removedPosition = null;
        lock (s_sync)
        {
            if (s_sourcesByCell.TryGetValue(cell, out Vector3 oldPosition))
                removedPosition = oldPosition;

            if (s_sourcesByCell.Remove(cell))
                RebuildSnapshot();

            if (terrain != null)
                s_terrain = terrain;
        }

        if (removedPosition.HasValue)
            RefreshGeometryNear(removedPosition.Value);
    }

    private static void RefreshGeometryNear(Vector3 position)
    {
        SubsystemTerrain terrain = s_terrain;
        TerrainUpdater updater = terrain?.TerrainUpdater;
        if (terrain == null || updater == null)
            return;

        TerrainChunk chunk = terrain.Terrain.GetChunkAtCell(
            (int)MathF.Floor(position.X),
            (int)MathF.Floor(position.Z));

        if (chunk != null)
        {
            // A 1-chunk neighborhood covers the 4.8-block light radius even
            // when a torch sits close to a chunk edge. Force slice hashes to
            // invalidate so already-built neighboring faces are regenerated.
            updater.DowngradeChunkNeighborhoodState(
                chunk.Coords, 1, TerrainChunkState.InvalidVertices1, true);
        }
    }

    private static void RebuildSnapshot()
    {
        Vector3[] snapshot = new Vector3[s_sourcesByCell.Count];
        s_sourcesByCell.Values.CopyTo(snapshot, 0);
        s_sourceSnapshot = snapshot;
    }

    // Only vertices appended by this block-generation call are modified.
    private static void GenerateCubeVerticesPrefix(
        TerrainGeometrySubset[] subsetsByFace,
        out int[] __state)
    {
        if (subsetsByFace == null)
        {
            __state = null;
            return;
        }

        __state = new int[subsetsByFace.Length];
        for (int i = 0; i < subsetsByFace.Length; i++)
            __state[i] = subsetsByFace[i]?.Vertices?.Count ?? 0;
    }

    private static void GenerateCubeVerticesPostfix(
        TerrainGeometrySubset[] subsetsByFace,
        int[] __state)
    {
        if (subsetsByFace == null || __state == null || s_sourceSnapshot.Length == 0)
            return;

        for (int subsetIndex = 0;
             subsetIndex < subsetsByFace.Length && subsetIndex < __state.Length;
             subsetIndex++)
        {
            TerrainGeometrySubset subset = subsetsByFace[subsetIndex];
            if (subset?.Vertices == null)
                continue;

            var vertices = subset.Vertices;
            int start = Math.Max(0, __state[subsetIndex]);
            int end = vertices.Count;
            if (start >= end || vertices.Array == null)
                continue;

            for (int vertexIndex = start; vertexIndex < end; vertexIndex++)
            {
                TerrainVertex vertex = vertices.Array[vertexIndex];
                float strength = CalculateTintStrength(vertex.X, vertex.Y, vertex.Z);
                if (strength <= 0.001f)
                    continue;

                vertex.Color = ApplyBlueTint(vertex.Color, strength);
                vertices.Array[vertexIndex] = vertex;
            }
        }
    }

    private static float CalculateTintStrength(float x, float y, float z)
    {
        Vector3[] sources = s_sourceSnapshot;
        float bestStrength = 0f;
        float radiusSquared = LightRadius * LightRadius;

        for (int i = 0; i < sources.Length; i++)
        {
            float dx = x - sources[i].X;
            float dy = y - sources[i].Y;
            float dz = z - sources[i].Z;
            float distanceSquared = dx * dx + dy * dy + dz * dz;
            if (distanceSquared >= radiusSquared)
                continue;

            float falloff = 1f - MathF.Sqrt(distanceSquared) / LightRadius;
            float strength = falloff * (0.35f + 0.65f * falloff) * MaximumTintStrength;
            if (strength > bestStrength)
                bestStrength = strength;
        }

        return bestStrength;
    }

    private static Color ApplyBlueTint(Color original, float strength)
    {
        // Preserve texture detail while reducing red/green and raising blue.
        float red = original.R * (1f - 0.88f * strength);
        float green = original.G * (1f - 0.92f * strength);
        float blue = original.B + (255f - original.B) * (0.62f * strength);

        return new Color(
            (byte)Math.Clamp((int)MathF.Round(red), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(green), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(blue), 0, 255),
            original.A);
    }
}
