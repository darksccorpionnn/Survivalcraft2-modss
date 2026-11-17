using System.Collections.Generic;
using Engine;
using HarmonyLib;

namespace Game;

/// <summary>
/// The embedded LivingTownsDoorBehavior subsystem owns villager births.
/// This loader deliberately does not register any random surface-spawn types.
/// Inventory-title patch waits for the LivingTowns UI assembly and patches its
/// real constructor once it becomes available.
/// </summary>
public sealed class KoylulerModuModLoader : ModLoader
{
    private const string LogPrefix = "[KoylulerModu] ";
    private static Harmony s_harmony;

    public override void __ModInitialize()
    {
        s_harmony = new Harmony("darksccorpionnn.KoylulerModu.InventoryTitle");
        VillagerInventoryNamePatch.Install(s_harmony);
        ModsManager.RegisterHook("OnLoadingFinished", this);
        Log.Information(LogPrefix + "Initialized; random surface spawning is disabled; inventory-title hook is listening for LivingTowns.");
    }

    public override void OnLoadingFinished(List<System.Action> actions)
    {
        VillagerInventoryNamePatch.Install(s_harmony);
        Log.Information(LogPrefix + "World loaded. Door-linked villager births and inventory-only villager names are enabled.");
    }
}
