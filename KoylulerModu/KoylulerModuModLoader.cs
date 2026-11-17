using System.Collections.Generic;
using Engine;
using HarmonyLib;

namespace Game;

/// <summary>
/// Installs the name hook for the actual LivingTowns inventory panel.
/// The hook attaches to its constructor and TryOpen method as soon as
/// the LivingTowns assembly is available.
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
            Log.Information(LogPrefix + "Listening for the LivingTowns villager inventory panel.");
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
        Log.Information(LogPrefix + "World loaded. Villager names are assigned when their inventory is opened.");
    }
}
