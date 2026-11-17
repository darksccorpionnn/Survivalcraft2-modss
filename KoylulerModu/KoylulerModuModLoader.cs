using System.Collections.Generic;
using System.Reflection;
using Engine;
using HarmonyLib;

namespace Game;

/// <summary>
/// The embedded LivingTownsDoorBehavior subsystem owns villager births.
/// This loader deliberately does not register any random surface-spawn types.
/// A small Harmony postfix puts the persistent villager name in the inventory title.
/// </summary>
public sealed class KoylulerModuModLoader : ModLoader
{
    private const string LogPrefix = "[KoylulerModu] ";

    public override void __ModInitialize()
    {
        ModsManager.RegisterHook("OnLoadingFinished", this);
        Log.Information(LogPrefix + "Initialized; random surface spawning is disabled.");
    }

    public override void OnLoadingFinished(List<System.Action> actions)
    {
        try
        {
            Harmony harmony = new Harmony("darksccorpionnn.KoylulerModu.InventoryTitle");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Information(LogPrefix + "Inventory-title name patch installed; floating name labels are disabled.");
        }
        catch (System.Exception ex)
        {
            Log.Error(LogPrefix + "Inventory-title patch could not be installed: " + ex);
        }

        Log.Information(LogPrefix + "World loaded. Door-linked villager birth subsystem and inventory-name display are ready.");
    }
}
