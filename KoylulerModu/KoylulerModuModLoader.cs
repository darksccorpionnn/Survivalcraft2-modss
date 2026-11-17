using System;
using System.Collections.Generic;
using System.Reflection;
using Engine;
using HarmonyLib;

namespace Game;

/// <summary>
/// Installs a transpiler that replaces the literal "Köylünün Takas Stoğu"
/// inside LivingTownsTradeWidget's constructor with the current villager name.
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
            Log.Information(LogPrefix + "Direct villager stock-label replacement initialized.");
        }
        catch (Exception ex)
        {
            Log.Error(LogPrefix + "Could not initialize direct stock-label replacement: " + ex);
        }

        ModsManager.RegisterHook("OnLoadingFinished", this);
    }

    public override void OnLoadingFinished(List<Action> actions)
    {
        try
        {
            VillagerInventoryNamePatch.Install(s_harmony);
            Log.Information(LogPrefix + "World loaded. The hard-coded trade-stock caption will be replaced by each villager's name.");
        }
        catch (Exception ex)
        {
            Log.Error(LogPrefix + "Inventory title hook retry failed: " + ex);
        }
    }
}
