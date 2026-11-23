using System;
using System.Collections.Generic;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game
{
    /// <summary>
    /// Independent New Generation villager brain.
    /// No references to LivingTowns, its private fields, or the old villager AI.
    /// First playable prototype: autonomous wandering, stuck recovery, simple social greeting.
    /// </summary>
    public sealed class ComponentYeniNesilVillager : Component, IUpdateable
    {
        private static readonly List<WeakReference<ComponentYeniNesilVillager>> Active =
            new List<WeakReference<ComponentYeniNesilVillager>>();
        private static Project s_project;
        private const float VillageRadius = 30f;
        private const float ArrivalRange = 1.5f;

        private ComponentBody m_body;
        private ComponentPathfinding m_path;
        private ComponentCreatureModel m_model;
        private SubsystemTerrain m_terrain;
        private readonly System.Random m_random = new System.Random(
            unchecked(Environment.TickCount ^ Guid.NewGuid().GetHashCode()));
        private bool m_registered;
        private bool m_hasOrigin;
        private Vector3 m_origin;
        private Vector3 m_lastPosition;
        private Vector3 m_target;
        private bool m_hasTarget;
        private float m_tick;
        private float m_idleWait;
        private float m_noProgress;
        private float m_moveDuration;
        private float m_talkCooldown = 8f;
        private float m_socialCheck;
        private float m_socialRemaining;
        private ComponentYeniNesilVillager m_partner;
        private bool m_isTalking;
        private int m_logCount;
        private int m_completedWalks;

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap)
        {
            base.Load(valuesDictionary, idToEntityMap);
            m_body = Entity.FindComponent<ComponentBody>(true);
            m_path = Entity.FindComponent<ComponentPathfinding>(true);
            m_model = Entity.FindComponent<ComponentCreatureModel>(true);
            m_terrain = Entity.Project.FindSubsystem<SubsystemTerrain>(true);
            m_idleWait = 1.2f + (float)m_random.NextDouble() * 2f;
            m_talkCooldown = 6f + (float)m_random.NextDouble() * 9f;

            if (valuesDictionary.GetValue<bool>("HasVillageOrigin", false))
            {
                m_origin = valuesDictionary.GetValue<Vector3>("VillageOrigin");
                m_hasOrigin = true;
            }
            Log.Information("[YeniNesil] New autonomous villager component loaded.");
        }

        public override void Save(ValuesDictionary valuesDictionary, EntityToIdMap entityToIdMap)
        {
            base.Save(valuesDictionary, entityToIdMap);
            if (m_hasOrigin)
            {
                valuesDictionary.SetValue("HasVillageOrigin", true);
                valuesDictionary.SetValue("VillageOrigin", m_origin);
            }
        }

        public void Update(float dt)
        {
            if (m_body == null || m_path == null || m_terrain?.Terrain == null)
                return;

            if (s_project != Entity.Project)
            {
                s_project = Entity.Project;
                Active.Clear(); // Old worlds must not leave partner references behind.
            }

            if (!m_registered)
            {
                Active.Add(new WeakReference<ComponentYeniNesilVillager>(this));
                m_registered = true;
            }

            if (!m_hasOrigin)
            {
                m_origin = m_body.Position;
                m_lastPosition = m_origin;
                m_hasOrigin = true;
            }

            float delta = Math.Min(Math.Max(dt, 0f), 0.3f);
            m_tick += delta;
            m_talkCooldown = Math.Max(0f, m_talkCooldown - delta);
            m_socialCheck -= delta;
            m_idleWait -= delta;

            if (m_partner != null)
            {
                m_socialRemaining -= delta;
                if (m_partner.m_body == null ||
                    Vector3.DistanceSquared(m_partner.m_body.Position, m_body.Position) > 64f ||
                    m_socialRemaining <= 0f)
                {
                    EndConversation();
                }
                else
                {
                    if (m_model != null)
                        m_model.LookAtOrder = m_partner.m_body.Position + new Vector3(0f, 1.25f, 0f);
                    SetTalking(true);
                }
            }

            if (m_tick < 0.35f) return;
            float step = m_tick;
            m_tick = 0f;

            if (m_partner != null) return;

            if (m_socialCheck <= 0f && m_talkCooldown <= 0f)
            {
                m_socialCheck = 1.2f + (float)m_random.NextDouble() * 1.6f;
                if (TryStartConversation()) return;
            }

            if (m_hasTarget)
            {
                float remaining = Vector3.DistanceSquared(m_body.Position, m_target);
                if (remaining <= (ArrivalRange + 0.7f) * (ArrivalRange + 0.7f))
                {
                    m_completedWalks++;
                    StopWalking(1.8f + (float)m_random.NextDouble() * 3.5f);
                    return;
                }
                if (Vector3.DistanceSquared(m_body.Position, m_lastPosition) >= 0.20f)
                    m_noProgress = 0f;
                else
                    m_noProgress += step;
                m_moveDuration += step;
                m_lastPosition = m_body.Position;

                if (m_path.IsStuck || m_noProgress > 6.5f || m_moveDuration > 28f)
                {
                    if (m_logCount < 12)
                    {
                        m_logCount++;
                        Log.Warning("[YeniNesil] Route interrupted (stuck or timeout), will retry. At " +
                                    m_body.Position + " target " + m_target);
                    }
                    StopWalking(1f + (float)m_random.NextDouble() * 2f);
                }
                return;
            }

            if (m_idleWait > 0f) return;

            if (TryFindWalkTarget(out Vector3 destination))
            {
                m_path.SetDestination(destination, 1.0f, ArrivalRange, 900,
                    true, false, false, null);
                m_target = destination;
                m_hasTarget = true;
                m_noProgress = 0f;
                m_moveDuration = 0f;
                m_lastPosition = m_body.Position;
                if (m_logCount < 9)
                {
                    m_logCount++;
                    Log.Information("[YeniNesil] WALK " + m_body.Position + " -> " + destination);
                }
            }
            else
            {
                m_idleWait = 2.0f;
                if (m_logCount < 9)
                {
                    m_logCount++;
                    Log.Warning("[YeniNesil] No nearby safe walking destination: " + m_body.Position);
                }
            }
        }

        private void StopWalking(float wait)
        {
            m_path.Stop();
            m_hasTarget = false;
            m_noProgress = 0f;
            m_moveDuration = 0f;
            m_idleWait = wait;
        }

        private bool TryFindWalkTarget(out Vector3 target)
        {
            target = default;
            Vector3 current = m_body.Position;
            for (int attempt = 0; attempt < 36; attempt++)
            {
                double angle = m_random.NextDouble() * Math.PI * 2;
                float radius = 5f + (float)m_random.NextDouble() * 17f;
                int x = (int)Math.Floor(current.X + Math.Cos(angle) * radius);
                int z = (int)Math.Floor(current.Z + Math.Sin(angle) * radius);

                float dx = (x + 0.5f) - m_origin.X;
                float dz = (z + 0.5f) - m_origin.Z;
                if (dx * dx + dz * dz > VillageRadius * VillageRadius)
                    continue;
                if (!TryFindStandingY(x, z, current.Y, out int y))
                    continue;

                Vector3 possible = new Vector3(x + 0.5f, y, z + 0.5f);
                if (Vector3.DistanceSquared(possible, current) < 16f)
                    continue;
                target = possible;
                return true;
            }
            return false;
        }

        private bool TryFindStandingY(int x, int z, float currentY, out int standingY)
        {
            standingY = 0;
            int top = Math.Min(245, (int)Math.Floor(currentY) + 3);
            int bottom = Math.Max(3, (int)Math.Floor(currentY) - 4);
            for (int y = top; y >= bottom; y--)
            {
                if (IsSolid(x,y-1,z) && IsPassable(x,y,z) && IsPassable(x,y+1,z))
                {
                    if (Math.Abs(y-currentY) > 3f) continue;
                    standingY = y;
                    return true;
                }
            }
            return false;
        }

        private bool IsSolid(int x,int y,int z)
        {
            int value = m_terrain.Terrain.GetCellValue(x,y,z);
            Block b = BlocksManager.Blocks[Terrain.ExtractContents(value)];
            return b != null && b.IsCollidable && b is not FluidBlock;
        }

        private bool IsPassable(int x,int y,int z)
        {
            int value = m_terrain.Terrain.GetCellValue(x,y,z);
            Block b = BlocksManager.Blocks[Terrain.ExtractContents(value)];
            return b != null && !b.IsCollidable && b is not FluidBlock;
        }

        private bool TryStartConversation()
        {
            // Two-person handshake: no forced speech with non-villager entities.
            // Only same-project villagers with free social state are eligible.
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                if (!Active[i].TryGetTarget(out ComponentYeniNesilVillager other) ||
                    other.Entity == null || other.Entity.Project != Entity.Project)
                {
                    Active.RemoveAt(i);
                    continue;
                }

                if (other == this || other.m_body == null || other.m_partner != null ||
                    other.m_talkCooldown > 0f)
                    continue;

                float d = Vector3.DistanceSquared(m_body.Position, other.m_body.Position);
                if (d > 20f || d < 0.25f) continue;
                m_partner = other;
                other.m_partner = this;
                float duration = 3f + (float)m_random.NextDouble() * 3f;
                m_socialRemaining = duration;
                other.m_socialRemaining = duration;
                StopWalking(0.5f);
                other.StopWalking(0.5f);
                SetTalking(true);
                other.SetTalking(true);
                if (m_logCount < 12)
                {
                    m_logCount++;
                    Log.Information("[YeniNesil] SOCIAL two villagers started talking near " +
                                    m_body.Position);
                }
                return true;
            }
            return false;
        }

        private void EndConversation()
        {
            ComponentYeniNesilVillager other = m_partner;
            m_partner = null;
            m_socialRemaining = 0f;
            m_talkCooldown = 18f + (float)m_random.NextDouble() * 14f;
            SetTalking(false);
            m_idleWait = 1f + (float)m_random.NextDouble() * 3f;

            if (other != null && other.m_partner == this)
            {
                other.m_partner = null;
                other.m_socialRemaining = 0f;
                other.m_talkCooldown = 18f + (float)other.m_random.NextDouble() * 14f;
                other.m_idleWait = 1f + (float)other.m_random.NextDouble() * 3f;
                other.SetTalking(false);
            }
        }

        private void SetTalking(bool talking)
        {
            // Animation controller can initialize after first AI tick; always sync state.
            m_isTalking = talking;
            var controller = m_model?.AnimationController;
            if (controller != null)
            {
                if (controller.Parameters.HasParameter("IsTalking"))
                    controller.Parameters.SetBool("IsTalking", talking);
            }
        }
    }

    public sealed class YeniNesilModLoader : ModLoader
    {
        public override void __ModInitialize()
        {
            Log.Information("[YeniNesil] Independent villager AI V0.1 registered. No LivingTowns.dll required.");
        }
    }
}
