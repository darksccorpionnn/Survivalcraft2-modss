using System;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game
{
    /// <summary>
    /// Experimental V0.3 evening return + nighttime rest routine.
    /// Uses vanilla SCAPI clock, blocks and pathfinding. No LivingTowns dependency.
    /// It does not claim a real bed: Natural Villages V0.11 currently has none.
    /// </summary>
    public sealed partial class ComponentYeniNesilVillager
    {
        private SubsystemTimeOfDay m_dayClock;
        private bool m_homeKnown;
        private bool m_homeFoundDoor;
        private Vector3 m_homeGoal;
        private float m_homeScanCooldown;
        private float m_clockTick;
        private bool m_inEvening;
        private bool m_restingAtNight;
        private float m_returnNoProgress;
        private float m_returnElapsed;
        private float m_returnRetryDelay;
        private int m_returnAttempts;
        private int m_returnFailures;
        private Vector3 m_lastHomeMovePosition;
        private int m_homeLogCount;

        private static bool IsInClockInterval(float t, float start, float end)
        {
            // Works for a night interval crossing midnight, e.g. 0.70 .. 0.23
            return start <= end ? t >= start && t < end : t >= start || t < end;
        }

        private void LoadHomeState(ValuesDictionary values)
        {
            m_dayClock = Entity.Project.FindSubsystem<SubsystemTimeOfDay>(false);
            if (values.GetValue<bool>("YeniNesilHasBed", false))
            {
                Vector3 stored = values.GetValue<Vector3>("YeniNesilBedPosition");
                m_bedPosition = new Point3((int)stored.X, (int)stored.Y, (int)stored.Z);
                m_hasBed = true;
            }
            if (values.GetValue<bool>("YeniNesilHasHomeGoal", false))
            {
                m_homeGoal = values.GetValue<Vector3>("YeniNesilHomeGoal");
                m_homeKnown = true;
                m_homeFoundDoor = values.GetValue<bool>("YeniNesilHomeHasDoor", false);
            }
        }

        private void SaveHomeState(ValuesDictionary values)
        {
            if (m_hasBed)
            {
                values.SetValue("YeniNesilHasBed", true);
                values.SetValue("YeniNesilBedPosition", new Vector3(m_bedPosition.X, m_bedPosition.Y, m_bedPosition.Z));
            }
            if (!m_homeKnown) return;
            values.SetValue("YeniNesilHasHomeGoal", true);
            values.SetValue("YeniNesilHomeGoal", m_homeGoal);
            values.SetValue("YeniNesilHomeHasDoor", m_homeFoundDoor);
        }

        private void SyncNightRestAnimation(bool resting)
        {
            var controller = m_model?.AnimationController;
            if (controller != null && controller.Parameters.HasParameter("IsSleeping"))
                controller.Parameters.SetBool("IsSleeping", resting);
        }

        private void BeginReturnHome()
        {
            if (m_partner != null) EndConversation();
            SetGreeting(false);
            SetTalking(false);
            if (m_hasTarget)
            {
                m_path.Stop();
                m_hasTarget = false;
            }
            m_restingAtNight = false;
            m_inEvening = true;
            m_returnRetryDelay = 0f;
            m_returnElapsed = 0f;
            m_returnNoProgress = 0f;
            m_returnAttempts = 0;
            m_returnFailures = 0;
            m_lastHomeMovePosition = m_body.Position;
            SyncNightRestAnimation(false);
            if (!m_hasBed || !IsMyBedPresent() || BedClaimedByOther(m_bedPosition))
                SearchForUnownedBed();
            if (!m_hasBed && (!m_homeKnown || !m_homeFoundDoor))
                SearchHomeNearOrigin();
            LogHome("Evening: owns a valid bed="+m_hasBed+", door found="+m_homeFoundDoor);
        }

        private void WakeForMorning()
        {
            // Return to the V0.2 walking + conversation pipeline.
            if (m_inEvening)
            {
                m_path.Stop();
                m_hasTarget = false;
                m_inEvening = false;
                m_restingAtNight = false;
                m_idleWait = 1f + (float)m_random.NextDouble()*3f;
                m_talkCooldown = Math.Max(m_talkCooldown, 3f);
                LogHome("Morning: villager resumed daily roaming.");
            }
            SyncNightRestAnimation(false);
        }

        private bool HandleHomeRoutine(float dt)
        {
            // If a time subsystem is unavailable, preserve V0.2 behaviour unchanged.
            if (m_dayClock == null) return false;
            m_homeScanCooldown = Math.Max(0f, m_homeScanCooldown - dt);
            m_clockTick += dt;
            if (m_clockTick < 0.35f)
            {
                if (m_restingAtNight) SyncNightRestAnimation(true);
                return m_inEvening;
            }
            float step = m_clockTick;
            m_clockTick = 0f;
            float time = m_dayClock.TimeOfDay;
            bool eveningOrNight = IsInClockInterval(time,
                m_dayClock.DuskStart, m_dayClock.DawnStart);
            if (!eveningOrNight)
            {
                WakeForMorning();
                // Claim at most one loaded bed, recheck when removed or unassigned.
                if (m_homeScanCooldown <= 0f)
                {
                    if (!m_hasBed || !IsMyBedPresent() || BedClaimedByOther(m_bedPosition))
                        SearchForUnownedBed();
                    if (!m_hasBed && (!m_homeKnown || !m_homeFoundDoor))
                        SearchHomeNearOrigin();
                }
                return false;
            }

            if (!m_inEvening) BeginReturnHome();
            // Social interactions are disabled while returning or resting.
            if (m_partner != null) EndConversation();

            if (m_restingAtNight)
            {
                SyncNightRestAnimation(true);
                return true;
            }

            if (m_hasBed && !IsMyBedPresent())
            {
                m_hasBed = false;
                m_homeKnown = false;
                m_returnAttempts = 0;
                m_returnFailures = 0;
                SearchForUnownedBed();
            }
            if (!m_hasBed && m_homeScanCooldown <= 0f) SearchForUnownedBed();
            if (!m_homeKnown) SearchHomeNearOrigin();
            if (!m_homeKnown) m_homeGoal = m_origin;

            float distanceSquared = Vector3.DistanceSquared(m_body.Position,m_homeGoal);
            if (distanceSquared <= 4.0f)
            {
                BeginNightRest("Reached home area.");
                return true;
            }

            if (m_returnRetryDelay > 0f)
            {
                m_returnRetryDelay -= step;
                return true;
            }

            if (m_returnAttempts == 0)
            {
                StartHomePath();
                return true;
            }

            if (Vector3.DistanceSquared(m_body.Position,m_lastHomeMovePosition)>0.03f)
                m_returnNoProgress=0f;
            else
                m_returnNoProgress+=step;
            m_lastHomeMovePosition=m_body.Position;
            m_returnElapsed+=step;

            if (m_path.IsStuck || m_returnNoProgress>10f || m_returnElapsed>36f)
            {
                m_path.Stop();
                m_returnFailures++;
                LogHome("Route to home stalled. Failure="+m_returnFailures);
                if (m_returnFailures>=3)
                {
                    // Do not teleport or get stuck in a permanent retry loop.
                    BeginNightRest("Cannot reach home: resting at current safe position.");
                    return true;
                }
                m_returnRetryDelay=2.5f;
                m_returnNoProgress=0f;
                m_returnElapsed=0f;
                // A later retry is allowed to use a different route.
                m_returnAttempts=0;
            }
            return true;
        }

        private void StartHomePath()
        {
            if (!m_homeKnown) m_homeGoal=m_origin;
            m_path.SetDestination(m_homeGoal,0.9f,1.25f,1400,
                true,false,false,null);
            m_returnAttempts++;
            m_returnElapsed=0f;
            m_returnNoProgress=0f;
            m_lastHomeMovePosition=m_body.Position;
            LogHome("Walking to home "+m_homeGoal+" from "+m_body.Position);
        }

        private void BeginNightRest(string reason)
        {
            m_path.Stop();
            m_restingAtNight=true;
            m_hasTarget=false;
            m_returnElapsed=0f;
            SyncNightRestAnimation(true);
            LogHome(reason+" Night rest active. Bed assigned="+m_hasBed+"; actual lying animation not yet confirmed.");
        }

        private void SearchHomeNearOrigin()
        {
            if (m_hasBed && IsMyBedPresent()) return;
            if(m_terrain?.Terrain==null || !m_hasOrigin)
                return;
            m_homeScanCooldown=60f;
            Vector3 origin=m_origin;
            int startX=(int)Math.Floor(origin.X), startZ=(int)Math.Floor(origin.Z);
            int startY=(int)Math.Floor(origin.Y);
            double best=double.MaxValue;
            Vector3 candidateHome=origin;
            bool found=false;
            // Search a limited area once, not each frame, for wooden door blocks.
            // Generated V0.11 houses have a real wooden door but do not yet have beds.
            for(int x=startX-18;x<=startX+18;x++)
            for(int z=startZ-18;z<=startZ+18;z++)
            {
                double planar=(x-origin.X)*(x-origin.X)+(z-origin.Z)*(z-origin.Z);
                if(planar>18*18 || planar>=best)continue;
                for(int y=Math.Max(3,startY-5);y<=Math.Min(245,startY+5);y++)
                {
                    int value=m_terrain.Terrain.GetCellValue(x,y,z);
                    Block block=BlocksManager.Blocks[Terrain.ExtractContents(value)];
                    if(block is not DoorBlock)continue;
                    foreach(var offset in new[]{new Point2(0,-2),new Point2(0,2),
                        new Point2(-2,0),new Point2(2,0),new Point2(0,-3),new Point2(0,3)})
                    {
                        int tx=x+offset.X,tz=z+offset.Y;
                        if(!TryFindStandingY(tx,tz,y+0.5f,out int standingY))continue;
                        Vector3 standing=new Vector3(tx+0.5f,standingY,tz+0.5f);
                        double proximity=Vector3.DistanceSquared(standing,origin);
                        if(proximity<best)
                        {
                            best=proximity;
                            candidateHome=standing;
                            found=true;
                        }
                    }
                }
            }
            if(found)
            {
                m_homeGoal=candidateHome;
                m_homeKnown=true;
                m_homeFoundDoor=true;
                LogHome("Recognized nearby house entrance: "+candidateHome);
            }
            else if(!m_homeKnown)
            {
                // A villager spawned in an open test field still has a safe night anchor.
                m_homeGoal=m_origin;
                m_homeKnown=true;
                m_homeFoundDoor=false;
                LogHome("No house door nearby; using spawn position as night anchor.");
            }
        }

        private void LogHome(string message)
        {
            if(m_homeLogCount>=16)return;
            m_homeLogCount++;
            Log.Information("[YeniNesil/HomeV03] "+message);
        }
    }
}
