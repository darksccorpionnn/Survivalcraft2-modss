using System;
using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>
/// Static GLB stove mesh. Native furnace interaction is retained, but the
/// custom mesh is assigned AFTER base.Initialize so FurnaceBlock cannot replace
/// it with the vanilla furnace mesh.
/// </summary>
public sealed class WoodStoveBlock : FurnaceBlock
{
    // Do not inherit FurnaceBlock.Index (64). This custom block must receive
    // its own dynamically allocated content ID.
    public new static int Index = -1;

    internal const int LitDataFlag = 4;
    private const float ModelScale = 0.45f;
    private const float ModelCenterY = 0.45f;

    private Texture2D m_texture;

    public override BlockPlacementData GetPlacementValue(
        SubsystemTerrain subsystemTerrain,
        ComponentMiner componentMiner,
        int value,
        TerrainRaycastResult raycastResult)
    {
        // FurnaceBlock's implementation bakes its vanilla static Index into
        // the placement value. Keep its orientation logic, then substitute the
        // dynamically allocated ID of this custom block.
        BlockPlacementData placement = base.GetPlacementValue(
            subsystemTerrain, componentMiner, value, raycastResult);
        int stoveIndex = BlocksManager.GetBlockIndex<WoodStoveBlock>();
        if (stoveIndex <= 0)
            throw new InvalidOperationException("[FlatNature] WoodStove block ID was not allocated.");
        placement.Value = Terrain.ReplaceContents(placement.Value, stoveIndex);
        return placement;
    }

    public override void Initialize()
    {
        // Initialize native furnace behavior first. Its implementation assigns
        // the vanilla mesh arrays, so the custom GLB must be applied afterwards.
        base.Initialize();

        Model model = ContentManager.Get<Model>("Models/WoodStove/WoodStove");
        if (model == null || model.Meshes.Count == 0)
            throw new InvalidOperationException("[FlatNature] WoodStove GLB loaded without meshes.");

        ModelMesh mesh = model.Meshes[0];
        if (mesh.MeshParts.Count == 0)
            throw new InvalidOperationException("[FlatNature] WoodStove GLB mesh has no mesh parts.");

        m_texture = model.GetDefaultBaseColorTexture();
        if (m_texture == null)
            throw new InvalidOperationException("[FlatNature] WoodStove GLB has no base-color texture.");

        Matrix boneTransform = mesh.ParentBone != null
            ? BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone)
            : Matrix.Identity;
        Matrix scale = Matrix.CreateScale(ModelScale);

        // Block data low two bits represent horizontal facing. Bit 2 remains
        // reserved for the lit state used by GetHeat/GetEmittedLightAmount.
        m_blockMeshesByData = new BlockMesh[4];
        for (int rotation = 0; rotation < 4; rotation++)
        {
            BlockMesh blockMesh = new BlockMesh();
            Matrix transform =
                boneTransform *
                scale *
                Matrix.CreateRotationY(rotation * MathF.PI * 0.5f) *
                Matrix.CreateTranslation(0.5f, ModelCenterY, 0.5f);

            foreach (ModelMeshPart part in mesh.MeshParts)
                blockMesh.AppendModelMeshPart(part, transform, false, false, false, false, Color.White);

            m_blockMeshesByData[rotation] = blockMesh;
        }

        m_standaloneBlockMesh = new BlockMesh();
        Matrix standaloneTransform = boneTransform * scale;
        foreach (ModelMeshPart part in mesh.MeshParts)
            m_standaloneBlockMesh.AppendModelMeshPart(
                part, standaloneTransform, false, false, false, false, Color.White);

        Log.Information("[FlatNature] Custom WoodStove GLB mesh assigned: " + mesh.Name);
    }

    public override Texture2D GetDefaultTexture(int value) => m_texture;

    public override float GetHeat(int value)
    {
        return (Terrain.ExtractData(value) & LitDataFlag) != 0 ? 1f : 0f;
    }

    public override int GetEmittedLightAmount(int value)
    {
        return (Terrain.ExtractData(value) & LitDataFlag) != 0 ? 8 : 0;
    }
}
