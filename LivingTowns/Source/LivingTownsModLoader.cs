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
            Log.Information("[LivingTowns] 0.4.56 initialized; healer treatment and guard defense enabled.");
        }

        public override void OnLoadingFinished(List<Action> actions)
        {
            Log.Information("[LivingTowns] 0.4.56 villagers, unique personal WORK signs searched to 200 blocks, incremental sign scanning, obstacle-aware long-distance work travel, late-resolved profession routes, healer treatment, guard predator defense, named wall-sign sleep positions, exclusive rest cells, safe home-rest fallback, controlled obstacle jumping, dusk return, night-only sleeping and sign-linked personal chest storage ready.");
        }
    }
}
