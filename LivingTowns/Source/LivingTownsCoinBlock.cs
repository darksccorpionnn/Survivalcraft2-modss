using System;
using Engine;
using Engine.Graphics;

namespace Game
{
    /// <summary>
    /// Shared renderer for Living Towns currency inventory items.
    /// </summary>
    public abstract class LivingTownsCoinBlock : Block
    {
        private readonly string m_modelAsset;
        private readonly string m_textureAsset;
        private BlockMesh m_coinMesh;
        private Texture2D m_coinTexture;

        protected LivingTownsCoinBlock(string modelAsset, string textureAsset)
        {
            m_modelAsset = modelAsset;
            m_textureAsset = textureAsset;
            IsCollidable = false;
            IsPlaceable = false;
            IsGatherable = false;
            IsFluidBlocker = false;
            MaxStacking = 99;
        }

        public override void Initialize()
        {
            base.Initialize();
            Model model = ContentManager.Get<Model>(m_modelAsset);
            if (model == null || model.Meshes.Count == 0 || model.Meshes[0].MeshParts.Count == 0)
                throw new InvalidOperationException("Currency model has no drawable mesh: " + m_modelAsset);

            m_coinMesh = new BlockMesh();
            ModelMesh mesh = model.Meshes[0];
            Matrix transform = BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone);
            m_coinMesh.AppendModelMeshPart(mesh.MeshParts[0], transform, false, false, false, false, Color.White);
            m_coinTexture = ContentManager.Get<Texture2D>(m_textureAsset);
            Log.Information("[LivingTowns] 3D currency loaded: " + GetType().Name + " / " + m_modelAsset);
        }

        public override void GenerateTerrainVertices(BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z)
        {
        }

        public override void DrawBlock(PrimitivesRenderer3D primitivesRenderer, int value, Color color,
            float size, ref Matrix matrix, DrawBlockEnvironmentData environmentData)
        {
            if (m_coinMesh == null || m_coinTexture == null)
                return;
            BlocksManager.DrawMeshBlock(primitivesRenderer, m_coinMesh, m_coinTexture, color,
                2f * size, ref matrix, environmentData);
        }
    }

    public sealed class SilverCoinBlock : LivingTownsCoinBlock
    {
        public SilverCoinBlock()
            : base("Models/LivingTownsCoins/SilverCoin", "Textures/LivingTownsCoins/SilverCoin")
        {
        }
    }

    public sealed class GoldCoinBlock : LivingTownsCoinBlock
    {
        public GoldCoinBlock()
            : base("Models/LivingTownsCoins/GoldCoin", "Textures/LivingTownsCoins/GoldCoin")
        {
        }
    }
}
