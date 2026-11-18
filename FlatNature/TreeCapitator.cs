using System;
using System.Collections.Generic;
using Engine;
using GameEntitySystem;

namespace Game;

internal static class TreeCapitator
{
    private const int MaxLogs = 128;
    private const int MaxLeaves = 512;
    private const int MaxHorizontalRadius = 6;
    private const int MaxVerticalSpan = 24;

    private static readonly Point3[] Neighbors =
    {
        new Point3(1, 0, 0), new Point3(-1, 0, 0),
        new Point3(0, 1, 0), new Point3(0, -1, 0),
        new Point3(0, 0, 1), new Point3(0, 0, -1)
    };

    internal static void TryChop(ComponentMiner miner, BlockPlacementData digValue, int dugCellValue)
    {
        if (miner == null)
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

            // ComponentMiner.Dig clears DigCellFace before firing OnBlockDug.
            // The hook's BlockPlacementData retains the exact cell coordinates.
            Point3 origin = new Point3(digValue.CellFace.X, digValue.CellFace.Y, digValue.CellFace.Z);
            Project project = GameManager.Project;
            if (project == null)
                return;

            SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(false);
            if (terrain == null)
                return;

            List<Point3> logs = FindConnectedLogs(terrain.Terrain, origin, dugContents);
            if (logs.Count < 2 || !HasNearbyCanopy(terrain.Terrain, logs))
                return;

            // Collect foliage attached to this trunk before removing the remaining logs.
            List<Point3> leaves = FindTreeLeaves(terrain.Terrain, logs);

            // DestroyCell handles normal drop values and particles for both logs and leaves.
            foreach (Point3 p in logs)
            {
                if (p == origin)
                    continue;

                int value = terrain.Terrain.GetCellValue(p.X, p.Y, p.Z);
                if (!IsWood(Terrain.ExtractContents(value)))
                    continue;

                terrain.DestroyCell(0, p.X, p.Y, p.Z, 0,
                    noDrop: false, noParticleSystem: true, movingBlock: null);
            }

            // Leaves are removed as well, using the game's normal leaf drop behavior.
            foreach (Point3 p in leaves)
            {
                int value = terrain.Terrain.GetCellValue(p.X, p.Y, p.Z);
                if (!IsLeaves(Terrain.ExtractContents(value)))
                    continue;

                terrain.DestroyCell(0, p.X, p.Y, p.Z, 0,
                    noDrop: false, noParticleSystem: true, movingBlock: null);
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


    private static List<Point3> FindTreeLeaves(Terrain terrain, List<Point3> logs)
    {
        var found = new List<Point3>();
        var visited = new HashSet<Point3>();
        var queue = new Queue<Point3>();

        // Start only from leaves touching this tree's logs, then follow the connected canopy.
        foreach (Point3 log in logs)
        {
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                if (x == 0 && y == 0 && z == 0)
                    continue;
                Point3 p = new Point3(log.X + x, log.Y + y, log.Z + z);
                if (p.Y < 1 || p.Y >= 255 || !visited.Add(p))
                    continue;
                if (IsLeaves(Terrain.ExtractContents(terrain.GetCellValue(p.X, p.Y, p.Z))))
                    queue.Enqueue(p);
            }
        }

        while (queue.Count > 0 && found.Count < MaxLeaves)
        {
            Point3 p = queue.Dequeue();
            found.Add(p);

            foreach (Point3 offset in Neighbors)
            {
                Point3 next = new Point3(p.X + offset.X, p.Y + offset.Y, p.Z + offset.Z);
                if (visited.Contains(next) ||
                    Math.Abs(next.X - logs[0].X) > MaxHorizontalRadius + 4 ||
                    Math.Abs(next.Z - logs[0].Z) > MaxHorizontalRadius + 4 ||
                    next.Y < 1 || next.Y >= 255)
                    continue;

                visited.Add(next);
                if (IsLeaves(Terrain.ExtractContents(terrain.GetCellValue(next.X, next.Y, next.Z))))
                    queue.Enqueue(next);
            }
        }

        return found;
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
