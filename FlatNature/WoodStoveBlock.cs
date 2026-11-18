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
    // FurnaceBlock.Index is inherited static state. Hide it so the block
    // registry assigns a unique ID to WoodStoveBlock instead of overwriting
    // the vanilla furnace's shared ID.
    public new static int Index;

    internal const int LitDataFlag = 4;