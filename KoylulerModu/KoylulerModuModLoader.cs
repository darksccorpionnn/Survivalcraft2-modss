using System.Collections.Generic;
using System.Reflection;
using Engine;
using HarmonyLib;

namespace Game;

/// <summary>
/// Registers a postfix on the base-game hit method. This postfix runs after
/// LivingTowns opens its villager inventory from its own prefix.
/// </summary>
public sealed class KoylulerModuModLoader : ModLoader
{
    private const string LogPrefix = "[KoylulerModu] ";

    public override void __ModInitialize()
    {
        try
        {
            Harmony harmony = new Harmony("darksccorpionnn.KoylulerModu.InventoryTitle");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Information(LogPrefix + "Villager inventory title postfix registered on ComponentMiner.Hit.");
        }
        catch (System.Exception ex)
        {
            Log.Error(LogPrefix + "Could not register inventory title postfix: " + ex);
        }

        ModsManager.RegisterHook("OnLoadingFinished", this);
        Log.Information(LogPrefix + "Initialized; random surface spawning is disabled.");
    }

    public override void OnLoadingFinished(List<System.Action> actions)
    {
        Log.Information(LogPrefix + "World loaded. Door-linked villager birth subsystem is registered; names will be assigned when the inventory opens.");
    }
}
