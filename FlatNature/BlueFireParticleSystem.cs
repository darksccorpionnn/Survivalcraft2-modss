using Engine;

namespace Game;

/// <summary>
/// Native fire particles with a per-particle blue vertex tint.
/// Color belongs to each FireParticleSystem.Particle, not the outer system.
/// </summary>
public sealed class BlueFireParticleSystem : FireParticleSystem
{
    private static readonly Color FlameColor = new Color(0.20f, 0.58f, 1f, 1f);

    public BlueFireParticleSystem(Vector3 position, float size, float maxVisibilityDistance)
        : base(position, size, maxVisibilityDistance)
    {
    }

    public override bool Simulate(float dt)
    {
        bool result = base.Simulate(dt);

        // Native Simulate assigns white to newly spawned particles.
        // Reapply the tint after each step so active and new particles stay blue.
        foreach (var particle in Particles)
        {
            if (particle.IsActive)
                particle.Color = FlameColor;
        }

        return result;
    }
}
