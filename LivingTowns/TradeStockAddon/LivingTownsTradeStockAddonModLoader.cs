using System;
using System.Collections.Generic;
using HarmonyLib;

namespace Game
{
    public sealed class LivingTownsTradeStockAddonModLoader : ModLoader
    {
        private const string HarmonyId = "darksccorpionnn.LivingTowns.TradeStockAddon";

        public override void __ModInitialize()
        {
            try
            {
                Harmony harmony = new Harmony(HarmonyId);
                LivingTownsTradeStockPatch.Install(harmony);
                Log.Information("[LivingTowns Trade Stock] 0.4.60 addon initialized.");
                ModsManager.RegisterHook("OnLoadingFinished", this);
            }
            catch (Exception ex)
            {
                Log.Warning("[LivingTowns Trade Stock] Initialization failed: " + ex);
            }
        }

        public override void OnLoadingFinished(List<Action> actions)
        {
            Log.Information("[LivingTowns Trade Stock] Profession-based Survivalcraft 2 inventory stock patch is ready.");
        }
    }
}
