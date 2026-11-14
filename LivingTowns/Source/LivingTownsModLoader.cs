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
            Log.Information("[LivingTowns] 0.4.59 initialized; 3D silver and gold coins and inventory-style villager trading enabled.");
        }

        public override void OnLoadingFinished(List<Action> actions)
        {
            Log.Information("[LivingTowns] 0.4.59 ready: persistent separate villager trade stock, native inventory-style trade panel, existing villager jobs and routines, Silver Coin and Gold Coin (1 Gold = 10 Silver). Fixed profession prices and trade recipes remain a separate next step.");
        }
    }
}
