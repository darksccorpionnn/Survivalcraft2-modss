using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Engine;
using HarmonyLib;

namespace Game;

/// <summary>
/// Repairs two failure cases in the existing LivingTowns controller without replacing
/// its DLL or taking ownership of jobs, beds, doors, inventory, or trade UI.
/// </summary>
internal static class VillagerNavigationPatch
{
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static Harmony s_harmony;
    private static bool s_listening;
    private static readonly HashSet<MethodBase> s_patchedProgress = new();
    private static readonly HashSet<MethodBase> s_patchedRest = new();

    public static void Install(Harmony harmony)
    {
        if (harmony == null)
            return;

        s_harmony = harmony;
        if (!s_listening)
        {
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            s_listening = true;
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            TryPatchAssembly(assembly);
    }

    private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
    {
        try
        {
            TryPatchAssembly(args.LoadedAssembly);
        }
        catch (Exception ex)
        {
            Log.Error("[KoylulerModu] Villager navigation assembly patch failed: " + ex);
        }
    }

    private static void TryPatchAssembly(Assembly assembly)
    {
        if (assembly == null || s_harmony == null)
            return;

        Type villagerType = assembly.GetType("Game.ComponentLivingTownsVillager", false);
        if (villagerType == null)
            return;

        MethodInfo postfix = typeof(VillagerNavigationPatch).GetMethod(
            nameof(RecoverWhenNativeDestinationIsCleared),
            BindingFlags.Public | BindingFlags.Static);

        MethodInfo progressMethod = AccessTools.Method(
            villagerType, "UpdateManagedPathProgress", new Type[] { typeof(float) });

        if (progressMethod != null && !s_patchedProgress.Contains(progressMethod))
        {
            s_harmony.Patch(progressMethod, postfix: new HarmonyMethod(postfix));
            s_patchedProgress.Add(progressMethod);
            Log.Information("[KoylulerModu] Missing-destination progress recovery installed.");
        }

        MethodInfo restMethod = AccessTools.Method(
            villagerType, "RestAtPosition",
            new Type[] { typeof(Vector3), typeof(bool), typeof(float) });

        MethodInfo transpiler = typeof(VillagerNavigationPatch).GetMethod(
            nameof(DisableBlockedBedSnap),
            BindingFlags.Public | BindingFlags.Static);

        if (restMethod != null && !s_patchedRest.Contains(restMethod))
        {
            s_harmony.Patch(restMethod, transpiler: new HarmonyMethod(transpiler));
            s_patchedRest.Add(restMethod);
            Log.Information("[KoylulerModu] Blocked-bed snap prevention installed.");
        }
    }

    /// <summary>
    /// LivingTowns clears Destination when its native path search fails. Its own
    /// UpdateManagedPathProgress then resets the no-progress timer to zero every frame,
    /// so SetManagedDestination never reaches the already-existing obstacle-detour code.
    /// Rebuild that timer from actual distance to the saved managed waypoint and release
    /// the retry cooldown once the villager has genuinely stopped approaching it.
    /// </summary>
    public static void RecoverWhenNativeDestinationIsCleared(object __instance, float dt)
    {
        try
        {
            Type type = __instance.GetType();
            object pathfinding = GetField(type, "m_pathfinding")?.GetValue(__instance);
            if (pathfinding == null)
                return;

            PropertyInfo destinationProperty = pathfinding.GetType().GetProperty("Destination", Flags);
            if (destinationProperty == null)
                return;

            // A non-null boxed Nullable<Vector3> is a real active native destination.
            if (destinationProperty.GetValue(pathfinding, null) != null)
            {
                ResetWatchState(__instance);
                return;
            }

            object pathResult = GetField(pathfinding.GetType(), "m_pathfindingResult")?.GetValue(pathfinding);
            if (ReadBool(pathResult, "IsInProgress"))
                return;

            FieldInfo hasManagedField = GetField(type, "m_hasManagedDestination");
            FieldInfo managedField = GetField(type, "m_managedDestination");
            FieldInfo bodyField = GetField(type, "m_body");
            FieldInfo timerField = GetField(type, "m_managedNoProgressTime");
            FieldInfo cooldownField = GetField(type, "m_destinationRetryCooldown");
            FieldInfo sampleField = GetField(type, "m_hasManagedProgressSample");
            FieldInfo trackedField = GetField(type, "m_managedProgressTarget");
            FieldInfo distanceField = GetField(type, "m_managedProgressDistance");

            if (hasManagedField == null || managedField == null || bodyField == null
                || timerField == null || cooldownField == null || sampleField == null
                || trackedField == null || distanceField == null
                || !(hasManagedField.GetValue(__instance) is bool hasManaged) || !hasManaged)
                return;

            object body = bodyField.GetValue(__instance);
            if (body == null)
                return;

            PropertyInfo positionProperty = body.GetType().GetProperty("Position", Flags);
            if (positionProperty == null)
                return;

            Vector3 position = (Vector3)positionProperty.GetValue(body, null);
            Vector3 target = (Vector3)managedField.GetValue(__instance);
            float distance = Vector3.Distance(position, target);

            // The native range/arrival code owns close targets. Do not manufacture a
            // detour when the villager is already standing beside its destination.
            if (distance <= 1.15f)
            {
                ResetWatchState(__instance);
                return;
            }

            WatchState state = s_watchStates.GetValue(__instance, _ => new WatchState());
            lock (state)
            {
                if (!state.Initialized || Vector3.DistanceSquared(state.Target, target) > 0.25f)
                {
                    state.Initialized = true;
                    state.Target = target;
                    state.BestDistance = distance;
                    state.NoProgressSeconds = 0.0;
                }
                else if (distance < state.BestDistance - 0.15f)
                {
                    state.BestDistance = distance;
                    state.NoProgressSeconds = 0.0;
                }
                else
                {
                    state.NoProgressSeconds += Math.Max(0.0, dt);
                }

                // Feed the controller's own existing TryChooseObstacleDetour logic.
                timerField.SetValue(__instance, state.NoProgressSeconds);
                trackedField.SetValue(__instance, target);
                distanceField.SetValue(__instance, state.BestDistance);
                sampleField.SetValue(__instance, true);

                if (state.NoProgressSeconds >= 0.65)
                {
                    // SetManagedDestination otherwise suppresses a retry for 3 seconds
                    // when Destination is null, even after the watchdog has fired.
                    cooldownField.SetValue(__instance, 0.0);

                    if (!state.WarningLogged)
                    {
                        state.WarningLogged = true;
                        Log.Warning("[KoylulerModu] A villager's native path search returned no destination; obstacle-route recovery has been triggered.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("[KoylulerModu] Could not recover a blocked villager route: " + ex);
        }
    }

    /// <summary>
    /// The old RestAtPosition method contains a profession-specific emergency shortcut:
    /// after two seconds it treats a bed within 1.9 blocks as reached and teleports the
    /// body there. That can place a villager across a wall. Replace only the first
    /// m_homeReturnNoProgressTime load (the 2-second snap test) with zero; preserve the
    /// later 14/30-second doorway retry watchdog unchanged.
    /// </summary>
    public static IEnumerable<CodeInstruction> DisableBlockedBedSnap(
        IEnumerable<CodeInstruction> instructions)
    {
        bool replaced = false;

        foreach (CodeInstruction instruction in instructions)
        {
            if (!replaced &&
                instruction.opcode == OpCodes.Ldfld &&
                instruction.operand is FieldInfo field &&
                field.Name == "m_homeReturnNoProgressTime")
            {
                yield return new CodeInstruction(OpCodes.Ldc_R8, 0.0);
                replaced = true;
            }
            else
            {
                yield return instruction;
            }
        }

        if (replaced)
            Log.Information("[KoylulerModu] Disabled the unsafe 2-second bed snap; normal doorway recovery remains enabled.");
        else
            Log.Error("[KoylulerModu] Bed-snap transpiler did not find its expected field load; the original bed routine was left unchanged.");
    }

    private sealed class WatchState
    {
        public bool Initialized;
        public bool WarningLogged;
        public Vector3 Target;
        public float BestDistance;
        public double NoProgressSeconds;
    }

    private static readonly ConditionalWeakTable<object, WatchState> s_watchStates = new();

    private static void ResetWatchState(object villager)
    {
        if (villager != null && s_watchStates.TryGetValue(villager, out WatchState state))
        {
            lock (state)
            {
                state.Initialized = false;
                state.NoProgressSeconds = 0.0;
                state.BestDistance = 0f;
                state.WarningLogged = false;
            }
        }
    }

    private static FieldInfo GetField(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(name, Flags | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        return null;
    }

    private static bool ReadBool(object target, string propertyName)
    {
        if (target == null)
            return false;

        PropertyInfo property = target.GetType().GetProperty(propertyName, Flags);
        return property != null && property.PropertyType == typeof(bool)
            && (bool)property.GetValue(target, null);
    }
}
