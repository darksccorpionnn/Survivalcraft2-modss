using System;
using Engine;

namespace Game
{
    public sealed partial class ComponentYeniNesilVillager
    {
        private Point3 m_bedPosition;
        private bool m_hasBed;
        private int m_bedLogs;

        private bool IsMyBedPresent()
        {
            if (!m_hasBed || m_terrain?.Terrain == null) return false;
            int cell = m_terrain.Terrain.GetCellValue(m_bedPosition.X, m_bedPosition.Y, m_bedPosition.Z);
            return BlocksManager.Blocks[Terrain.ExtractContents(cell)] is YeniNesilBedBlock;
        }

        private bool BedClaimedByOther(Point3 location)
        {
            // Active is cleared on project change. Never reserve another world's beds.
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                if (!Active[i].TryGetTarget(out ComponentYeniNesilVillager other))
                {
                    Active.RemoveAt(i);
                    continue;
                }
                if (other == this || other.Entity.Project != Entity.Project || !other.m_hasBed)
                    continue;
                if (other.m_bedPosition.X == location.X &&
                    other.m_bedPosition.Y == location.Y &&
                    other.m_bedPosition.Z == location.Z) return true;
            }
            return false;
        }

        private void SearchForUnownedBed()
        {
            if (m_terrain?.Terrain == null || !m_hasOrigin) return;
            m_homeScanCooldown = 25f;
            if (m_hasBed && IsMyBedPresent() && !BedClaimedByOther(m_bedPosition))
                return;
            m_hasBed = false;
            int ox = (int)Math.Floor(m_origin.X);
            int oy = (int)Math.Floor(m_origin.Y);
            int oz = (int)Math.Floor(m_origin.Z);
            float bestDistance = float.MaxValue;
            Point3 bestBed = default;
            Vector3 bestStand = default;
            bool found = false;

            for (int x = ox - 18; x <= ox + 18; x++)
            for (int z = oz - 18; z <= oz + 18; z++)
            {
                int dx = x - ox, dz = z - oz;
                if (dx*dx + dz*dz > 324) continue;
                for (int y = Math.Max(3, oy-5); y <= Math.Min(244, oy+5); y++)
                {
                    int value = m_terrain.Terrain.GetCellValue(x,y,z);
                    if (BlocksManager.Blocks[Terrain.ExtractContents(value)] is not YeniNesilBedBlock)
                        continue;
                    Point3 candidate = new Point3(x,y,z);
                    if (BedClaimedByOther(candidate)) continue;
                    foreach (Point2 offset in new [] {new Point2(0,-1),new Point2(0,1),
                                                  new Point2(-1,0),new Point2(1,0)})
                    {
                        int tx = x+offset.X, tz = z+offset.Y;
                        if (!TryFindStandingY(tx,tz,y+0.5f,out int floor)) continue;
                        Vector3 stand = new Vector3(tx+0.5f,floor,tz+0.5f);
                        float d = Vector3.DistanceSquared(stand,m_origin);
                        if (d >= bestDistance) continue;
                        bestDistance = d;
                        bestBed = candidate;
                        bestStand = stand;
                        found = true;
                    }
                }
            }
            if (!found)
            {
                LogBed("No free bed with safe adjacent approach within 18 blocks.");
                return;
            }
            m_bedPosition = bestBed;
            m_hasBed = true;
            m_homeGoal = bestStand;
            m_homeKnown = true;
            m_homeFoundDoor = false;
            m_returnAttempts = 0;
            m_returnFailures = 0;
            LogBed("Claimed bed " + bestBed + " bedside destination " + bestStand);
        }

        private void LogBed(string message)
        {
            if(m_bedLogs++ < 10)
                Log.Information("[YeniNesil/BedV04] " + message);
        }
    }
}
