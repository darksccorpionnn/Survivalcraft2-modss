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
            int eggContent = BlocksManager.GetBlockIndex<VillageEggBlock>();
            if (Terrain.ExtractContents(value) != eggContent ||
                Terrain.ExtractContents(oldValue) == eggContent)
                return;

            // Temporarily remove the placed egg so the flat-site clearance test
            // does not mistake the trigger block for an obstruction.
            m_terrain.ChangeCell(x, y, z, 0);
            if (TownPlacementComponent.GenerateFromEgg(m_terrain, x, y, z))
            {
                Log.Information("[TownBootstrap] Village Egg consumed; village generated.");
            }
            else
            {
                // Keep the egg in the world on failure; the player can pick it up.
                m_terrain.ChangeCell(x, y, z, value);
                Log.Information("[TownBootstrap] No suitable site; Village Egg restored.");
            }
        }
    }
}
