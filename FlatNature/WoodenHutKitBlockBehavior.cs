using System;
using TemplatesDatabase;

namespace Game;

public sealed class WoodenHutKitBlockBehavior : SubsystemBlockBehavior
{
    private SubsystemTerrain m_terrain;

    public override int[] HandledBlocks
    {
        get
        {
            int index = BlocksManager.GetBlockIndex<WoodenHutKitBlock>();
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
        int kitIndex = BlocksManager.GetBlockIndex<WoodenHutKitBlock>();
        if (kitIndex <= 0 || Terrain.ExtractContents(value) != kitIndex || m_terrain == null)
            return;

        int waterIndex = 0;
        SubsystemGameInfo gameInfo = Project.FindSubsystem<SubsystemGameInfo>(false);
        if (gameInfo?.WorldSettings != null)
            waterIndex = gameInfo.WorldSettings.TerrainOceanBlockIndex;

        try
        {
            bool built = WoodenHutGenerator.TryBuildManual(
                m_terrain, x, y, z, waterIndex, kitIndex);

            if (built)
                Console.WriteLine("[FlatNature] Manual wooden hut placed at " + x + "," + y + "," + z);
            else
                Console.WriteLine("[FlatNature] Hut kit kept in place: needs a clear, level 7x7 area.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FlatNature] Manual hut placement failed safely: " + ex);
        }
    }
}
