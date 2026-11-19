using System;
using Engine;
using TemplatesDatabase;

namespace Game;

/// <summary>
/// Dedicated native-torch behavior for BlueTorchBlock.
/// It preserves the vanilla particle positions and block-support rules,
/// but uses BlueFireParticleSystem for the visible flame.
/// </summary>
public sealed class BlueTorchBlockBehavior : SubsystemTorchBlockBehavior, IUpdateable
{
    private SubsystemTerrain m_terrain;
    private float m_glowTimer;

    public int UpdateOrder => 0;

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

    public void Update(float dt)
    {
        if (m_subsystemParticles == null || m_particleSystemsByCell == null || m_particleSystemsByCell.Count == 0)
            return;

        m_glowTimer -= dt;
        if (m_glowTimer > 0f)
            return;

        // A tinted, soft billboard is emitted continuously around each active blue flame.
        // This creates a visible blue halo even though native terrain light is monochrome.
        m_glowTimer = 0.55f;
        foreach (Point3 cell in m_particleSystemsByCell.Keys)
        {
            int value = m_terrain.Terrain.GetCellValueFast(cell.X, cell.Y, cell.Z);
            if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<BlueTorchBlock>())
                continue;

            Vector3 position = new Vector3(cell.X + 0.5f, cell.Y + 0.72f, cell.Z + 0.5f);
            FloatingImageParticleSystem glow = new FloatingImageParticleSystem(
                position, Vector3.Zero, new Vector2(1.15f),
                new Color(0.12f, 0.42f, 1f, 0.42f), 0.8f, "Textures/Fire");
            m_subsystemParticles.AddParticleSystem(glow);
        }
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
        // The inherited cleanup removes FireParticleSystem instances from the
        // same native dictionary, and BlueFireParticleSystem derives from that type.
        base.OnChunkDiscarding(chunk);
    }

    public override void OnNeighborBlockChanged(
        int x, int y, int z, int neighborX, int neighborY, int neighborZ)
    {
        // Native behavior hard-codes the vanilla torch IDs (31/132), so validate
        // this dynamic block's support with the same face-to-cell mapping.
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
        BlueFireParticleSystem fire = new BlueFireParticleSystem(position, 0.15f, 32f);

        Point3 cell = new Point3(x, y, z);
        m_particleSystemsByCell[cell] = fire;
        m_subsystemParticles.AddParticleSystem(fire, false);
    }

    private void RemoveBlueTorch(int x, int y, int z)
    {
        if (m_particleSystemsByCell == null)
            return;

        Point3 cell = new Point3(x, y, z);
        if (m_particleSystemsByCell.TryGetValue(cell, out FireParticleSystem fire))
        {
            m_subsystemParticles?.RemoveParticleSystem(fire, false);
            m_particleSystemsByCell.Remove(cell);
        }
    }
}
