using System;
using System.Collections.Generic;
using System.Reflection;

namespace Game;

/// <summary>Loads the sheep behavior component contained in this addon assembly.</summary>
public sealed class LivingTownsSheepAddonModLoader : ModLoader
{
    private readonly Dictionary<ComponentPlayer, double> m_nextCreatureMeleeTime = new();
    private readonly Dictionary<ComponentCreature, double> m_nextPreyRetaliationTime = new();
    private readonly Dictionary<ComponentChaseBehavior, double> m_nextPredatorFallbackHitTime = new();
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
    private static readonly MethodInfo s_chaseGetHitBodyMethod =
        typeof(ComponentChaseBehavior).GetMethod(
            "GetHitBody",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(ComponentBody), typeof(Engine.Vector3).MakeByRefType() },
            null);
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
        ModsManager.RegisterHook("OnMinerHit", this);
        ModsManager.RegisterHook("OnMinerHit2", this);
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

    public override void OnMinerHit(
        ComponentMiner miner,
        ComponentBody componentBody,
        Engine.Vector3 hitPoint,
        Engine.Vector3 hitDirection,
        ref float attackPower,
        ref float playerProbability,
        ref float creatureProbability,
        out bool hitted)
    {
        // The API exposes this as an OUT hit decision. Returning false here
        // cancels the creature hit even when playerProbability is raised to 1.
        hitted = false;
        // Do not alter AI-vs-creature attacks. Player melee should be dependable:
        // the vanilla bare-hand probability is only 0.66 while a target is moving.
        if (miner == null || miner.ComponentPlayer == null ||
            componentBody == null || componentBody.Entity == null)
            return;

        if (componentBody.Entity.FindComponent<ComponentLivingTownsSheepBehavior>() == null ||
            componentBody.Entity.FindComponent<ComponentCreature>() == null ||
            componentBody.Entity.FindComponent<ComponentHealth>() == null)
            return;

        playerProbability = Math.Max(playerProbability, 1f);
        attackPower = Math.Max(attackPower, 1f);
        hitted = true;
    }

    public override void OnMinerHit2(
        ComponentMiner componentMiner,
        ComponentBody componentBody,
        Engine.Vector3 hitPoint,
        Engine.Vector3 hitDirection,
        ref int durabilityReduction,
        ref Attackment attackment)
    {
        // Final safety check before the native Attackment is processed.
        if (componentMiner == null || componentMiner.ComponentPlayer == null ||
            componentBody == null || componentBody.Entity == null || attackment == null)
            return;

        if (componentBody.Entity.FindComponent<ComponentLivingTownsSheepBehavior>() == null ||
            componentBody.Entity.FindComponent<ComponentCreature>() == null ||
            componentBody.Entity.FindComponent<ComponentHealth>() == null)
            return;

        attackment.AttackPower = Math.Max(attackment.AttackPower, 1f);
    }

    /// <summary>
    /// Lets native predators choose living non-player creatures as targets even
    /// when a species' AutoChaseMask or non-player chance excludes that category.
    /// Herbivores are not made aggressive; only native predator categories use
    /// this fallback. The normal ChaseBehavior still handles pursuit and attack.
    /// </summary>
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
        bool isPredator =
            attacker.Category == CreatureCategory.LandPredator ||
            attacker.Category == CreatureCategory.WaterPredator;
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
            if (distance > 9f)
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
        s_chaseNonPlayerProbabilityField?.SetValue(chaseBehavior, 0.25f);

        double scanNow = attacker.Project.FindSubsystem<SubsystemTime>(throwOnError: true).GameTime;
        if (m_nextPredatorTargetScanTime.TryGetValue(chaseBehavior, out double nextScan) &&
            scanNow < nextScan)
            return;
        m_nextPredatorTargetScanTime[chaseBehavior] = scanNow + 3.0;

        ComponentCreature currentTarget = chaseBehavior.Target;
        if (IsValidAnimalPrey(attacker, currentTarget))
            return;

        SubsystemBodies bodies =
            attacker.Project.FindSubsystem<SubsystemBodies>(throwOnError: true);
        const float acquisitionRange = 9f;
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
                new object[] { nearestPrey, acquisitionRange, 8f, false });
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
        if (chaseBehavior == null || s_chaseGetHitBodyMethod == null)
            return;

        ComponentCreature attacker =
            chaseBehavior.Entity.FindComponent<ComponentCreature>();
        ComponentCreature target = chaseBehavior.Target;
        if (!IsValidAnimalPrey(attacker, target))
            return;

        ComponentMiner miner = attacker.Entity.FindComponent<ComponentMiner>();
        ComponentBody attackerBody = attacker.ComponentBody;
        ComponentBody targetBody = target.ComponentBody;
        if (miner == null || attackerBody == null || targetBody == null)
            return;

        double now = attacker.Project.FindSubsystem<SubsystemTime>(throwOnError: true).GameTime;
        if (!m_nextPredatorFallbackHitTime.TryGetValue(chaseBehavior, out double nextHit))
        {
            // Give the native model animation a chance to deliver its normal hit first.
            m_nextPredatorFallbackHitTime[chaseBehavior] = now + 1.1;
            return;
        }
        if (now < nextHit)
            return;

        object[] hitArgs = { targetBody, Engine.Vector3.Zero };
        ComponentBody hitBody =
            s_chaseGetHitBodyMethod.Invoke(chaseBehavior, hitArgs) as ComponentBody;
        if (hitBody == null)
        {
            // No unobstructed target body in native attack range; retry gently.
            m_nextPredatorFallbackHitTime[chaseBehavior] = now + 0.35;
            return;
        }

        Engine.Vector3 hitPoint = (Engine.Vector3)hitArgs[1];

        // Use the same entry point as the game's native animation-hit path.
        // ComponentMiner.Hit enforces the creature's own HitInterval and uses
        // the normal AI hit probability (1.0), preventing duplicate damage.
        miner.Hit(hitBody, hitPoint, attackerBody.Matrix.Forward);

        // A bounded fallback attempt rate avoids repeated attack spam. A native
        // animation hit will extend this window through OnChaseBehaviorAttacked.
        m_nextPredatorFallbackHitTime[chaseBehavior] = now + 1.25;
    }

    /// <summary>
    /// When the native attack animation already produced a valid hit moment,
    /// defer the fallback so the same chase cannot deliver a second strike.
    /// Do not suppress the original native hit or its attack sound.
    /// </summary>
    public override void OnChaseBehaviorAttacked(
        ComponentChaseBehavior chaseBehavior,
        float chaseTimeBefore,
        ref float chaseTime,
        ref bool hitBody,
        ref bool playAttackSound)
    {
        if (chaseBehavior == null)
            return;

        ComponentCreature attacker =
            chaseBehavior.Entity.FindComponent<ComponentCreature>();
        if (!IsNaturalPredator(attacker) ||
            !IsValidAnimalPrey(attacker, chaseBehavior.Target))
            return;

        double now = attacker.Project.FindSubsystem<SubsystemTime>(throwOnError: true).GameTime;
        m_nextPredatorFallbackHitTime[chaseBehavior] = now + 1.25;
    }

    /// <summary>
    /// Gives prey a real counter-hit when a natural predator lands a native melee
    /// hit. The counter uses the prey's own melee power and the game's native
    /// Attackment -> Injury -> ComponentHealth pipeline, with a short per-prey
    /// cooldown and a strict one-HP / half-remaining-health ceiling.
    /// </summary>
    private void TryApplyPreyRetaliation(
        ComponentCreature prey,
        ComponentCreature predator)
    {
        if (prey == null || predator == null ||
            prey.Entity == null || predator.Entity == null ||
            !prey.Entity.IsAddedToProject || !predator.Entity.IsAddedToProject)
            return;

        ComponentHealth predatorHealth = predator.ComponentHealth;
        ComponentBody predatorBody = predator.ComponentBody;
        ComponentBody preyBody = prey.ComponentBody;
        if (predatorHealth == null || predatorHealth.Health <= 0f ||
            predatorHealth.IsInvulnerable ||
            predatorBody == null || preyBody == null)
            return;

        double now = prey.Project.FindSubsystem<SubsystemTime>(throwOnError: true).GameTime;
        if (m_nextPreyRetaliationTime.TryGetValue(prey, out double nextRetaliation) &&
            now < nextRetaliation)
            return;

        // Set the cooldown before invoking AttackBody; it synchronously re-enters
        // ProcessAttackment for the prey-to-predator counter-hit.
        m_nextPreyRetaliationTime[prey] = now + 1.25;

        ComponentMiner preyMiner = prey.Entity.FindComponent<ComponentMiner>();
        float power = preyMiner != null ? Math.Max(preyMiner.AttackPower, 1f) : 1f;

        Engine.Vector3 direction = predatorBody.BoundingBox.Center() -
            preyBody.BoundingBox.Center();
        if (direction.LengthSquared() < 0.0001f)
            direction = preyBody.Matrix.Forward;
        else
            direction = Engine.Vector3.Normalize(direction);

        float hitCeiling = Math.Min(1f, predatorHealth.Health * 0.5f);
        if (hitCeiling <= 0f)
            return;

        var counterHit = new Attackment(
            predatorBody,
            prey.Entity,
            predatorBody.BoundingBox.Center(),
            direction,
            power)
        {
            EnableArmorProtection = true,
            EnableResilienceFactor = true,
            EnableHitValueParticleSystem = true
        };

        // ProcessAttackment below caps the actual injury; this initial value is
        // also clamped so other mods see a bounded counter-hit from its outset.
        float calculated = counterHit.CalculateInjuryAmount();
        if (float.IsNaN(calculated) || float.IsInfinity(calculated) || calculated < 0f)
            calculated = 0f;
        counterHit.m_injuryAmount = Math.Min(Math.Max(calculated, 1f), hitCeiling);
        ComponentMiner.AttackBody(counterHit);
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

        // Do not change player attacks or damage to Living Towns villagers.
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

        // Retaliation is applied only after the predator's native melee attack
        // has passed ComponentMiner's range, animation-hit-moment and hit-interval
        // checks and entered the real Attackment pipeline. Many prey models have
        // no melee hit-moment animation of their own, so merely assigning a chase
        // target makes them turn/follow but never actually hurt the predator.
        if (IsNaturalPredator(attackerCreature) &&
            !IsNaturalPredator(targetCreature) &&
            attackment is MeleeAttackment)
        {
            TryApplyPreyRetaliation(targetCreature, attackerCreature);
        }

        // Herbivore/prey retaliation must never one-shot a natural predator.
        // Cap each non-predator animal hit against a predator to 1 HP; this is
        // applied to the actual native Attackment so armor/resilience extremes
        // cannot turn a single retaliation into a lethal strike.
        if (!IsNaturalPredator(attackerCreature) &&
            IsNaturalPredator(targetCreature))
        {
            float preyInjury = attackment.CalculateInjuryAmount();
            if (float.IsNaN(preyInjury) || float.IsInfinity(preyInjury) ||
                preyInjury < 0f)
                preyInjury = 0f;
            attackment.m_injuryAmount = Math.Min(preyInjury, 1f);
        }

        // Native AI melee already created this Attackment after a successful hit
        // roll. Some creature templates expose a zero/near-zero melee power, so
        // the native injury calculation becomes zero and no health is removed.
        // Keep a reliable minimum for weak creature attacks.
        if (float.IsNaN(attackment.AttackPower) ||
            float.IsInfinity(attackment.AttackPower) ||
            attackment.AttackPower < 1f)
        {
            attackment.AttackPower = 1f;
        }

        // Predator damage was previously allowed to divide by a small target
        // AttackResilience, which could turn one hit into a lethal injury.
        // Cap only natural predator -> non-predator animal hits. While prey has
        // more than 3 HP, one strike removes at most 3 HP. From 1-3 HP, strikes
        // remove at most half of the remaining health; at 1 HP or less the
        // normal final killing blow is allowed. This gives healthy prey several
        // meaningful hits without making low-health targets effectively immortal.
        float nativeInjury = attackment.CalculateInjuryAmount();
        if (IsNaturalPredator(attackerCreature) && !IsNaturalPredator(targetCreature))
        {
            if (float.IsNaN(nativeInjury) || float.IsInfinity(nativeInjury) ||
                nativeInjury < 0f)
                nativeInjury = 0f;

            if (targetHealth.Health > 3f)
            {
                // Keep normal predator strikes meaningful even against high-
                // resilience prey, while preventing a one-hit kill.
                attackment.m_injuryAmount = Math.Clamp(nativeInjury, 1f, 3f);
            }
            else if (targetHealth.Health > 1f)
            {
                attackment.m_injuryAmount = Math.Min(
                    Math.Max(nativeInjury, 1f), targetHealth.Health * 0.5f);
            }
            else
            {
                attackment.m_injuryAmount = Math.Min(
                    Math.Max(nativeInjury, 1f), targetHealth.Health);
            }
        }
        else if (float.IsNaN(nativeInjury) || float.IsInfinity(nativeInjury) ||
                 nativeInjury < 1f)
        {
            // Preserve the existing minimum for other creature-vs-creature
            // hits without changing player damage or villager interactions.
            attackment.m_injuryAmount = 1f;
        }
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

        // Keep player combat, villagers and predator-vs-predator combat unchanged.
        if (attacker.Entity.FindComponent<ComponentPlayer>() != null ||
            IsLivingTownsVillager(attacker.Entity) ||
            IsLivingTownsVillager(target.Entity) ||
            IsNaturalPredator(attacker) ||
            !IsNaturalPredator(target))
            return;

        float amount = injury.Amount;
        float health = injury.ComponentHealth.Health;
        if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f ||
            float.IsNaN(health) || float.IsInfinity(health) || health <= 0.1f)
            return;

        // Limit each strike to at most 1 HP and at most 50% of current health.
        // This also protects predators whose total health is below 2 HP.
        float maximumInjury = Math.Min(1f, health * 0.5f);
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
