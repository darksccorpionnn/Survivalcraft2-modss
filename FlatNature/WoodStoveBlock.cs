using System;
using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>
/// Static GLB stove mesh. Native furnace interaction is retained. If the optional
/// custom model is absent, the initialized vanilla furnace mesh remains usable.
/// </summary>
public sealed class WoodStoveBlock : FurnaceBlock
{
    public new static int Index = -1;
    internal const int LitDataFlag = 4;
    private const float ModelScale = 0.45f;
    private const float ModelCenterY = 0.45f;
    private Texture2D m_texture;

    public override BlockPlacementData GetPlacementValue(SubsystemTerrain subsystemTerrain, ComponentMiner componentMiner, int value, TerrainRaycastResult raycastResult)
    {
        BlockPlacementData placement = base.GetPlacementValue(subsystemTerrain, componentMiner, value, raycastResult);
        int stoveIndex = BlocksManager.GetBlockIndex<WoodStoveBlock>();
        if (stoveIndex <= 0) throw new InvalidOperationException("[FlatNature] WoodStove block ID was not allocated.");
        placement.Value = Terrain.ReplaceContents(placement.Value, stoveIndex);
        return placement;
    }

    public override bool IsFaceTransparent(SubsystemTerrain subsystemTerrain, int face, int value) => true;

    public override void Initialize()
    {
        base.Initialize();
        Model model = ContentManager.Get<Model>("Models/WoodStove/WoodStove");
        if (model == null || model.Meshes.Count == 0)
        {
            Log.Warning("[FlatNature] WoodStove model missing; using native furnace mesh fallback.");
            return;
        }
        ModelMesh mesh = model.Meshes[0];
        Texture2D texture = model.GetDefaultBaseColorTexture();
        if (mesh.MeshParts.Count == 0 || texture == null)
        {
            Log.Warning("[FlatNature] WoodStove model incomplete; using native furnace mesh fallback.");
            return;
        }

        Matrix boneTransform = mesh.ParentBone != null ? BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone) : Matrix.Identity;
        BlockMesh[] rotations = new BlockMesh[4];
        for (int rotation = 0; rotation < 4; rotation++)
        {
            BlockMesh bm = new BlockMesh();
            Matrix transform = boneTransform * Matrix.CreateScale(ModelScale) *
                Matrix.CreateRotationY(rotation * MathF.PI * 0.5f) *
                Matrix.CreateTranslation(0.5f, ModelCenterY, 0.5f);
            foreach (ModelMeshPart part in mesh.MeshParts)
                bm.AppendModelMeshPart(part, transform, false, false, false, false, Color.White);
            rotations[rotation] = bm;
        }
        m_blockMeshesByData = new BlockMesh[16];
        for (int data = 0; data < 16; data++) m_blockMeshesByData[data] = rotations[data & 3];

        m_standaloneBlockMesh = new BlockMesh();
        Matrix standalone = boneTransform * Matrix.CreateScale(ModelScale);
        foreach (ModelMeshPart part in mesh.MeshParts)
            m_standaloneBlockMesh.AppendModelMeshPart(part, standalone, false, false, false, false, Color.White);
        m_texture = texture;
        Log.Information("[FlatNature] Custom WoodStove GLB mesh assigned: " + mesh.Name);
    }

    public override Texture2D GetDefaultTexture(int value) => m_texture ?? base.GetDefaultTexture(value);
    public override float GetHeat(int value) => (Terrain.ExtractData(value) & LitDataFlag) != 0 ? 1f : 0f;
    public override int GetEmittedLightAmount(int value) => (Terrain.ExtractData(value) & LitDataFlag) != 0 ? 8 : 0;
}