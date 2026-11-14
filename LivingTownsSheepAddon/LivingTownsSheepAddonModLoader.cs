using System;
using System.Collections.Generic;

namespace Game;

/// <summary>Loads the sheep behavior component contained in this addon assembly.</summary>
public sealed class LivingTownsSheepAddonModLoader : ModLoader
{
    public override void __ModInitialize()
    {
        ModsManager.RegisterHook("OnLoadingFinished", this);
    }

    public override void OnLoadingFinished(List<Action> actions)
    {
        // The component is discovered by the database Class mapping.
    }
}
