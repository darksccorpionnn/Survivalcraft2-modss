using Engine;
using Engine.Animation;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

/// <summary>GLB creature model shared by sheep, rams and lambs.</summary>
public sealed class ComponentLivingTownsSheepModel : ComponentCreatureModel
{
    public override void AnimateCreature()
    {
        // GLB animation clips are selected by the JSON gait/death state controller.
        // Do not layer a second procedural animation over the skinned GLB rig.
    }

    public override void SyncAnimationParameters()
    {
        // ComponentCreatureModel populates the engine's native parameters such as
        // SpeedAbs, IsFeeding and DeathPhase. The JSON controllers depend on them.
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
