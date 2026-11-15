using Engine;
using Engine.Animation;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

/// <summary>GLB-driven model shared by sheep, rams and lambs.</summary>
public sealed class ComponentLivingTownsSheepModel : ComponentCreatureModel
{
    public override void AnimateCreature()
    {
        // GLB clips are evaluated by AnimationController before this legacy fallback.
        // Keep this empty so no second procedural gait can rotate the same rig.
    }

    public override void SyncAnimationParameters()
    {
        base.SyncAnimationParameters();

        AnimationController controller = AnimationController;
        if (controller == null || m_componentCreature == null ||
            m_componentCreature.ComponentBody == null)
            return;

        // The FourLegged template exposes Gait and Activity as enum state tracks.
        // Drive them explicitly from the live creature and its behavior orders.
        float speed = m_componentCreature.ComponentBody.Velocity.Length();
        string gait = speed > 0.85f ? "Canter" :
                      speed > 0.08f ? "Walk" : "Idle";
        string activity = FeedOrder ? "Feed" :
                          AttackOrder ? "Attack" : "None";

        controller.Parameters.SetString("Gait", gait);
        controller.Parameters.SetString("Activity", activity);
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
