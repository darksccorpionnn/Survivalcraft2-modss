using System;
using System.Collections.Generic;
using Engine;
using HarmonyLib;

namespace Game
{
    public sealed class KoylulerSocialAddonLoader : ModLoader
    {
        private static Harmony s_harmony;

        public override void __ModInitialize()
        {
            try
            {
                s_harmony ??= new Harmony("darksccorpionnn.Koyluler.SocialRoaming.Test1");
                VillagerBedRoamingPatch.Install(s_harmony);
                Log.Information("[KoylulerSocial] Native idle roaming enhanced. Work, home and sleep behaviors unchanged.");
            }
            catch (Exception ex)
            {
                Log.Error("[KoylulerSocial] Could not install social roaming: " + ex);
            }
            ModsManager.RegisterHook("OnLoadingFinished", this);
        }

        public override void OnLoadingFinished(List<Action> actions)
        {
            try { VillagerBedRoamingPatch.Install(s_harmony); }
            catch (Exception ex) { Log.Error("[KoylulerSocial] Late install failed: " + ex); }
        }
    }
}
