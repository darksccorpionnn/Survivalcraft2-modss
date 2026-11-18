using System;
using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>
/// Decorative iron kettle designed to sit on top of the wood stove.
/// It is a separate placeable block and does not alter furnace fuel or heat logic.
/// </summary>
public sealed class IronKettleBlock : FurnaceBlock
{
    public new static int Index = -1;

    private const float ModelScale = 0.8f;
    private const float ModelOffsetY = -0.2f;
    private Texture2D m_texture;

    public override BlockPlacementData GetPlacementValue(
        SubsystemTerrain subsystemTerrain,
        ComponentMiner componentMiner,
        int value,
        TerrainRaycastResult raycastResult)
    {
        BlockPlacementData placement = base.GetPlacementValue(
            subsystemTerrain, componentMiner, value, raycastResult);
        int kettleIndex = BlocksManager.GetBlockIndex<IronKettleBlock>();
        if (kettleIndex <= 0)
            throw new InvalidOperationException("[FlatNature] IronKettle block ID was not allocated.");
        placement.Value = Terrain.ReplaceContents(placement.Value, kettleIndex);
        return placement;
    }

    public override float GetHeat(int value) => 0f;
    public override int GetEmittedLightAmount(int value) => 0;

    public override bool IsFaceTransparent(SubsystemTerrain subsystemTerrain, int face, int value)
    {
        return true;
    }

    public override void Initialize()
    {
        base.Initialize();

        Model model = ContentManager.Get<Model>("Models/IronKettle/IronKettle");
        if (model == null || model.Meshes.Count == 0)
            throw new InvalidOperationException("[FlatNature] IronKettle GLB loaded without meshes.");

        ModelMesh mesh = model.Meshes[0];
        if (mesh.MeshParts.Count == 0)
            throw new InvalidOperationException("[FlatNature] IronKettle GLB mesh has no mesh parts.");

        m_texture = model.GetDefaultBaseColorTexture();
        if (m_texture == null)
            throw new InvalidOperationException("[FlatNature] IronKettle GLB has no base-color texture.");

        Matrix boneTransform = mesh.ParentBone != null
            ? BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone)
            : Matrix.Identity;
        Matrix transform =
            boneTransform *
            Matrix.CreateScale(ModelScale) *
            Matrix.CreateTranslation(0.5f, ModelOffsetY, 0.5f);

        m_blockMeshesByData = new BlockMesh[16];
        for (int data = 0; data < m_blockMeshesByData.Length; data++)
        {
            BlockMesh blockMesh = new BlockMesh();
            foreach (ModelMeshPart part in mesh.MeshParts)
                blockMesh.AppendModelMeshPart(part, transform, false, false, false, false, Color.White);
            m_blockMeshesByData[data] = blockMesh;
        }

        m_standaloneBlockMesh = new BlockMesh();
        foreach (ModelMeshPart part in mesh.MeshParts)
            m_standaloneBlockMesh.AppendModelMeshPart(
                part, boneTransform * Matrix.CreateScale(ModelScale), false, false, false, false, Color.White);

        Log.Information("[FlatNature] Iron kettle GLB mesh assigned: " + mesh.Name);
    }

    public override Texture2D GetDefaultTexture(int value) => m_texture;
}
