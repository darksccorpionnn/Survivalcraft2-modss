using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Engine;

namespace Game;

/// <summary>
/// Read-only in-game diagnostics for Living Towns.
/// Combat monitoring observes native melee hooks and later health values.
/// It never spawns, injures, moves, edits, or deletes world entities.
/// </summary>
public sealed class LivingTownsTestHarnessModLoader : ModLoader
{
    private const string VillagerComponentName = "ComponentLivingTownsVillager";
    private const string SheepComponentName = "ComponentLivingTownsSheepBehavior";
    private const float HealthEpsilon = 0.0001f;
    private const double AttackConfirmationTimeout = 0.9;
    private const double RetaliationObservationWindow = 0.75;
    private const double NativeStrikeCorrelationWindow = 0.9;
    private const double RetaliationCooldown = 2.5;

    private BevelledButtonWidget m_testButton;
    private CanvasWidget m_testCanvas;
    private ContainerWidget m_testCanvasParent;
    private bool m_combatWatch;

    private readonly List<PendingWolfBullHit> m_pendingWolfBullHits = new();
    private readonly List<PendingBullWolfStrike> m_pendingBullWolfStrikes = new();
    private readonly HashSet<Attackment> m_seenAttackments = new();
    private readonly Dictionary<ComponentCreature, double> m_lastRetaliationTime = new();

    private int m_wolfBullAttempts;
    private int m_confirmedWolfHits;
    private int m_unconfirmedWolfHits;
    private int m_confirmedBullRetaliations;
    private int m_missingBullRetaliations;
    private int m_nativeBullStrikeAttempts;
    private int m_nativeBullStrikeDamageConfirmed;
    private int m_nativeBullStrikeNoDamage;
    private int m_unverifiedBullHealthLoss;
    private int m_capFailures;
    private int m_lethalHits;
    private int m_cooldownPasses;
    private int m_cooldownFailures;
    private int m_cooldownBlocked;

    private sealed class PendingWolfBullHit
    {
        public ComponentCreature Wolf;
        public ComponentCreature Bull;
        public double AttackTime;
        public float WolfHealthBefore;
        public float BullHealthBefore;
        public bool DamageConfirmed;
        public double ConfirmedAt;
        public double ObserveUntil;
        public bool CooldownProbe;
        public bool CooldownProbeWindowValid;
    }

    private sealed class PendingBullWolfStrike
    {
        public ComponentCreature Bull;
        public ComponentCreature Wolf;
        public double AttackTime;
        public float WolfHealthBefore;
        public bool DamageConfirmed;
        public bool LinkedToWolfHit;
    }

    public override void __ModInitialize()
    {
        ModsManager.RegisterHook("OnLoadingFinished", this);
        ModsManager.RegisterHook("GuiUpdate", this);
        ModsManager.RegisterHook("ProcessAttackment", this);
        Log.Information("[LivingTownsTest] Harness initialized. Combat observer is idle; READ ONLY.");
    }

    public override void OnLoadingFinished(List<Action> actions)
    {
        Log.Information("[LivingTownsTest] World loading finished. Tap WATCH to observe real wolf-versus-bull combat.");
    }

    public override void GuiUpdate(ComponentGui componentGui)
    {
        if (componentGui == null)
            return;

        ComponentPlayer player = componentGui.m_componentPlayer;
        if (player == null || player.Project == null)
        {
            if (m_testButton != null)
                m_testButton.IsVisible = false;
            if (m_testCanvas != null)
                m_testCanvas.IsVisible = false;
            if (m_combatWatch)
                StopCombatWatch("Player/project became unavailable.");
            return;
        }

        if (m_testButton == null)
        {
            m_testButton = new BevelledButtonWidget
            {
                Text = "WATCH",
                DesiredSize = new Engine.Vector2(82f, 48f),
                Margin = Engine.Vector2.Zero,
                HorizontalAlignment = WidgetAlignment.Near,
                VerticalAlignment = WidgetAlignment.Near,
                IsVisible = false
            };
        }

        ContainerWidget parent = componentGui.ControlsContainerWidget;
        if (parent != null && m_testCanvasParent != parent)
        {
            if (m_testCanvas != null && m_testCanvasParent != null)
                m_testCanvasParent.RemoveChildren(m_testCanvas);

            m_testCanvas = new CanvasWidget
            {
                Size = new Engine.Vector2(90f, 56f),
                DesiredSize = new Engine.Vector2(90f, 56f),
                HorizontalAlignment = WidgetAlignment.Near,
                VerticalAlignment = WidgetAlignment.Near,
                Margin = new Engine.Vector2(12f, 12f),
                IsVisible = false
            };
            m_testCanvas.AddChildren(m_testButton);
            parent.AddChildren(m_testCanvas);
            m_testCanvasParent = parent;
        }

        if (m_testCanvas != null)
            CanvasWidget.SetPosition(m_testButton, new Engine.Vector2(4f, 4f));

        bool guiBusy = componentGui.ModalPanelWidget != null || componentGui.IsInventoryVisible();
        bool canShow = !guiBusy && m_testCanvas != null;
        m_testButton.IsVisible = canShow;
        m_testButton.IsEnabled = canShow;
        m_testButton.Text = m_combatWatch ? "STOP" : "WATCH";
        if (m_testCanvas != null)
            m_testCanvas.IsVisible = canShow;

        if (canShow && m_testButton.IsClicked)
        {
            if (m_combatWatch)
                StopCombatWatch("Stopped by player.");
            else
                StartCombatWatch(player);
        }

        if (m_combatWatch)
            ProcessPendingWolfBullHits(player);
    }

    private void StartCombatWatch(ComponentPlayer player)
    {
        if (player == null || player.Project == null)
            return;

        m_pendingWolfBullHits.Clear();
        m_pendingBullWolfStrikes.Clear();
        m_seenAttackments.Clear();
        m_lastRetaliationTime.Clear();

        m_wolfBullAttempts = 0;
        m_confirmedWolfHits = 0;
        m_unconfirmedWolfHits = 0;
        m_confirmedBullRetaliations = 0;
        m_missingBullRetaliations = 0;
        m_nativeBullStrikeAttempts = 0;
        m_nativeBullStrikeDamageConfirmed = 0;
        m_nativeBullStrikeNoDamage = 0;
        m_unverifiedBullHealthLoss = 0;
        m_capFailures = 0;
        m_lethalHits = 0;
        m_cooldownPasses = 0;
        m_cooldownFailures = 0;
        m_cooldownBlocked = 0;

        RunReadOnlyScan(player);
        m_combatWatch = true;
        Log.Information("[LivingTownsCombatTest] WATCH STARTED. Let a wolf and a bull fight naturally; no damage is generated by this harness.");
    }

    private void StopCombatWatch(string reason)
    {
        if (!m_combatWatch)
            return;

        m_combatWatch = false;
        int unresolved = m_pendingWolfBullHits.Count + m_pendingBullWolfStrikes.Count;
        Log.Information("[LivingTownsCombatTest] WATCH STOPPED: " + reason);
        Log.Information("[LivingTownsCombatTest] SUMMARY attempts=" + m_wolfBullAttempts +
            " wolfHitsConfirmed=" + m_confirmedWolfHits +
            " wolfHitsNoHealthLoss=" + m_unconfirmedWolfHits +
            " bullRetaliationsConfirmed=" + m_confirmedBullRetaliations +
            " missingRetaliations=" + m_missingBullRetaliations +
            " bullNativeMeleeAttempts=" + m_nativeBullStrikeAttempts +
            " bullNativeMeleeDamageConfirmed=" + m_nativeBullStrikeDamageConfirmed +
            " bullNativeMeleeNoDamage=" + m_nativeBullStrikeNoDamage +
            " unverifiedBullHealthLoss=" + m_unverifiedBullHealthLoss +
            " damageCapFailures=" + m_capFailures +
            " oneHitKills=" + m_lethalHits +
            " cooldownPass=" + m_cooldownPasses +
            " cooldownFail=" + m_cooldownFailures +
            " cooldownBlocked=" + m_cooldownBlocked +
            " unresolved=" + unresolved);
        Log.Information("[LivingTownsCombatTest] A zero retaliation count is not a pass; it means the fight did not produce a confirmed counter-hit during this watch.");
        m_pendingWolfBullHits.Clear();
        m_pendingBullWolfStrikes.Clear();
        m_seenAttackments.Clear();
    }

    /// <summary>
    /// Observes real engine melee attacks before the engine applies the injury.
    /// No Attackment fields or health values are modified here.
    /// </summary>
    public override void ProcessAttackment(Attackment attackment)
    {
        if (!m_combatWatch || attackment == null ||
            !(attackment is MeleeAttackment) ||
            attackment.Attacker == null || attackment.Target == null ||
            attackment.Attacker == attackment.Target)
            return;

        ComponentCreature attacker = attackment.Attacker.FindComponent<ComponentCreature>();
        ComponentCreature target = attackment.Target.FindComponent<ComponentCreature>();
        if (attacker == null || target == null)
            return;

        bool wolfAttacksBull = IsWolf(attacker) && IsBull(target);
        bool bullAttacksWolf = IsBull(attacker) && IsWolf(target);
        if ((!wolfAttacksBull && !bullAttacksWolf) || !m_seenAttackments.Add(attackment))
            return;

        ComponentHealth attackerHealth = attacker.ComponentHealth;
        ComponentHealth targetHealth = target.ComponentHealth;
        if (attackerHealth == null || targetHealth == null ||
            attackerHealth.Health <= 0f || targetHealth.Health <= 0f)
            return;

        double now = attacker.Project.FindSubsystem<SubsystemTime>(throwOnError: true).GameTime;

        if (wolfAttacksBull)
        {
            m_pendingWolfBullHits.Add(new PendingWolfBullHit
            {
                Wolf = attacker,
                Bull = target,
                AttackTime = now,
                WolfHealthBefore = attackerHealth.Health,
                BullHealthBefore = targetHealth.Health
            });
            m_wolfBullAttempts++;

            Log.Information("[LivingTownsCombatTest] WOLF_MELEE_ATTEMPT #" + m_wolfBullAttempts +
                " gameTime=" + now.ToString("F3") +
                " wolfHP=" + attackerHealth.Health.ToString("F3") +
                " bullHP=" + targetHealth.Health.ToString("F3"));
            return;
        }

        // This hook is the engine's native melee attackment at the strike/hit
        // frame. It is not inferred from a later health decrease.
        m_nativeBullStrikeAttempts++;
        m_pendingBullWolfStrikes.Add(new PendingBullWolfStrike
        {
            Bull = attacker,
            Wolf = target,
            AttackTime = now,
            WolfHealthBefore = targetHealth.Health
        });

        Log.Information("[LivingTownsCombatTest] BULL_NATIVE_MELEE_ATTEMPT #" +
            m_nativeBullStrikeAttempts + " gameTime=" + now.ToString("F3") +
            " bullHP=" + attackerHealth.Health.ToString("F3") +
            " wolfHP=" + targetHealth.Health.ToString("F3"));

        if (m_lastRetaliationTime.TryGetValue(attacker, out double lastStrikeTime))
        {
            double elapsed = now - lastStrikeTime;
            if (elapsed + 0.001 < RetaliationCooldown)
            {
                m_cooldownFailures++;
                Log.Information("[LivingTownsCombatTest] FAIL bull native melee cooldown: interval=" +
                    elapsed.ToString("F3") + " seconds; expected at least " +
                    RetaliationCooldown.ToString("F1") + ".");
            }
            else
            {
                m_cooldownPasses++;
                Log.Information("[LivingTownsCombatTest] PASS bull native melee cooldown: interval=" +
                    elapsed.ToString("F3") + " seconds.");
            }
        }
        m_lastRetaliationTime[attacker] = now;
    }

    private void ProcessPendingWolfBullHits(ComponentPlayer player)
    {
        if (player == null || player.Project == null)
            return;

        double now;
        try
        {
            now = player.Project.FindSubsystem<SubsystemTime>(throwOnError: true).GameTime;
        }
        catch (Exception ex)
        {
            Log.Information("[LivingTownsCombatTest] BLOCKED: Could not read game time: " + ex.GetType().Name);
            return;
        }

        ProcessPendingBullWolfStrikes(player, now);

        for (int i = m_pendingWolfBullHits.Count - 1; i >= 0; i--)
        {
            PendingWolfBullHit hit = m_pendingWolfBullHits[i];
            if (hit.Wolf == null || hit.Bull == null ||
                hit.Wolf.Entity == null || hit.Bull.Entity == null ||
                !hit.Wolf.Entity.IsAddedToProject || !hit.Bull.Entity.IsAddedToProject)
            {
                m_pendingWolfBullHits.RemoveAt(i);
                continue;
            }

            ComponentHealth wolfHealth = hit.Wolf.ComponentHealth;
            ComponentHealth bullHealth = hit.Bull.ComponentHealth;
            if (wolfHealth == null || bullHealth == null)
            {
                m_pendingWolfBullHits.RemoveAt(i);
                continue;
            }

            if (!hit.DamageConfirmed)
            {
                if (bullHealth.Health < hit.BullHealthBefore - HealthEpsilon)
                {
                    hit.DamageConfirmed = true;
                    hit.ConfirmedAt = now;
                    hit.ObserveUntil = now + RetaliationObservationWindow;
                    m_confirmedWolfHits++;

                    float incomingDamage = hit.BullHealthBefore - bullHealth.Health;
                    float incomingCap = GetTwelvePercentCap(hit.BullHealthBefore);
                    bool incomingCapPassed = incomingDamage <= incomingCap + 0.001f;
                    if (!incomingCapPassed)
                    {
                        m_capFailures++;
                        Log.Information("[LivingTownsCombatTest] FAIL wolf damage cap: actual=" +
                            incomingDamage.ToString("F4") + " allowed=" + incomingCap.ToString("F4"));
                    }
                    else
                    {
                        Log.Information("[LivingTownsCombatTest] PASS wolf damage cap: actual=" +
                            incomingDamage.ToString("F4") + " allowed=" + incomingCap.ToString("F4"));
                    }

                    if (hit.BullHealthBefore > 0.05f && bullHealth.Health <= HealthEpsilon)
                    {
                        m_lethalHits++;
                        Log.Information("[LivingTownsCombatTest] FAIL: one wolf strike killed a bull that had more than 0.05 health.");
                    }

                    if (m_lastRetaliationTime.TryGetValue(hit.Bull, out double lastRetaliation))
                    {
                        double remainingCooldown = lastRetaliation + RetaliationCooldown - now;
                        hit.CooldownProbe = remainingCooldown > 0.0;
                        hit.CooldownProbeWindowValid = remainingCooldown > 0.0;
                        if (hit.CooldownProbe && hit.CooldownProbeWindowValid)
                        {
                            // Observe the entire remaining cooldown, not just the ordinary
                            // retaliation window. A shorter sample cannot prove a 2.5s cooldown.
                            hit.ObserveUntil = Math.Max(
                                hit.ObserveUntil,
                                now + remainingCooldown + 0.05);
                        }
                        if (hit.CooldownProbe && !hit.CooldownProbeWindowValid)
                        {
                            m_cooldownBlocked++;
                            Log.Information("[LivingTownsCombatTest] BLOCKED cooldown probe: hit occurred too close to the end of the 2.5-second cooldown.");
                        }
                    }

                    if (hit.CooldownProbe)
                    {
                        Log.Information("[LivingTownsCombatTest] COOLDOWN_PROBE: remaining=" +
                            Math.Max(0.0, lastRetaliation + RetaliationCooldown - now).ToString("F3") +
                            " seconds; watching for an extra counter-hit.");
                    }
                    else
                    {
                        Log.Information("[LivingTownsCombatTest] BULL_HIT_CONFIRMED: bullHP " +
                            hit.BullHealthBefore.ToString("F3") + " -> " + bullHealth.Health.ToString("F3") +
                            "; watching the wolf's real health for retaliation.");
                    }
                }
                else if (now - hit.AttackTime >= AttackConfirmationTimeout)
                {
                    m_unconfirmedWolfHits++;
                    Log.Information("[LivingTownsCombatTest] NO_CONFIRMED_BULL_HEALTH_LOSS: wolf melee attempt did not lower bull health.");
                    m_seenAttackments.RemoveWhere(x => x == null);
                    m_pendingWolfBullHits.RemoveAt(i);
                }

                continue;
            }

            if (wolfHealth.Health < hit.WolfHealthBefore - HealthEpsilon)
            {
                float retaliationDamage = hit.WolfHealthBefore - wolfHealth.Health;
                if (!TryFindNativeBullStrike(hit.Bull, hit.Wolf, hit.ConfirmedAt, now,
                    out PendingBullWolfStrike nativeStrike))
                {
                    m_unverifiedBullHealthLoss++;
                    m_missingBullRetaliations++;
                    Log.Information("[LivingTownsCombatTest] FAIL HEALTH_LOSS_WITHOUT_NATIVE_BULL_MELEE: " +
                        "wolfHP " + hit.WolfHealthBefore.ToString("F3") + " -> " +
                        wolfHealth.Health.ToString("F3") +
                        "; no matching bull->wolf native MeleeAttackment was observed.");
                }
                else
                {
                    m_confirmedBullRetaliations++;
                    bool capPassed = retaliationDamage <= GetTwelvePercentCap(hit.WolfHealthBefore) + 0.001f;
                    if (!capPassed)
                    {
                        m_capFailures++;
                        Log.Information("[LivingTownsCombatTest] FAIL bull native melee damage cap: actual=" +
                            retaliationDamage.ToString("F4") + " allowed=" +
                            GetTwelvePercentCap(hit.WolfHealthBefore).ToString("F4"));
                    }
                    else
                    {
                        Log.Information("[LivingTownsCombatTest] PASS bull native melee damage cap: actual=" +
                            retaliationDamage.ToString("F4") + " allowed=" +
                            GetTwelvePercentCap(hit.WolfHealthBefore).ToString("F4"));
                    }

                    if (hit.WolfHealthBefore > 0.05f && wolfHealth.Health <= HealthEpsilon)
                    {
                        m_lethalHits++;
                        Log.Information("[LivingTownsCombatTest] FAIL: one bull native melee strike killed a wolf that had more than 0.05 health.");
                    }

                    Log.Information("[LivingTownsCombatTest] BULL_NATIVE_RETALIATION_CONFIRMED: bull melee event at " +
                        nativeStrike.AttackTime.ToString("F3") + "; wolfHP " +
                        hit.WolfHealthBefore.ToString("F3") + " -> " + wolfHealth.Health.ToString("F3"));
                }

                m_pendingWolfBullHits.RemoveAt(i);
                continue;
            }

            if (now >= hit.ObserveUntil)
            {
                if (hit.CooldownProbe)
                {
                    if (hit.CooldownProbeWindowValid)
                    {
                        m_cooldownPasses++;
                        Log.Information("[LivingTownsCombatTest] PASS cooldown: no additional bull retaliation was observed during the safe 2.5-second cooldown window.");
                    }
                }
                else
                {
                    m_missingBullRetaliations++;
                    Log.Information("[LivingTownsCombatTest] FAIL: bull health decreased from a wolf attack, but no corresponding wolf health decrease was observed within " +
                        RetaliationObservationWindow.ToString("F2") + " game seconds.");
                }

                m_pendingWolfBullHits.RemoveAt(i);
            }
        }

        if (m_pendingWolfBullHits.Count > 64)
            m_pendingWolfBullHits.RemoveRange(0, m_pendingWolfBullHits.Count - 64);
        if (m_pendingBullWolfStrikes.Count > 64)
            m_pendingBullWolfStrikes.RemoveRange(0, m_pendingBullWolfStrikes.Count - 64);
    }

    private void ProcessPendingBullWolfStrikes(ComponentPlayer player, double now)
    {
        for (int i = m_pendingBullWolfStrikes.Count - 1; i >= 0; i--)
        {
            PendingBullWolfStrike strike = m_pendingBullWolfStrikes[i];
            if (strike.Bull == null || strike.Wolf == null ||
                strike.Bull.Entity == null || strike.Wolf.Entity == null ||
                !strike.Bull.Entity.IsAddedToProject || !strike.Wolf.Entity.IsAddedToProject)
            {
                m_pendingBullWolfStrikes.RemoveAt(i);
                continue;
            }

            ComponentHealth wolfHealth = strike.Wolf.ComponentHealth;
            if (wolfHealth == null)
            {
                m_pendingBullWolfStrikes.RemoveAt(i);
                continue;
            }

            if (wolfHealth.Health < strike.WolfHealthBefore - HealthEpsilon)
            {
                if (!strike.DamageConfirmed)
                {
                    strike.DamageConfirmed = true;
                    m_nativeBullStrikeDamageConfirmed++;
                    Log.Information("[LivingTownsCombatTest] BULL_NATIVE_MELEE_DAMAGE_CONFIRMED: wolfHP " +
                        strike.WolfHealthBefore.ToString("F3") + " -> " + wolfHealth.Health.ToString("F3") +
                        "; gameTime=" + now.ToString("F3"));
                }

                if (strike.LinkedToWolfHit || now - strike.AttackTime > NativeStrikeCorrelationWindow)
                    m_pendingBullWolfStrikes.RemoveAt(i);
            }
            else if (now - strike.AttackTime >= NativeStrikeCorrelationWindow)
            {
                m_nativeBullStrikeNoDamage++;
                Log.Information("[LivingTownsCombatTest] BULL_NATIVE_MELEE_NO_HEALTH_LOSS: a native bull melee event was observed, but the target wolf's health did not fall within " +
                    NativeStrikeCorrelationWindow.ToString("F2") + " game seconds.");
                m_pendingBullWolfStrikes.RemoveAt(i);
            }
        }
    }

    private bool TryFindNativeBullStrike(
        ComponentCreature bull,
        ComponentCreature wolf,
        double wolfHitConfirmedAt,
        double now,
        out PendingBullWolfStrike matchingStrike)
    {
        for (int i = m_pendingBullWolfStrikes.Count - 1; i >= 0; i--)
        {
            PendingBullWolfStrike strike = m_pendingBullWolfStrikes[i];
            if (strike.Bull == bull && strike.Wolf == wolf &&
                !strike.LinkedToWolfHit &&
                strike.AttackTime >= wolfHitConfirmedAt - 0.15 &&
                strike.AttackTime <= now &&
                now - strike.AttackTime <= NativeStrikeCorrelationWindow)
            {
                strike.LinkedToWolfHit = true;
                matchingStrike = strike;
                return true;
            }
        }

        matchingStrike = null;
        return false;
    }

    private static float GetTwelvePercentCap(float health)
    {
        if (float.IsNaN(health) || float.IsInfinity(health) || health <= 0f)
            return 0f;
        if (health <= 0.05f)
            return health;
        return health * 0.12f;
    }

    private static bool IsNaturalPredator(ComponentCreature creature)
    {
        const CreatureCategory predatorMask =
            CreatureCategory.LandPredator | CreatureCategory.WaterPredator;
        return creature != null && (creature.Category & predatorMask) != 0;
    }

    private static bool IsWolf(ComponentCreature creature)
    {
        if (creature == null || !IsNaturalPredator(creature))
            return false;
        return GetCreatureSignature(creature).IndexOf("wolf", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsBull(ComponentCreature creature)
    {
        if (creature == null || IsNaturalPredator(creature))
            return false;
        return GetCreatureSignature(creature).IndexOf("bull", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Survivalcraft entity templates can expose their species label through
    /// different component/template fields. Read string metadata reflectively so
    /// this monitor does not depend on an undocumented template property.
    /// </summary>
    private static string GetCreatureSignature(ComponentCreature creature)
    {
        var values = new StringBuilder();
        if (creature == null)
            return string.Empty;

        try
        {
            values.Append(creature.Category.ToString()).Append('|');
            AppendStringMetadata(creature, values, 0);
            if (creature.Entity != null)
            {
                AppendStringMetadata(creature.Entity, values, 0);
                foreach (GameEntitySystem.Component component in creature.Entity.Components)
                    AppendStringMetadata(component, values, 0);
            }
        }
        catch
        {
            // Metadata is optional; lack of a readable species label is reported
            // as BLOCKED during the initial scan rather than guessed.
        }

        return values.ToString();
    }

    private static void AppendStringMetadata(object source, StringBuilder output, int depth)
    {
        if (source == null || depth > 2)
            return;

        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        for (Type type = source.GetType(); type != null && type != typeof(object); type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(flags))
            {
                try
                {
                    if (field.FieldType == typeof(string))
                    {
                        string value = field.GetValue(source) as string;
                        if (!string.IsNullOrWhiteSpace(value))
                            output.Append(value).Append('|');
                    }
                    else if (depth < 2 && IsNameCarrier(field.FieldType))
                    {
                        object nested = field.GetValue(source);
                        if (nested != null)
                            AppendStringMetadata(nested, output, depth + 1);
                    }
                }
                catch
                {
                    // Some engine fields are not readable in every runtime state.
                }
            }

            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length != 0 || property.GetMethod == null)
                    continue;

                try
                {
                    if (property.PropertyType == typeof(string))
                    {
                        string value = property.GetValue(source, null) as string;
                        if (!string.IsNullOrWhiteSpace(value))
                            output.Append(value).Append('|');
                    }
                    else if (depth < 2 && IsNameCarrier(property.PropertyType))
                    {
                        object nested = property.GetValue(source, null);
                        if (nested != null && !ReferenceEquals(nested, source))
                            AppendStringMetadata(nested, output, depth + 1);
                    }
                }
                catch
                {
                    // Reflection is best-effort and must never interrupt the game.
                }
            }
        }
    }

    private static bool IsNameCarrier(Type type)
    {
        if (type == null)
            return false;
        string name = type.Name;
        return name.IndexOf("Template", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Model", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Dictionary", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Entity", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void RunReadOnlyScan(ComponentPlayer player)
    {
        if (player == null || player.Project == null)
            return;

        var report = new StringBuilder();
        int passed = 0;
        int failed = 0;
        int blocked = 0;

        report.AppendLine("=== Living Towns in-game diagnostic ===");
        report.AppendLine("Mode: READ ONLY (no entities spawned or modified)");
        report.AppendLine("UTC: " + DateTime.UtcNow.ToString("O"));

        try
        {
            SubsystemBodies bodies = player.Project.FindSubsystem<SubsystemBodies>(throwOnError: true);
            int bodyCount = 0;
            int villagerCount = 0;
            int sheepCount = 0;
            int wolfCount = 0;
            int bullCount = 0;
            int predatorCount = 0;
            int invalidLivingTownsHealth = 0;
            int duplicateVillagerControllers = 0;

            foreach (ComponentBody body in bodies.Bodies)
            {
                if (body == null || body.Entity == null)
                    continue;

                bodyCount++;
                int villagerComponents = 0;
                bool hasSheepBehavior = false;

                foreach (GameEntitySystem.Component component in body.Entity.Components)
                {
                    if (component == null)
                        continue;

                    string typeName = component.GetType().Name;
                    if (typeName == VillagerComponentName)
                        villagerComponents++;
                    if (typeName == SheepComponentName)
                        hasSheepBehavior = true;
                }

                bool isVillager = villagerComponents > 0;
                bool isSheep = hasSheepBehavior;
                ComponentCreature creature = body.Entity.FindComponent<ComponentCreature>();

                if (creature != null)
                {
                    if (IsNaturalPredator(creature))
                        predatorCount++;
                    if (IsWolf(creature))
                        wolfCount++;
                    if (IsBull(creature))
                        bullCount++;
                }

                if (isVillager)
                {
                    villagerCount++;
                    if (villagerComponents > 1)
                        duplicateVillagerControllers++;

                    if (creature == null || creature.ComponentHealth == null ||
                        creature.ComponentHealth.Health <= 0f)
                        invalidLivingTownsHealth++;
                }

                if (isSheep)
                {
                    sheepCount++;
                    if (creature == null || creature.ComponentHealth == null ||
                        creature.ComponentHealth.Health <= 0f)
                        invalidLivingTownsHealth++;
                }
            }

            report.AppendLine("Loaded body count: " + bodyCount);
            report.AppendLine("Living Towns villagers detected: " + villagerCount);
            report.AppendLine("Living Towns sheep/ram/lamb behaviors detected: " + sheepCount);
            report.AppendLine("Natural predators detected: " + predatorCount);
            report.AppendLine("Wolf labels detected: " + wolfCount);
            report.AppendLine("Bull labels detected: " + bullCount);

            AddResult(report, "World entity scan", true,
                "SubsystemBodies was read successfully (" + bodyCount + " bodies).", ref passed, ref failed, ref blocked);

            if (villagerCount > 0)
                AddResult(report, "Villager presence", true, villagerCount + " villager controller(s) detected.", ref passed, ref failed, ref blocked);
            else
                AddResult(report, "Villager presence", null, "No loaded Living Towns villagers found.", ref passed, ref failed, ref blocked);

            if (sheepCount > 0)
                AddResult(report, "Sheep-family presence", true, sheepCount + " sheep/ram/lamb behavior component(s) detected.", ref passed, ref failed, ref blocked);
            else
                AddResult(report, "Sheep-family presence", null, "No loaded Living Towns sheep-family entities found.", ref passed, ref failed, ref blocked);

            AddResult(report, "Living Towns health integrity", invalidLivingTownsHealth == 0,
                invalidLivingTownsHealth + " identified Living Towns entity/entities had missing, absent, or zero health.",
                ref passed, ref failed, ref blocked);

            AddResult(report, "Duplicate villager controller", duplicateVillagerControllers == 0,
                duplicateVillagerControllers + " villager entity/entities had more than one Living Towns controller.",
                ref passed, ref failed, ref blocked);

            if (wolfCount > 0)
                AddResult(report, "Wolf identification", true, wolfCount + " wolf label(s) detected from runtime metadata.", ref passed, ref failed, ref blocked);
            else
                AddResult(report, "Wolf identification", null, "No loaded wolf label was readable from creature metadata; no species will be guessed.", ref passed, ref failed, ref blocked);

            if (bullCount > 0)
                AddResult(report, "Bull identification", true, bullCount + " bull label(s) detected from runtime metadata.", ref passed, ref failed, ref blocked);
            else
                AddResult(report, "Bull identification", null, "No loaded bull label was readable from creature metadata; no species will be guessed.", ref passed, ref failed, ref blocked);

            AddResult(report, "Wolf versus bull combat", null,
                "Tap WATCH and let the real game AI fight. Attack and health hooks are observed without applying damage.",
                ref passed, ref failed, ref blocked);
        }
        catch (Exception ex)
        {
            AddResult(report, "World entity scan", false,
                ex.GetType().Name + ": " + ex.Message, ref passed, ref failed, ref blocked);
        }

        report.AppendLine();
        report.AppendLine("SUMMARY: PASS=" + passed + " FAIL=" + failed + " BLOCKED=" + blocked);
        report.AppendLine("A BLOCKED result means the scenario was not executed; it is not a pass.");
        report.AppendLine("=== End Living Towns diagnostic ===");

        foreach (string line in report.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (!string.IsNullOrWhiteSpace(line))
                Log.Information("[LivingTownsTest] " + line);
        }
    }

    private static void AddResult(
        StringBuilder report,
        string name,
        bool? passedResult,
        string detail,
        ref int passed,
        ref int failed,
        ref int blocked)
    {
        string status;
        if (!passedResult.HasValue)
        {
            status = "BLOCKED";
            blocked++;
        }
        else if (passedResult.Value)
        {
            status = "PASS";
            passed++;
        }
        else
        {
            status = "FAIL";
            failed++;
        }

        report.AppendLine("[" + status + "] " + name + ": " + detail);
    }
}