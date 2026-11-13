using Engine.Graphics;
using HarmonyLib;

namespace Game
{
    // The supplied Mixamo-rigged villagers use "mixamorig:RightHand" instead of
    // the stock humanoid bone name "Hand2". ComponentHumanModel.DrawExtras uses
    // m_hand2Bone to place the active inventory block in the character's hand.
    [HarmonyPatch(typeof(ComponentHumanModel), nameof(ComponentHumanModel.SetModel))]
    internal static class LivingTownsHumanModelCompatibilityPatch
    {
        private static void Postfix(ComponentHumanModel __instance, Model __0)
        {
            if (__instance == null || __0 == null)
                return;

            if (__instance.m_hand2Bone == null)
                __instance.m_hand2Bone = __0.FindBone("mixamorig:RightHand", false);
        }
    }
}
