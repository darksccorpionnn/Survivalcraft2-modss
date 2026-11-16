using System;
using System.Collections.Generic;
using System.Reflection;

namespace Game;

/// <summary>Loads the sheep behavior component contained in this addon assembly.</summary>
public sealed class LivingTownsSheepAddonModLoader : ModLoader
{
    private readonly Dictionary<ComponentPlayer, double> m_nextCreatureMeleeTime = new();
    private readonly Dictionary<ComponentCreature, double> m_nextPreyRetaliationTime = new();
    private readonly Dictionary<ComponentCreature, ComponentCreature> m_pendingPreyRetaliationPredators = new();
    private readonly Dictionary<ComponentCreature, double> m_pendingPreyRetaliationTimes = new();
    private readonly Dictionary<ComponentChaseBehavior, double> m_nextPredatorStrikeTime = new();
    private readonly Dictionary<ComponentChaseBehavior, double> m_nextPredatorTargetScanTime = new();
    private static readonly MethodInfo s_chaseAttackMethod = typeof(ComponentChaseBehavior).GetMethod(
        "Attack",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        null,
        new[] { typeof(ComponentCreature), typeof(float), typeof(float), typeof(bool) },
        null);
    private static readonly FieldInfo s_chaseMaxAttackRangeField =
        typeof(ComponentChaseBehavior).GetField(
            "MaxAttackRange", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly MethodInfo s_chaseStopAttackMethod =
        typeof(ComponentChaseBehavior).GetMethod(
            "StopAttack", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo s_chaseAutoChaseMaskField =
        typeof(ComponentChaseBehavior).GetField(
            "m_autoChaseMask", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo s_chaseNonPlayerProbabilityField =
        typeof(ComponentChaseBehavior).GetField(
            "m_chaseNonPlayerProbability", BindingFlags.Instance | BindingFlags.NonPublic);
    private BevelledButtonWidget m_villagerTradeButton;
    private CanvasWidget m_villagerTradeCanvas;
    private ContainerWidget m_villagerTradeButtonParent;

    public override void __ModInitialize()
    {
        ModsManager.RegisterHook("OnLoadingFinished", this);
        ModsManager.RegisterHook("GuiUpdate", this);
        ModsManager.RegisterHook("OnPlayerInputInteract", this);
        ModsManager.RegisterHook("OnPlayerInputHit", this);
        ModsManager.RegisterHook("ProcessAttackment", this);
        ModsManager.RegisterHook("CalculateCreatureInjuryAmount", this);
        ModsManager.RegisterHook("ChaseBehaviorScoreTarget", this);
        ModsManager.RegisterHook("UpdateChaseBehaviorLookingForTarget", this);
        ModsManager.RegisterHook("UpdateChaseBehaviorChasing", this);
        ModsManager.RegisterHook("OnChaseBehaviorAttacked", this);
        ModsManager.RegisterHook("InitializeCreatureTypes", this);
    }


    public override void GuiUpdate(ComponentGui componentGui)
    {
        if (componentGui == null)
            return;

        ComponentPlayer player = componentGui.m_componentPlayer;
        if (player != null)
            ProcessPendingPreyRetaliations(player);

        if (player == null || player.ComponentInput == null ||
            player.ComponentCreatureModel == null || player.ComponentMiner == null)
        {
            if (m_villagerTradeButton != null)
                m_villagerTradeButton.IsVisible = false;
            if (m_villagerTradeCanvas != null)
                m_villagerTradeCanvas.IsVisible = false;
            return;
        }

        if (m_villagerTradeButton == null)
        {
            m_villagerTradeButton = new BevelledButtonWidget
            {
                Text = "≡",
                DesiredSize = new Engine.Vector2(60f, 60f),
                Margin = Engine.Vector2.Zero,
                HorizontalAlignment = WidgetAlignment.Near,
                VerticalAlignment = WidgetAlignment.Near,
                IsVisible = false
            };
        }

        // Keep this overlay button-sized. A full-screen transparent canvas
        // still sits above the mobile HUD and intercepts all touch controls.
        // This 68x68 canvas occupies only the trade icon's hit-test area.
        ContainerWidget buttonParent = componentGui.ControlsContainerWidget;
        if (buttonParent != null && m_villagerTradeButtonParent != buttonParent)
        {
            if (m_villagerTradeCanvas != null && m_villagerTradeButtonParent != null)
                m_villagerTradeButtonParent.RemoveChildren(m_villagerTradeCanvas);

            m_villagerTradeCanvas = new CanvasWidget
            {
                Size = new Engine.Vector2(68f, 68f),
                DesiredSize = new Engine.Vector2(68f, 68f),
                HorizontalAlignment = WidgetAlignment.Far,
                VerticalAlignment = WidgetAlignment.Center,
                Margin = new Engine.Vector2(8f, 0f),
                IsVisible = false
            };
            m_villagerTradeCanvas.AddChildren(m_villagerTradeButton);
            buttonParent.AddChildren(m_villagerTradeCanvas);
            m_villagerTradeButtonParent = buttonParent;
        }

        if (m_villagerTradeCanvas != null)
            CanvasWidget.SetPosition(m_villagerTradeButton, new Engine.Vector2(4f, 4f));

        if (componentGui.ModalPanelWidget != null || componentGui.IsInventoryVisible())
        {
            m_villagerTradeButton.IsVisible = false;
            if (m_villagerTradeCanvas != null)
                m_villagerTradeCanvas.IsVisible = false;
            return;
        }

        // Show the button while the player is within 3.75 blocks of any
        // Living Towns villager; looking directly at the villager is not required.
        SubsystemBodies bodies = player.Project.FindSubsystem<SubsystemBodies>(throwOnError: true);
        Engine.Vector3 playerEye = player.ComponentCreatureModel.EyePosition;
        ComponentBody nearestVillager = null;
        float nearestDistance = 3.75f;

        foreach (ComponentBody body in bodies.Bodies)
        {
            if (body == null || body.Entity == null || body.Entity == player.Entity ||
                !IsLivingTownsVillager(body.Entity))
                continue;

            float distance = Engine.Vector3.Distance(playerEye, body.Position);
            if (distance <= nearestDistance)
            {
                nearestDistance = distance;
                nearestVillager = body;
            }
        }

        m_villagerTradeButton.IsVisible = nearestVillager != null;
        m_villagerTradeButton.IsEnabled = nearestVillager != null;
        if (m_villagerTradeCanvas != null)
            m_villagerTradeCanvas.IsVisible = nearestVillager != null;

        // IsClicked is the button's one-frame touch/click event.
        if (nearestVillager != null && m_villagerTradeButton.IsClicked)
            OpenVillagerTrade(player, nearestVillager);
    }

    /// <summary>
    /// Runs herbivore retaliation on a later GUI/game update, never from inside
    /// the predator's own Attackment processing. A retaliation is queued only
    /// after CalculateCreatureInjuryAmount confirms a real predator injury.
    /// </summary>
    private void ProcessPendingPreyRetaliations(ComponentPlayer player)
    {
        if (player == null)
            return;

        double now = player.Project.FindSubsystem<SubsystemTime>(throwOnError: true).GameTime;
        var pendingPrey = new List<ComponentCreature>(m_pendingPreyRetaliationPredators.Keys);
        foreach (ComponentCreature prey in pendingPrey)
        {
            if (prey == null ||
                !m_pendingPreyRetaliationPredators.TryGetValue(prey, out ComponentCreature predator) ||
                predator == null ||
                prey.Project != player.Project ||
                predator.Project != player.Project ||
                prey.Entity == null || predator.Entity == null ||
                !prey.Entity.IsAddedToProject || !predator.Entity.IsAddedToProject)
            {
                m_pendingPreyRetaliationPredators.Remove(prey);
                m_pendingPreyRetaliationTimes.Remove(prey);
                continue;
            }

            if (!m_pendingPreyRetaliationTimes.TryGetValue(prey, out double dueTime) ||
                now < dueTime)
                continue;

            // Remove before attacking: the counter-hit itself must not be able
            // to re-enter or duplicate this pending retaliation.
            m_pendingPreyRetaliationPredators.Remove(prey);
            m_pendingPreyRetaliationTimes.Remove(prey);
            TryApplyPreyRetaliation(prey, predator);
        }
    }

    private static bool IsLivingTownsVillager(GameEntitySystem.Entity entity)
    {
        if (entity == null)
            return false;

        foreach (GameEntitySystem.Component component in entity.Components)
        {
            if (component != null)
            {
                string componentName = component.GetType().Name;
                if (componentName == "ComponentLivingTownsVillager" ||
                    componentName == "ComponentLivingTownsVillagerController")
                    return true;
            }
        }

        return false;
    }

    private static void OpenVillagerTrade(ComponentPlayer player, ComponentBody villagerBody)
    {
        if (player == null || player.ComponentCreatureModel == null ||
            player.ComponentMiner == null || villagerBody == null)
            return;

        Engine.Vector3 origin = player.ComponentCreatureModel.EyePosition;
        Engine.Vector3 direction = villagerBody.Position - origin;
        if (direction.LengthSquared() < 0.0001f)
            return;

        // Call the same native body-hit entry that the game's tap input uses.
        // Living Towns' existing Harmony interaction patch intercepts this
        // villager hit and opens the existing trade screen without damage.
        player.ComponentMiner.Hit(
            villagerBody,
            villagerBody.Position,
            Engine.Vector3.Normalize(direction));
    }

    public override void InitializeCreatureTypes(
        SubsystemCreatureSpawn spawn,
        List<SubsystemCreatureSpawn.CreatureType> creatureTypes)
    {
        if (spawn == null || creatureTypes == null)
            return;

        foreach (SubsystemCreatureSpawn.CreatureType existing in creatureTypes)
        {
            if (existing != null && existing.Name == "LivingTowns_SheepHerd")
                return;
        }

        SubsystemTerrain terrain = spawn.Project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
        SubsystemSeasons seasons = spawn.Project.FindSubsystem<SubsystemSeasons>(throwOnError: true);

        // One native surface-spawn entry creates a small mixed flock. The game's
        // own spawn scheduler handles chunk limits, daylight, visibility and spacing.
        var flockType = new SubsystemCreatureSpawn.CreatureType(
            "LivingTowns_SheepHerd", SpawnLocationType.Surface,
            randomSpawn: true, constantSpawn: false);

        flockType.SpawnSuitabilityFunction = (type, point) =>
        {
            if (seasons.Season != Season.Spring && seasons.Season != Season.Summer)
                return 0f;

            float shoreDistance = terrain.TerrainContentsGenerator.CalculateOceanShoreDistance(
                point.X, point.Z);
            int humidity = terrain.Terrain.GetHumidity(point.X, point.Z);
            int temperature = terrain.Terrain.GetTemperature(point.X, point.Z);
            if (shoreDistance <= 20f || humidity <= 4 || temperature < 5 || point.Y >= 90)
                return 0f;

            int ground = Terrain.ExtractContents(
                terrain.Terrain.GetCellValue(point.X, point.Y - 1, point.Z));
            if (ground != GrassBlock.Index && ground != DirtBlock.Index)
                return 0f;

            int grazingPatches = 0;
            for (int x = point.X - 3; x <= point.X + 3; x++)
            for (int z = point.Z - 3; z <= point.Z + 3; z++)
            {
                int below = Terrain.ExtractContents(
                    terrain.Terrain.GetCellValue(x, point.Y - 1, z));
                int above = Terrain.ExtractContents(
                    terrain.Terrain.GetCellValue(x, point.Y, z));
                if (below == GrassBlock.Index || above == TallGrassBlock.Index)
                    grazingPatches++;
            }

            // Prefer inland grassland/meadow surfaces with enough nearby feed.
            return grazingPatches >= 3 ? 0.06f : 0f;
        };

        flockType.SpawnFunction = (type, point) =>
        {
            int ewes = spawn.SpawnCreatures(type, "LivingTowns_Sheep", point, 3).Count;
            int rams = spawn.SpawnCreatures(type, "LivingTowns_Ram", point, 1).Count;
            return ewes + rams;
        };

        creatureTypes.Add(flockType);
    }

    public override void OnPlayerInputHit(
        ComponentPlayer componentPlayer,
        ref bool playerOperated,
        ref double timeIntervalHit,
        ref float meleeAttackRange,
        bool skippedByOtherMods,
        out bool skipVanilla)
    {
        // Respect earlier mods and preserve the normal game path unless this
        // handler has positively identified a living creature under the hit ray.
        skipVanilla = skippedByOtherMods;
        if (skippedByOtherMods || playerOperated || componentPlayer == null)
            return;

        ComponentMiner miner = componentPlayer.ComponentMiner;
        ComponentInput input = componentPlayer.ComponentInput;
        if (miner == null || input == null)
            return;

        // Use the exact body-only raycast used by ComponentPlayer.Update.
        // The old generic Raycast call could return terrain/moving-block results
        // instead of the BodyRaycastResult expected by the melee path.
        meleeAttackRange = Math.Max(meleeAttackRange, 3.25f);
        timeIntervalHit = Math.Max(timeIntervalHit, 0.33);

        var hitRay = input.PlayerInput.Hit;
        if (!hitRay.HasValue)
            return;

        var hitResult = miner.Raycast<BodyRaycastResult>(
            hitRay.Value,
            RaycastMode.Digging,
            true,
            true,
            true,
            meleeAttackRange);

        if (!hitResult.HasValue)
            return;

        BodyRaycastResult bodyHit = hitResult.Value;
        ComponentBody targetBody = bodyHit.ComponentBody;
        if (targetBody == null || targetBody.Entity == null ||
            targetBody.Entity == componentPlayer.Entity)
            return;

        if (IsLivingTownsVillager(targetBody.Entity))
        {
            // Villagers are interacted with through the proximity trade button,
            // never through the attack input.
            playerOperated = true;
            skipVanilla = true;
            return;
        }

        // Route every living creature through the same native Attackment path.
        // This prevents the sheep-specific fix from leaving ordinary animals
        // dependent on the vanilla moving-target hit-probability roll.
        ComponentCreature targetCreature =
            targetBody.Entity.FindComponent<ComponentCreature>();
        ComponentHealth targetHealth =
            targetBody.Entity.FindComponent<ComponentHealth>();
        if (targetCreature == null || targetHealth == null || targetHealth.Health <= 0f)
            return;

        // Match the game's own distance guard (from the player's eye to the
        // precise body hit point), rather than trusting raycast reach alone.
        Engine.Vector3 hitPoint = bodyHit.HitPoint();
        float hitDistance = Engine.Vector3.Distance(
            hitPoint, componentPlayer.ComponentCreatureModel.EyePosition);
        if (hitDistance > meleeAttackRange)
            return;

        // ComponentMiner.Hit can reject attacks while another action is pending
        // or while its internal cooldown is active. Once a creature is positively
        // targeted, use the same native Attackment damage pipeline directly.
        double now = componentPlayer.Project.FindSubsystem<SubsystemTime>(throwOnError: true).GameTime;
        if (m_nextCreatureMeleeTime.TryGetValue(componentPlayer, out double nextTime) &&
            now < nextTime)
        {
            playerOperated = true;
            skipVanilla = true;
            return;
        }

        if (!miner.IsLevelSufficientForTool(miner.ActiveBlockValue))
            return;

        float power = miner.AttackPower;
        if (miner.ActiveBlockValue != 0)
        {
            int contents = Terrain.ExtractContents(miner.ActiveBlockValue);
            Block heldBlock = BlocksManager.Blocks[contents];
            power *= heldBlock.GetMeleePower(miner.ActiveBlockValue);
        }

        power *= miner.StrengthFactor;
        // Never allow a valid player-vs-animal melee hit to become zero power.
        power = Math.Max(power, 1f);

        float healthBefore = targetHealth.Health;
        var attackment = new Attackment(
            targetBody,
            componentPlayer.Entity,
            hitPoint,
            hitRay.Value.Direction,
            power);

        // Preserve the game's normal armor and resilience rules while ensuring
        // the usual native hit feedback can be emitted by Attackment.
        attackment.EnableArmorProtection = true;
        attackment.EnableResilienceFactor = true;
        attackment.EnableHitValueParticleSystem = true;

        ComponentMiner.AttackBody(attackment);

        if (targetHealth.Health < healthBefore)
        {
            // Match the native successful-hit side effects that DoMeleeHit
            // normally performs after AttackBody.
            miner.DamageActiveTool(1);
            ComponentCreature attackerCreature = miner.ComponentCreature;
            if (attackerCreature != null && attackerCreature.PlayerStats != null)
            {
                attackerCreature.PlayerStats.MeleeAttacks++;
                attackerCreature.PlayerStats.MeleeHits++;
            }
            miner.Poke(false);

            // We handled this exact creature hit. Stop vanilla from applying
            // another attack for the same input and throttle held touch input.
            m_nextCreatureMeleeTime[componentPlayer] = now + timeIntervalHit;
            playerOperated = true;
            skipVanilla = true;
            return;
        }

        // Do not consume the input if the direct Attackment did not lower health.
        // ComponentPlayer.Update will then perform exactly one normal vanilla hit.
    }

    public override void ChaseBehaviorScoreTarget(
        ComponentChaseBehavior chaseBehavior,
        ComponentCreature creatureToBeTarget,
        ref float score)
    {
        if (chaseBehavior == null || creatureToBeTarget == null ||
            creatureToBeTarget.Entity == null)
            return;

        ComponentCreature attacker =
            chaseBehavior.Entity.FindComponent<ComponentCreature>();
        if (attacker == null || attacker == creatureToBeTarget ||
            attacker.Entity == null || !attacker.Entity.IsAddedToProject)
            return;

        // Keep player combat settings and villagers untouched.
        if (attacker.Entity.FindComponent<ComponentPlayer>() != null ||
            creatureToBeTarget.Entity.FindComponent<ComponentPlayer>() != null)
            return;

        if (IsLivingTownsVillager(creatureToBeTarget.Entity))
        {
            score = 0f;
            return;
        }

        // Natural predators may hunt ordinary animals. Other animals only receive
        // a target score for a predator that is actively engaging them (the
        // retaliation target is assigned in ProcessAttackment).
        bool isPredator = IsNaturalPredator(attacker);
        if (!isPredator)
            return;

        ComponentHealth targetHealth = creatureToBeTarget.ComponentHealth;
        if (targetHealth == null || targetHealth.Health <= 0f ||
            targetHealth.IsInvulnerable)
            return;

        // Do not let predators lock onto one another; only living prey can
        // receive a positive chase score.
        if (IsNaturalPredator(creatureToBeTarget))
        {
            score = 0f;
            return;
        }

        // Native target scoring can produce zero when AutoChaseMask does not
        // include prey or ChaseNonPlayerProbability rejects it. Restore a small
        // positive score so native FindTarget can select the living prey.
        if (score <= 0f)
        {
            float distance = Engine.Vector3.Distance(
                attacker.ComponentBody.Position,
                creatureToBeTarget.ComponentBody.Position);
            if (distance > 5f)
                return;
            score = 0.01f / (1f + distance);
        }

        chaseBehavior.AttacksNonPlayerCreature = true;
    }

    private static bool IsNaturalPredator(ComponentCreature creature)
    {
        const CreatureCategory predatorMask =
            CreatureCategory.LandPredator | CreatureCategory.WaterPredator;
        return creature != null && (creature.Category & predatorMask) != 0;
    }

    private static bool IsValidAnimalPrey(
        ComponentCreature attacker,
        ComponentCreature target)
    {
        if (attacker == null || target == null || attacker == target ||
            attacker.Entity == null || target.Entity == null ||
            attacker.Entity == target.Entity ||
            !attacker.Entity.IsAddedToProject || !target.Entity.IsAddedToProject)
            return false;

        if (!IsNaturalPredator(attacker) ||
            target.Entity.FindComponent<ComponentPlayer>() != null ||
            IsLivingTownsVillager(target.Entity) ||
            IsNaturalPredator(target))
            return false;

        ComponentHealth health = target.ComponentHealth;
        return health != null && health.Health > 0f && !health.IsInvulnerable;
    }

    /// <summary>
    /// The native ScoreTarget hook can still be unable to produce a target when
    /// a predator's AutoChaseMask/ChaseNonPlayerProbability excludes prey.
    /// Explicitly start the normal native chase state for the nearest living
    /// non-player creature when the predator has no valid current target.
    /// </summary>
    public override void UpdateChaseBehaviorLookingForTarget(
        ComponentChaseBehavior chaseBehavior)
    {
        if (chaseBehavior == null || s_chaseAttackMethod == null)
            return;

        ComponentCreature attacker =
            chaseBehavior.Entity.FindComponent<ComponentCreature>();
        if (attacker == null || !IsNaturalPredator(attacker) ||
            attacker.ComponentHealth == null || attacker.ComponentHealth.Health <= 0f)
            return;

        // Configure the native chase filter itself instead of relying only on
        // a post-score hook. The engine will now consider land, water and bird
        // prey; ScoreTarget below removes players, villagers and other predators.
        chaseBehavior.AttacksNonPlayerCreature = true;
        CreatureCategory preyMask =
            CreatureCategory.LandOther |
            CreatureCategory.WaterOther |
            CreatureCategory.Bird;
        s_chaseAutoChaseMaskField?.SetValue(chaseBehavior, preyMask);
        s_chaseNonPlayerProbabilityField?.SetValue(chaseBehavior, 0.10f);

        double scanNow = attacker.Project.FindSubsystem<SubsystemTime>(throwOnError: true).GameTime;
        if (m_nextPredatorTargetScanTime.TryGetValue(chaseBehavior, out double nextScan) &&
            scanNow < nextScan)
            return;
        m_nextPredatorTargetScanTime[chaseBehavior] = scanNow + 5.0;

        ComponentCreature currentTarget = chaseBehavior.Target;
        if (IsValidAnimalPrey(attacker, currentTarget))
            return;

        SubsystemBodies bodies =
            attacker.Project.FindSubsystem<SubsystemBodies>(throwOnError: true);
        const float acquisitionRange = 5f;
        float bestDistanceSquared = acquisitionRange * acquisitionRange;
        ComponentCreature nearestPrey = null;

        foreach (ComponentBody body in bodies.Bodies)
        {
            if (body == null || body.Entity == null || body.Entity == attacker.Entity)
                continue;

            ComponentCreature candidate = body.Entity.FindComponent<ComponentCreature>();
            if (!IsValidAnimalPrey(attacker, candidate))
                continue;

            // Avoid predator-on-predator targeting. Natural predators hunt
            // other living animals, rather than constantly fighting each other.
            if (IsNaturalPredator(candidate))
                continue;

            float distanceSquared = Engine.Vector3.DistanceSquared(
                attacker.ComponentBody.Position, candidate.ComponentBody.Position);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                nearestPrey = candidate;
            }
        }

        if (nearestPrey != null)
        {
            // Keep the engine's own pathfinding, facing, chase timeout and
            // animation state machine. Only seed its native Attack target.
            s_chaseAttackMethod.Invoke(
                chaseBehavior,
                new object[] { nearestPrey, acquisitionRange, 6f, false });
        }
        else if (currentTarget != null &&
                 IsLivingTownsVillager(currentTarget.Entity) &&
                 s_chaseStopAttackMethod != null)
        {
            // Do not keep an older chase target when it is a protected villager.
            s_chaseStopAttackMethod.Invoke(chaseBehavior, null);
        }
    }

    /// <summary>
    /// Fallback only for predator models whose animation does not produce a
    /// reliable native IsAttackHitMoment. The hit still goes through the game's
    /// ComponentMiner.Hit method, so its normal hit interval, creature hit chance,
    /// Attackment and damage calculation remain authoritative.
    /// </summary>
    public override void UpdateChaseBehaviorChasing(ComponentChaseBehavior chaseBehavior)
    {
        if (chaseBehavior == null || chaseBehavior.Entity == null)
            return;

        ComponentCreature attacker =
            chaseBehavior.Entity.FindComponent<ComponentCreature>();
        ComponentCreature target = chaseBehavior.Target;

        if (!IsValidAnimalPrey(attacker, target) ||
            target.ComponentBody == null ||
            !chaseBehavior.IsTargetInAttackRange(target.ComponentBody))
            return;

        // This is the fallback for predator models that do not emit a usable
        // animation hit moment. It shares one cooldown with the native animation
        // callback below, so there is never a second hit path for the same swing.
        TryPerformPredatorStrike(chaseBehavior, attacker, target);
    }

    private bool TryPerformPredatorStrike(
        ComponentChaseBehavior chaseBehavior,
        ComponentCreature attacker,
        ComponentCreature target)
    {
        if (chaseBehavior == null || !IsValidAnimalPrey(attacker, target))
            return false;

        ComponentMiner miner = attacker.Entity.FindComponent<ComponentMiner>();
        ComponentBody attackerBody = attacker.ComponentBody;
        ComponentBody targetBody = target.ComponentBody;
        ComponentHealth targetHealth = target.ComponentHealth;
        if (miner == null || attackerBody == null || targetBody == null ||
            targetHealth == null || targetHealth.Health <= 0f ||
            targetHealth.IsInvulnerable ||
            !chaseBehavior.IsTargetInAttackRange(targetBody))
            return false;

        double now = attacker.Project.FindSubsystem<SubsystemTime>(throwOnError: true).GameTime;
        if (m_nextPredatorStrikeTime.TryGetValue(chaseBehavior, out double nextHit) &&
            now < nextHit)
            return false;

        // One controlled strike every 2.0 game seconds. The native animation hit
        // is cancelled in OnChaseBehaviorAttacked, and both routes arrive here.
        m_nextPredatorStrikeTime[chaseBehavior] = now + 2.5;

        Engine.Vector3 hitPoint = targetBody.BoundingBox.Center();
        Engine.Vector3 hitDirection = hitPoint - attackerBody.BoundingBox.Center();
        if (hitDirection.LengthSquared() < 0.0001f)
            hitDirection = attackerBody.Matrix.Forward;
        else
            hitDirection = Engine.Vector3.Normalize(hitDirection);

        float attackPower = miner.AttackPower * miner.StrengthFactor;
        if (float.IsNaN(attackPower) || float.IsInfinity(attackPower) ||
            attackPower < 1f)
            attackPower = 1f;

        var attackment = new MeleeAttackment(
            targetBody,
            attacker.Entity,
            hitPoint,
            hitDirection,
            attackPower)
        {
            EnableArmorProtection = true,
            EnableResilienceFactor = true,
            EnableHitValueParticleSystem = true
        };

        float healthBefore = targetHealth.Health;
        ComponentMiner.AttackBody(attackment);
        return targetHealth.Health < healthBefore;
    }

    /// <summary>
    /// Intercepts the native animation hit so it cannot stack with the direct
    /// fallback. A valid native hit moment is converted into the same single,
    /// rate-limited Attackment used by UpdateChaseBehaviorChasing.
    /// </summary>
    public override void OnChaseBehaviorAttacked(
        ComponentChaseBehavior chaseBehavior,
        float chaseTimeBefore,
        ref float chaseTime,
        ref bool hitBody,
        ref bool playAttackSound)
    {
        if (chaseBehavior == null || chaseBehavior.Entity == null)
            return;

        ComponentCreature attacker =
            chaseBehavior.Entity.FindComponent<ComponentCreature>();
        if (!IsNaturalPredator(attacker) ||
            !IsValidAnimalPrey(attacker, chaseBehavior.Target))
            return;

        // Never allow ComponentMiner.Hit to add a second, uncapped native strike.
        hitBody = false;

        bool applied = TryPerformPredatorStrike(
            chaseBehavior, attacker, chaseBehavior.Target);
        if (!applied)
            playAttackSound = false;
    }

    /// <summary>
    /// Gives prey a counter-hit when a natural predator lands a native melee hit.
    /// The counter is sent through the same native MeleeAttackment/AttackBody path
    /// used for predator strikes, so it receives the game's normal Injury processing.
    /// </summary>
    private void TryApplyPreyRetaliation(
        ComponentCreature prey,
        ComponentCreature predator)
    {
        if (prey == null || predator == null ||
            prey.Entity == null || predator.Entity == null ||
            !prey.Entity.IsAddedToProject || !predator.Entity.IsAddedToProject ||
            prey.Entity == predator.Entity)
            return;

        ComponentHealth predatorHealth = predator.ComponentHealth;
        ComponentHealth preyHealth = prey.ComponentHealth;
        ComponentBody preyBody = prey.ComponentBody;
        ComponentBody predatorBody = predator.ComponentBody;
        if (predatorHealth == null || predatorHealth.Health <= 0f ||
            predatorHealth.IsInvulnerable ||
            preyHealth == null || preyHealth.Health <= 0f ||
            preyBody == null || predatorBody == null)
            return;

        double now = prey.Project.FindSubsystem<SubsystemTime>(throwOnError: true).GameTime;
        if (m_nextPreyRetaliationTime.TryGetValue(prey, out double nextRetaliation) &&
            now < nextRetaliation)
            return;

        float counterDamage = GetPreyStrikeDamageCap(predatorHealth.Health);
        if (counterDamage <= 0f ||
            float.IsNaN(counterDamage) || float.IsInfinity(counterDamage))
            return;

        // One counter-hit per prey every 2.5 game seconds. Use the native
        // AttackBody pipeline instead of constructing Injury directly.
        m_nextPreyRetaliationTime[prey] = now + 2.5;

        ComponentMiner preyMiner = prey.Entity.FindComponent<ComponentMiner>();
        float attackPower = preyMiner != null
            ? preyMiner.AttackPower * preyMiner.StrengthFactor
            : 1f;
        if (float.IsNaN(attackPower) || float.IsInfinity(attackPower) ||
            attackPower < 1f)
            attackPower = 1f;

        Engine.Vector3 hitPoint = predatorBody.BoundingBox.Center();
        Engine.Vector3 hitDirection = hitPoint - preyBody.BoundingBox.Center();
        if (hitDirection.LengthSquared() < 0.0001f)
            hitDirection = preyBody.Matrix.Forward;
        else
            hitDirection = Engine.Vector3.Normalize(hitDirection);

        var counterAttackment = new MeleeAttackment(
            predatorBody,
            prey.Entity,
            hitPoint,
            hitDirection,
            attackPower)
        {
            EnableArmorProtection = true,
            EnableResilienceFactor = true,
            EnableHitValueParticleSystem = true
        };

        // Give the counter a meaningful but bounded amount. A predator can never
        // lose more than 12% of its remaining health to one herbivore counter-hit.
        counterAttackment.m_injuryAmount = counterDamage;
        ComponentMiner.AttackBody(counterAttackment);
    }

    /// <summary>
    /// Makes creature-vs-creature melee attacks use a non-zero native attack power.
    /// This hook is intentionally separate from player melee: AI attacks go through
    /// Attackment.ProcessAttackment and do not pass through OnPlayerInputHit.
    /// </summary>
    public override void ProcessAttackment(Attackment attackment)
    {
        if (attackment == null || attackment.Attacker == null ||
            attackment.Target == null || attackment.Attacker == attackment.Target)
            return;

        // Player combat and Living Towns villagers are deliberately untouched.
        if (attackment.Attacker.FindComponent<ComponentPlayer>() != null ||
            IsLivingTownsVillager(attackment.Attacker) ||
            IsLivingTownsVillager(attackment.Target))
            return;

        ComponentCreature attackerCreature =
            attackment.Attacker.FindComponent<ComponentCreature>();
        ComponentCreature targetCreature =
            attackment.Target.FindComponent<ComponentCreature>();
        ComponentHealth targetHealth =
            attackment.Target.FindComponent<ComponentHealth>();

        if (attackerCreature == null || targetCreature == null ||
            targetHealth == null || targetHealth.Health <= 0f ||
            targetHealth.IsInvulnerable)
            return;

        bool attackerIsPredator = IsNaturalPredator(attackerCreature);
        bool targetIsPredator = IsNaturalPredator(targetCreature);

        // Prey retaliation is deliberately not started here. ProcessAttackment
        // runs inside the incoming hit pipeline, before the target's health is
        // reduced. A confirmed injury is queued later by
        // CalculateCreatureInjuryAmount and handled on the next GUI/game update.

        if (float.IsNaN(attackment.AttackPower) ||
            float.IsInfinity(attackment.AttackPower) ||
            attackment.AttackPower < 1f)
        {
            attackment.AttackPower = 1f;
        }

        float nativeInjury = attackment.CalculateInjuryAmount();
        if (float.IsNaN(nativeInjury) || float.IsInfinity(nativeInjury) ||
            nativeInjury < 0f)
            nativeInjury = 0f;

        if (attackerIsPredator && !targetIsPredator)
        {
            // Adaptive per-hit ceiling: no more than 20% of current prey health,
            // and never more than 2 HP. This gives low-health species multiple
            // hits too; only the final 0.25 HP may be taken in one finishing hit.
            float maximumInjury = GetPredatorStrikeDamageCap(targetHealth.Health);
            attackment.m_injuryAmount =
                Math.Min(Math.Max(nativeInjury, 0.05f), maximumInjury);
            return;
        }

        if (!attackerIsPredator && targetIsPredator)
        {
            // Keep a prey creature's individual hit small against a predator.
            float maximumInjury = GetPreyStrikeDamageCap(targetHealth.Health);
            attackment.m_injuryAmount =
                Math.Min(Math.Max(nativeInjury, 0.05f), maximumInjury);
            return;
        }

        // Other animal matchups retain the game's native attack and resilience values.
    }

    private static float GetPredatorStrikeDamageCap(float currentPreyHealth)
    {
        if (float.IsNaN(currentPreyHealth) ||
            float.IsInfinity(currentPreyHealth) ||
            currentPreyHealth <= 0f)
            return 0f;

        // Limit each predator hit to 20% of the target's remaining health.
        // This works for native 0..1 creatures and custom creatures with larger
        // Health values (for example, Living Towns sheep with Health=30).
        if (currentPreyHealth <= 0.05f)
            return currentPreyHealth;

        return currentPreyHealth * 0.12f;
    }

    private static float GetPreyStrikeDamageCap(float currentPredatorHealth)
    {
        if (float.IsNaN(currentPredatorHealth) ||
            float.IsInfinity(currentPredatorHealth) ||
            currentPredatorHealth <= 0f)
            return 0f;

        // Herbivore retaliation is capped at 12% of remaining predator health.
        // A critically weak predator may receive a final blow.
        if (currentPredatorHealth <= 0.05f)
            return currentPredatorHealth;

        return currentPredatorHealth * 0.12f;
    }

    /// <summary>
    /// Last-stage prey damage guard. ProcessAttackment alone is not sufficient for
    /// every source of Injury: Attackment invokes later body/injury callbacks before
    /// health is reduced. This hook clamps the actual Injury.Amount immediately
    /// before ComponentHealth subtracts it, including native AI and direct Injury paths.
    /// A healthy predator can never lose more than half of its remaining health to
    /// one non-player, non-predator animal strike.
    /// </summary>
    public override void CalculateCreatureInjuryAmount(Injury injury)
    {
        if (injury == null || injury.ComponentHealth == null)
            return;

        ComponentCreature attacker = injury.Attacker;
        ComponentCreature target =
            injury.ComponentHealth.Entity?.FindComponent<ComponentCreature>();
        if (attacker == null || target == null ||
            attacker.Entity == null || target.Entity == null)
            return;

        // Do not alter player combat, villager interactions or predator-vs-predator.
        if (attacker.Entity.FindComponent<ComponentPlayer>() != null ||
            IsLivingTownsVillager(attacker.Entity) ||
            IsLivingTownsVillager(target.Entity))
            return;

        bool attackerIsPredator = IsNaturalPredator(attacker);
        bool targetIsPredator = IsNaturalPredator(target);
        float amount = injury.Amount;
        float health = injury.ComponentHealth.Health;

        if (float.IsNaN(amount) || float.IsInfinity(amount) ||
            amount <= 0f || float.IsNaN(health) ||
            float.IsInfinity(health) || health <= 0f)
            return;

        if (attackerIsPredator && !targetIsPredator)
        {
            // Final guard immediately before Health is reduced. It also covers
            // injuries produced by the native animation path or another mod,
            // so no later Attackment calculation can turn one strike into a kill.
            float maximumInjury = GetPredatorStrikeDamageCap(health);
            if (amount > maximumInjury)
                injury.Amount = maximumInjury;

            // Queue a counter only for a confirmed, positive predator injury.
            // The separate update prevents the prey from damaging the predator
            // as part of the same incoming Attackment.
            if (injury.Amount > 0f && IsValidAnimalPrey(attacker, target))
            {
                double now = attacker.Project.FindSubsystem<SubsystemTime>(throwOnError: true).GameTime;
                m_pendingPreyRetaliationPredators[target] = attacker;
                m_pendingPreyRetaliationTimes[target] = now + 0.35;
            }
            return;
        }

        if (!attackerIsPredator && targetIsPredator)
        {
            // A herbivore hit cannot remove more than 15% of remaining predator health.
            float maximumInjury = GetPreyStrikeDamageCap(health);
            if (amount > maximumInjury)
                injury.Amount = maximumInjury;
        }
    }

    public override void OnPlayerInputInteract(
        ComponentPlayer componentPlayer,
        ref bool playerOperated,
        ref double timeIntervalLastActionTime,
        ref int priorityUse,
        ref int priorityInteract,
        ref int priorityPlace)
    {
        if (componentPlayer == null || componentPlayer.ComponentMiner == null)
            return;

        ComponentMiner miner = componentPlayer.ComponentMiner;

        // A physical tap on a villager must not open the legacy trade inventory.
        // The only permitted route is the dedicated proximity button below.
        var villagerInteractRay = componentPlayer.ComponentInput?.PlayerInput.Interact;
        if (villagerInteractRay.HasValue)
        {
            object villagerHit = miner.Raycast(
                villagerInteractRay.Value,
                RaycastMode.Interaction,
                false,
                true,
                false,
                5f);

            if (villagerHit is BodyRaycastResult villagerBodyHit &&
                villagerBodyHit.ComponentBody != null &&
                villagerBodyHit.ComponentBody.Entity != null &&
                IsLivingTownsVillager(villagerBodyHit.ComponentBody.Entity))
            {
                playerOperated = true;
                priorityUse = Math.Max(priorityUse, 10000);
                priorityInteract = Math.Max(priorityInteract, 10000);
                priorityPlace = Math.Max(priorityPlace, 10000);
                return;
            }
        }

        if (playerOperated)
            return;

        int heldContents = Terrain.ExtractContents(miner.ActiveBlockValue);
        bool holdingGrain = heldContents == RyeBlock.Index;
        bool holdingEmptyBucket = heldContents == EmptyBucketBlock.Index;
        if (!holdingGrain && !holdingEmptyBucket)
            return;

        var interactionRay = componentPlayer.ComponentInput.PlayerInput.Interact;
        if (!interactionRay.HasValue)
            return;

        object hit = miner.Raycast(
            interactionRay.Value,
            RaycastMode.Interaction,
            false,
            true,
            false,
            5f);

        if (hit is not BodyRaycastResult bodyHit || bodyHit.ComponentBody == null)
            return;

        ComponentLivingTownsSheepBehavior sheep =
            bodyHit.ComponentBody.Entity.FindComponent<ComponentLivingTownsSheepBehavior>();
        if (sheep == null)
            return;

        IInventory inventory = miner.Inventory;
        int activeSlot = inventory.ActiveSlotIndex;
        int activeCount = inventory.GetSlotCount(activeSlot);
        if (activeCount <= 0)
            return;

        if (holdingEmptyBucket)
        {
            // The vanilla Udder component cannot be attached safely to this custom
            // creature template; handle the bucket conversion here so the ewe egg
            // remains spawnable. Keep one empty bucket if no milk-bucket slot exists.
            int milkBucketValue = MilkBucketBlock.Index;
            int destinationSlot = activeSlot;

            if (activeCount > 1)
            {
                destinationSlot = -1;
                for (int i = 0; i < inventory.SlotsCount; i++)
                {
                    if (i != activeSlot && inventory.GetSlotCapacity(i, milkBucketValue) > 0)
                    {
                        destinationSlot = i;
                        break;
                    }
                }
                if (destinationSlot < 0)
                    return;
            }

            if (!sheep.TryMilk())
                return;

            inventory.RemoveSlotItems(activeSlot, 1);
            inventory.AddSlotItems(destinationSlot, milkBucketValue, 1);
            playerOperated = true;
            priorityInteract = Math.Max(priorityInteract, 100);
            return;
        }

        if (!sheep.TryTame())
            return;

        // Consume exactly one native cereal item only after first-time taming.
        inventory.RemoveSlotItems(activeSlot, 1);
        playerOperated = true;
        priorityInteract = Math.Max(priorityInteract, 100);
    }

    public override void OnLoadingFinished(List<Action> actions)
    {
        // The component is discovered by the database Class mapping.
    }
}
