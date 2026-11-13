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
            Log.Information("[LivingTowns] 0.4.40 initialized.");
        }

        public override void OnLoadingFinished(List<Action> actions)
        {
            Log.Information("[LivingTowns] 0.4.40 villagers, persistent unique names, named wall-sign sleep positions, exclusive rest cells, safe home-rest fallback, controlled obstacle jumping, 100-block locked tree harvesting with leaves, 25-block canopy height, dusk return, night-only sleeping and sign-linked personal chest storage ready.");
        }
    }
}
