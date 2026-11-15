using Engine;
using Engine.Animation;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

/// <summary>glTF sheep model with custom soft footfalls instead of the inherited clop sound.</summary>
public sealed class ComponentLivingTownsSheepModel : ComponentCreatureModel
{
    private SubsystemAudio m_audio;

    public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap)
    {
        base.Load(valuesDictionary, idToEntityMap);
        m_audio = Project.FindSubsystem<SubsystemAudio>(throwOnError: true);
    }

    // glTF clips drive this creature. No legacy procedural bone animation is used.
    public override void AnimateCreature()
    {
    }

    public override void SyncAnimationParameters()
    {
        // Required: the base class supplies IsFeeding, SpeedAbs, DeathPhase and other
        // animation parameters consumed by the GLB animation controller.
        base.SyncAnimationParameters();
    }

    public override void HandleAnimationEvent(AnimationEvent animationEvent)
    {
        if (animationEvent != null && (animationEvent.Name == "Footstep" || animationEvent.Name == "SheepFootstep"))
        {
            if (m_audio != null && m_componentCreature != null && m_componentCreature.ComponentBody != null)
            {
                m_audio.PlayRandomSound("Audio/LivingTowns/Sheep/footstep", 0.22f, -0.05f,
                    m_componentCreature.ComponentBody.Position, 4.2f, autoDelay: false);
            }

            // Do not call base for Footstep: it plays the default quadruped clop.
            return;
        }

        // Preserve built-in handling for all other events (including attack/death events).
        base.HandleAnimationEvent(animationEvent);
    }
}