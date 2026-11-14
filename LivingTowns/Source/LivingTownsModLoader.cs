using System;
using System.Collections.Generic;
using Engine;
using HarmonyLib;

namespace Game
{
    public sealed class LivingTownsModLoader : ModLoader
    {
        public const string PackageName = "darksccorpionnn.LivingTowns";

        public override void __ModInitialize()
        {
            new Harmony(PackageName + ".VillagerInteraction").PatchAll();
            ModsManager.RegisterHook("OnLoadingFinished", this);
            Log.Information("[LivingTowns] 0.4.58 initialized; 3D silver and gold currency items added.");
        }

        public override void OnLoadingFinished(List<Action> actions)
        {
            Log.Information("[LivingTowns] 0.4.58 ready: existing villager AI plus Silver Coin and Gold Coin inventory items. Currency ratio: 1 Gold = 10 Silver. Profession trades are not enabled yet.");
        }
    }
}
