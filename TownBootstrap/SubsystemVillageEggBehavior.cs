using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game
{
    /// <summary>
    /// Handles placement of the Village Egg item.
    /// </summary>
    public sealed class SubsystemVillageEggBehavior : SubsystemBlockBehavior
    {
        private SubsystemTerrain m_terrain;

        public override int[] HandledBlocks => new int[]
        {
            BlocksManager.GetBlockIndex<VillageEggBlock>()
        };

        public override void Load(ValuesDictionary valuesDictionary)
        {
            m_terrain = Project.FindSubsystem<SubsystemTerrain>();
            Log.Information("[TownBootstrap] Village Egg behavior loaded.");
        }

        public override void OnBlockAdded(int value, int oldValue, int x, int y, int z)
        {
            if (Terrain.ExtractContents(oldValue) == BlocksManager.GetBlockIndex<VillageEggBlock>())
                return;

            if (TownPlacementComponent.GenerateFromEgg(m_terrain, x, y, z))
            {
                // Consume the placed egg block only after successful generation.
                m_terrain.ChangeCell(x, y, z, 0);
                Log.Information("[TownBootstrap] Village Egg consumed; village generated.");
            }
            else
            {
                Log.Information("[TownBootstrap] Village Egg was not consumed because generation did not succeed.");
            }
        }
    }
}
