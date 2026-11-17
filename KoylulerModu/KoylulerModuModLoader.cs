using System;
using System.Collections.Generic;
using System.Reflection;
using Engine;
using HarmonyLib;

namespace Game;

/// <summary>
/// Installs patches for the villager inventory heading and bed-centred idle roaming.
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
            VillagerBedRoamingPatch.Install(s_harmony);
            Log.Information(LogPrefix + "Villager name and bed-centred idle-roaming patches initialized.");
        }
        catch (Exception ex)
        {
            Log.Error(LogPrefix + "Could not initialize villager patches: " + ex);
        }

        ModsManager.RegisterHook("OnLoadingFinished", this);
    }

    public override void OnLoadingFinished(List<Action> actions)
    {
        try
        {
            VillagerInventoryNamePatch.Install(s_harmony);
            VillagerBedRoamingPatch.Install(s_harmony);
            Log.Information(LogPrefix + "World loaded. Villager name and 25-block bed-roaming patches are active when the LivingTowns controller is available.");
        }
        catch (Exception ex)
        {
            Log.Error(LogPrefix + "World-load villager patch retry failed: " + ex);
        }
    }
}
