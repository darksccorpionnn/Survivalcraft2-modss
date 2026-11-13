using Engine;
using HarmonyLib;

namespace Game
{
    [HarmonyPatch(typeof(ComponentPlayer), nameof(ComponentPlayer.Load))]
    public static class VillageEggPlayerPatch
    {
        public static void Postfix(ComponentPlayer __instance)
        {
            try
            {
                int content = BlocksManager.GetBlockIndex<VillageEggBlock>();
                if (content <= 0)
                    return;

                ComponentInventory inventory = __instance.ComponentMiner.Inventory;
                int value = Terrain.MakeBlockValue(content);

                // Do not grant a second egg if one is already in the player's inventory.
                for (int slot = 0; slot < inventory.SlotsCount; slot++)
                {
                    if (Terrain.ExtractContents(inventory.GetSlotValue(slot)) == content &&
                        inventory.GetSlotCount(slot) > 0)
                        return;
                }

                for (int slot = 0; slot < inventory.SlotsCount; slot++)
                {
                    if (inventory.GetSlotCount(slot) == 0)
                    {
                        inventory.AddSlotItems(slot, value, 1);
                        Log.Information("[TownBootstrap] Village Egg granted to player.");
                        return;
                    }
                }

                Log.Information("[TownBootstrap] No free inventory slot; Village Egg could not be granted.");
            }
            catch (System.Exception ex)
            {
                Log.Information("[TownBootstrap] Failed to grant Village Egg: " + ex);
            }
        }
    }
}
