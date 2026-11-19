using System;
using System.Collections.Generic;
using Engine;
using TemplatesDatabase;

namespace Game;

/// <summary>
/// Dedicated torch behavior for BlueTorchBlock.
/// The native torch flame is replaced by a blue flame and a softer,
/// wider blue particle layer to create a visible glow around the torch.
/// </summary>
public sealed class BlueTorchBlockBehavior : SubsystemTorchBlockBehavior
{
    private SubsystemTerrain m_terrain;
    private readonly Dictionary<Point3, FireParticleSystem> m_glowParticleSystemsByCell = new();

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
        // Remove only the extra glow systems belonging to this discarded chunk.
        List<Point3> cellsToRemove = new();
        foreach (KeyValuePair<Point3, FireParticleSystem> pair in m_glowParticleSystemsByCell)
        {
            Point3 cell = pair.Key;
            if (cell.X >= chunk.BoundingBox.Min.X && cell.X < chunk.BoundingBox.Max.X &&
                cell.Z >= chunk.BoundingBox.Min.Z && cell.Z < chunk.BoundingBox.Max.Z)
            {
                m_subsystemParticles?.RemoveParticleSystem(pair.Value, false);
                cellsToRemove.Add(cell);
            }
        }

        foreach (Point3 cell in cellsToRemove)
            m_glowParticleSystemsByCell.Remove(cell);

        // The inherited cleanup removes the regular blue flame system.
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

        // A larger, low-opacity blue particle layer creates a soft aura.
        // It is separate from the sharp flame so the flame remains readable.
        BlueGlowParticleSystem glow = new BlueGlowParticleSystem(position, 0.52f, 24f);
        m_glowParticleSystemsByCell[cell] = glow;
        m_subsystemParticles.AddParticleSystem(glow, false);
    }

    private void RemoveBlueTorch(int x, int y, int z)
    {
        Point3 cell = new Point3(x, y, z);

        if (m_glowParticleSystemsByCell.TryGetValue(cell, out FireParticleSystem glow))
        {
            m_subsystemParticles?.RemoveParticleSystem(glow, false);
            m_glowParticleSystemsByCell.Remove(cell);
        }

        if (m_particleSystemsByCell == null)
            return;

        if (m_particleSystemsByCell.TryGetValue(cell, out FireParticleSystem fire))
        {
            m_subsystemParticles?.RemoveParticleSystem(fire, false);
            m_particleSystemsByCell.Remove(cell);
        }
    }
}
