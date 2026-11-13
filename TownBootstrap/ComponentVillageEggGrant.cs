using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game
{
    /// <summary>
    /// Grants one Village Egg after the player's inventory is initialized.
    /// </summary>
    public sealed class ComponentVillageEggGrant : Component, IUpdateable
    {
        private ComponentPlayer m_player;
        private bool m_done;

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap)
        {
            m_player = Entity.FindComponent<ComponentPlayer>(true);
            Log.Information("[TownBootstrap] Village egg grant component loaded.");
        }

        public void Update(float dt)
        {
            if (m_done || m_player == null || m_player.ComponentMiner == null)
                return;

            IInventory inventory = m_player.ComponentMiner.Inventory;
            if (inventory == null)
                return;

            int content = BlocksManager.GetBlockIndex<VillageEggBlock>();
            if (content <= 0)
            {
                Log.Information("[TownBootstrap] ERROR: VillageEggBlock is not registered. Index=" + content);
                m_done = true;
                return;
            }

            for (int slot = 0; slot < inventory.SlotsCount; slot++)
            {
                if (Terrain.ExtractContents(inventory.GetSlotValue(slot)) == content &&
                    inventory.GetSlotCount(slot) > 0)
                {
                    m_done = true;
                    return;
                }
            }

            for (int slot = 0; slot < inventory.SlotsCount; slot++)
            {
                if (inventory.GetSlotCount(slot) == 0)
                {
                    inventory.AddSlotItems(slot, Terrain.MakeBlockValue(content), 1);
                    m_done = true;
                    Log.Information("[TownBootstrap] SUCCESS: Village Egg added to player inventory. Index=" + content);
                    return;
                }
            }

            // Retry on the next update if inventory is full.
        }
    }
}
