using Engine;
using System.Collections.Generic;

namespace Game;

/// <summary>
/// The embedded LivingTownsDoorBehavior subsystem owns villager births.
/// This loader deliberately does not register any random surface-spawn types.
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
        Log.Information(LogPrefix + "World loaded. Door-linked villager birth subsystem is registered in the project database.");
    }
}