using System;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game
{
    /// <summary>
    /// Handles placement of the Village Egg without re-entering OnBlockAdded
    /// when the trigger block is temporarily removed or restored.
    /// </summary>
    public sealed class SubsystemVillageEggBehavior : SubsystemBlockBehavior
    {
        private SubsystemTerrain m_terrain;
        private bool m_internalCellChange;

        public override int[] HandledBlocks
        {
            get
            {
                int contents = BlocksManager.GetBlockIndex<VillageEggBlock>();
                return contents > 0 ? new int[] { contents } : Array.Empty<int>();
            }
        }

        public override void Load(ValuesDictionary valuesDictionary)
        {
            m_terrain = Project.FindSubsystem<SubsystemTerrain>();
            Log.Information("[TownBootstrap] Village Egg behavior loaded.");
        }

        public override void OnBlockAdded(int value, int oldValue, int x, int y, int z)
        {
            if (m_internalCellChange || m_terrain == null)
                return;

            int eggContent = BlocksManager.GetBlockIndex<VillageEggBlock>();
            if (eggContent <= 0 ||
                Terrain.ExtractContents(value) != eggContent ||
                Terrain.ExtractContents(oldValue) == eggContent)
                return;

            // Remove the egg while generating. The guard is set during ChangeCell
            // because that call synchronously dispatches block behavior callbacks.
            SetCellInternally(x, y, z, 0);

            bool generated = false;
            try
            {
                generated = TownPlacementComponent.GenerateFromEgg(m_terrain, x, y, z);
            }
            catch (Exception ex)
            {
                Log.Information("[TownBootstrap] Village generation threw an exception: " + ex);
            }

            if (generated)
            {
                Log.Information("[TownBootstrap] Village Egg consumed; village generated.");
            }
            else
            {
                // Restore the egg once, while the callback guard prevents recursion.
                SetCellInternally(x, y, z, value);
                Log.Information("[TownBootstrap] No suitable site; Village Egg restored safely.");
            }
        }

        private void SetCellInternally(int x, int y, int z, int value)
        {
            m_internalCellChange = true;
            try
            {
                m_terrain.ChangeCell(x, y, z, value);
            }
            finally
            {
                m_internalCellChange = false;
            }
        }
    }
}