using System;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game
{
    /// <summary>
    /// First terrain-editing test: places a small stone marker near the player.
    /// Existing blocks are never replaced.
    /// </summary>
    public sealed class TownPlacementComponent : Component, IUpdateable
    {
        private static Project s_processedProject;
        private ComponentPlayer m_componentPlayer;

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap)
        {
            base.Load(valuesDictionary, idToEntityMap);
            m_componentPlayer = Entity.FindComponent<ComponentPlayer>(true);
            Log.Information("[TownBootstrap] TownPlacementComponent loaded.");
        }

        public void Update(float dt)
        {
            Project project = GameManager.Project;
            if (project == null || project == s_processedProject || m_componentPlayer == null)
                return;

            SubsystemTerrain subsystemTerrain = project.FindSubsystem<SubsystemTerrain>();
            if (subsystemTerrain == null)
                return;

            // Mark this world as processed before editing so the update loop cannot repeat it.
            s_processedProject = project;

            try
            {
                BuildTestMarker(subsystemTerrain, m_componentPlayer.ComponentBody.Position);
            }
            catch (Exception ex)
            {
                Log.Information("[TownBootstrap] Test marker placement failed: " + ex);
            }
        }

        private static void BuildTestMarker(SubsystemTerrain subsystemTerrain, Vector3 playerPosition)
        {
            int scanY = (int)Math.Floor(playerPosition.Y);
            int centerX = (int)Math.Floor(playerPosition.X) + 2;
            int centerZ = (int)Math.Floor(playerPosition.Z) + 2;
            int stoneContents = BlocksManager.GetBlockIndex<StoneBlock>();
            int stoneValue = Terrain.MakeBlockValue(stoneContents);

            // Detect the marker after a world is reopened; do not stack another copy.
            if (HasExistingMarker(subsystemTerrain, centerX, centerZ, scanY, stoneContents))
            {
                Log.Information("[TownBootstrap] Test marker already exists; no changes made.");
                return;
            }

            int centerSurfaceY;
            if (!TryFindSurfaceY(subsystemTerrain, centerX, centerZ, scanY, out centerSurfaceY))
            {
                Log.Information("[TownBootstrap] No nearby surface found; marker skipped.");
                return;
            }

            int placed = 0;

            // A small 3x3 pad. Each block is placed above the existing surface,
            // so this test never replaces terrain or structures.
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int x = centerX + dx;
                    int z = centerZ + dz;
                    int surfaceY;

                    if (!TryFindSurfaceY(subsystemTerrain, x, z, scanY, out surfaceY))
                        continue;

                    int targetY = surfaceY + 1;
                    if (targetY >= 255)
                        continue;

                    int existing = subsystemTerrain.Terrain.GetCellValue(x, targetY, z);
                    if (Terrain.ExtractContents(existing) != 0)
                        continue;

                    subsystemTerrain.ChangeCell(x, targetY, z, stoneValue);
                    placed++;
                }
            }

            // Two extra blocks form a visible marker column at the pad center.
            int columnBaseY = centerSurfaceY + 2;
            for (int i = 0; i < 2; i++)
            {
                int y = columnBaseY + i;
                if (y >= 255)
                    break;

                int existing = subsystemTerrain.Terrain.GetCellValue(centerX, y, centerZ);
                if (Terrain.ExtractContents(existing) != 0)
                    break;

                subsystemTerrain.ChangeCell(centerX, y, centerZ, stoneValue);
                placed++;
            }

            Log.Information(
                "[TownBootstrap] Test marker placed near player at X=" + centerX +
                ", Z=" + centerZ + ". Blocks placed: " + placed);
        }

        private static bool TryFindSurfaceY(
            SubsystemTerrain subsystemTerrain, int x, int z, int scanY, out int surfaceY)
        {
            int minY = Math.Max(1, scanY - 32);
            int maxY = Math.Min(254, scanY);

            for (int y = maxY; y >= minY; y--)
            {
                int value = subsystemTerrain.Terrain.GetCellValue(x, y, z);
                if (Terrain.ExtractContents(value) != 0)
                {
                    surfaceY = y;
                    return true;
                }
            }

            surfaceY = 0;
            return false;
        }

        private static bool HasExistingMarker(
            SubsystemTerrain subsystemTerrain, int x, int z, int scanY, int stoneContents)
        {
            int minY = Math.Max(1, scanY - 32);
            int maxY = Math.Min(252, scanY + 8);

            for (int y = minY; y <= maxY; y++)
            {
                int a = Terrain.ExtractContents(subsystemTerrain.Terrain.GetCellValue(x, y, z));
                int b = Terrain.ExtractContents(subsystemTerrain.Terrain.GetCellValue(x, y + 1, z));
                int c = Terrain.ExtractContents(subsystemTerrain.Terrain.GetCellValue(x, y + 2, z));

                if (a == stoneContents && b == stoneContents && c == stoneContents)
                    return true;
            }

            return false;
        }
    }
}
