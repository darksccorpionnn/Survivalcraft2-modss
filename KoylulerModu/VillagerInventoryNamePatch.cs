using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Engine;
using HarmonyLib;

namespace Game;

/// <summary>
/// Patches the actual LivingTowns trade-inventory constructor when its assembly
/// loads. Only the inventory's ChestLabel is renamed; no world nameplate is created.
/// </summary>
internal static class VillagerInventoryNamePatch
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static Harmony s_harmony;
    private static bool s_listening;
    private static readonly HashSet<MethodBase> s_patched = new HashSet<MethodBase>();

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

        TryPatchLoadedAssemblies();
    }

    private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
    {
        try
        {
            TryPatchAssembly(args.LoadedAssembly);
        }
        catch (Exception ex)
        {
            Log.Error("[KoylulerModu] Inventory-title assembly-load hook failed: " + ex);
        }
    }

    private static void TryPatchLoadedAssemblies()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            TryPatchAssembly(assembly);
    }

    private static void TryPatchAssembly(Assembly assembly)
    {
        if (assembly == null || s_harmony == null)
            return;

        Type widgetType = assembly.GetType("Game.LivingTownsTradeWidget", false);
        if (widgetType == null)
            return;

        MethodInfo postfixMethod = typeof(VillagerInventoryNamePatch).GetMethod(
            nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic);
        if (postfixMethod == null)
            throw new MissingMethodException("Villager inventory title postfix was not found.");

        int patchedNow = 0;
        foreach (ConstructorInfo constructor in widgetType.GetConstructors(InstanceFlags))
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            if (parameters.Length != 4 ||
                parameters[0].ParameterType.FullName != "Game.ComponentPlayer" ||
                parameters[1].ParameterType.FullName != "Game.ComponentLivingTownsVillager")
                continue;

            if (s_patched.Contains(constructor))
                continue;

            s_harmony.Patch(constructor, postfix: new HarmonyMethod(postfixMethod));
            s_patched.Add(constructor);
            patchedNow++;
        }

        if (patchedNow > 0)
            Log.Information("[KoylulerModu] Villager inventory title patch attached to " + patchedNow + " constructor(s).");
        else if (s_patched.Count == 0)
            Log.Warning("[KoylulerModu] LivingTownsTradeWidget loaded, but its expected constructor signature was not found.");
    }

    private static void Postfix(object __instance)
    {
        try
        {
            if (__instance == null)
                return;

            FieldInfo villagerField = FindField(__instance.GetType(), "m_villager");
            object villager = villagerField != null ? villagerField.GetValue(__instance) : null;
            string name = ReadVillagerName(villager);
            if (string.IsNullOrWhiteSpace(name))
            {
                Log.Warning("[KoylulerModu] Inventory opened but villager identity was empty; title was not changed.");
                return;
            }

            object children = GetMemberValue(__instance, "Children");
            if (children == null)
                return;

            MethodInfo findMethod = null;
            foreach (MethodInfo method in children.GetType().GetMethods(InstanceFlags))
            {
                if (method.Name == "Find" && method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType == typeof(string))
                {
                    findMethod = method;
                    break;
                }
            }
            if (findMethod == null)
            {
                Log.Warning("[KoylulerModu] ChestLabel lookup method was not found.");
                return;
            }

            object label = findMethod.MakeGenericMethod(typeof(LabelWidget))
                .Invoke(children, new object[] { "ChestLabel" });
            if (label == null)
            {
                Log.Warning("[KoylulerModu] ChestLabel was not found in villager inventory.");
                return;
            }

            PropertyInfo textProperty = label.GetType().GetProperty("Text", InstanceFlags);
            if (textProperty == null || textProperty.PropertyType != typeof(string) || !textProperty.CanWrite)
            {
                Log.Warning("[KoylulerModu] ChestLabel.Text is not writable.");
                return;
            }

            textProperty.SetValue(label, name + " – Envanteri", null);
            Log.Information("[KoylulerModu] Villager inventory title set: " + name);
        }
        catch (Exception ex)
        {
            Log.Error("[KoylulerModu] Could not set villager inventory title: " + ex);
        }
    }

    private static string ReadVillagerName(object villager)
    {
        if (villager == null)
            return null;

        object entity = GetMemberValue(villager, "Entity");
        object components = GetMemberValue(entity, "Components");
        if (components is IEnumerable enumerable)
        {
            foreach (object component in enumerable)
            {
                if (component is ComponentKoyluIdentity identity)
                {
                    string ensured = identity.EnsureNameNow();
                    if (!string.IsNullOrWhiteSpace(ensured))
                        return ensured;
                }
            }
        }

        FieldInfo field = FindField(villager.GetType(), "m_villagerName");
        return field != null ? field.GetValue(villager) as string : null;
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        return null;
    }

    private static object GetMemberValue(object target, string name)
    {
        if (target == null)
            return null;

        PropertyInfo property = target.GetType().GetProperty(name, InstanceFlags);
        if (property != null && property.GetIndexParameters().Length == 0)
            return property.GetValue(target, null);

        FieldInfo field = FindField(target.GetType(), name);
        return field != null ? field.GetValue(target) : null;
    }
}
