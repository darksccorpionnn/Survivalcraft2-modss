using System;
using System.Collections.Generic;

namespace Game;

/// <summary>Loads the sheep behavior component contained in this addon assembly.</summary>
public sealed class LivingTownsSheepAddonModLoader : ModLoader
{
    private readonly Dictionary<ComponentPlayer, double> m_nextCreatureMeleeTime = new();

    public override void __ModInitialize()
    {
        ModsManager.RegisterHook("OnLoadingFinished", this);
        ModsManager.RegisterHook("OnPlayerInputInteract", this);
        ModsManager.RegisterHook("OnPlayerInputHit", this);
        ModsManager.RegisterHook("OnMinerHit", this);
        ModsManager.RegisterHook("OnMinerHit2", this);
        ModsManager.RegisterHook("InitializeCreatureTypes", this);
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

        // Fallback to the native hit method if the direct Attackment did not
        // lower health (for example, a target-specific native rule intercepted it).
        // ComponentPlayer.Update will also attempt its regular hit after this hook.
        miner.Hit(targetBody, hitPoint, hitRay.Value.Direction);
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
        hitted = false;
        // Do not alter AI-vs-creature attacks. Player melee should be dependable:
        // the vanilla bare-hand probability is only 0.66 while a target is moving.
        if (miner == null || miner.ComponentPlayer == null ||
            componentBody == null || componentBody.Entity == null)
            return;

        if (componentBody.Entity.FindComponent<ComponentCreature>() == null ||
            componentBody.Entity.FindComponent<ComponentHealth>() == null)
            return;

        playerProbability = Math.Max(playerProbability, 1f);
        attackPower = Math.Max(attackPower, 1f);
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

        if (componentBody.Entity.FindComponent<ComponentCreature>() == null ||
            componentBody.Entity.FindComponent<ComponentHealth>() == null)
            return;

        attackment.AttackPower = Math.Max(attackment.AttackPower, 1f);
    }

    public override void OnPlayerInputInteract(
        ComponentPlayer componentPlayer,
        ref bool playerOperated,
        ref double timeIntervalLastActionTime,
        ref int priorityUse,
        ref int priorityInteract,
        ref int priorityPlace)
    {
        if (playerOperated || componentPlayer == null || componentPlayer.ComponentMiner == null)
            return;

        ComponentMiner miner = componentPlayer.ComponentMiner;
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
