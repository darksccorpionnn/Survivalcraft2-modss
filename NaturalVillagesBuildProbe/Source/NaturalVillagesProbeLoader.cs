using System;
using Engine;
using Game;

namespace NaturalVillagesBuildProbe
{
    // Non-world-mutating compile probe for API 1.9.5.1.
    public sealed class NaturalVillagesProbeLoader : ModLoader
    {
        public override void __ModInitialize()
        {
            ModsManager.RegisterHook("TerrainContentsGenerator24Initialize", this);
            Log.Information("[NaturalVillagesProbe] API load hook registered.");
        }

        public override void TerrainContentsGenerator24Initialize(
            ITerrainContentsGenerator terrainGenerator, SubsystemTerrain subsystemTerrain)
        {
            if (terrainGenerator is not TerrainContentsGenerator24 generator) return;
            generator.ChunkGenerationStep4.Add(
                new ChunkGenerationStep(1500, chunk =>
                {
                    // Read-only API compatibility probe, no placed blocks.
                    int value = chunk.GetCellValueFast(0, 70, 0);
                    int content = Terrain.ExtractContents(value);
                    if (content < 0)
                        Log.Warning("[NaturalVillagesProbe] Unexpected content.");
                })
            );
        }
    }
}
