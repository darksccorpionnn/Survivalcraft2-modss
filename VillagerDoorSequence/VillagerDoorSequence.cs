using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Game;
using Engine;

namespace KoylulerDoorStages
{
    public sealed class DoorSequenceModLoader : ModLoader
    {
        private static Harmony s_harmony;
        private static bool s_patched;
        private static bool s_runtimeWarningShown;

        public override void __ModInitialize()
        {
            try
            {
                s_harmony = new Harmony("darksccorpionnn.KoylulerModu.DoorSequence.0214");
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                TryPatchLoadedAssemblies();
                ModsManager.RegisterHook("OnLoadingFinished", this);
                Log.Information("[KoylulerDoorStages] Door-first home entry patch initialized.");
            }
            catch (Exception ex)
            {
                Log.Error("[KoylulerDoorStages] Initialization failed: " + ex);
            }
        }

        public override void OnLoadingFinished(List<Action> actions)
        {
            TryPatchLoadedAssemblies();
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try
            {
                if (args.LoadedAssembly != null &&
                    args.LoadedAssembly.GetType("Game.ComponentLivingTownsVillager", false) != null)
                    TryPatchAssembly(args.LoadedAssembly);
            }
            catch (Exception ex)
            {
                Log.Warning("[KoylulerDoorStages] Assembly-load patch check failed: " + ex.Message);
            }
        }

        private static void TryPatchLoadedAssemblies()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetType("Game.ComponentLivingTownsVillager", false) != null)
                {
                    TryPatchAssembly(assembly);
                    return;
                }
            }
        }

        private static void TryPatchAssembly(Assembly assembly)
        {
            if (s_patched || s_harmony == null)
                return;

            Type villagerType = assembly.GetType("Game.ComponentLivingTownsVillager", false);
            if (villagerType == null)
                return;

            MethodInfo target = villagerType.GetMethod(
                "TickRestRoutine",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(float), typeof(bool) },
                null);
            MethodInfo prefix = typeof(DoorEntryPatch).GetMethod(
                nameof(DoorEntryPatch.TickRestRoutinePrefix),
                BindingFlags.Static | BindingFlags.Public);

            if (target == null || prefix == null)
                throw new MissingMethodException("TickRestRoutine or door-entry prefix was not found.");

            s_harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            s_patched = true;
            Log.Information("[KoylulerDoorStages] Installed door-first entry: door -> interior -> personal bed/sign. Dawn exit remains native.");
        }

        internal static void WarnRuntimeOnce(string message)
        {
            if (s_runtimeWarningShown)
                return;
            s_runtimeWarningShown = true;
            Log.Warning("[KoylulerDoorStages] " + message);
        }
    }

    internal static class DoorEntryPatch
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static bool TickRestRoutinePrefix(object __instance, object[] __args)
        {
            if (__instance == null || __args == null || __args.Length < 1)
                return true;

            try
            {
                return RunDoorFirstEntry(__instance, Convert.ToSingle(__args[0]));
            }
            catch (Exception ex)
            {
                DoorSequenceModLoader.WarnRuntimeOnce(
                    "Door-first route could not run; original 0.2.14 rest logic was allowed to continue. " + ex);
                return true;
            }
        }

        private static bool RunDoorFirstEntry(object villager, float dt)
        {
            Type villagerType = villager.GetType();
            if (!ReadBool(villager, "m_hasHomeDoor"))
                return true;

            object doorCell = ReadField(villager, "m_homeDoor");
            if (doorCell == null)
                return true;

            int doorX = ReadInt(doorCell, "X");
            int doorY = ReadInt(doorCell, "Y");
            int doorZ = ReadInt(doorCell, "Z");

            Type vector3Type = FindType("Engine.Vector3");
            Type terrainType = FindType("Game.Terrain");
            Type doorBlockType = FindType("Game.DoorBlock");
            if (vector3Type == null || terrainType == null || doorBlockType == null)
                return true;

            object body = ReadField(villager, "m_body");
            object currentPosition = ReadProperty(body, "Position");
            object terrainSubsystem = ReadField(villager, "m_terrain");
            object terrain = ReadProperty(terrainSubsystem, "Terrain");
            if (currentPosition == null || terrain == null)
                return true;

            MethodInfo getCellValue = FindMethod(terrain.GetType(), "GetCellValue", 3);
            MethodInfo extractData = FindMethod(terrainType, "ExtractData", 1);
            MethodInfo getRotation = FindMethod(doorBlockType, "GetRotation", 1);
            if (getCellValue == null || extractData == null || getRotation == null)
                return true;

            int cellValue = Convert.ToInt32(getCellValue.Invoke(terrain, new object[] { doorX, doorY, doorZ }));
            int data = Convert.ToInt32(extractData.Invoke(null, new object[] { cellValue }));
            int rotation = Convert.ToInt32(getRotation.Invoke(null, new object[] { data }));

            object axis = ReadStaticField(vector3Type, (rotation & 1) != 0 ? "UnitX" : "UnitZ");
            object doorCenter = CreateVector3(vector3Type, doorX + 0.5f, doorY, doorZ + 0.5f);
            if (axis == null || doorCenter == null)
                return true;

            MethodInfo inferInside = villagerType.GetMethod("TryInferHomeDoorInsideDirection", InstanceFlags);
            MethodInfo chooseApproach = villagerType.GetMethod("FindHomeDoorApproachPoint", InstanceFlags);
            MethodInfo restAtPosition = villagerType.GetMethod("RestAtPosition", InstanceFlags);
            if (inferInside == null || chooseApproach == null || restAtPosition == null)
                return true;

            object zero = CreateVector3(vector3Type, 0f, 0f, 0f);
            object[] inferArgs = new object[] { doorCenter, axis, zero };
            bool inferred = Convert.ToBoolean(inferInside.Invoke(villager, inferArgs));
            object inward = inferArgs[2];
            if (!inferred || inward == null)
                inward = TryReadCachedInward(villager, vector3Type);
            if (inward == null)
                return true;

            float side = Dot(Subtract(currentPosition, doorCenter), inward);
            bool routeActive = ReadBool(villager, "m_homeDoorRouteActive");

            // The door-first prefix remains active until the villager is clearly
            // inside the house and the native door-crossing route has completed.
            if (side >= 1.45f && !routeActive)
                return true;

            FieldInfo variantField = villagerType.GetField("m_homeDoorApproachVariant", InstanceFlags);
            if (variantField != null && Convert.ToInt32(variantField.GetValue(villager)) == 0)
                variantField.SetValue(villager, 1);

            object target = chooseApproach.Invoke(villager, new object[] { doorCenter, inward, true });
            if (target == null)
                return true;

            float targetSide = Dot(Subtract(target, doorCenter), inward);
            if (targetSide < 1.45f && variantField != null)
            {
                variantField.SetValue(villager, 2);
                target = chooseApproach.Invoke(villager, new object[] { doorCenter, inward, true });
                if (target == null)
                    return true;
                targetSide = Dot(Subtract(target, doorCenter), inward);
            }

            if (targetSide < 1.45f || !IsStandingTarget(villager, target, targetSide))
                return true;

            // The native 0.2.14 door-route helper needs either an assigned bed
            // or a fallback rest point to infer the indoor side. Seed that field
            // with the validated interior waypoint without assigning a bed/sign.
            SetField(villager, "m_fallbackRestPosition", target);
            SetField(villager, "m_hasFallbackRestPosition", true);
            SetField(villager, "m_nextFallbackRestSearchTime", 0.0);

            restAtPosition.Invoke(villager, new object[] { target, false, dt });
            return false; // Do not run EnsureAssignedBed until the villager is inside.
        }

        private static bool IsStandingTarget(object villager, object target, float targetSide)
        {
            object terrainSubsystem = ReadField(villager, "m_terrain");
            object terrain = ReadProperty(terrainSubsystem, "Terrain");
            MethodInfo isStanding = villager.GetType().GetMethod("IsValidStandingCell", InstanceFlags);
            if (terrain == null || isStanding == null)
                return targetSide >= 1.45f;

            float x = ReadFloat(target, "X");
            float y = ReadFloat(target, "Y");
            float z = ReadFloat(target, "Z");
            Type terrainType = FindType("Game.Terrain");
            MethodInfo toCell = terrainType == null ? null : FindMethod(terrainType, "ToCell", 1);

            int cellX = toCell != null ? Convert.ToInt32(toCell.Invoke(null, new object[] { x })) : (int)Math.Floor(x);
            int cellY = toCell != null ? Convert.ToInt32(toCell.Invoke(null, new object[] { y - 1f })) : (int)Math.Floor(y - 1f);
            int cellZ = toCell != null ? Convert.ToInt32(toCell.Invoke(null, new object[] { z })) : (int)Math.Floor(z);
            return Convert.ToBoolean(isStanding.Invoke(villager, new object[] { cellX, cellY, cellZ }));
        }

        private static object TryReadCachedInward(object villager, Type vector3Type)
        {
            if (ReadBool(villager, "m_hasCachedHomeDoorInward"))
                return ReadField(villager, "m_cachedHomeDoorInward");

            if (ReadBool(villager, "m_hasBedCell"))
            {
                object bedCell = ReadField(villager, "m_bedCell");
                object doorCell = ReadField(villager, "m_homeDoor");
                float dx = ReadInt(bedCell, "X") - ReadInt(doorCell, "X");
                float dz = ReadInt(bedCell, "Z") - ReadInt(doorCell, "Z");
                if (Math.Abs(dx) > Math.Abs(dz))
                    return CreateVector3(vector3Type, Math.Sign(dx), 0f, 0f);
                if (Math.Abs(dz) > 0f)
                    return CreateVector3(vector3Type, 0f, 0f, Math.Sign(dz));
            }
            return null;
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }
            return null;
        }

        private static MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            if (type == null)
                return null;
            foreach (MethodInfo method in type.GetMethods(InstanceFlags | StaticFlags))
            {
                if (method.Name == name && method.GetParameters().Length == parameterCount)
                    return method;
            }
            return null;
        }

        private static object ReadField(object instance, string name)
        {
            if (instance == null)
                return null;
            FieldInfo field = instance.GetType().GetField(name, InstanceFlags);
            return field == null ? null : field.GetValue(instance);
        }

        private static bool ReadBool(object instance, string name)
        {
            object value = ReadField(instance, name);
            return value != null && Convert.ToBoolean(value);
        }

        private static int ReadInt(object instance, string name)
        {
            object value = ReadField(instance, name);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static float ReadFloat(object instance, string name)
        {
            object value = ReadField(instance, name);
            return value == null ? 0f : Convert.ToSingle(value);
        }

        private static object ReadProperty(object instance, string name)
        {
            if (instance == null)
                return null;
            PropertyInfo property = instance.GetType().GetProperty(name, InstanceFlags);
            return property == null ? null : property.GetValue(instance, null);
        }

        private static object ReadStaticField(Type type, string name)
        {
            FieldInfo field = type.GetField(name, StaticFlags);
            return field == null ? null : field.GetValue(null);
        }

        private static object CreateVector3(Type vector3Type, float x, float y, float z)
        {
            return Activator.CreateInstance(vector3Type, new object[] { x, y, z });
        }

        private static void SetField(object instance, string name, object value)
        {
            FieldInfo field = instance.GetType().GetField(name, InstanceFlags);
            if (field != null)
                field.SetValue(instance, value);
        }

        private static void SetField(object instance, string name, bool value)
        {
            FieldInfo field = instance.GetType().GetField(name, InstanceFlags);
            if (field != null)
                field.SetValue(instance, value);
        }

        private static void SetField(object instance, string name, double value)
        {
            FieldInfo field = instance.GetType().GetField(name, InstanceFlags);
            if (field != null)
                field.SetValue(instance, value);
        }

        private static float Dot(object a, object b)
        {
            return ReadFloat(a, "X") * ReadFloat(b, "X") +
                   ReadFloat(a, "Y") * ReadFloat(b, "Y") +
                   ReadFloat(a, "Z") * ReadFloat(b, "Z");
        }

        private static object Subtract(object a, object b)
        {
            Type vector3Type = a.GetType();
            return CreateVector3(
                vector3Type,
                ReadFloat(a, "X") - ReadFloat(b, "X"),
                ReadFloat(a, "Y") - ReadFloat(b, "Y"),
                ReadFloat(a, "Z") - ReadFloat(b, "Z"));
        }
    }
}
