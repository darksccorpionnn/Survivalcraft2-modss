using System;
using System.Collections;
using System.Reflection;
using Engine;
using TemplatesDatabase;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Builds one level 12x10 plot at this farmer's own wooden-sign work position.
/// The native LivingTowns controller remains responsible for movement,
/// planting mature rye, harvesting and replanting.
/// </summary>
public sealed class ComponentKoyluFarmerPlot : Component, IUpdateable
{
    private const string ControllerName = "Game.ComponentLivingTownsVillager";
    private const int PlotWidth = 12;
    private const int PlotDepth = 10;
    private const int RequiredSeedCount = 64;

    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private object m_controller;
    private SubsystemTerrain m_terrain;
    private IInventory m_inventory;
    private ComponentBody m_body;

    private bool m_extraKitGranted;
    private bool m_plotBuilt;
    private float m_retryTimer;
    private float m_plotRetryTimer;
    private bool m_loggedWaitingForSign;
    private bool m_loggedNoSite;

    public UpdateOrder UpdateOrder => UpdateOrder.Default;

    public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap)
    {
        base.Load(valuesDictionary, idToEntityMap);
        m_extraKitGranted = valuesDictionary.GetValue<bool>("FarmerExtraKitGranted", false);
        m_plotBuilt = valuesDictionary.GetValue<bool>("FarmerPlotBuilt", false);
    }

    public override void Save(ValuesDictionary valuesDictionary, EntityToIdMap entityToIdMap)
    {
        base.Save(valuesDictionary, entityToIdMap);
        valuesDictionary.SetValue("FarmerExtraKitGranted", m_extraKitGranted);
        valuesDictionary.SetValue("FarmerPlotBuilt", m_plotBuilt);
    }

    public void Update(float dt)
    {
        m_retryTimer -= dt;
        m_plotRetryTimer -= dt;
        if (m_retryTimer > 0f)
            return;
        m_retryTimer = 0.5f;

        if (!TryBindNativeFarmer())
            return;

        EnsureNativeStarterKit();
        if (!m_extraKitGranted)
            m_extraKitGranted = EnsureExtraStarterItems();

        if (m_plotBuilt || !m_extraKitGranted)
            return;

        if (!TryGetWorkSign(out Point3 signCell))
            return;

        if (!IsAtOwnWorkLocation(signCell))
            return;

        if (m_plotRetryTimer > 0f)
            return;

        if (!TryFindPlot(signCell, out PlotCandidate candidate))
        {
            m_plotRetryTimer = 3f;
            if (!m_loggedNoSite)
            {
                Log.Information("[KoylulerModu] Farmer is waiting for a clear, nearly level 12x10 farm site beside its own wooden sign.");
                m_loggedNoSite = true;
            }
            return;
        }

        if (!BuildPlot(candidate))
        {
            m_plotRetryTimer = 3f;
            return;
        }

        m_plotBuilt = true;
        Log.Information("[KoylulerModu] Farmer built its 12x10 fenced rye plot, prepared soil twice, treated the plot with salt and placed two water channels. Native LivingTowns planting/harvest remains enabled.");
    }

    private bool TryBindNativeFarmer()
    {
        if (Entity == null)
            return false;

        if (m_controller == null)
        {
            foreach (object component in Entity.Components)
            {
                if (component != null && component.GetType().FullName == ControllerName)
                {
                    m_controller = component;
                    break;
                }
            }
        }

        if (m_controller == null)
            return false;

        object terrainObject = ReadField(m_controller, "m_terrain");
        object inventoryObject = ReadField(m_controller, "m_inventory");
        object bodyObject = ReadField(m_controller, "m_body");

        m_terrain = terrainObject as SubsystemTerrain;
        m_inventory = inventoryObject as IInventory;
        m_body = bodyObject as ComponentBody;

        return m_terrain != null && m_inventory != null && m_body != null;
    }

    private void EnsureNativeStarterKit()
    {
        try
        {
            MethodInfo ensure = FindMethod(m_controller, "EnsureFarmerKit", Type.EmptyTypes);
            ensure?.Invoke(m_controller, null);
        }
        catch (Exception ex)
        {
            Log.Error("[KoylulerModu] Could not invoke the native farmer starter kit: " + ex);
        }
    }

    private bool EnsureExtraStarterItems()
    {
        int saltIndex = SafeBlockIndex<KoyluSaltBlock>();
        int waterBucketIndex = SafeBlockIndex<WaterBucketBlock>();
        int seedsIndex = SafeBlockIndex<SeedsBlock>();

        if (saltIndex <= 0 || waterBucketIndex <= 0 || seedsIndex <= 0)
            return false;

        bool saltReady = EnsureInventoryCount(saltIndex, 1);
        bool bucketReady = EnsureInventoryCount(waterBucketIndex, 1);
        bool seedsReady = EnsureInventoryCount(seedsIndex, RequiredSeedCount);

        return saltReady && bucketReady && seedsReady;
    }

    private bool EnsureInventoryCount(int contents, int requiredCount)
    {
        int present = 0;
        for (int slot = 0; slot < m_inventory.SlotsCount; slot++)
        {
            int count = m_inventory.GetSlotCount(slot);
            if (count > 0 && Terrain.ExtractContents(m_inventory.GetSlotValue(slot)) == contents)
                present += count;
        }

        int remaining = requiredCount - present;
        if (remaining <= 0)
            return true;

        int blockValue = Terrain.MakeBlockValue(contents);
        for (int slot = 0; slot < m_inventory.SlotsCount && remaining > 0; slot++)
        {
            int slotCount = m_inventory.GetSlotCount(slot);
            int slotValue = m_inventory.GetSlotValue(slot);
            bool compatible = slotCount == 0 ||
                Terrain.ExtractContents(slotValue) == contents;
            if (!compatible)
                continue;

            int capacity = m_inventory.GetSlotCapacity(slot, blockValue);
            if (capacity <= 0)
                continue;

            int amount = Math.Min(remaining, capacity);
            m_inventory.AddSlotItems(slot, blockValue, amount);
            remaining -= amount;
        }

        return remaining == 0;
    }

    private bool TryGetWorkSign(out Point3 signCell)
    {
        signCell = default;
        object hasSignValue = ReadField(m_controller, "m_hasWorkSignCell");
        if (!(hasSignValue is bool hasSign) || !hasSign)
        {
            if (!m_loggedWaitingForSign)
            {
                Log.Information("[KoylulerModu] Farmer plot builder is waiting for its own wooden work sign.");
                m_loggedWaitingForSign = true;
            }
            return false;
        }

        MethodInfo getCenter = FindMethod(m_controller, "GetWorkCenterCell", Type.EmptyTypes);
        if (getCenter == null)
            return false;

        try
        {
            object result = getCenter.Invoke(m_controller, null);
            if (!(result is Point3 point))
                return false;
            signCell = point;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("[KoylulerModu] Could not read the farmer's wooden-sign work position: " + ex.Message);
            return false;
        }
    }

    private bool IsAtOwnWorkLocation(Point3 signCell)
    {
        Vector3 position = m_body.Position;
        float dx = position.X - (signCell.X + 0.5f);
        float dz = position.Z - (signCell.Z + 0.5f);
        return dx * dx + dz * dz <= 64f &&
            Math.Abs(position.Y - signCell.Y) <= 5f;
    }

    private bool TryFindPlot(Point3 signCell, out PlotCandidate selected)
    {
        // Four candidate sites keep the sign outside the fence and leave the
        // native work/home routing controller in charge of the villager.
        PlotCandidate[] candidates =
        {
            new PlotCandidate(signCell.X - 6, signCell.Z - 13, PlotWidth, PlotDepth),
            new PlotCandidate(signCell.X - 6, signCell.Z + 4, PlotWidth, PlotDepth),
            new PlotCandidate(signCell.X - 13, signCell.Z - 5, PlotDepth, PlotWidth),
            new PlotCandidate(signCell.X + 4, signCell.Z - 5, PlotDepth, PlotWidth)
        };

        foreach (PlotCandidate candidate in candidates)
        {
            if (TryValidateCandidate(candidate, signCell.Y, out int groundY))
            {
                selected = candidate.WithGroundY(groundY);
                return true;
            }
        }

        selected = default;
        return false;
    }

    private bool TryValidateCandidate(PlotCandidate candidate, int centerY, out int groundY)
    {
        int minY = int.MaxValue;
        int maxY = int.MinValue;

        for (int x = candidate.X; x < candidate.X + candidate.Width; x++)
        for (int z = candidate.Z; z < candidate.Z + candidate.Depth; z++)
        {
            if (!TryFindSurface(x, z, centerY, out int surfaceY))
            {
                groundY = 0;
                return false;
            }

            minY = Math.Min(minY, surfaceY);
            maxY = Math.Max(maxY, surfaceY);
            if (maxY - minY > 1)
            {
                groundY = 0;
                return false;
            }
        }

        groundY = maxY;
        return true;
    }

    private bool TryFindSurface(int x, int z, int centerY, out int surfaceY)
    {
        int grass = SafeBlockIndex<GrassBlock>();
        int dirt = SafeBlockIndex<DirtBlock>();
        int tallGrass = SafeBlockIndex<TallGrassBlock>();

        for (int y = centerY + 4; y >= Math.Max(1, centerY - 7); y--)
        {
            int value = m_terrain.Terrain.GetCellValue(x, y, z);
            int contents = Terrain.ExtractContents(value);
            if (contents != grass && contents != dirt)
                continue;

            // Only clear ordinary tall grass. Trees, buildings, signs, rocks,
            // water and other structures are treated as obstacles, never bulldozed.
            for (int above = y + 1; above <= y + 2; above++)
            {
                int aboveContents = Terrain.ExtractContents(
                    m_terrain.Terrain.GetCellValue(x, above, z));
                if (aboveContents != 0 && aboveContents != tallGrass)
                {
                    surfaceY = 0;
                    return false;
                }
            }

            surfaceY = y;
            return true;
        }

        surfaceY = 0;
        return false;
    }

    private bool BuildPlot(PlotCandidate candidate)
    {
        int grass = SafeBlockIndex<GrassBlock>();
        int dirt = SafeBlockIndex<DirtBlock>();
        int soil = SafeBlockIndex<SoilBlock>();
        int tallGrass = SafeBlockIndex<TallGrassBlock>();
        int fence = SafeBlockIndex<WoodenFenceBlock>();
        int water = SafeBlockIndex<WaterBlock>();
        int salt = SafeBlockIndex<KoyluSaltBlock>();

        int rakeSlot = FindInventorySlot(SafeBlockIndex<IronRakeBlock>());
        if (grass <= 0 || dirt <= 0 || soil <= 0 || fence <= 0 ||
            water <= 0 || salt <= 0 || rakeSlot < 0)
            return false;

        // Re-check every column immediately before changing the terrain.
        if (!TryValidateCandidate(candidate, candidate.GroundY, out int checkedY) ||
            checkedY != candidate.GroundY)
            return false;

        // Level low ground by one block and remove only tall grass.
        for (int x = candidate.X; x < candidate.X + candidate.Width; x++)
        for (int z = candidate.Z; z < candidate.Z + candidate.Depth; z++)
        {
            int surfaceY = FindSurfaceAtKnownHeight(x, z, candidate.GroundY);
            if (surfaceY < 0 || candidate.GroundY - surfaceY > 1)
                return false;

            for (int above = candidate.GroundY + 1; above <= candidate.GroundY + 2; above++)
            {
                int aboveValue = m_terrain.Terrain.GetCellValue(x, above, z);
                if (Terrain.ExtractContents(aboveValue) == tallGrass)
                    m_terrain.ChangeCell(x, above, z, 0);
                else if (Terrain.ExtractContents(aboveValue) != 0)
                    return false;
            }

            if (surfaceY < candidate.GroundY)
            {
                int fillValue = m_terrain.Terrain.GetCellValue(x, candidate.GroundY, z);
                if (Terrain.ExtractContents(fillValue) != 0)
                    return false;
                m_terrain.ChangeCell(x, candidate.GroundY, z, Terrain.MakeBlockValue(dirt));
            }
        }

        // Enclose all four sides with wooden fence (40 blocks for 12x10).
        for (int x = candidate.X; x < candidate.X + candidate.Width; x++)
        {
            if (!PlaceFence(x, candidate.GroundY + 1, candidate.Z, fence) ||
                !PlaceFence(x, candidate.GroundY + 1, candidate.Z + candidate.Depth - 1, fence))
                return false;
        }

        for (int z = candidate.Z + 1; z < candidate.Z + candidate.Depth - 1; z++)
        {
            if (!PlaceFence(candidate.X, candidate.GroundY + 1, z, fence) ||
                !PlaceFence(candidate.X + candidate.Width - 1, candidate.GroundY + 1, z, fence))
                return false;
        }

        // Two parallel irrigation rows. The remaining 64 interior cells become rye plots.
        int waterColumnA = candidate.X + candidate.Width / 3;
        int waterColumnB = candidate.X + (candidate.Width * 2) / 3;
        for (int z = candidate.Z + 1; z < candidate.Z + candidate.Depth - 1; z++)
        {
            m_terrain.ChangeCell(waterColumnA, candidate.GroundY, z, Terrain.MakeBlockValue(water));
            m_terrain.ChangeCell(waterColumnB, candidate.GroundY, z, Terrain.MakeBlockValue(water));
        }

        MethodInfo damageRake = FindMethod(m_controller, "DamageRake", new[] { typeof(int) });
        if (damageRake == null)
            return false;

        for (int x = candidate.X + 1; x < candidate.X + candidate.Width - 1; x++)
        for (int z = candidate.Z + 1; z < candidate.Z + candidate.Depth - 1; z++)
        {
            if (x == waterColumnA || x == waterColumnB)
                continue;

            int value = m_terrain.Terrain.GetCellValue(x, candidate.GroundY, z);
            int contents = Terrain.ExtractContents(value);

            // First rake hit: green grass -> dirt.
            if (contents == grass)
            {
                m_terrain.ChangeCell(x, candidate.GroundY, z, Terrain.MakeBlockValue(dirt));
                DamageRakeOnce(damageRake);
                contents = dirt;
            }

            // Second rake hit: dirt -> farmland.
            if (contents == dirt)
            {
                m_terrain.ChangeCell(x, candidate.GroundY, z, Terrain.MakeBlockValue(soil));
                DamageRakeOnce(damageRake);
            }
            else if (contents != soil)
            {
                return false;
            }
        }

        // Salt has no native Survivalcraft block. The custom salt item is
        // therefore consumed as a one-time whole-plot soil treatment and its
        // completion is saved with this component.
        if (!ConsumeOneItem(salt))
            return false;

        return true;
    }

    private int FindSurfaceAtKnownHeight(int x, int z, int targetY)
    {
        int grass = SafeBlockIndex<GrassBlock>();
        int dirt = SafeBlockIndex<DirtBlock>();
        for (int y = targetY; y >= targetY - 1; y--)
        {
            int contents = Terrain.ExtractContents(m_terrain.Terrain.GetCellValue(x, y, z));
            if (contents == grass || contents == dirt)
                return y;
        }
        return -1;
    }

    private bool PlaceFence(int x, int y, int z, int fenceIndex)
    {
        int current = Terrain.ExtractContents(m_terrain.Terrain.GetCellValue(x, y, z));
        int tallGrass = SafeBlockIndex<TallGrassBlock>();
        if (current == fenceIndex)
            return true;
        if (current == tallGrass)
            m_terrain.ChangeCell(x, y, z, 0);
        else if (current != 0)
            return false;

        m_terrain.ChangeCell(x, y, z, Terrain.MakeBlockValue(fenceIndex));
        return true;
    }

    private void DamageRakeOnce(MethodInfo damageRake)
    {
        int slot = FindInventorySlot(SafeBlockIndex<IronRakeBlock>());
        if (slot < 0)
            return;
        try
        {
            damageRake.Invoke(m_controller, new object[] { slot });
        }
        catch (Exception ex)
        {
            Log.Error("[KoylulerModu] Rake durability update failed: " + ex.Message);
        }
    }

    private bool ConsumeOneItem(int contents)
    {
        int slot = FindInventorySlot(contents);
        if (slot < 0)
            return false;
        return m_inventory.RemoveSlotItems(slot, 1) == 1;
    }

    private int FindInventorySlot(int contents)
    {
        if (contents <= 0 || m_inventory == null)
            return -1;

        for (int slot = 0; slot < m_inventory.SlotsCount; slot++)
        {
            if (m_inventory.GetSlotCount(slot) > 0 &&
                Terrain.ExtractContents(m_inventory.GetSlotValue(slot)) == contents)
                return slot;
        }

        return -1;
    }

    private static int SafeBlockIndex<T>() where T : Block
    {
        try
        {
            return BlocksManager.GetBlockIndex<T>();
        }
        catch
        {
            return 0;
        }
    }

    private static object ReadField(object instance, string name)
    {
        if (instance == null)
            return null;

        for (Type current = instance.GetType(); current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field.GetValue(instance);
        }

        return null;
    }

    private static MethodInfo FindMethod(object instance, string name, Type[] parameters)
    {
        if (instance == null)
            return null;

        for (Type current = instance.GetType(); current != null; current = current.BaseType)
        {
            MethodInfo method = current.GetMethod(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, parameters, null);
            if (method != null)
                return method;
        }

        return null;
    }

    private readonly struct PlotCandidate
    {
        public readonly int X;
        public readonly int Z;
        public readonly int Width;
        public readonly int Depth;
        public readonly int GroundY;

        public PlotCandidate(int x, int z, int width, int depth, int groundY = 0)
        {
            X = x;
            Z = z;
            Width = width;
            Depth = depth;
            GroundY = groundY;
        }

        public PlotCandidate WithGroundY(int y)
            => new PlotCandidate(X, Z, Width, Depth, y);
    }
}
