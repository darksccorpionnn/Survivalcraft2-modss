using System;
using System.Collections.Generic;
using Engine;
using HarmonyLib;

namespace Game
{
    public sealed class LivingTownsTradeStockAddonModLoader : ModLoader
    {
        private const string HarmonyId = "darksccorpionnn.LivingTowns.TradeStockAddon";
        private Harmony m_harmony;

        public override void __ModInitialize()
        {
            try
            {
                m_harmony = new Harmony(HarmonyId);
                ModsManager.RegisterHook("OnLoadingFinished", this);
                Log.Information("[LivingTowns Trade Stock] 0.4.61 currency shop initialized; waiting for world loading to finish.");
            }
            catch (Exception ex)
            {
                Log.Warning("[LivingTowns Trade Stock] Initialization failed: " + ex);
            }
        }

        public override void OnLoadingFinished(List<Action> actions)
        {
            try
            {
                if (m_harmony != null)
                    LivingTownsTradeStockPatch.Install(m_harmony);
                Log.Information("[LivingTowns Trade Stock] Profession currency shop and stock patch are ready.");
            }
            catch (Exception ex)
            {
                Log.Warning("[LivingTowns Trade Stock] Patch installation failed: " + ex);
            }
        }
    }
}
