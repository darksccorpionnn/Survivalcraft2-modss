using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

/// <summary>
/// Passive sheep behavior for Living Towns.
/// It follows the nearest living player while that player holds Grass or TallGrass.
/// Without grass, it flees from a nearby player. Navigation is delegated to the
/// game's own ComponentPathfinding so terrain and obstacle handling stay native.
/// </summary>
public sealed class ComponentLivingTownsSheepBehavior : ComponentBehavior, IUpdateable
{
    private const float FollowRange = 16f;
    private const float FollowStopDistance = 2.35f;
    private const float FleeRange = 5.5f;
    private const float FollowSpeed = 0.68f;
    private const float FleeSpeed = 1.15f;

    private enum SheepMode
    {
        None,
        Follow,
        Flee
    }

    private ComponentCreature m_creature;
    private ComponentPathfinding m_pathfinding;
    private SubsystemPlayers m_players;
    private SubsystemTime m_time;
    private SubsystemAudio m_audio;
    private bool m_fleeBleatPlayed;
    private SheepMode m_appliedMode;
    private Vector3? m_lastOwnedDestination;
    private ComponentPlayer m_target;
    private ComponentPlayer m_fleeTarget;
    private SheepMode m_mode;
    private double m_nextPathUpdate;

    public UpdateOrder UpdateOrder => UpdateOrder.Default;

    public override float ImportanceLevel
    {
        get
        {
            RefreshTargets();
            return m_mode switch
            {
                SheepMode.Follow => 35f,
                SheepMode.Flee => 90f,
                _ => 0f
            };
        }
    }

    public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap)
    {
        base.Load(valuesDictionary, idToEntityMap);
        m_creature = Entity.FindComponent<ComponentCreature>(throwOnError: true);
        m_pathfinding = Entity.FindComponent<ComponentPathfinding>(throwOnError: true);
        m_players = Project.FindSubsystem<SubsystemPlayers>(throwOnError: true);
        m_time = Project.FindSubsystem<SubsystemTime>(throwOnError: true);
        m_audio = Project.FindSubsystem<SubsystemAudio>(throwOnError: true);
        m_mode = SheepMode.None;
    }

    public void Update(float dt)
    {
        if (m_creature == null)
            return;

        RefreshTargets();
        if (m_creature.ComponentHealth.Health <= 0f)
        {
            StopOwnedPath();
            return;
        }

        if (m_mode != SheepMode.Flee)
            m_fleeBleatPlayed = false;

        if (!IsActive)
        {
            StopOwnedPath();
            m_appliedMode = SheepMode.None;
            return;
        }

        if (m_mode != m_appliedMode)
        {
            StopOwnedPath();
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
                m_pathfinding.Stop();
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

        // A grass-holding player always takes precedence over a nearby threat.
        if (m_target != null)
        {
            m_mode = SheepMode.Follow;
            return;
        }

        foreach (ComponentPlayer player in m_players.ComponentPlayers)
        {
            if (player == null || player.ComponentHealth == null || player.ComponentHealth.Health <= 0f || player.ComponentMiner == null)
                continue;

            // Holding grass is an invitation, not a threat.
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
        m_creature.ComponentCreatureModel.LookAtOrder = targetPosition + new Vector3(0f, 0.8f, 0f);

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

        m_creature.ComponentCreatureModel.LookAtOrder = threatPosition + new Vector3(0f, 0.8f, 0f);
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
