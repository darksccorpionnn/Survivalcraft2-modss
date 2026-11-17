using System;
using System.Collections;
using System.Reflection;
using Engine;
using HarmonyLib;

namespace Game;

/// <summary>
/// Runs after LivingTowns handles a villager tap. LivingTowns opens its modal
/// trade/inventory panel in a Harmony prefix; this postfix then sets its actual
/// ChestLabel using the identity on the clicked villager entity.
/// </summary>
[HarmonyPatch(typeof(ComponentMiner), nameof(ComponentMiner.Hit), new Type[]
{
    typeof(ComponentBody), typeof(Vector3), typeof(Vector3)
})]
internal static class VillagerInventoryNamePatch
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static void Postfix(ComponentMiner __instance, ComponentBody componentBody)
    {
        try
        {
            if (__instance?.ComponentPlayer?.ComponentGui == null || componentBody == null)
                return;

            object panel = __instance.ComponentPlayer.ComponentGui.ModalPanelWidget;
            if (panel == null)
                return;

            object villager = GetMemberValue(panel, "m_villager");
            if (villager == null)
                return;

            string name = null;
            object entity = GetMemberValue(villager, "Entity");
            if (entity != null)
            {
                MethodInfo findIdentity = typeof(Entity).GetMethod("FindComponent", InstanceFlags);
                // Prefer the strongly typed generic component lookup via reflection.
                foreach (MethodInfo method in entity.GetType().GetMethods(InstanceFlags))
                {
                    if (method.Name != "FindComponent" || !method.IsGenericMethodDefinition ||
                        method.GetGenericArguments().Length != 1)
                        continue;
                    MethodInfo generic = method.MakeGenericMethod(typeof(ComponentKoyluIdentity));
                    ParameterInfo[] parameters = generic.GetParameters();
                    if (parameters.Length == 0)
                    {
                        ComponentKoyluIdentity identity = generic.Invoke(entity, null) as ComponentKoyluIdentity;
                        name = identity?.EnsureNameNow();
                        break;
                    }
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                    {
                        ComponentKoyluIdentity identity = generic.Invoke(entity, new object[] { false }) as ComponentKoyluIdentity;
                        name = identity?.EnsureNameNow();
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                FieldInfo nameField = FindField(villager.GetType(), "m_villagerName");
                name = nameField != null ? nameField.GetValue(villager) as string : null;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                Log.Warning("[KoylulerModu] Villager inventory opened, but no identity component/name was found.");
                return;
            }

            object children = GetMemberValue(panel, "Children");
            if (children == null)
                return;

            MethodInfo findLabel = null;
            foreach (MethodInfo method in children.GetType().GetMethods(InstanceFlags))
            {
                if (method.Name == "Find" && method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType == typeof(string))
                {
                    findLabel = method;
                    break;
                }
            }
            if (findLabel == null)
            {
                Log.Warning("[KoylulerModu] Inventory panel child lookup was not found.");
                return;
            }

            object label = findLabel.MakeGenericMethod(typeof(LabelWidget))
                .Invoke(children, new object[] { "ChestLabel" });
            if (label == null)
            {
                Log.Warning("[KoylulerModu] Inventory panel ChestLabel was not found.");
                return;
            }

            PropertyInfo text = label.GetType().GetProperty("Text", InstanceFlags);
            if (text == null || text.PropertyType != typeof(string) || !text.CanWrite)
                return;

            text.SetValue(label, name + " – Envanteri", null);
            Log.Information("[KoylulerModu] Inventory title successfully set: " + name);
        }
        catch (Exception ex)
        {
            Log.Error("[KoylulerModu] Inventory name display failed: " + ex);
        }
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
