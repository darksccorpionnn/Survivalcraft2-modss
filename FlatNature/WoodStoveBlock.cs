using System;
using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>
/// Static stove mesh. It inherits the native furnace placement, collision and
/// interaction rules, while its heat/light state is stored in data bit 2.
/// </summary>
public sealed class WoodStoveBlock : FurnaceBlock
{
    internal const int LitDataFlag = 4;
    private const float ModelScale = 0.45f;
    private const float ModelCenterY = 0.45f;

    private Texture2D m_texture;

    public override void Initialize()
    {
        Model model;
        ModelMesh mesh;

        try
        {
            model = ContentManager.Get<Model>("Models/WoodStove/WoodStove");
            if (model.Meshes.Count == 0)
                throw new InvalidOperationException("Wood stove GLB contains no meshes.");
            mesh = model.Meshes[0];
        }
        catch (Exception ex)
        {
            // Allows the code package to build and load before the supplied GLB
            // is copied into Assets/Models/WoodStove. The final package uses the
            // custom GLB and does not take this fallback path.
            Log.Warning("[FlatNature] Stove GLB not found; using the vanilla furnace mesh temporarily: " + ex.Message);
            model = ContentManager.Get<Model>("Models/Furnace");
            mesh = model.FindMesh("Furnace", true);
        }

        if (mesh.MeshParts.Count == 0)
            throw new InvalidOperationException("Wood stove model mesh has no mesh parts.");

        m_texture = model.GetDefaultBaseColorTexture();
        if (m_texture == null && FurnaceBlock.Index > 0)
            m_texture = BlocksManager.Blocks[FurnaceBlock.Index].GetDefaultTexture(FurnaceBlock.Index);

        Matrix boneTransform = mesh.ParentBone != null
            ? BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone)
            : Matrix.Identity;
        Matrix scale = Matrix.CreateScale(ModelScale);

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

        base.Initialize();
        Log.Information("[FlatNature] Wood stove block initialized. GLB mesh: " + mesh.Name);
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
