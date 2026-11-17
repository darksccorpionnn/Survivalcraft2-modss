using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Engine;
using HarmonyLib;

namespace Game;

/// <summary>
/// Changes only the villager stock label in LivingTownsTradeWidget.
/// It does not create or enable a name label over the villager in the world.
/// </summary>
[HarmonyPatch]
internal static class VillagerInventoryNamePatch
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static IEnumerable<MethodBase> TargetMethods()
    {
        Type widgetType = FindLoadedType("Game.LivingTownsTradeWidget");
        if (widgetType == null)
        {
            Log.Information("[KoylulerModu] LivingTownsTradeWidget was not loaded yet; no inventory-title constructor was found.");
            yield break;
        }

        foreach (ConstructorInfo constructor in widgetType.GetConstructors(InstanceFlags))
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            if (parameters.Length == 4 &&
                parameters[0].ParameterType.FullName == "Game.ComponentPlayer" &&
                parameters[1].ParameterType.FullName == "Game.ComponentLivingTownsVillager")
            {
                yield return constructor;
            }
        }
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
                return;

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
                return;

            object label = findMethod.MakeGenericMethod(typeof(LabelWidget))
                .Invoke(children, new object[] { "ChestLabel" });
            if (label == null)
                return;

            PropertyInfo textProperty = label.GetType().GetProperty("Text", InstanceFlags);
            if (textProperty == null || textProperty.PropertyType != typeof(string) || !textProperty.CanWrite)
                return;

            textProperty.SetValue(label, name + " – Envanteri", null);
        }
        catch (Exception ex)
        {
            Log.Error("[KoylulerModu] Could not set the villager inventory title: " + ex);
        }
    }

    private static string ReadVillagerName(object villager)
    {
        if (villager == null)
            return null;

        FieldInfo field = FindField(villager.GetType(), "m_villagerName");
        if (field != null)
        {
            string value = field.GetValue(villager) as string;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        object entity = GetMemberValue(villager, "Entity");
        object components = GetMemberValue(entity, "Components");
        if (components is IEnumerable enumerable)
        {
            foreach (object component in enumerable)
                if (component is ComponentKoyluIdentity identity &&
                    !string.IsNullOrWhiteSpace(identity.Name))
                    return identity.Name;
        }
        return null;
    }

    private static Type FindLoadedType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(fullName, throwOnError: false);
            if (type != null)
                return type;
        }
        return null;
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
