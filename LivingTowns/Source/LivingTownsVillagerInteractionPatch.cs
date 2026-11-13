using System;
using HarmonyLib;
using Engine;

namespace Game
{
    [HarmonyPatch(typeof(ComponentMiner), nameof(ComponentMiner.Hit), new Type[]
    {
        typeof(ComponentBody), typeof(Vector3), typeof(Vector3)
    })]
    internal static class LivingTownsVillagerInteractionPatch
    {
        private static bool Prefix(ComponentMiner __instance, ComponentBody componentBody, Vector3 hitPoint, Vector3 hitDirection)
        {
            if (__instance?.ComponentPlayer == null || componentBody == null)
                return true;

            ComponentLivingTownsVillager villager = componentBody.Entity.FindComponent<ComponentLivingTownsVillager>();
            if (villager == null)
                return true;

            villager.ShowJobMenu(__instance.ComponentPlayer);
            return false;
        }
    }
}
