using System;
using System.Collections.Generic;
using HarmonyLib;

namespace Game;

/// <summary>
/// Installs only the villager inventory-heading patch.
/// LivingTowns remains the sole owner of villager navigation: wooden-sign
/// work destinations, sunset return-home routing, doors and beds.
/// No secondary movement or patrol patch is installed.
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
            Log.Information(LogPrefix + "Inventory-name patch initialized. LivingTowns sign/work/home/door/bed navigation is left untouched.");
        }
        catch (Exception ex)
        {
            Log.Error(LogPrefix + "Could not initialize villager inventory patch: " + ex);
        }

        ModsManager.RegisterHook("OnLoadingFinished", this);
    }

    public override void OnLoadingFinished(List<Action> actions)
    {
        try
        {
            VillagerInventoryNamePatch.Install(s_harmony);
            Log.Information(LogPrefix + "World loaded. No secondary villager movement patch was installed; native LivingTowns navigation remains in control.");
        }
        catch (Exception ex)
        {
            Log.Error(LogPrefix + "World-load villager inventory patch retry failed: " + ex);
        }
    }
}
