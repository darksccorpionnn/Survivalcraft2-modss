using System;
using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>
/// Separate torch block. It keeps native placement and the wooden handle,
/// but omits the static orange Flame mesh so only blue particles are visible.
/// </summary>
public sealed class BlueTorchBlock : TorchBlock
{
    public new static int Index = -1;

    public override BlockPlacementData GetPlacementValue(
        SubsystemTerrain subsystemTerrain,
        ComponentMiner componentMiner,
        int value,
        TerrainRaycastResult raycastResult)
    {
        int face = raycastResult.CellFace.Face;
        if (face < 0 || face > 4)
        {
            BlockPlacementData rejected = default;
            rejected.CellFace = raycastResult.CellFace;
            return rejected;
        }

        BlockPlacementData placement = base.GetPlacementValue(
            subsystemTerrain, componentMiner, value, raycastResult);

        int blueTorchIndex = BlocksManager.GetBlockIndex<BlueTorchBlock>();
        if (blueTorchIndex <= 0)
            throw new InvalidOperationException("[FlatNature] BlueTorch block ID was not allocated.");

        placement.Value = Terrain.ReplaceContents(placement.Value, blueTorchIndex);
        return placement;
    }

    // Matches the vanilla torch's scalar light emission.
    public override int GetEmittedLightAmount(int value) => 14;

    public override void Initialize()
    {
        // Initialize native arrays first, then rebuild them with the wooden torch
        // mesh only. The native static orange Flame mesh is intentionally omitted.
        base.Initialize();

        Model model = ContentManager.Get<Model>("Models/Torch");
        if (model == null)
            throw new InvalidOperationException("[FlatNature] Models/Torch could not be loaded.");

        ModelMesh torchMesh = model.FindMesh("Torch", true);
        if (torchMesh.MeshParts.Count == 0)
            throw new InvalidOperationException("[FlatNature] Native Torch mesh has no mesh parts.");

        Matrix boneTransform = torchMesh.ParentBone != null
            ? BlockMesh.GetBoneAbsoluteTransform(torchMesh.ParentBone)
            : Matrix.Identity;

        float[] yaw =
        {
            0f,
            (float)Math.PI / 2f,
            (float)Math.PI,
            3f * (float)Math.PI / 2f
        };

        Vector3[] offsets =
        {
            new Vector3(0.5f, 0.15f, -0.05f),
            new Vector3(-0.05f, 0.15f, 0.5f),
            new Vector3(0.5f, 0.15f, 1.05f),
            new Vector3(1.05f, 0.15f, 0.5f)
        };

        m_blockMeshesByVariant = new BlockMesh[5];
        m_collisionBoxes = new BoundingBox[5][];

        for (int variant = 0; variant < 4; variant++)
        {
            BlockMesh blockMesh = new BlockMesh();
            Matrix transform =
                boneTransform *
                Matrix.CreateRotationX(0.6f) *
                Matrix.CreateRotationY(yaw[variant]) *
                Matrix.CreateTranslation(offsets[variant].X, offsets[variant].Y, offsets[variant].Z);

            foreach (ModelMeshPart part in torchMesh.MeshParts)
            {
                blockMesh.AppendModelMeshPart(
                    part, transform, false, false, false, false, Color.White);
            }

            m_blockMeshesByVariant[variant] = blockMesh;
            m_collisionBoxes[variant] = new[] { blockMesh.CalculateBoundingBox() };
        }

        // Variant 4 is the native floor-placed torch position.
        BlockMesh floorMesh = new BlockMesh();
        Matrix floorTransform = boneTransform * Matrix.CreateTranslation(0.5f, 0f, 0.5f);
        foreach (ModelMeshPart part in torchMesh.MeshParts)
        {
            floorMesh.AppendModelMeshPart(
                part, floorTransform, false, false, false, false, Color.White);
        }
        m_blockMeshesByVariant[4] = floorMesh;
        m_collisionBoxes[4] = new[] { floorMesh.CalculateBoundingBox() };

        // Inventory/hand preview: wooden handle only; active fire exists in-world.
        m_standaloneBlockMesh = new BlockMesh();
        Matrix standaloneTransform = boneTransform * Matrix.CreateTranslation(0.5f, 0f, 0.5f);
        foreach (ModelMeshPart part in torchMesh.MeshParts)
        {
            m_standaloneBlockMesh.AppendModelMeshPart(
                part, standaloneTransform, false, false, false, false, Color.White);
        }

        Log.Information("[FlatNature] BlueTorch initialized without the static orange flame mesh.");
    }
}
