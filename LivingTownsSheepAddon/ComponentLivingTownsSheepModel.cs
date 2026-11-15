using Engine;
using Engine.Animation;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

/// <summary>Four-legged glTF model with soft sheep footfalls instead of inherited hoof clops.</summary>
public sealed class ComponentLivingTownsSheepModel : ComponentFourLeggedModel
{
    private SubsystemAudio m_audio;

    public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap)
    {
        base.Load(valuesDictionary, idToEntityMap);
        m_audio = Project.FindSubsystem<SubsystemAudio>(throwOnError: true);
    }

    public override void HandleAnimationEvent(AnimationEvent animationEvent)
    {
        if (animationEvent != null && animationEvent.Name == "Footstep")
        {
            if (m_audio != null && m_componentCreature != null && m_componentCreature.ComponentBody != null)
            {
                m_audio.PlayRandomSound("Audio/LivingTowns/Sheep/footstep", 0.22f, -0.05f,
                    m_componentCreature.ComponentBody.Position, 4.2f, autoDelay: false);
            }
            // Skip the inherited handler for Footstep so its hoof/clop sound is not played.
            return;
        }
        base.HandleAnimationEvent(animationEvent);
    }
}