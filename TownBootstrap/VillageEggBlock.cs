using Engine;

namespace Game
{
    /// <summary>
    /// Placeable inventory item that triggers one village generation attempt.
    /// </summary>
    public sealed class VillageEggBlock : CubeBlock
    {
        public override void Initialize()
        {
            base.Initialize();
            int contents = BlocksManager.GetBlockIndex<VillageEggBlock>();
            Log.Information("[TownBootstrap] Village Egg block registered. Contents=" + contents);
        }
    }
}
