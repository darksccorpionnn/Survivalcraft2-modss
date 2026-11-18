using System;
using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>
/// Wall-mounted iron lantern. Placement/orientation follows the native TorchBlock
/// wall-face convention, while rendering uses the supplied IronLantern GLB.
/// </summary>
public sealed class IronLanternBlock : TorchBlock
{
    public new static int Index = -1;

    private const float ModelScale = 1.0f;
    private Texture2D m_texture;

    public override BlockPlacementData GetPlacementValue(
        SubsystemTerrain subsystemTerrain,
        ComponentMiner componentMiner,
        int value,
        TerrainRaycastResult raycastResult)
    {
        // TorchBlock encodes the four horizontal support faces as variants 0..3.
        // Reject floor and ceiling faces: this lantern is a wall fixture only.
        int face = raycastResult.CellFace.Face;
        if (face < 0 || face > 3)
        {
            BlockPlacementData rejected = default;
            rejected.CellFace = raycastResult.CellFace;
            return rejected;
        }

        BlockPlacementData placement = base.GetPlacementValue(
            subsystemTerrain, componentMiner, value, raycastResult);

        int lanternIndex = BlocksManager.GetBlockIndex<IronLanternBlock>();
        if (lanternIndex <= 0)
            throw new InvalidOperationException("[FlatNature] IronLantern block ID was not allocated.");

        placement.Value = Terrain.ReplaceContents(placement.Value, lanternIndex);
        return placement;
    }

    public override float GetHeat(int value) => 0f;

    public override int GetEmittedLightAmount(int value) => 12;

    public override bool IsFaceTransparent(
        SubsystemTerrain subsystemTerrain,
        int face,
        int value) => true;

    public override void Initialize()
    {
        // Initializes TorchBlock's native variant/collision arrays and the base Block.
        base.Initialize();

        Model model = ContentManager.Get<Model>("Models/IronLantern/IronLantern");
        if (model == null || model.Meshes.Count == 0)
            throw new InvalidOperationException("[FlatNature] IronLantern GLB loaded without meshes.");

        ModelMesh mesh = model.Meshes[0];
        if (mesh.MeshParts.Count == 0)
            throw new InvalidOperationException("[FlatNature] IronLantern GLB mesh has no mesh parts.");

        m_texture = model.GetDefaultBaseColorTexture();
        if (m_texture == null)
            throw new InvalidOperationException("[FlatNature] IronLantern GLB has no base-color texture.");

        Matrix boneTransform = mesh.ParentBone != null
            ? BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone)
            : Matrix.Identity;

        // The packaged GLB already has its bottom aligned at Y ~= 0.024.
        // Keep it vertical and move its rear edge exactly to the wall plane.
        // CellFace 0/1/2/3 follows Survivalcraft's native +Z/+X/-Z/-X order.
        float[] yaw =
        {
            0f,
            (float)Math.PI / 2f,
            (float)Math.PI,
            3f * (float)Math.PI / 2f
        };

        Vector3[] offsets =
        {
            new Vector3(0.5f, 0f, 0.27075f), // wall at local z = 0
            new Vector3(0.27075f, 0f, 0.5f), // wall at local x = 0
            new Vector3(0.5f, 0f, 0.72925f), // wall at local z = 1
            new Vector3(0.72925f, 0f, 0.5f)  // wall at local x = 1
        };

        m_blockMeshesByVariant = new BlockMesh[5];
        m_collisionBoxes = new BoundingBox[5][];

        for (int variant = 0; variant < 4; variant++)
        {
            BlockMesh blockMesh = new BlockMesh();
            Matrix transform =
                boneTransform *
                Matrix.CreateScale(ModelScale) *
                Matrix.CreateRotationY(yaw[variant]) *
                Matrix.CreateTranslation(offsets[variant]);

            foreach (ModelMeshPart part in mesh.MeshParts)
            {
                blockMesh.AppendModelMeshPart(
                    part, transform, false, false, false, false, Color.White);
            }

            m_blockMeshesByVariant[variant] = blockMesh;
            m_collisionBoxes[variant] = new[] { blockMesh.CalculateBoundingBox() };
        }

        // Variant 4 is intentionally empty: floor placement is not supported.
        m_blockMeshesByVariant[4] = new BlockMesh();
        m_collisionBoxes[4] = Array.Empty<BoundingBox>();

        // Inventory/hand preview only; actual world placement uses the four wall meshes.
        m_standaloneBlockMesh = new BlockMesh();
        Matrix standaloneTransform =
            boneTransform *
            Matrix.CreateScale(ModelScale) *
            Matrix.CreateTranslation(0.5f, 0f, 0.5f);

        foreach (ModelMeshPart part in mesh.MeshParts)
        {
            m_standaloneBlockMesh.AppendModelMeshPart(
                part, standaloneTransform, false, false, false, false, Color.White);
        }

        Log.Information("[FlatNature] Wall-mounted IronLantern GLB initialized.");
    }

    public override void GenerateTerrainVertices(
        BlockGeometryGenerator generator,
        TerrainGeometry geometry,
        int value,
        int x,
        int y,
        int z)
    {
        int variant = Terrain.ExtractData(value);
        if (variant >= 0 && variant < 4)
        {
            // Route the model vertices into a geometry subset bound to the GLB's
            // embedded base-color texture. Using geometry.SubsetOpaque here binds
            // the terrain atlas and causes the UVs to sample unrelated block tiles.
            TerrainGeometry lanternGeometry = geometry.GetGeometry(m_texture);
            generator.GenerateMeshVertices(
                this,
                x,
                y,
                z,
                m_blockMeshesByVariant[variant],
                Color.White,
                null,
                lanternGeometry.SubsetOpaque);
        }
    }

    public override BoundingBox[] GetCustomCollisionBoxes(SubsystemTerrain terrain, int value)
    {
        int variant = Terrain.ExtractData(value);
        return variant >= 0 && variant < 4 ? m_collisionBoxes[variant] : null;
    }

    public override void DrawBlock(
        PrimitivesRenderer3D primitivesRenderer,
        int value,
        Color color,
        float size,
        ref Matrix matrix,
        DrawBlockEnvironmentData environmentData)
    {
        BlocksManager.DrawMeshBlock(
            primitivesRenderer,
            m_standaloneBlockMesh,
            m_texture,
            color,
            2f * size,
            ref matrix,
            environmentData);
    }

    public override Texture2D GetDefaultTexture(int value) => m_texture;
}
