using System;
using System.Collections.Generic;
using Engine;\nusing HarmonyLib;

namespace Game
{
    /// <summary>
    /// Minimal API 1.9.5.1 smoke-test mod.
    /// It logs once during initialization and once after game loading.
    /// </summary>
    public sealed class TownBootstrapModLoader : ModLoader
    {
        public const string PackageName = "darksccorpionnn.TownBootstrap";

        public override void __ModInitialize()
        {
            ModsManager.RegisterHook("OnLoadingFinished", this);\n            new Harmony(PackageName).PatchAll();
            Log.Information("[TownBootstrap] Mod initialized. Package: " + PackageName);
        }

        public override void OnLoadingFinished(List<Action> actions)
        {
            Log.Information("[TownBootstrap] OnLoadingFinished reached. The mod is active.");
        }
    }
}
