using Engine;
using Engine.Graphics;
namespace Game
{
    /// <summary>A physical, placeable one-tile villager bed: v0.4 test geometry.</summary>
    public sealed class YeniNesilBedBlock : CubeBlock
    {
        private Texture2D m_bedTexture;
        public override void Initialize()
        {
            base.Initialize();
            m_bedTexture = ContentManager.Get<Texture2D>("Textures/YeniNesilBed");
            CanBeBuiltIntoFurniture = true;
            Log.Information("[YeniNesilBed] ID " + BlocksManager.GetBlockIndex<YeniNesilBedBlock>());
        }
        public override int GetFaceTextureSlot(int face, int value) => face == 4 ? 1 : 0;
        public override int GetTextureSlotCount(int value) => 2;
        public override Texture2D GetDefaultTexture(int value) => m_bedTexture;
    }
}
