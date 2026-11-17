using System;
using System.Collections.Generic;
using System.Reflection;
using Engine;
using HarmonyLib;

namespace Game;

/// <summary>
/// Keeps idle/patrol movement inside a circle around each villager's own bed.
/// The existing LivingTowns controller still owns job, door, home and sleep
/// destinations; only its TickPatrol fallback is modified.
/// </summary>
internal static class VillagerBedRoamingPatch
{
    private const float MinimumRoamRadius = 2.5f;
    private const float MaximumRoamRadius = 25.0f;

    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static Harmony s_harmony;
    private static bool s_listening;
    private static readonly HashSet<MethodBase> s_patched = new HashSet<MethodBase>();
    private static FieldInfo s_hasBedCellField;
    private static FieldInfo s_bedCellField;

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
            Log.Error("[KoylulerModu] Bed-roaming assembly-load patch failed: " + ex);
        }
    }

    private static void TryPatchAssembly(Assembly assembly)
    {
        if (assembly == null || s_harmony == null)
            return;

        Type controllerType = assembly.GetType("Game.ComponentLivingTownsVillager", false);
        if (controllerType == null)
            return;

        FieldInfo hasBedCell = controllerType.GetField("m_hasBedCell", InstanceFlags);
        FieldInfo bedCell = controllerType.GetField("m_bedCell", InstanceFlags);
        if (hasBedCell == null || hasBedCell.FieldType != typeof(bool) ||
            bedCell == null || bedCell.FieldType != typeof(Point3))
        {
            Log.Error("[KoylulerModu] Bed-roaming patch skipped: controller bed fields do not match the expected bool/Point3 types.");
            return;
        }

        s_hasBedCellField = hasBedCell;
        s_bedCellField = bedCell;

        MethodInfo patrol = controllerType.GetMethod(
            "TickPatrol", InstanceFlags, null, new Type[] { typeof(float), typeof(float) }, null);
        MethodInfo center = controllerType.GetMethod(
            "GetWorkCenterCell", InstanceFlags, null, Type.EmptyTypes, null);

        MethodInfo patrolPrefix = typeof(VillagerBedRoamingPatch).GetMethod(
            nameof(TickPatrolPrefix), BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo centerPrefix = typeof(VillagerBedRoamingPatch).GetMethod(
            nameof(GetWorkCenterCellPrefix), BindingFlags.Static | BindingFlags.NonPublic);

        if (patrol == null || center == null || patrolPrefix == null || centerPrefix == null)
        {
            Log.Error("[KoylulerModu] Bed-roaming patch skipped: TickPatrol/GetWorkCenterCell signature was not found.");
            return;
        }

        if (!s_patched.Contains(patrol))
        {
            s_harmony.Patch(patrol, prefix: new HarmonyMethod(patrolPrefix));
            s_patched.Add(patrol);
            Log.Information("[KoylulerModu] Idle patrol radius set to 2.5-25 blocks around each villager's own bed.");
        }

        if (!s_patched.Contains(center))
        {
            s_harmony.Patch(center, prefix: new HarmonyMethod(centerPrefix));
            s_patched.Add(center);
            Log.Information("[KoylulerModu] Idle patrol centre now uses the villager's assigned bed cell.");
        }
    }

    private static bool TickPatrolPrefix(
        object __instance, ref float minRadius, ref float maxRadius)
    {
        // Do not allow free roaming until this villager has its own saved bed.
        if (!TryGetBedCell(__instance, out _))
            return false;

        minRadius = MinimumRoamRadius;
        maxRadius = MaximumRoamRadius;
        return true;
    }

    private static bool GetWorkCenterCellPrefix(object __instance, ref Point3 __result)
    {
        if (!TryGetBedCell(__instance, out Point3 bedCell))
            return true;

        __result = bedCell;
        return false;
    }

    private static bool TryGetBedCell(object instance, out Point3 bedCell)
    {
        bedCell = default;
        if (instance == null || s_hasBedCellField == null || s_bedCellField == null)
            return false;

        try
        {
            object hasBed = s_hasBedCellField.GetValue(instance);
            if (!(hasBed is bool assigned) || !assigned)
                return false;

            object storedCell = s_bedCellField.GetValue(instance);
            if (!(storedCell is Point3 point))
                return false;

            bedCell = point;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("[KoylulerModu] Could not read a villager's assigned bed for roaming: " + ex.Message);
            return false;
        }
    }
}
