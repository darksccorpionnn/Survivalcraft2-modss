using Engine;
using Engine.Animation;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

/// <summary>Legacy-compatible creature model wrapper. GLB sheep variants use the engine FourLeggedModel template.</summary>
public sealed class ComponentLivingTownsSheepModel : ComponentCreatureModel
{
    public override void AnimateCreature()
    {
        // GLB animation clips are driven by AnimationConfigPath / AnimationController.
        // This remains empty for legacy compatibility; do not apply legacy procedural
        // four-legged rotations to the Meshy GLB skeleton.
    }

    public override void SyncAnimationParameters()
    {
        base.SyncAnimationParameters();
    }

    public override void HandleAnimationEvent(AnimationEvent animationEvent)
    {
        if (animationEvent != null &&
            (animationEvent.Name == "Footstep" || animationEvent.Name == "SheepFootstep"))
        {
            if (m_componentCreature != null &&
                m_componentCreature.ComponentCreatureSounds != null)
                m_componentCreature.ComponentCreatureSounds.PlayFootstepSound(0.85f);
            return;
        }

        base.HandleAnimationEvent(animationEvent);
    }
}
