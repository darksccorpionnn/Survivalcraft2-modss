using System;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

/// <summary>Grass grazing, loose flocking, coordinated fleeing and controlled bleats.</summary>
public sealed class ComponentLivingTownsSheepBehavior : ComponentBehavior, IUpdateable
{
    private const string SheepHerdName = "LivingTowns_Sheep";
    private const float FollowRange = 24f;
    private const float FollowStopDistance = 1.45f;
    private const float FleeSpeed = 1.12f;
    private const float FollowSpeed = 0.68f;
    private const float GrazeApproachSpeed = 0.42f;
    private const float HerdScanRange = 32f;
    private string m_voiceSet = "Sheep";

    private enum SheepMode { None, Follow, Flee }

    private ComponentCreature m_creature;
    private ComponentPathfinding m_pathfinding;
    private ComponentCreatureModel m_model;
    private SubsystemPlayers m_players;
    private SubsystemTime m_time;
    private SubsystemAudio m_audio;
    private SubsystemTerrain m_subsystemTerrain;
    private SubsystemCreatureSpawn m_subsystemCreatureSpawn;
    private SubsystemSeasons m_subsystemSeasons;
    private readonly System.Random m_random = new System.Random();

    private SheepMode m_appliedMode;
    private SheepMode m_mode;
    private Vector3? m_lastOwnedDestination;
    private ComponentPlayer m_target;
    private ComponentBody m_fleeTarget;
    private ComponentBody m_sharedFleeTarget;
    private double m_sharedFleeUntil;
    private double m_nextPathUpdate;
    private double m_nextGrassSearch;
    private double m_grazeEnd;
    private double m_nextFleeBleat;
    private Vector3 m_grassTarget;
    private Vector3 m_followOffset;
    private int m_grassCellX;
    private int m_grassCellY;
    private int m_grassCellZ;
    private bool m_grassCellIsTallGrass;
    private bool m_isGrazing;
    private bool m_isTamed;
    private bool m_isPregnant;
    private double m_nextReproductionCheck;

    public UpdateOrder UpdateOrder => UpdateOrder.Default;

    public bool IsTamed => m_isTamed;

    /// <summary>Marks adult sheep or rams as player-tamed. Lambs remain dependent young.</summary>
    public bool TryTame()
    {
        if (m_isTamed || m_voiceSet == "Lamb")
            return false;

        m_isTamed = true;
        return true;
    }

    public override float ImportanceLevel
    {
        get
        {
            RefreshTargets();
            return m_mode switch
            {
                SheepMode.Follow => 65f,
                SheepMode.Flee => 420f,
                _ when m_isGrazing => 90f,
                _ => 45f
            };
        }
    }

    public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap)
    {
        base.Load(valuesDictionary, idToEntityMap);

        // Existing sheep saved before per-variant voices may not have this key.
        // Fall back to the original sheep voice instead of failing component load.
        string voiceSet = valuesDictionary.GetValue<string>("VoiceSet", "Sheep");
        m_voiceSet = voiceSet == "Ram" || voiceSet == "Lamb" ? voiceSet : "Sheep";

        m_creature = Entity.FindComponent<ComponentCreature>(throwOnError: true);
        m_pathfinding = Entity.FindComponent<ComponentPathfinding>(throwOnError: true);
        m_model = Entity.FindComponent<ComponentCreatureModel>(throwOnError: true);
        m_players = Project.FindSubsystem<SubsystemPlayers>(throwOnError: true);
        m_time = Project.FindSubsystem<SubsystemTime>(throwOnError: true);
        m_audio = Project.FindSubsystem<SubsystemAudio>(throwOnError: true);
        m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
        m_subsystemCreatureSpawn = Project.FindSubsystem<SubsystemCreatureSpawn>(throwOnError: true);
        m_subsystemSeasons = Project.FindSubsystem<SubsystemSeasons>(throwOnError: true);
        m_isTamed = valuesDictionary.GetValue<bool>("IsTamed", false);
        m_isPregnant = valuesDictionary.GetValue<bool>("IsPregnant", false);

        double angle = m_random.NextDouble() * Math.PI * 2.0;
        float radius = 2.7f + (float)m_random.NextDouble() * 2.0f;
        m_followOffset = new Vector3((float)Math.Cos(angle) * radius, 0f, (float)Math.Sin(angle) * radius);

        m_mode = SheepMode.None;
        m_appliedMode = SheepMode.None;
        m_nextGrassSearch = m_time.GameTime + 1.0 + m_random.NextDouble() * 2.0;

        // Damage sounds are event-driven: one bleat per actual hit, not a polling loop.
        m_creature.ComponentHealth.Attacked += OnAttacked;
    }

    public override void Save(ValuesDictionary valuesDictionary, EntityToIdMap entityToIdMap)
    {
        base.Save(valuesDictionary, entityToIdMap);
        valuesDictionary.SetValue("IsTamed", m_isTamed);
        valuesDictionary.SetValue("IsPregnant", m_isPregnant);
    }

    public void Update(float dt)
    {
        if (m_creature == null || m_model == null)
            return;

        RefreshTargets();

        if (m_creature.ComponentHealth.Health <= 0f)
        {
            StopGrazing();
            StopOwnedPath();
            return;
        }

        // Keep feeding state owned by this component. The behavior scheduler may
        // temporarily select another AI behavior; clearing FeedOrder here caused
        // the grazing animation to restart and the sheep to bob its head.

        if (m_mode != m_appliedMode)
        {
            StopOwnedPath();
            StopGrazing();
            m_nextPathUpdate = 0;
            m_appliedMode = m_mode;
        }

        UpdateReproduction();

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
        SheepMode previousMode = m_mode;
        m_target = null;
        m_fleeTarget = null;
        m_mode = SheepMode.None;

        double now = m_time.GameTime;
        if (m_sharedFleeTarget != null &&
            m_sharedFleeUntil > now &&
            m_sharedFleeTarget.Entity.IsAddedToProject)
        {
            m_fleeTarget = m_sharedFleeTarget;
            m_mode = SheepMode.Flee;
            return;
        }

        Vector3 position = m_creature.ComponentBody.Position;
        float nearestGrassPlayerDistance = FollowRange * FollowRange;

        foreach (ComponentPlayer player in m_players.ComponentPlayers)
        {
            if (player == null || player.ComponentHealth == null ||
                player.ComponentHealth.Health <= 0f || player.ComponentMiner == null)
                continue;

            float distanceSquared = Vector3.DistanceSquared(position, player.ComponentBody.Position);
            if (distanceSquared <= nearestGrassPlayerDistance &&
                (IsHoldingGrass(player) || (m_isTamed && IsHoldingGrain(player))))
            {
                nearestGrassPlayerDistance = distanceSquared;
                m_target = player;
            }
        }

        if (m_target != null)
        {
            m_mode = SheepMode.Follow;
            return;
        }

        // Sheep do not flee merely because a player approaches.
        // Fleeing is triggered only by an actual attack (OnAttacked).
        if (m_sharedFleeUntil <= now)
            m_sharedFleeTarget = null;
    }

    private static bool IsHoldingGrass(ComponentPlayer player)
    {
        int contents = Terrain.ExtractContents(player.ComponentMiner.ActiveBlockValue);
        return contents == GrassBlock.Index || contents == TallGrassBlock.Index;
    }

    private static bool IsHoldingGrain(ComponentPlayer player)
    {
        // Survivalcraft 2.4.0.0 exposes RyeBlock as its native cereal crop block.
        return Terrain.ExtractContents(player.ComponentMiner.ActiveBlockValue) == RyeBlock.Index;
    }

    private void UpdateReproduction()
    {
        // Adult female sheep can become pregnant only in autumn; birth is tied to
        // the game's own season transition into winter. No parallel calendar is used.
        if (m_voiceSet != "Sheep" || !m_isTamed || m_subsystemSeasons == null)
            return;

        if (m_isPregnant)
        {
            if (m_subsystemSeasons.Season == Season.Winter)
            {
                Vector3 birthPosition = m_creature.ComponentBody.Position + new Vector3(1.2f, 0f, 0f);
                Entity lamb = m_subsystemCreatureSpawn.SpawnCreature("LivingTowns_Lamb", birthPosition, false);
                if (lamb != null)
                    m_isPregnant = false;
            }
            return;
        }

        if (m_subsystemSeasons.Season != Season.Autumn || m_time.GameTime < m_nextReproductionCheck)
            return;

        m_nextReproductionCheck = m_time.GameTime + 8.0;
        Vector3 position = m_creature.ComponentBody.Position;
        foreach (ComponentCreature creature in m_subsystemCreatureSpawn.Creatures)
        {
            if (creature == null || creature.ComponentHealth == null ||
                creature.ComponentHealth.Health <= 0f ||
                Vector3.DistanceSquared(position, creature.ComponentBody.Position) > 6f * 6f)
                continue;

            ComponentLivingTownsSheepBehavior partner =
                creature.Entity.FindComponent<ComponentLivingTownsSheepBehavior>();
            if (partner != null && partner.m_voiceSet == "Ram" && partner.m_isTamed)
            {
                m_isPregnant = true;
                return;
            }
        }
    }

    private void UpdateFollow()
    {
        if (m_target == null)
            return;

        Vector3 playerPosition = m_target.ComponentBody.Position;
        Vector3 destination = playerPosition + m_followOffset;
        destination.Y = playerPosition.Y;

        float distanceSquared = Vector3.DistanceSquared(m_creature.ComponentBody.Position, destination);
        m_model.LookAtOrder = playerPosition + new Vector3(0f, 0.8f, 0f);

        if (distanceSquared <= FollowStopDistance * FollowStopDistance)
        {
            StopOwnedPath();
            return;
        }

        if (m_time.GameTime >= m_nextPathUpdate ||
            !m_pathfinding.Destination.HasValue || m_pathfinding.IsStuck)
        {
            m_nextPathUpdate = m_time.GameTime + 0.55;
            m_pathfinding.SetDestination(destination, FollowSpeed, 1.2f, 200,
                useRandomMovements: true, ignoreHeightDifference: false,
                raycastDestination: true, doNotAvoidBody: m_target.ComponentBody);
            m_lastOwnedDestination = destination;
        }
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

            EatSelectedGrass();
            StopGrazing();
            m_nextGrassSearch = now + 3.5 + m_random.NextDouble() * 4.5;
        }

        m_model.FeedOrder = false;
        if (now < m_nextGrassSearch)
            return;

        m_nextGrassSearch = now + 1.2;
        if (!TryFindGrass(out Vector3 target, out int cellX, out int cellY, out int cellZ, out bool isTallGrass))
        {
            StopOwnedPath();
            return;
        }

        float distanceSquared = Vector3.DistanceSquared(m_creature.ComponentBody.Position, target);
        if (distanceSquared <= 2.0f * 2.0f)
        {
            StopOwnedPath();
            m_grassTarget = target;
            m_grassCellX = cellX;
            m_grassCellY = cellY;
            m_grassCellZ = cellZ;
            m_grassCellIsTallGrass = isTallGrass;
            m_isGrazing = true;
            m_grazeEnd = now + 10.0 + m_random.NextDouble() * 5.0;
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

    private bool TryFindGrass(out Vector3 target, out int cellX, out int cellY, out int cellZ, out bool isTallGrass)
    {
        target = Vector3.Zero;
        cellX = cellY = cellZ = 0;
        isTallGrass = false;

        Vector3 position = m_creature.ComponentBody.Position;
        int cx = Terrain.ToCell(position.X);
        int cy = Terrain.ToCell(position.Y);
        int cz = Terrain.ToCell(position.Z);
        float bestScore = 10f * 10f;
        bool found = false;

        for (int x = cx - 6; x <= cx + 6; x++)
        for (int z = cz - 6; z <= cz + 6; z++)
        for (int y = Math.Max(1, cy - 2); y <= Math.Min(254, cy + 1); y++)
        {
            int value = m_subsystemTerrain.Terrain.GetCellValue(x, y, z);
            int contents = Terrain.ExtractContents(value);
            bool tall = contents == TallGrassBlock.Index;
            bool turf = contents == GrassBlock.Index;
            if (!tall && !turf)
                continue;

            Vector3 candidate;
            if (tall)
            {
                int ground = Terrain.ExtractContents(m_subsystemTerrain.Terrain.GetCellValue(x, y - 1, z));
                if (ground != GrassBlock.Index && ground != DirtBlock.Index)
                    continue;
                candidate = new Vector3(x + 0.5f, y, z + 0.5f);
            }
            else
            {
                candidate = new Vector3(x + 0.5f, y + 1f, z + 0.5f);
            }

            float distanceSquared = Vector3.DistanceSquared(position, candidate);
            // Prefer grazing a tuft before stripping the ground cover itself.
            float score = tall ? Math.Max(0f, distanceSquared - 1.25f) : distanceSquared;
            if (score < bestScore)
            {
                bestScore = score;
                target = candidate;
                cellX = x;
                cellY = y;
                cellZ = z;
                isTallGrass = tall;
                found = true;
            }
        }

        return found;
    }

    private void EatSelectedGrass()
    {
        int value = m_subsystemTerrain.Terrain.GetCellValue(m_grassCellX, m_grassCellY, m_grassCellZ);
        int contents = Terrain.ExtractContents(value);

        if (m_grassCellIsTallGrass && contents == TallGrassBlock.Index)
        {
            m_subsystemTerrain.ChangeCell(m_grassCellX, m_grassCellY, m_grassCellZ, 0, true);
        }
        else if (!m_grassCellIsTallGrass && contents == GrassBlock.Index)
        {
            // Turn eaten turf into dirt; the normal grass spreading system can regrow it.
            m_subsystemTerrain.ChangeCell(m_grassCellX, m_grassCellY, m_grassCellZ,
                Terrain.MakeBlockValue(DirtBlock.Index), true);
        }
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

    private void UpdateFlee()
    {
        if (m_fleeTarget == null || !m_fleeTarget.Entity.IsAddedToProject)
            return;

        TryPlayFleeBleat();

        Vector3 threatPosition = m_fleeTarget.Position;
        Vector3 herdCenter = FindHerdCenter();
        Vector3 away = new Vector3(herdCenter.X - threatPosition.X, 0f, herdCenter.Z - threatPosition.Z);
        if (away.LengthSquared() < 0.001f)
            away = m_creature.ComponentBody.Matrix.Forward;
        away = Vector3.Normalize(new Vector3(away.X, 0f, away.Z));

        Vector3 groupDestination = herdCenter + away * 10f;
        // A small personal offset keeps sheep from stacking on the same destination.
        Vector3 destination = groupDestination + m_followOffset * 0.65f;
        m_model.LookAtOrder = destination + new Vector3(0f, 0.7f, 0f);

        if (m_time.GameTime >= m_nextPathUpdate ||
            !m_pathfinding.Destination.HasValue || m_pathfinding.IsStuck)
        {
            m_nextPathUpdate = m_time.GameTime + 0.55;
            m_pathfinding.SetDestination(destination, FleeSpeed, 1.25f, 200,
                useRandomMovements: false, ignoreHeightDifference: true,
                raycastDestination: false, doNotAvoidBody: null);
            m_lastOwnedDestination = destination;
        }
    }

    private string GetVoiceSoundPath(string soundName)
    {
        return $"Audio/LivingTowns/{m_voiceSet}/{soundName}";
    }

    private void TryPlayFleeBleat()
    {
        double now = m_time.GameTime;
        if (now < m_nextFleeBleat)
            return;

        ComponentLivingTownsSheepBehavior speaker = FindFleeSpeaker();
        if (speaker != this)
            return;

        m_audio.PlayRandomSound(GetVoiceSoundPath("baa_alarm"), 0.52f, 0f,
            m_creature.ComponentBody.Position, 16f, autoDelay: false);

        double next = now + 6.0 + m_random.NextDouble() * 2.0;
        SetHerdBleatTime(next);
    }

    private ComponentLivingTownsSheepBehavior FindFleeSpeaker()
    {
        ComponentLivingTownsSheepBehavior speaker = this;
        float bestDistance = m_fleeTarget != null
            ? Vector3.DistanceSquared(m_creature.ComponentBody.Position, m_fleeTarget.Position)
            : 0f;

        foreach (ComponentCreature creature in m_subsystemCreatureSpawn.Creatures)
        {
            if (creature == m_creature || creature.ComponentHealth.Health <= 0f)
                continue;

            ComponentHerdBehavior herd = creature.Entity.FindComponent<ComponentHerdBehavior>();
            if (herd == null || herd.HerdName != SheepHerdName)
                continue;

            if (Vector3.DistanceSquared(creature.ComponentBody.Position,
                m_creature.ComponentBody.Position) > HerdScanRange * HerdScanRange)
                continue;

            ComponentLivingTownsSheepBehavior behavior =
                creature.Entity.FindComponent<ComponentLivingTownsSheepBehavior>();
            if (behavior == null || behavior.m_mode != SheepMode.Flee || behavior.m_fleeTarget == null)
                continue;

            float distance = Vector3.DistanceSquared(creature.ComponentBody.Position, m_fleeTarget.Position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                speaker = behavior;
            }
        }

        return speaker;
    }

    private void SetHerdBleatTime(double nextTime)
    {
        foreach (ComponentCreature creature in m_subsystemCreatureSpawn.Creatures)
        {
            ComponentHerdBehavior herd = creature.Entity.FindComponent<ComponentHerdBehavior>();
            if (herd == null || herd.HerdName != SheepHerdName)
                continue;

            if (Vector3.DistanceSquared(creature.ComponentBody.Position,
                m_creature.ComponentBody.Position) > HerdScanRange * HerdScanRange)
                continue;

            ComponentLivingTownsSheepBehavior behavior =
                creature.Entity.FindComponent<ComponentLivingTownsSheepBehavior>();
            if (behavior != null && behavior.m_mode == SheepMode.Flee)
                behavior.m_nextFleeBleat = nextTime;
        }
    }

    private void OnAttacked(ComponentCreature attacker)
    {
        Vector3 position = m_creature.ComponentBody.Position;
        // PainSound is played by the native CreatureSounds component.
        if (attacker == null || attacker.ComponentBody == null)
            return;

        // Any attacker (player or creature) triggers one coordinated flock response.
        PropagateHerdFlee(attacker.ComponentBody);

        // Native runaway AI already knows how to choose a safe place relative to its herd.
        // Wake the nearby flock when one sheep is actually attacked.
        foreach (ComponentCreature creature in m_subsystemCreatureSpawn.Creatures)
        {
            if (creature == m_creature || creature.ComponentHealth.Health <= 0f)
                continue;

            ComponentHerdBehavior herd = creature.Entity.FindComponent<ComponentHerdBehavior>();
            if (herd == null || herd.HerdName != SheepHerdName)
                continue;

            if (Vector3.DistanceSquared(creature.ComponentBody.Position, position) > HerdScanRange * HerdScanRange)
                continue;

            ComponentRunAwayBehavior runaway = creature.Entity.FindComponent<ComponentRunAwayBehavior>();
            if (runaway != null)
                runaway.RunAwayFrom(attacker.ComponentBody);
        }
    }

    private void PropagateHerdFlee(ComponentBody threat)
    {
        if (threat == null)
            return;

        double until = m_time.GameTime + 10.0;
        foreach (ComponentCreature creature in m_subsystemCreatureSpawn.Creatures)
        {
            if (creature.ComponentHealth.Health <= 0f)
                continue;

            ComponentHerdBehavior herd = creature.Entity.FindComponent<ComponentHerdBehavior>();
            if (herd == null || herd.HerdName != SheepHerdName)
                continue;

            if (Vector3.DistanceSquared(creature.ComponentBody.Position,
                m_creature.ComponentBody.Position) > HerdScanRange * HerdScanRange)
                continue;

            ComponentLivingTownsSheepBehavior behavior =
                creature.Entity.FindComponent<ComponentLivingTownsSheepBehavior>();
            if (behavior != null)
                behavior.SetSharedFleeTarget(threat, until);
        }
    }

    private void SetSharedFleeTarget(ComponentBody threat, double until)
    {
        if (threat == null)
            return;

        m_sharedFleeTarget = threat;
        m_sharedFleeUntil = Math.Max(m_sharedFleeUntil, until);
        m_fleeTarget = threat;
        m_mode = SheepMode.Flee;
        m_nextFleeBleat = Math.Min(m_nextFleeBleat, m_time.GameTime);
    }

    private Vector3 FindHerdCenter()
    {
        Vector3 ownPosition = m_creature.ComponentBody.Position;
        Vector3 sum = Vector3.Zero;
        int count = 0;

        foreach (ComponentCreature creature in m_subsystemCreatureSpawn.Creatures)
        {
            if (creature.ComponentHealth.Health <= 0f)
                continue;

            ComponentHerdBehavior herd = creature.Entity.FindComponent<ComponentHerdBehavior>();
            if (herd == null || herd.HerdName != SheepHerdName)
                continue;

            Vector3 position = creature.ComponentBody.Position;
            if (Vector3.DistanceSquared(ownPosition, position) <= HerdScanRange * HerdScanRange)
            {
                sum += position;
                count++;
            }
        }

        return count > 0 ? sum / count : ownPosition;
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
}
