using System;
using System.Collections.Generic;
using System.Reflection;

namespace Game;

/// <summary>Loads the sheep behavior component contained in this addon assembly.</summary>
public sealed class LivingTownsSheepAddonModLoader : ModLoader
{
    private readonly Dictionary<ComponentPlayer, double> m_nextCreatureMeleeTime = new();
    private readonly Dictionary<ComponentCreature, double> m_nextPreyRetaliationTime = new();
    private readonly HashSet<ComponentCreature> m_pendingPreyRetaliations = new();
    private readonly Dictionary<ComponentChaseBehavior, ComponentCreature> m_defensiveTargets = new();
    private readonly Dictionary<ComponentChaseBehavior, double> m_defensiveTargetUntil = new();
    // Shared across every chase component and addon-loader instance: one animal can land only one defensive hit per cooldown.
    private static readonly Dictionary<ComponentCreature, double> s_nextDefensiveStrikeTime = new();
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
    private static readonly FieldInfo s_chaseWhenAttackedProbabilityField =
        typeof(ComponentChaseBehavior).GetField(
            "m_chaseWhenAttackedProbability", BindingFlags.Instance | BindingFlags.NonPublic);
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
        {
            // A non-predator may pursue only the predator that recently injured it.
            // The prey's own ChaseBehavior controls movement and attack animation.
            if (!IsValidDefensiveTarget(chaseBehavior, attacker, creatureToBeTarget))
                return;

            float defensiveDistance = Engine.Vector3.Distance(
                attacker.ComponentBody.Position,
                creatureToBeTarget.ComponentBody.Position);
            score = Math.Max(score, 0.01f / (1f + defensiveDistance));
            chaseBehavior.AttacksNonPlayerCreature = true;
            return;
        }

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

    // Player targets are allowed only when the creature's own chase state has
    // already selected that player. Non-player targets still use predator/prey rules.
    private bool IsValidChaseStrikeTarget(
        ComponentCreature attacker,
        ComponentCreature target)
    {
        if (attacker == null || target == null ||
            attacker == target || attacker.Entity == null || target.Entity == null ||
            attacker.Entity == target.Entity ||
            !attacker.Entity.IsAddedToProject || !target.Entity.IsAddedToProject ||
            attacker.Entity.FindComponent<ComponentPlayer>() != null ||
            IsLivingTownsVillager(target.Entity))
            return false;

        ComponentHealth health = target.ComponentHealth;
        if (health == null || health.Health <= 0f || health.IsInvulnerable)
            return false;

        if (target.Entity.FindComponent<ComponentPlayer>() != null)
            return true;

        return IsValidAnimalPrey(attacker, target) ||
               IsValidDefensiveStrikeTarget(attacker, target);
    }

    private bool IsValidDefensiveTarget(
        ComponentChaseBehavior chaseBehavior,
        ComponentCreature defender,
        ComponentCreature predator)
    {
        if (chaseBehavior == null || defender == null || predator == null ||
            defender.Entity == null || predator.Entity == null ||
            IsNaturalPredator(defender) || !IsNaturalPredator(predator) ||
            !defender.Entity.IsAddedToProject || !predator.Entity.IsAddedToProject ||
            predator.Entity.FindComponent<ComponentPlayer>() != null ||
            IsLivingTownsVillager(predator.Entity))
            return false;

        if (!m_defensiveTargets.TryGetValue(chaseBehavior, out ComponentCreature expected) ||
            expected != predator ||
            !m_defensiveTargetUntil.TryGetValue(chaseBehavior, out double expiresAt))
            return false;

        double now = defender.Project.FindSubsystem<SubsystemTime>(throwOnError: true).GameTime;
        ComponentHealth defenderHealth = defender.ComponentHealth;
        ComponentHealth predatorHealth = predator.ComponentHealth;
        return now <= expiresAt &&
               defenderHealth != null && defenderHealth.Health > 0f &&
               predatorHealth != null && predatorHealth.Health > 0f &&
               !predatorHealth.IsInvulnerable;
    }

    private bool IsValidDefensiveStrikeTarget(
        ComponentCreature defender,
        ComponentCreature predator)
    {
        if (defender?.Entity == null)
            return false;

        ComponentChaseBehavior chaseBehavior =
            defender.Entity.FindComponent<ComponentChaseBehavior>();
        return IsValidDefensiveTarget(chaseBehavior, defender, predator);
    }

    private void ResumeDefensiveChase(ComponentChaseBehavior chaseBehavior)
    {
        if (chaseBehavior == null || chaseBehavior.Entity == null)
            return;

        ComponentCreature defender =
            chaseBehavior.Entity.FindComponent<ComponentCreature>();
        if (defender == null ||
            !m_defensiveTargets.TryGetValue(chaseBehavior, out ComponentCreature predator) ||
            !IsValidDefensiveTarget(chaseBehavior, defender, predator) ||
            s_chaseAttackMethod == null)
        {
            m_defensiveTargets.Remove(chaseBehavior);
            m_defensiveTargetUntil.Remove(chaseBehavior);
            s_chaseStopAttackMethod?.Invoke(chaseBehavior, null);
            return;
        }

        float distance = Engine.Vector3.Distance(
            defender.ComponentBody.Position,
            predator.ComponentBody.Position);
        if (distance > 7f)
        {
            m_defensiveTargets.Remove(chaseBehavior);
            m_defensiveTargetUntil.Remove(chaseBehavior);
            s_chaseStopAttackMethod?.Invoke(chaseBehavior, null);
            return;
        }

        chaseBehavior.AttacksNonPlayerCreature = true;
        s_chaseAttackMethod.Invoke(
            chaseBehavior,
            new object[] { predator, 6f, 6f, false });
    }

    private static bool HasNativeRetaliation(ComponentCreature creature)
    {
        if (creature?.Entity == null)
            return false;

        ComponentChaseBehavior chase = creature.Entity.FindComponent<ComponentChaseBehavior>();
        if (chase == null)
            return false;

        // Let the game's own Injured-event listener handle creatures that have
        // a non-zero ChaseWhenAttackedProbability. Manual retaliation is only
        // for creatures that have no native chase response.
        if (s_chaseWhenAttackedProbabilityField == null)
            return true;

        try
        {
            object value = s_chaseWhenAttackedProbabilityField.GetValue(chase);
            return value is float probability ? probability > 0f : true;
        }
        catch
        {
            return true;
        }
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
        if (attacker == null || attacker.ComponentHealth == null ||
            attacker.ComponentHealth.Health <= 0f)
            return;

        if (!IsNaturalPredator(attacker))
        {
            ResumeDefensiveChase(chaseBehavior);
            return;
        }

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

        if (!IsValidChaseStrikeTarget(attacker, target) ||
            target.ComponentBody == null ||
            !chaseBehavior.IsTargetInAttackRange(target.ComponentBody))
            return;

        // The predator fallback remains for creatures whose native animation
        // callback is unreliable. Defensive prey must strike ONLY from
        // OnChaseBehaviorAttacked; running a second per-frame fallback here caused
        // one visible swing to deal damage twice (fallback + native animation hit).
        if (IsNaturalPredator(attacker))
            TryPerformPredatorStrike(chaseBehavior, attacker, target);
    }

    private bool TryPerformPredatorStrike(
        ComponentChaseBehavior chaseBehavior,
        ComponentCreature attacker,
        ComponentCreature target)
    {
        if (chaseBehavior == null || !IsValidChaseStrikeTarget(attacker, target))
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
        // Preserve species-defined low attack values. Raising every value below
        // 1 to 1 made weak modded creatures hit 2-10x harder than their database.
        if (float.IsNaN(attackPower) || float.IsInfinity(attackPower) ||
            attackPower <= 0f)
            return false;

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
    /// A provoked herbivore makes its own melee strike against the predator.
    /// This is called from that herbivore's chase/animation path, never from
    /// the predator's incoming ProcessAttackment callback.
    /// </summary>
    private bool TryPerformDefensiveStrike(
        ComponentChaseBehavior chaseBehavior,
        ComponentCreature defender,
        ComponentCreature predator)
    {
        if (chaseBehavior == null ||
            !IsValidDefensiveStrikeTarget(defender, predator))
            return false;

        ComponentMiner miner = defender.Entity.FindComponent<ComponentMiner>();
        ComponentBody defenderBody = defender.ComponentBody;
        ComponentBody predatorBody = predator.ComponentBody;
        ComponentHealth predatorHealth = predator.ComponentHealth;
        if (miner == null || defenderBody == null || predatorBody == null ||
            predatorHealth == null || predatorHealth.Health <= 0f ||
            predatorHealth.IsInvulnerable ||
            !chaseBehavior.IsTargetInAttackRange(predatorBody))
            return false;

        double now = defender.Project.FindSubsystem<SubsystemTime>(throwOnError: true).GameTime;
        // Key by the defending creature, not its chase component. Some creature
        // templates can expose multiple chase callbacks; all must share one lock.
        if (s_nextDefensiveStrikeTime.TryGetValue(defender, out double nextHit) &&
            now < nextHit)
            return false;

        s_nextDefensiveStrikeTime[defender] = now + 2.5;

        Engine.Vector3 hitPoint = predatorBody.BoundingBox.Center();
        Engine.Vector3 hitDirection = hitPoint - defenderBody.BoundingBox.Center();
        if (hitDirection.LengthSquared() < 0.0001f)
            hitDirection = defenderBody.Matrix.Forward;
        else
            hitDirection = Engine.Vector3.Normalize(hitDirection);

        float attackPower = miner.AttackPower * miner.StrengthFactor;
        if (float.IsNaN(attackPower) || float.IsInfinity(attackPower) ||
            attackPower <= 0f)
            return false;

        var attackment = new MeleeAttackment(
            predatorBody,
            defender.Entity,
            hitPoint,
            hitDirection,
            attackPower)
        {
            EnableArmorProtection = true,
            EnableResilienceFactor = true,
            EnableHitValueParticleSystem = true
        };

        float healthBefore = predatorHealth.Health;
        ComponentMiner.AttackBody(attackment);
        return predatorHealth.Health < healthBefore;
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
        if (attacker == null ||
            attacker.Entity.FindComponent<ComponentPlayer>() != null ||
            !IsValidChaseStrikeTarget(attacker, chaseBehavior.Target))
            return;

        // Consume the native animation hit frame and route it through exactly
        // one controlled Attackment. This is the ONLY damage path for defensive
        // prey; UpdateChaseBehaviorChasing no longer applies a parallel hit.
        hitBody = false;

        bool applied = IsNaturalPredator(attacker)
            ? TryPerformPredatorStrike(chaseBehavior, attacker, chaseBehavior.Target)
            : TryPerformDefensiveStrike(chaseBehavior, attacker, chaseBehavior.Target);

        if (!applied)
            playAttackSound = false;
    }

    /// <summary>
    /// Starts an independent defensive AI response only after the incoming
    /// predator injury has completed and the prey's Health value has decreased.
    /// No ComponentHealth.Injure call is nested or injected here.
    /// </summary>
    /// <summary>
    /// After a real predator hit is confirmed, starts the prey's own native
    /// defensive chase. It does not directly injure the predator.
    /// </summary>
    private void TryStartPreyDefense(
        ComponentCreature prey,
        ComponentCreature predator,
        float preyHealthBefore)
    {
        if (prey == null || predator == null ||
            prey.Entity == null || predator.Entity == null ||
            !prey.Entity.IsAddedToProject || !predator.Entity.IsAddedToProject ||
            prey.Entity == predator.Entity ||
            IsNaturalPredator(prey) || !IsNaturalPredator(predator))
            return;

        ComponentHealth predatorHealth = predator.ComponentHealth;
        ComponentHealth preyHealth = prey.ComponentHealth;
        if (predatorHealth == null || predatorHealth.Health <= 0f ||
            predatorHealth.IsInvulnerable || preyHealth == null ||
            float.IsNaN(preyHealthBefore) || float.IsInfinity(preyHealthBefore))
            return;

        ComponentChaseBehavior preyChase =
            prey.Entity.FindComponent<ComponentChaseBehavior>();
        if (preyChase == null || s_chaseAttackMethod == null)
            return;

        SubsystemTime subsystemTime =
            prey.Project.FindSubsystem<SubsystemTime>(throwOnError: true);
        double now = subsystemTime.GameTime;
        if (m_nextPreyRetaliationTime.TryGetValue(prey, out double nextRetaliation) &&
            now < nextRetaliation)
            return;

        if (!m_pendingPreyRetaliations.Add(prey))
            return;

        subsystemTime.QueueGameTimeDelayedExecution(
            now + 0.05,
            delegate
            {
                m_pendingPreyRetaliations.Remove(prey);

                if (prey.Entity == null || predator.Entity == null ||
                    !prey.Entity.IsAddedToProject || !predator.Entity.IsAddedToProject)
                    return;

                ComponentHealth currentPreyHealth = prey.ComponentHealth;
                ComponentHealth currentPredatorHealth = predator.ComponentHealth;
                if (currentPreyHealth == null ||
                    currentPreyHealth.Health >= preyHealthBefore - 0.0001f ||
                    currentPredatorHealth == null ||
                    currentPredatorHealth.Health <= 0f ||
                    currentPredatorHealth.IsInvulnerable)
                    return;

                ComponentBody preyBody = prey.ComponentBody;
                ComponentBody predatorBody = predator.ComponentBody;
                if (preyBody == null || predatorBody == null ||
                    Engine.Vector3.Distance(
                        preyBody.BoundingBox.Center(),
                        predatorBody.BoundingBox.Center()) > 6f)
                    return;

                ComponentChaseBehavior currentChase =
                    prey.Entity.FindComponent<ComponentChaseBehavior>();
                if (currentChase == null || s_chaseAttackMethod == null)
                    return;

                double responseTime =
                    predator.Project.FindSubsystem<SubsystemTime>(throwOnError: true).GameTime;
                if (m_nextPreyRetaliationTime.TryGetValue(prey, out double nextTime) &&
                    responseTime < nextTime)
                    return;

                // Remember who injured this prey, then start the prey's own chase.
                // Its own animation event/fallback creates its later damage event.
                m_nextPreyRetaliationTime[prey] = responseTime + 2.5;
                m_defensiveTargets[currentChase] = predator;
                m_defensiveTargetUntil[currentChase] = responseTime + 8.0;
                currentChase.AttacksNonPlayerCreature = true;
                s_chaseAttackMethod.Invoke(
                    currentChase,
                    new object[] { predator, 6f, 6f, false });
            });
    }

    /// <summary>
    /// Keeps animal-versus-animal melee injuries bounded while allowing the
    /// defender's own chase behavior to decide when its next strike occurs.
    /// </summary>
    public override void ProcessAttackment(Attackment attackment)
    {
        if (attackment == null || attackment.Attacker == null ||
            attackment.Target == null || attackment.Attacker == attackment.Target)
            return;

        if (attackment.Attacker.FindComponent<ComponentPlayer>() != null ||
            attackment.Target.FindComponent<ComponentPlayer>() != null ||
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
        if (float.IsNaN(attackment.AttackPower) ||
            float.IsInfinity(attackment.AttackPower) ||
            attackment.AttackPower <= 0f)
            return;

        float nativeInjury = attackment.CalculateInjuryAmount();
        if (float.IsNaN(nativeInjury) || float.IsInfinity(nativeInjury) ||
            nativeInjury < 0f)
            nativeInjury = 0f;

        float maximumInjury = GetPredatorStrikeDamageCap(targetHealth.Health);
        float boundedInjury = Math.Min(nativeInjury, maximumInjury);
        attackment.m_injuryAmount = boundedInjury;

        // A predator hit may provoke the prey's AI, but never writes damage
        // directly to the predator. The prey must later make its own strike.
        if (attackerIsPredator && !targetIsPredator &&
            attackment is MeleeAttackment && boundedInjury > 0f)
        {
            TryStartPreyDefense(
                targetCreature, attackerCreature, targetHealth.Health);
        }
    }

    private static float GetPredatorStrikeDamageCap(float currentPreyHealth)
    {
        if (float.IsNaN(currentPreyHealth) ||
            float.IsInfinity(currentPreyHealth) ||
            currentPreyHealth <= 0f)
            return 0f;

        // Limit each animal hit to 12% of the target's remaining health.
        // This works for native 0..1 creatures and custom creatures with larger
        // Health values (for example, Living Towns sheep with Health=30).
        if (currentPreyHealth <= 0.05f)
            return currentPreyHealth;

        return currentPreyHealth * 0.12f;
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

        // Leave player combat and Living Towns villager damage unchanged.
        if (attacker.Entity.FindComponent<ComponentPlayer>() != null ||
            target.Entity.FindComponent<ComponentPlayer>() != null ||
            IsLivingTownsVillager(attacker.Entity) ||
            IsLivingTownsVillager(target.Entity))
            return;

        float amount = injury.Amount;
        float health = injury.ComponentHealth.Health;
        if (float.IsNaN(amount) || float.IsInfinity(amount) ||
            amount <= 0f || float.IsNaN(health) ||
            float.IsInfinity(health) || health <= 0f)
            return;

        // Final safety cap for every animal-vs-animal Injury. Retaliation itself
        // is queued after the incoming Injury has completed, never from inside
        // this callback.
        float maximumInjury = GetPredatorStrikeDamageCap(health);
        if (amount > maximumInjury)
            injury.Amount = maximumInjury;
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
