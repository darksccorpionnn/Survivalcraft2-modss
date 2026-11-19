using System;

namespace Game;

/// <summary>
/// Placeable one-use blueprint block. It reuses the native torch block's
/// rendering and placement implementation, then converts into a wooden hut.
/// </summary>
public sealed class WoodenHutKitBlock : TorchBlock
{
    public new static int Index = -1;

    public override BlockPlacementData GetPlacementValue(
        SubsystemTerrain subsystemTerrain,
        ComponentMiner componentMiner,
        int value,
        TerrainRaycastResult raycastResult)
    {
        // A hut needs a ground anchor; reject wall and ceiling placement.
        if (raycastResult.CellFace.Face != 4)
        {
            BlockPlacementData rejected = default;
            rejected.CellFace = raycastResult.CellFace;
            return rejected;
        }

        BlockPlacementData placement = base.GetPlacementValue(
            subsystemTerrain, componentMiner, value, raycastResult);

        int kitIndex = BlocksManager.GetBlockIndex<WoodenHutKitBlock>();
        if (kitIndex <= 0)
            throw new InvalidOperationException("[FlatNature] Wooden hut kit block ID was not allocated.");

        placement.Value = Terrain.ReplaceContents(placement.Value, kitIndex);
        return placement;
    }

    // The item is a temporary placement trigger, not a light source.
    public override int GetEmittedLightAmount(int value) => 0;
}
