using System;
using System.Collections.Generic;

namespace Game;

/// <summary>Loads the sheep behavior component contained in this addon assembly.</summary>
public sealed class LivingTownsSheepAddonModLoader : ModLoader
{
    public override void __ModInitialize()
    {
        ModsManager.RegisterHook("OnLoadingFinished", this);
        Log.Information("[LivingTowns Sheep] Sheep follow/flee behavior component registered.");
    }

    public override void OnLoadingFinished(List<Action> actions)
    {
        Log.Information("[LivingTowns Sheep] Loaded. Grass-follow and player-flee AI is ready.");
    }
}
