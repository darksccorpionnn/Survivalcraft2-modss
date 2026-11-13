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
            Log.Information("[LivingTowns] 0.4.1 initialized.");
        }

        public override void OnLoadingFinished(List<Action> actions)
        {
            Log.Information("[LivingTowns] 0.4.1 villagers, jobs, Celtic beds, conversations and doors ready.");
        }
    }
}
