using System;
using System.Collections.Generic;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

/// <summary>
/// Reuses the native furnace block entity and furnace interface for the stove.
/// Unlike a smelting furnace, the stove consumes wood even when no recipe is
/// placed in its input slot. Native fire particles are reused and smoke is added
/// with the game's own SmokeTrailParticleSystem.
/// </summary>
public sealed class WoodStoveBlockBehavior : SubsystemFurnaceBlockBehavior, IUpdateable
{
    private const float UpdateInterval = 0.15f;
    private const int LitDataFlag = WoodStoveBlock.LitDataFlag;

    private readonly HashSet<Point3> m_stoveCells = new();
    private readonly Dictionary<Point3, SmokeTrailParticleSystem> m_smokeByCell = new();

    private SubsystemTerrain m_terrain;
    private SubsystemBlockEntities m_blockEntities;
    private SubsystemParticles m_particles;
    private float m_updateAccumulator;

    public override int[] HandledBlocks
    {
        get
        {
            int index = BlocksManager.GetBlockIndex<WoodStoveBlock>();
            return index > 0 ? new[] { index } : Array.Empty<int>();
        }
    }

    public UpdateOrder UpdateOrder => UpdateOrder.Default;

    public override void Load(ValuesDictionary valuesDictionary)
    {
        base.Load(valuesDictionary);
        m_terrain = Project.FindSubsystem<SubsystemTerrain>(true);
        m_blockEntities = Project.FindSubsystem<SubsystemBlockEntities>(true);
        m_particles = Project.FindSubsystem<SubsystemParticles>(true);
        Log.Information("[FlatNature] Wood stove behavior loaded. Native Furnace entity/UI will be reused.");
    }

    public override void OnBlockAdded(int value, int oldValue, int x, int y, int z)
    {
        base.OnBlockAdded(value, oldValue, x, y, z);
        if (Terrain.ExtractContents(value) == BlocksManager.GetBlockIndex<WoodStoveBlock>())
            m_stoveCells.Add(new Point3(x, y, z));
    }

    public override void OnBlockGenerated(int value, int x, int y, int z, bool isLoaded)
    {
        base.OnBlockGenerated(value, x, y, z, isLoaded);
        if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<WoodStoveBlock>())
            return;

        Point3 cell = new Point3(x, y, z);
        m_stoveCells.Add(cell);

        // FurnaceBlockBehavior's native generated-block callback only handles
        // vanilla block IDs 64/65. Create the inherited Furnace entity for this
        // new dynamic block when a saved chunk is loaded.
        if (m_blockEntities.GetBlockEntity(x, y, z) == null)
            base.OnBlockAdded(value, 0, x, y, z);
    }

    public override void OnBlockRemoved(int value, int newValue, int x, int y, int z)
    {
        Point3 cell = new Point3(x, y, z);
        RemoveEffects(cell);
        m_stoveCells.Remove(cell);
        base.OnBlockRemoved(value, newValue, x, y, z);
    }

    public override void OnChunkDiscarding(TerrainChunk chunk)
    {
        base.OnChunkDiscarding(chunk);

        int minX = chunk.Origin.X;
        int minZ = chunk.Origin.Y;
        List<Point3> remove = new();

        foreach (Point3 cell in m_stoveCells)
        {
            if (cell.X >= minX && cell.X < minX + 16 &&
                cell.Z >= minZ && cell.Z < minZ + 16)
                remove.Add(cell);
        }

        foreach (Point3 cell in remove)
        {
            RemoveEffects(cell);
            m_stoveCells.Remove(cell);
        }
    }

    public void Update(float dt)
    {
        m_updateAccumulator += dt;
        if (m_updateAccumulator < UpdateInterval)
            return;

        m_updateAccumulator = 0f;
        int stoveIndex = BlocksManager.GetBlockIndex<WoodStoveBlock>();
        if (stoveIndex <= 0 || m_terrain == null || m_blockEntities == null || m_particles == null)
            return;

        List<Point3> remove = new();

        foreach (Point3 cell in m_stoveCells)
        {
            int value = m_terrain.Terrain.GetCellValue(cell.X, cell.Y, cell.Z);
            if (Terrain.ExtractContents(value) != stoveIndex)
            {
                RemoveEffects(cell);
                remove.Add(cell);
                continue;
            }

            ComponentBlockEntity blockEntity = m_blockEntities.GetBlockEntity(cell);
            if (blockEntity == null)
            {
                // Recover gracefully if a chunk was restored without its block entity.
                base.OnBlockAdded(value, 0, cell.X, cell.Y, cell.Z);
                blockEntity = m_blockEntities.GetBlockEntity(cell);
            }

            ComponentFurnace furnace = blockEntity?.Entity.FindComponent<ComponentFurnace>(false);
            if (furnace == null)
                continue;

            // Native ComponentFurnace only starts another fuel item when a
            // smelting recipe exists. A room heater must burn logs on its own.
            if (furnace.HeatLevel <= 0f && furnace.FireTimeRemaining <= 0f)
                furnace.UseFuel();

            bool burning = furnace.HeatLevel > 0f && furnace.FireTimeRemaining > 0f;
            UpdateLitBlock(cell, value, burning);
            UpdateFire(furnace, blockEntity, burning);
            UpdateSmoke(cell, blockEntity, burning);
        }

        foreach (Point3 cell in remove)
            m_stoveCells.Remove(cell);
    }

    private void UpdateLitBlock(Point3 cell, int value, bool burning)
    {
        int oldData = Terrain.ExtractData(value);
        int newData = burning ? oldData | LitDataFlag : oldData & ~LitDataFlag;
        if (oldData != newData)
            m_terrain.ChangeCell(cell.X, cell.Y, cell.Z, Terrain.ReplaceData(value, newData));
    }

    private void UpdateFire(ComponentFurnace furnace, ComponentBlockEntity blockEntity, bool burning)
    {
        FireParticleSystem fire = furnace.m_fireParticleSystem;
        if (fire == null)
            return;

        fire.m_position = blockEntity.Position + new Vector3(0.5f, 0.48f, 0.5f);
        bool exists = m_particles.ContainsParticleSystem(fire);

        if (burning && !exists)
            m_particles.AddParticleSystem(fire, false);
        else if (!burning && exists)
            m_particles.RemoveParticleSystem(fire, false);
    }

    private void UpdateSmoke(Point3 cell, ComponentBlockEntity blockEntity, bool burning)
    {
        if (!m_smokeByCell.TryGetValue(cell, out SmokeTrailParticleSystem smoke) && burning)
        {
            smoke = new SmokeTrailParticleSystem(
                28, 0.075f, 2.4f, new Color(0.42f, 0.42f, 0.42f, 0.62f));
            m_smokeByCell[cell] = smoke;
        }

        if (smoke == null)
            return;

        smoke.Position = blockEntity.Position + new Vector3(0.5f, 1.12f, 0.5f);
        smoke.IsStopped = !burning;

        if (burning && !m_particles.ContainsParticleSystem(smoke))
            m_particles.AddParticleSystem(smoke, false);

        if (!burning && !m_particles.ContainsParticleSystem(smoke))
            m_smokeByCell.Remove(cell);
    }

    private void RemoveEffects(Point3 cell)
    {
        if (m_blockEntities != null)
        {
            ComponentBlockEntity blockEntity = m_blockEntities.GetBlockEntity(cell);
            ComponentFurnace furnace = blockEntity?.Entity.FindComponent<ComponentFurnace>(false);
            if (furnace?.m_fireParticleSystem != null && m_particles != null)
                m_particles.RemoveParticleSystem(furnace.m_fireParticleSystem, false);
        }

        if (m_smokeByCell.TryGetValue(cell, out SmokeTrailParticleSystem smoke))
        {
            if (m_particles != null)
                m_particles.RemoveParticleSystem(smoke, false);
            m_smokeByCell.Remove(cell);
        }
    }
}
