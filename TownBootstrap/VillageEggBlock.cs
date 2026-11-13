using Engine;
using Engine.Graphics;

namespace Game
{
    /// <summary>
    /// Cross-plane Village Egg trigger. The transparent texture gives it an egg
    /// silhouette instead of the default cube appearance.
    /// </summary>
    public sealed class VillageEggBlock : CrossBlock
    {
        private Texture2D m_texture;

        public override void Initialize()
        {
            base.Initialize();
            m_texture = ContentManager.Get<Texture2D>("Textures/VillageEgg");
            IsTransparent = true;
            int contents = BlocksManager.GetBlockIndex<VillageEggBlock>();
            Log.Information("[TownBootstrap] Cross-shaped Village Egg registered. Contents=" + contents);
        }

        public override int GetFaceTextureSlot(int face, int value) => 0;

        public override int GetTextureSlotCount(int value) => 1;

        public override Texture2D GetDefaultTexture(int value) => m_texture;
    }
}