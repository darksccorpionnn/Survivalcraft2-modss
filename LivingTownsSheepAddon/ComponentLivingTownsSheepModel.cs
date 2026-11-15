using Engine;
using Engine.Animation;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

/// <summary>glTF sheep model using the game's native terrain/material footsteps.</summary>
public sealed class ComponentLivingTownsSheepModel : ComponentCreatureModel
{
    public override void AnimateCreature()
    {
    }

    public override void SyncAnimationParameters()
    {
        // The base class supplies IsFeeding, SpeedAbs, DeathPhase and animation parameters.
        base.SyncAnimationParameters();
    }

    public override void HandleAnimationEvent(AnimationEvent animationEvent)
    {
        if (animationEvent != null &&
            (animationEvent.Name == "Footstep" || animationEvent.Name == "SheepFootstep"))
        {
            // Use the same surface/material footstep system used by native livestock.
            // This gives dirt, grass, stone and other terrain their correct sounds.
            if (m_componentCreature != null && m_componentCreature.ComponentCreatureSounds != null)
                m_componentCreature.ComponentCreatureSounds.PlayFootstepSound(0.85f);
            return;
        }

        base.HandleAnimationEvent(animationEvent);
    }
}
