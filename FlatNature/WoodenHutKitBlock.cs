namespace Game;

/// <summary>
/// Placeable one-use blueprint block. When placed, its behavior replaces it
/// with a complete wooden hut if the surrounding footprint is suitable.
/// </summary>
public sealed class WoodenHutKitBlock : Block
{
    public new static int Index = -1;
}
