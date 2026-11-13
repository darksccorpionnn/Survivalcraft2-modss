using System.Collections.Generic;
using Engine;
using HarmonyLib;

namespace Game
{
    [HarmonyPatch(typeof(ComponentPlayer), nameof(ComponentPlayer.Update))]
    public static class VillageEggPlayerPatch
    {
        private static readonly HashSet<ComponentPlayer> s_grantedPlayers = new HashSet<ComponentPlayer>();

        public static void Postfix(ComponentPlayer __instance)
        {
            if (__instance == null || s_grantedPlayers.Contains(__instance))
                return;

            try
            {
                if (__instance.ComponentMiner == null)
                    return;

                IInventory inventory = __instance.ComponentMiner.Inventory;
                if (inventory == null)
                    return;

                int content = BlocksManager.GetBlockIndex<VillageEggBlock>();
                if (content <= 0)
                {
                    Log.Information("[TownBootstrap] VillageEggBlock is not registered. Content index=" + content);
                    s_grantedPlayers.Add(__instance);
                    return;
                }

                // Do not duplicate an egg already present in the player's inventory.
                for (int slot = 0; slot < inventory.SlotsCount; slot++)
                {
                    if (Terrain.ExtractContents(inventory.GetSlotValue(slot)) == content &&
                        inventory.GetSlotCount(slot) > 0)
                    {
                        s_grantedPlayers.Add(__instance);
                        return;
                    }
                }

                for (int slot = 0; slot < inventory.SlotsCount; slot++)
                {
                    if (inventory.GetSlotCount(slot) == 0)
                    {
                        inventory.AddSlotItems(slot, Terrain.MakeBlockValue(content), 1);
                        s_grantedPlayers.Add(__instance);
                        Log.Information("[TownBootstrap] Village Egg granted from player update. Content=" + content);
                        return;
                    }
                }

                // Keep retrying until a free inventory slot appears.
                Log.Information("[TownBootstrap] No free inventory slot for Village Egg yet.");
            }
            catch (System.Exception ex)
            {
                Log.Information("[TownBootstrap] Failed to grant Village Egg from player update: " + ex);
            }
        }
    }
}
