using System;
using System.Collections.Generic;
using Engine;
using GameEntitySystem;

namespace Game;

internal static class TreeCapitator
{
    private const int MaxLogs = 128;
    private const int MaxHorizontalRadius = 6;
    private const int MaxVerticalSpan = 24;

    private static readonly Point3[] Neighbors =
    {
        new Point3(1, 0, 0), new Point3(-1, 0, 0),
        new Point3(0, 1, 0), new Point3(0, -1, 0),
        new Point3(0, 0, 1), new Point3(0, 0, -1)
    };

    internal static void TryChop(ComponentMiner miner, int dugCellValue)
    {
        if (miner == null || miner.DigCellFace == null)
            return;

        try
        {
            int toolContents = Terrain.ExtractContents(miner.ActiveBlockValue);
            if (toolContents <= 0 || toolContents >= BlocksManager.Blocks.Length ||
                BlocksManager.Blocks[toolContents] is not AxeBlock)
                return;

            int dugContents = Terrain.ExtractContents(dugCellValue);
            if (!IsWood(dugContents))
                return;

            Point3 origin = miner.DigCellFace.Value.Point;
            Project project = GameManager.Project;
            if (project == null)
                return;

            SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(false);
            if (terrain == null)
                return;

            List<Point3> logs = FindConnectedLogs(terrain.Terrain, origin, dugContents);
            if (logs.Count < 2 || !HasNearbyCanopy(terrain.Terrain, logs))
                return;

            // The first log has already been removed by the game's normal mining flow.
            // DestroyCell handles each remaining log's normal drop values and particles.
            foreach (Point3 p in logs)
            {
                if (p == origin)
                    continue;

                int value = terrain.Terrain.GetCellValue(p.X, p.Y, p.Z);
                if (!IsWood(Terrain.ExtractContents(value)))
                    continue;

                terrain.DestroyCell(0, p.X, p.Y, p.Z, 0,
                    noDrop: false, noParticleSystem: false, movingBlock: null);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FlatNature] TreeCapitator failed safely: " + ex);
        }
    }

    private static List<Point3> FindConnectedLogs(Terrain terrain, Point3 origin, int originContents)
    {
        var found = new List<Point3>();
        var visited = new HashSet<Point3>();
        var queue = new Queue<Point3>();
        queue.Enqueue(origin);
        visited.Add(origin);

        while (queue.Count > 0 && found.Count < MaxLogs)
        {
            Point3 p = queue.Dequeue();
            int value = terrain.GetCellValue(p.X, p.Y, p.Z);
            int contents = Terrain.ExtractContents(value);

            // The struck cell is already air by the time OnBlockDug runs.
            if (p != origin)
            {
                if (!IsWood(contents))
                    continue;
                found.Add(p);
            }
            else
            {
                found.Add(p);
            }

            foreach (Point3 offset in Neighbors)
            {
                Point3 next = new Point3(p.X + offset.X, p.Y + offset.Y, p.Z + offset.Z);
                if (visited.Contains(next) ||
                    Math.Abs(next.X - origin.X) > MaxHorizontalRadius ||
                    Math.Abs(next.Z - origin.Z) > MaxHorizontalRadius ||
                    Math.Abs(next.Y - origin.Y) > MaxVerticalSpan ||
                    next.Y < 1 || next.Y >= 255)
                    continue;

                int nextContents = Terrain.ExtractContents(terrain.GetCellValue(next.X, next.Y, next.Z));
                if (IsWood(nextContents))
                {
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }
        }

        return found;
    }

    private static bool HasNearbyCanopy(Terrain terrain, List<Point3> logs)
    {
        int highestY = 0;
        foreach (Point3 p in logs)
            if (p.Y > highestY) highestY = p.Y;

        // A connected wood structure without foliage is likely a player-built structure.
        // Require leaves close to the upper part of the connected trunk.
        foreach (Point3 log in logs)
        {
            if (log.Y < highestY - 4)
                continue;

            for (int x = -4; x <= 4; x++)
            for (int y = -3; y <= 4; y++)
            for (int z = -4; z <= 4; z++)
            {
                int contents = Terrain.ExtractContents(
                    terrain.GetCellValue(log.X + x, log.Y + y, log.Z + z));
                if (IsLeaves(contents))
                    return true;
            }
        }

        return false;
    }

    private static bool IsWood(int contents)
    {
        return contents > 0 && contents < BlocksManager.Blocks.Length &&
               BlocksManager.Blocks[contents] is WoodBlock;
    }

    private static bool IsLeaves(int contents)
    {
        if (contents <= 0 || contents >= BlocksManager.Blocks.Length)
            return false;
        Block block = BlocksManager.Blocks[contents];
        return block is LeavesBlock ||
               block.GetType().Name.IndexOf("Leaves", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
