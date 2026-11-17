using System.Collections.Generic;
using Engine;
using HarmonyLib;

namespace Game;

/// <summary>
/// Installs a late-bound postfix on LivingTownsTradeWidget.TryOpen, the exact
/// method that creates the villager inventory panel.
/// </summary>
public sealed class KoylulerModuModLoader : ModLoader
{
    private const string LogPrefix = "[KoylulerModu] ";
    private static Harmony s_harmony;

    public override void __ModInitialize()
    {
        try
        {
            s_harmony = new Harmony("darksccorpionnn.KoylulerModu.InventoryTitle");
            VillagerInventoryNamePatch.Install(s_harmony);
            Log.Information(LogPrefix + "Listening for LivingTownsTradeWidget.TryOpen.");
        }
        catch (System.Exception ex)
        {
            Log.Error(LogPrefix + "Could not install inventory title hook: " + ex);
        }

        ModsManager.RegisterHook("OnLoadingFinished", this);
        Log.Information(LogPrefix + "Initialized; random surface spawning is disabled.");
    }

    public override void OnLoadingFinished(List<System.Action> actions)
    {
        VillagerInventoryNamePatch.Install(s_harmony);
        Log.Information(LogPrefix + "World loaded. Villager identity will be written into the villager inventory title.");
    }
}
