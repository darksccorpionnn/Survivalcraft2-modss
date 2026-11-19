using System;
using System.Collections.Generic;
using Engine;
using TemplatesDatabase;

namespace Game;

/// <summary>
/// Dedicated torch behavior for BlueTorchBlock.
/// It keeps the blue flame, adds native soft-blue glow sprites and registers
/// the torch as a local color source for terrain vertex tinting.
/// </summary>
public sealed class BlueTorchBlockBehavior : SubsystemTorchBlockBehavior
{
    private SubsystemTerrain m_terrain;
    private SubsystemGlow m_subsystemGlow;
    private readonly Dictionary<Point3, GlowPoint[]> m_glowPointsByCell = new();

    public override int[] HandledBlocks
    {
        get
        {
            int index = BlocksManager.GetBlockIndex<BlueTorchBlock>();
            return index > 0 ? new[] { index } : Array.Empty<int>();
        }
    }

    public override void Load(ValuesDictionary valuesDictionary)
    {
        base.Load(valuesDictionary);
        m_terrain = Project.FindSubsystem<SubsystemTerrain>(true);
        m_subsystemGlow = Project.FindSubsystem<SubsystemGlow>(true);
    }

    public override void OnBlockAdded(int value, int oldValue, int x, int y, int z)
    {
        AddBlueTorch(value, x, y, z);
    }

    public override void OnBlockGenerated(int value, int x, int y, int z, bool isLoaded)
    {
        AddBlueTorch(value, x, y, z);
    }

    public override void OnBlockModified(int value, int oldValue, int x, int y, int z)
    {
        RemoveBlueTorch(x, y, z);
        AddBlueTorch(value, x, y, z);
    }

    public override void OnBlockRemoved(int value, int newValue, int x, int y, int z)
    {
        RemoveBlueTorch(x, y, z);
    }

    public override void OnChunkDiscarding(TerrainChunk chunk)
    {
        // Remove the extra glow visuals and tint registrations for this chunk.
        List<Point3> cellsToRemove = new();
        foreach (Point3 cell in m_glowPointsByCell.Keys)
        {
            if (cell.X >= chunk.Origin.X && cell.X < chunk.Origin.X + TerrainChunk.Size &&
                cell.Z >= chunk.Origin.Y && cell.Z < chunk.Origin.Y + TerrainChunk.Size)
            {
                cellsToRemove.Add(cell);
            }
        }

        foreach (Point3 cell in cellsToRemove)
            RemoveGlowForCell(cell);

        // Let the native behavior clean up the blue flame particle systems.
        base.OnChunkDiscarding(chunk);
    }

    public override void OnNeighborBlockChanged(
        int x, int y, int z, int neighborX, int neighborY, int neighborZ)
    {
        int value = m_terrain.Terrain.GetCellValueFast(x, y, z);
        if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<BlueTorchBlock>())
            return;

        int face = Terrain.ExtractData(value);
        if (face < 0 || face > 4)
        {
            m_terrain.DestroyCell(0, x, y, z, 0, false, false, null);
            return;
        }

        Point3 faceOffset = CellFace.FaceToPoint3(face);
        int supportX = x - faceOffset.X;
        int supportY = y - faceOffset.Y;
        int supportZ = z - faceOffset.Z;
        int supportValue = m_terrain.Terrain.GetCellValueFast(supportX, supportY, supportZ);
        int supportContents = Terrain.ExtractContents(supportValue);
        Block[] blocks = BlocksManager.Blocks;

        bool supported =
            supportContents >= 0 &&
            supportContents < blocks.Length &&
            blocks[supportContents] != null &&
            blocks[supportContents].IsCollidable_(supportValue);

        if (!supported)
            m_terrain.DestroyCell(0, x, y, z, 0, false, false, null);
    }

    private void AddBlueTorch(int value, int x, int y, int z)
    {
        int blueTorchIndex = BlocksManager.GetBlockIndex<BlueTorchBlock>();
        if (blueTorchIndex <= 0 ||
            Terrain.ExtractContents(value) != blueTorchIndex ||
            m_subsystemParticles == null ||
            m_particleSystemsByCell == null)
        {
            return;
        }

        RemoveBlueTorch(x, y, z);

        int data = Terrain.ExtractData(value);
        Vector3 offset = data switch
        {
            0 => new Vector3(0.5f, 0.58f, 0.27f),
            1 => new Vector3(0.27f, 0.58f, 0.5f),
            2 => new Vector3(0.5f, 0.58f, 0.73f),
            3 => new Vector3(0.73f, 0.58f, 0.5f),
            _ => new Vector3(0.5f, 0.53f, 0.5f)
        };

        Vector3 position = new Vector3(x, y, z) + offset;
        Point3 cell = new Point3(x, y, z);

        BlueFireParticleSystem fire = new BlueFireParticleSystem(position, 0.15f, 32f);
        m_particleSystemsByCell[cell] = fire;
        m_subsystemParticles.AddParticleSystem(fire, false);

        // Two crossed round glow planes keep the soft blue aura visible from
        // both sides of a wall-mounted torch and from different viewing angles.
        GlowPoint first = CreateGlowPoint(position, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        GlowPoint second = CreateGlowPoint(position, Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX);
        m_glowPointsByCell[cell] = new[] { first, second };

        // The terrain-vertex hook adds a local blue cast to nearby block faces.
        BlueTorchTerrainLightPatch.Register(cell, new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), m_terrain);
    }

    private GlowPoint CreateGlowPoint(Vector3 position, Vector3 right, Vector3 up, Vector3 forward)
    {
        GlowPoint glow = m_subsystemGlow.AddGlowPoint();
        glow.Position = position;
        glow.Right = right;
        glow.Up = up;
        glow.Forward = forward;
        glow.Color = new Color(55, 145, 255, 92);
        glow.Size = 0.40f;
        glow.FarSize = 0.62f;
        glow.FarDistance = 12f;
        glow.Type = GlowPointType.Round;
        return glow;
    }

    private void RemoveBlueTorch(int x, int y, int z)
    {
        Point3 cell = new Point3(x, y, z);
        RemoveGlowForCell(cell);

        if (m_particleSystemsByCell == null)
            return;

        if (m_particleSystemsByCell.TryGetValue(cell, out FireParticleSystem fire))
        {
            m_subsystemParticles?.RemoveParticleSystem(fire, false);
            m_particleSystemsByCell.Remove(cell);
        }
    }

    private void RemoveGlowForCell(Point3 cell)
    {
        if (m_glowPointsByCell.TryGetValue(cell, out GlowPoint[] glows))
        {
            foreach (GlowPoint glow in glows)
                m_subsystemGlow?.RemoveGlowPoint(glow);

            m_glowPointsByCell.Remove(cell);
        }

        BlueTorchTerrainLightPatch.Unregister(cell, m_terrain);
    }
}
