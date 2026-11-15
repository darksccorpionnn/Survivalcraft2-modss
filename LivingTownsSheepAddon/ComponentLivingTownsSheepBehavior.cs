using System;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

/// <summary>Grazing, player response and sheep vocalizations.</summary>
public sealed class ComponentLivingTownsSheepBehavior : ComponentBehavior, IUpdateable
{
    private const float FollowRange = 16f;
    private const float FollowStopDistance = 2.35f;
    private const float FleeRange = 5.5f;
    private const float FollowSpeed = 0.68f;
    private const float FleeSpeed = 1.15f;
    private const float GrazeApproachSpeed = 0.42f;

    private enum SheepMode { None, Follow, Flee }

    private ComponentCreature m_creature;
    private ComponentPathfinding m_pathfinding;
    private ComponentCreatureModel m_model;
    private SubsystemPlayers m_players;
    private SubsystemTime m_time;
    private SubsystemAudio m_audio;
    private SubsystemTerrain m_subsystemTerrain;
    private readonly System.Random m_random = new System.Random();
    private SheepMode m_appliedMode;
    private Vector3? m_lastOwnedDestination;
    private ComponentPlayer m_target;
    private ComponentPlayer m_fleeTarget;
    private SheepMode m_mode;
    private double m_nextPathUpdate;
    private double m_nextGrassSearch;
    private double m_grazeEnd;
    private Vector3 m_grassTarget;
    private bool m_isGrazing;
    private bool m_fleeBleatPlayed;
    private float m_lastHealth;
    private double m_nextAmbientBleat;
    private bool m_audioProbePlayed;

    public UpdateOrder UpdateOrder => UpdateOrder.Default;

    public override float ImportanceLevel
    {
        get
        {
            RefreshTargets();
            return m_mode switch
            {
                SheepMode.Follow => 65f,
                SheepMode.Flee => 110f,
                _ => 45f
            };
        }
    }

    public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap)
    {
        base.Load(valuesDictionary, idToEntityMap);
        m_creature = Entity.FindComponent<ComponentCreature>(throwOnError: true);
        m_pathfinding = Entity.FindComponent<ComponentPathfinding>(throwOnError: true);
        m_model = Entity.FindComponent<ComponentCreatureModel>(throwOnError: true);
        m_players = Project.FindSubsystem<SubsystemPlayers>(throwOnError: true);
        m_time = Project.FindSubsystem<SubsystemTime>(throwOnError: true);
        m_audio = Project.FindSubsystem<SubsystemAudio>(throwOnError: true);
        m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
        m_mode = SheepMode.None;
        m_nextGrassSearch = m_time.GameTime + 1.0;
        m_lastHealth = m_creature.ComponentHealth.Health;
        m_nextAmbientBleat = m_time.GameTime + 7.0 + m_random.NextDouble() * 5.0;
    }

    public void Update(float dt)
    {
        if (m_creature == null || m_model == null)
            return;

        RefreshTargets();
        UpdateSheepSounds();

        if (m_creature.ComponentHealth.Health <= 0f)
        {
            StopGrazing();
            StopOwnedPath();
            return;
        }

        if (m_mode != SheepMode.Flee)
            m_fleeBleatPlayed = false;

        if (!IsActive)
        {
            StopGrazing();
            StopOwnedPath();
            m_appliedMode = SheepMode.None;
            return;
        }

        if (m_mode != m_appliedMode)
        {
            StopOwnedPath();
            StopGrazing();
            m_nextPathUpdate = 0;
            m_appliedMode = m_mode;
        }

        switch (m_mode)
        {
            case SheepMode.Follow:
                UpdateFollow();
                break;
            case SheepMode.Flee:
                UpdateFlee();
                break;
            default:
                UpdateGrazing();
                break;
        }
    }

    private void RefreshTargets()
    {
        m_target = null;
        m_fleeTarget = null;
        m_mode = SheepMode.None;

        Vector3 position = m_creature.ComponentBody.Position;
        float nearestGrassDistance = FollowRange * FollowRange;
        float nearestThreatDistance = FleeRange * FleeRange;

        foreach (ComponentPlayer player in m_players.ComponentPlayers)
        {
            if (player == null || player.ComponentHealth == null || player.ComponentHealth.Health <= 0f || player.ComponentMiner == null)
                continue;
            float distanceSquared = Vector3.DistanceSquared(position, player.ComponentBody.Position);
            if (distanceSquared <= nearestGrassDistance && IsHoldingGrass(player))
            {
                nearestGrassDistance = distanceSquared;
                m_target = player;
            }
        }

        if (m_target != null)
        {
            m_mode = SheepMode.Follow;
            return;
        }

        foreach (ComponentPlayer player in m_players.ComponentPlayers)
        {
            if (player == null || player.ComponentHealth == null || player.ComponentHealth.Health <= 0f || player.ComponentMiner == null)
                continue;
            if (IsHoldingGrass(player))
                continue;
            float distanceSquared = Vector3.DistanceSquared(position, player.ComponentBody.Position);
            if (distanceSquared <= nearestThreatDistance)
            {
                nearestThreatDistance = distanceSquared;
                m_fleeTarget = player;
            }
        }

        if (m_fleeTarget != null)
            m_mode = SheepMode.Flee;
    }

    private static bool IsHoldingGrass(ComponentPlayer player)
    {
        int contents = Terrain.ExtractContents(player.ComponentMiner.ActiveBlockValue);
        return contents == GrassBlock.Index || contents == TallGrassBlock.Index;
    }

    private void UpdateGrazing()
    {
        double now = m_time.GameTime;
        if (m_isGrazing)
        {
            if (now < m_grazeEnd)
            {
                m_model.FeedOrder = true;
                m_model.LookAtOrder = m_grassTarget + new Vector3(0f, 0.25f, 0f);
                return;
            }
            StopGrazing();
            m_nextGrassSearch = now + 3.0 + m_random.NextDouble() * 4.0;
        }

        m_model.FeedOrder = false;
        if (now < m_nextGrassSearch)
            return;
        m_nextGrassSearch = now + 1.0;

        if (!TryFindGrass(out Vector3 target))
        {
            StopOwnedPath();
            return;
        }

        Vector3 position = m_creature.ComponentBody.Position;
        float distanceSquared = Vector3.DistanceSquared(position, target);
        if (distanceSquared <= 2.1f * 2.1f)
        {
            StopOwnedPath();
            m_grassTarget = target;
            m_isGrazing = true;
            m_grazeEnd = now + 3.5 + m_random.NextDouble() * 3.5;
            m_model.FeedOrder = true;
            m_model.LookAtOrder = target + new Vector3(0f, 0.25f, 0f);
            return;
        }

        if (now >= m_nextPathUpdate || !m_pathfinding.Destination.HasValue || m_pathfinding.IsStuck)
        {
            m_nextPathUpdate = now + 1.4;
            m_pathfinding.SetDestination(target, GrazeApproachSpeed, 0.75f, 90,
                useRandomMovements: true, ignoreHeightDifference: false,
                raycastDestination: true, doNotAvoidBody: null);
            m_lastOwnedDestination = target;
        }
    }

    private bool TryFindGrass(out Vector3 target)
    {
        target = Vector3.Zero;
        Vector3 position = m_creature.ComponentBody.Position;
        int cx = (int)Math.Floor(position.X);
        int cy = (int)Math.Floor(position.Y);
        int cz = (int)Math.Floor(position.Z);
        float bestDistance = 7f * 7f;
        bool found = false;

        for (int x = cx - 4; x <= cx + 4; x++)
        for (int z = cz - 4; z <= cz + 4; z++)
        for (int y = Math.Max(1, cy - 2); y <= Math.Min(254, cy + 1); y++)
        {
            int value = m_subsystemTerrain.Terrain.GetCellValue(x, y, z);
            int contents = Terrain.ExtractContents(value);
            if (contents != GrassBlock.Index && contents != TallGrassBlock.Index)
                continue;
            Vector3 candidate = new Vector3(x + 0.5f, y + 1f, z + 0.5f);
            float d = Vector3.DistanceSquared(position, candidate);
            if (d < bestDistance)
            {
                bestDistance = d;
                target = candidate;
                found = true;
            }
        }
        return found;
    }

    private void StopGrazing()
    {
        m_isGrazing = false;
        if (m_model != null)
        {
            m_model.FeedOrder = false;
            m_model.LookAtOrder = null;
        }
    }

    private void UpdateSheepSounds()
    {
        Vector3 position = m_creature.ComponentBody.Position;
        double now = m_time.GameTime;

        // Audible startup probe: play on the first component update.
        if (!m_audioProbePlayed)
        {
            m_audio.PlayRandomSound("Audio/LivingTowns/Sheep/baa", 1f, 0f,
                position, 24f, autoDelay: false);
            Log.Information("[LivingTowns Sheep] First bleat playback requested.");
            m_audioProbePlayed = true;
            m_nextAmbientBleat = now + 6.0;
        }

        float health = m_creature.ComponentHealth.Health;
        if (health < m_lastHealth - 0.0001f)
        {
            m_audio.PlayRandomSound("Audio/LivingTowns/Sheep/baa_hurt", 1f, 0f,
                position, 24f, autoDelay: false);
        }
        m_lastHealth = health;

        // Repeat at a predictable interval while alive, even when walking.
        if (health > 0f && now >= m_nextAmbientBleat)
        {
            string sound = m_random.NextDouble() < 0.25
                ? "Audio/LivingTowns/Sheep/baa_rare"
                : "Audio/LivingTowns/Sheep/baa";
            m_audio.PlayRandomSound(sound, 1f, 0f,
                position, 24f, autoDelay: false);
            m_nextAmbientBleat = now + 7.0 + m_random.NextDouble() * 3.0;
        }
    }

    private void StopOwnedPath()
    {
        if (!m_lastOwnedDestination.HasValue)
            return;
        if (m_pathfinding.Destination.HasValue &&
            Vector3.DistanceSquared(m_pathfinding.Destination.Value, m_lastOwnedDestination.Value) < 0.04f)
            m_pathfinding.Stop();
        m_lastOwnedDestination = null;
    }

    private void UpdateFollow()
    {
        if (m_target == null)
            return;
        Vector3 targetPosition = m_target.ComponentBody.Position;
        float distanceSquared = Vector3.DistanceSquared(m_creature.ComponentBody.Position, targetPosition);
        m_model.LookAtOrder = targetPosition + new Vector3(0f, 0.8f, 0f);
        if (distanceSquared <= FollowStopDistance * FollowStopDistance)
        {
            StopOwnedPath();
            return;
        }
        if (m_time.GameTime >= m_nextPathUpdate || !m_pathfinding.Destination.HasValue || m_pathfinding.IsStuck)
        {
            m_nextPathUpdate = m_time.GameTime + 0.45;
            m_pathfinding.SetDestination(targetPosition, FollowSpeed, 1.2f, 200,
                useRandomMovements: true, ignoreHeightDifference: false,
                raycastDestination: true, doNotAvoidBody: m_target.ComponentBody);
            m_lastOwnedDestination = targetPosition;
        }
    }

    private void UpdateFlee()
    {
        if (m_fleeTarget == null)
            return;
        if (!m_fleeBleatPlayed)
        {
            m_audio.PlayRandomSound("Audio/LivingTowns/Sheep/baa_alarm", 0.8f, 0f,
                m_creature.ComponentBody.Position, 9f, autoDelay: false);
            m_fleeBleatPlayed = true;
        }
        Vector3 sheepPosition = m_creature.ComponentBody.Position;
        Vector3 threatPosition = m_fleeTarget.ComponentBody.Position;
        Vector3 away = new Vector3(sheepPosition.X - threatPosition.X, 0f, sheepPosition.Z - threatPosition.Z);
        if (away.LengthSquared() < 0.001f)
            away = m_creature.ComponentBody.Matrix.Forward;
        away = Vector3.Normalize(new Vector3(away.X, 0f, away.Z));
        Vector3 destination = sheepPosition + away * 10f;
        m_model.LookAtOrder = threatPosition + new Vector3(0f, 0.8f, 0f);
        if (m_time.GameTime >= m_nextPathUpdate || !m_pathfinding.Destination.HasValue || m_pathfinding.IsStuck)
        {
            m_nextPathUpdate = m_time.GameTime + 0.55;
            m_pathfinding.SetDestination(destination, FleeSpeed, 1.2f, 200,
                useRandomMovements: false, ignoreHeightDifference: true,
                raycastDestination: false, doNotAvoidBody: m_fleeTarget.ComponentBody);
            m_lastOwnedDestination = destination;
        }
    }
}