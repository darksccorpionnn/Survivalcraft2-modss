using System;
using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>
/// Wall-mounted iron lantern. Uses its GLB when available and safely falls back
/// to the native torch mesh when the optional model asset is absent.
/// </summary>
public sealed class IronLanternBlock : TorchBlock
{
    public new static int Index = -1;
    private const float ModelScale = 1.0f;
    private Texture2D m_texture;

    public override BlockPlacementData GetPlacementValue(SubsystemTerrain subsystemTerrain, ComponentMiner componentMiner, int value, TerrainRaycastResult raycastResult)
    {
        int face = raycastResult.CellFace.Face;
        if (face < 0 || face > 3) { BlockPlacementData rejected = default; rejected.CellFace = raycastResult.CellFace; return rejected; }
        BlockPlacementData placement = base.GetPlacementValue(subsystemTerrain, componentMiner, value, raycastResult);
        int index = BlocksManager.GetBlockIndex<IronLanternBlock>();
        if (index <= 0) throw new InvalidOperationException("[FlatNature] IronLantern block ID was not allocated.");
        placement.Value = Terrain.ReplaceContents(placement.Value, index);
        return placement;
    }

    public override float GetHeat(int value) => 0f;
    public override int GetEmittedLightAmount(int value) => 12;
    public override bool IsFaceTransparent(SubsystemTerrain subsystemTerrain, int face, int value) => true;

    public override void Initialize()
    {
        base.Initialize();
        Model model = ContentManager.Get<Model>("Models/IronLantern/IronLantern");
        if (model == null || model.Meshes.Count == 0)
        {
            Log.Warning("[FlatNature] IronLantern model missing; using native torch mesh fallback.");
            return;
        }
        ModelMesh mesh = model.Meshes[0];
        Texture2D texture = model.GetDefaultBaseColorTexture();
        if (mesh.MeshParts.Count == 0 || texture == null)
        {
            Log.Warning("[FlatNature] IronLantern model incomplete; using native torch mesh fallback.");
            return;
        }

        Matrix bone = mesh.ParentBone != null ? BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone) : Matrix.Identity;
        float[] yaw = { 0f, (float)Math.PI / 2f, (float)Math.PI, 3f * (float)Math.PI / 2f };
        Vector3[] offsets =
        {
            new Vector3(0.5f, 0f, 0.27075f), new Vector3(0.27075f, 0f, 0.5f),
            new Vector3(0.5f, 0f, 0.72925f), new Vector3(0.72925f, 0f, 0.5f)
        };
        BlockMesh[] meshes = new BlockMesh[5];
        BoundingBox[][] boxes = new BoundingBox[5][];
        for (int i = 0; i < 4; i++)
        {
            BlockMesh bm = new BlockMesh();
            Matrix transform = bone * Matrix.CreateScale(ModelScale) *
                Matrix.CreateRotationY(yaw[i]) * Matrix.CreateTranslation(offsets[i]);
            foreach (ModelMeshPart part in mesh.MeshParts)
                bm.AppendModelMeshPart(part, transform, false, false, false, false, Color.White);
            meshes[i] = bm;
            boxes[i] = new[] { bm.CalculateBoundingBox() };
        }
        meshes[4] = new BlockMesh();
        boxes[4] = Array.Empty<BoundingBox>();
        BlockMesh standalone = new BlockMesh();
        Matrix st = bone * Matrix.CreateScale(ModelScale) * Matrix.CreateTranslation(0.5f, 0f, 0.5f);
        foreach (ModelMeshPart part in mesh.MeshParts)
            standalone.AppendModelMeshPart(part, st, false, false, false, false, Color.White);
        m_blockMeshesByVariant = meshes;
        m_collisionBoxes = boxes;
        m_standaloneBlockMesh = standalone;
        m_texture = texture;
        Log.Information("[FlatNature] Wall-mounted IronLantern GLB initialized.");
    }

    public override void GenerateTerrainVertices(BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z)
    {
        if (m_texture == null) { base.GenerateTerrainVertices(generator, geometry, value, x, y, z); return; }
        int variant = Terrain.ExtractData(value);
        if (variant >= 0 && variant < 4)
        {
            TerrainGeometry lanternGeometry = geometry.GetGeometry(m_texture);
            generator.GenerateMeshVertices(this, x, y, z, m_blockMeshesByVariant[variant], Color.White, null, lanternGeometry.SubsetOpaque);
        }
    }

    public override BoundingBox[] GetCustomCollisionBoxes(SubsystemTerrain terrain, int value)
    {
        if (m_texture == null) return base.GetCustomCollisionBoxes(terrain, value);
        int variant = Terrain.ExtractData(value);
        return variant >= 0 && variant < 4 ? m_collisionBoxes[variant] : null;
    }

    public override void DrawBlock(PrimitivesRenderer3D primitivesRenderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData environmentData)
    {
        if (m_texture == null) { base.DrawBlock(primitivesRenderer, value, color, size, ref matrix, environmentData); return; }
        BlocksManager.DrawMeshBlock(primitivesRenderer, m_standaloneBlockMesh, m_texture, color, 2f * size, ref matrix, environmentData);
    }

    public override Texture2D GetDefaultTexture(int value) => m_texture ?? base.GetDefaultTexture(value);
}