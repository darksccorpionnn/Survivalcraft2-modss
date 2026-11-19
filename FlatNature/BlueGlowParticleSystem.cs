using Engine;

namespace Game;

/// <summary>
/// Low-opacity, enlarged blue fire particles that create a soft aura around
/// the sharper blue flame. This is a visual glow effect, not RGB voxel lighting.
/// </summary>
public sealed class BlueGlowParticleSystem : FireParticleSystem
{
    private static readonly Color GlowColor = new Color(0.10f, 0.34f, 1f, 0.20f);

    public BlueGlowParticleSystem(Vector3 position, float size, float maxVisibilityDistance)
        : base(position, size, maxVisibilityDistance)
    {
    }

    public override bool Simulate(float dt)
    {
        bool result = base.Simulate(dt);

        // Apply the tint after native simulation because new particles are reset
        // to the default fire color by the base implementation.
        foreach (var particle in Particles)
        {
            if (particle.IsActive)
                particle.Color = GlowColor;
        }

        return result;
    }
}
