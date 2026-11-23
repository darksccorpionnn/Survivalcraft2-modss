using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using HarmonyLib;

namespace Game
{
    /// <summary>
    /// Supplemental idle-only path scheduling for the ORIGINAL 0.2.14.7 controller.
    /// All existing job/home/sleep/door methods and inventories remain intact.
    /// </summary>
    public sealed class KoylulerAutonomousRoamLoader : ModLoader
    {
        private static Harmony s_harmony;
        public override void __ModInitialize()
        {
            try
            {
                s_harmony ??= new Harmony("darksccorpionnn.Koyluler.AutonomousRoam.02148");
                RoamControllerPatch.Install(s_harmony);
            }
            catch (Exception e)
            {
                Log.Error("[KoylulerRoam] initialization failed: " + e);
            }
            ModsManager.RegisterHook("OnLoadingFinished", this);
        }

        public override void OnLoadingFinished(List<Action> actions)
        {
            try { RoamControllerPatch.Install(s_harmony); }
            catch (Exception e) { Log.Error("[KoylulerRoam] world hook failed: " + e); }
        }
    }

    internal static class RoamControllerPatch
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const BindingFlags Methods = BindingFlags.Static | BindingFlags.NonPublic;
        private const float CheckEverySeconds = 2.4f;

        private static Harmony s_harmony;
        private static Type s_villagerType;
        private static FieldInfo s_body;
        private static FieldInfo s_terrain;
        private static FieldInfo s_pathfinding;
        private static FieldInfo s_hasManagedDestination;
        private static FieldInfo s_managedNoProgressTime;
        private static MethodInfo s_isNight;
        private static MethodInfo s_isDusk;
        private static MethodInfo s_isValidStandingCell;
        private static MethodInfo s_setManagedDestination;
        private static MethodInfo s_stopManagedPath;
        private static bool s_listening;
        private static readonly HashSet<MethodBase> Patched = new HashSet<MethodBase>();
        private static readonly ConditionalWeakTable<object, VillagerState> States =
            new ConditionalWeakTable<object, VillagerState>();

        private static readonly string[] CriticalBusyFields = new[]
        {
            "m_isLeavingHome",
            "m_isGoingToWorkSign",
            "m_homeDoorRouteActive",
            "m_waitingForHomeDoorPassage",
            "m_isTalking",
            "m_hasWorkTarget",
            "m_isReturningForStorage",
            "m_obstacleDetourActive"
        };
        private static readonly Dictionary<string, FieldInfo> BusyFields =
            new Dictionary<string, FieldInfo>();

        private sealed class VillagerState
        {
            public float Elapsed;
            public float Cooldown;
            public int Attempts;
            public int Successes;
            public int Logs;
            public int DiagnosticCountdown;
            public Random Random;
        }

        public static void Install(Harmony harmony)
        {
            if (harmony == null) return;
            s_harmony = harmony;
            if (!s_listening)
            {
                AppDomain.CurrentDomain.AssemblyLoad += (_, args) => TryPatchAssembly(args.LoadedAssembly);
                s_listening = true;
            }
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                TryPatchAssembly(asm);
        }

        private static void TryPatchAssembly(Assembly assembly)
        {
            Type candidate = assembly?.GetType("Game.ComponentLivingTownsVillager", false);
            if (candidate == null || s_harmony == null || s_villagerType != null)
                return;
            try
            {
                MethodInfo update = candidate.GetMethod("Update", Flags, null,
                    new[] { typeof(float) }, null);
                s_body = candidate.GetField("m_body", Flags);
                s_terrain = candidate.GetField("m_terrain", Flags);
                s_pathfinding = candidate.GetField("m_pathfinding", Flags);
                s_hasManagedDestination = candidate.GetField("m_hasManagedDestination", Flags);
                s_managedNoProgressTime = candidate.GetField("m_managedNoProgressTime", Flags);
                s_isNight = candidate.GetMethod("IsNight", Flags, null, Type.EmptyTypes, null);
                s_isDusk = candidate.GetMethod("IsDusk", Flags, null, Type.EmptyTypes, null);
                s_isValidStandingCell = candidate.GetMethod("IsValidStandingCell", Flags,
                    null, new[] {typeof(int),typeof(int),typeof(int)}, null);
                s_setManagedDestination = candidate.GetMethod("SetManagedDestination", Flags);
                s_stopManagedPath = candidate.GetMethod("StopManagedPath", Flags, null,
                    Type.EmptyTypes, null);

                if (update == null || s_body == null || s_terrain == null || s_pathfinding == null ||
                    s_hasManagedDestination == null || s_managedNoProgressTime == null ||
                    s_isNight == null || s_isDusk == null || s_isValidStandingCell == null ||
                    s_setManagedDestination == null || s_stopManagedPath == null)
                {
                    Log.Error("[KoylulerRoam] core controller signatures differ; no patch installed.");
                    return;
                }

                var expected = s_setManagedDestination.GetParameters();
                if (expected.Length != 9 ||
                    expected[0].ParameterType != typeof(Vector3) ||
                    expected[1].ParameterType != typeof(float) ||
                    expected[2].ParameterType != typeof(float) ||
                    expected[3].ParameterType != typeof(int) ||
                    expected[4].ParameterType != typeof(bool) ||
                    expected[5].ParameterType != typeof(bool) ||
                    expected[6].ParameterType != typeof(bool) ||
                    expected[8].ParameterType != typeof(bool))
                {
                    Log.Error("[KoylulerRoam] destination signature mismatch; safe abort.");
                    return;
                }

                BusyFields.Clear();
                foreach (string name in CriticalBusyFields)
                {
                    FieldInfo field = candidate.GetField(name, Flags);
                    if (field == null || field.FieldType != typeof(bool))
                    {
                        Log.Error("[KoylulerRoam] missing busy flag " + name + "; patch disabled.");
                        return;
                    }
                    BusyFields.Add(name, field);
                }

                MethodInfo postfix = typeof(RoamControllerPatch).GetMethod(
                    nameof(UpdatePostfix), Methods);
                if (postfix == null) return;
                s_harmony.Patch(update, postfix: new HarmonyMethod(postfix));
                Patched.Add(update);
                s_villagerType = candidate;
                Log.Information("[KoylulerRoam] Autonomous idle movement installed: " +
                    "original day/night/work/door/sleep AI is retained; 8-16-block nearby targets.");
            }
            catch (Exception e)
            {
                Log.Error("[KoylulerRoam] controller patch rejected: " + e);
            }
        }

        // Invoked after the original Update has handled its own sleep,
        // home, sign, conversation and profession state. The original method
        // is never skipped or rewritten.
        private static void UpdatePostfix(object __instance, float __0)
        {
            if (__instance == null) return;
            VillagerState state = States.GetValue(__instance, _ => new VillagerState());
            if (state.Random == null)
                state.Random = new Random(unchecked(Environment.TickCount ^
                    RuntimeHelpers.GetHashCode(__instance) * 1664525));
            float dt = __0;
            if (dt <= 0f || dt > 4f) dt = 0.05f;
            state.Elapsed += dt;
            if (state.Cooldown > 0f) state.Cooldown -= dt;
            if (state.Elapsed < CheckEverySeconds) return;
            state.Elapsed = 0f;

            try
            {
                if ((bool)s_isNight.Invoke(__instance, null) ||
                    (bool)s_isDusk.Invoke(__instance, null))
                    return;
                if (s_body.GetValue(__instance) is not ComponentBody body ||
                    s_terrain.GetValue(__instance) is not SubsystemTerrain terrain ||
                    s_pathfinding.GetValue(__instance) == null)
                    return;

                foreach (KeyValuePair<string, FieldInfo> busy in BusyFields)
                {
                    if ((bool)busy.Value.GetValue(__instance))
                    {
                        // Limited telemetry makes failures diagnosable in an Android log,
                        // without logging every game tick.
                        if (state.Logs < 3)
                        {
                            state.Logs++;
                            Log.Information("[KoylulerRoam] Idle blocked by " + busy.Key +
                                " at " + body.Position);
                        }
                        return;
                    }
                }
                if (state.Cooldown > 0f) return;

                bool hasDestination = (bool)s_hasManagedDestination.GetValue(__instance);
                if (hasDestination)
                {
                    double noProgress = Convert.ToDouble(s_managedNoProgressTime.GetValue(__instance));
                    if (noProgress < 14.0) return;
                    // Cancel only an idle stalled route: job/home/sign states were checked above.
                    s_stopManagedPath.Invoke(__instance, null);
                    if (state.Logs < 6)
                    {
                        state.Logs++;
                        Log.Information("[KoylulerRoam] Recovering stuck *idle* destination.");
                    }
                }

                state.Attempts++;
                if (TryChooseDestination(__instance, terrain, body.Position,
                    state.Random, out Vector3 goal))
                {
                    // Use native managed pathfinding, exactly as TickPatrol does.
                    s_setManagedDestination.Invoke(__instance, new object[]
                    {
                        goal, 2.2f, 1.1f, 900, true, false, false, null, true
                    });
                    state.Successes++;
                    state.Cooldown = 7f + (float)state.Random.NextDouble() * 5f;
                    if (state.Logs < 8)
                    {
                        state.Logs++;
                        Log.Information("[KoylulerRoam] Idle walk goal " + goal +
                            " from " + body.Position + " success " + state.Successes +
                            "/" + state.Attempts);
                    }
                }
                else
                {
                    state.Cooldown = 2.5f;
                    if (state.Logs < 8)
                    {
                        state.Logs++;
                        Log.Warning("[KoylulerRoam] No valid nearby walking cell after 32 attempts at " +
                            body.Position);
                    }
                }
            }
            catch (Exception e)
            {
                state.Cooldown = 10f;
                if (state.Logs < 8)
                {
                    state.Logs++;
                    Log.Error("[KoylulerRoam] idle target failure: " + e);
                }
            }
        }

        private static bool TryChooseDestination(
            object villager, SubsystemTerrain terrain, Vector3 position,
            Random random, out Vector3 target)
        {
            target = default;
            if (terrain?.Terrain == null) return false;
            for (int attempt = 0; attempt < 32; attempt++)
            {
                double angle = random.NextDouble() * 2 * Math.PI;
                float distance = 7f + 9f * (float)random.NextDouble();
                int x = (int)Math.Floor(position.X + Math.Cos(angle) * distance);
                int z = (int)Math.Floor(position.Z + Math.Sin(angle) * distance);
                int y = terrain.Terrain.GetTopHeight(x, z);
                if (y <= 2 || y >= 250) continue;
                if (Math.Abs((y + 1f) - position.Y) > 3.5f) continue;
                if (!(bool)s_isValidStandingCell.Invoke(villager, new object[]{x,y,z}))
                    continue;
                Vector3 candidate = new Vector3(x + 0.5f, y + 1f, z + 0.5f);
                if (Vector3.DistanceSquared(candidate, position) < 16f)
                    continue;
                target = candidate;
                return true;
            }
            return false;
        }
    }
}
