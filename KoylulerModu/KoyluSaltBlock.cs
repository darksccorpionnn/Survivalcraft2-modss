using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>
/// A small custom salt item. Survivalcraft 2 has saltpeter, but no edible/field
/// salt block, so this item is kept separate rather than misusing saltpeter.
/// One item treats one farmer-built plot.
/// </summary>
public sealed class KoyluSaltBlock : CrossBlock
{
    private Texture2D m_texture;

    public override void Initialize()
    {
        base.Initialize();
        m_texture = ContentManager.Get<Texture2D>("Textures/KoyluSalt");
        IsTransparent = true;
        Log.Information("[KoylulerModu] Farmer plot salt item registered.");
    }

    public override int GetFaceTextureSlot(int face, int value) => 0;

    public override int GetTextureSlotCount(int value) => 1;

    public override Texture2D GetDefaultTexture(int value) => m_texture;
}
