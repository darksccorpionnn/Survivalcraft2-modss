using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Engine;
using HarmonyLib;

namespace Game;

/// <summary>
/// Hooks the exact LivingTownsTradeWidget.TryOpen method.
/// Its postfix runs after the NPC inventory panel is created and assigned
/// to ComponentGui.ModalPanelWidget, then changes the villager stock title.
/// </summary>
internal static class VillagerInventoryNamePatch
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

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
            Log.Error("[KoylulerModu] LivingTowns assembly patch failed: " + ex);
        }
    }

    private static void TryPatchAssembly(Assembly assembly)
    {
        if (assembly == null || s_harmony == null)
            return;

        Type widgetType = assembly.GetType("Game.LivingTownsTradeWidget", false);
        if (widgetType == null)
            return;

        MethodInfo tryOpen = null;
        foreach (MethodInfo method in widgetType.GetMethods(StaticFlags))
        {
            if (method.Name != "TryOpen" || method.ReturnType != typeof(bool))
                continue;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 2 &&
                parameters[0].ParameterType.FullName == "Game.ComponentPlayer" &&
                parameters[1].ParameterType.FullName == "Game.ComponentLivingTownsVillager")
            {
                tryOpen = method;
                break;
            }
        }

        if (tryOpen == null)
        {
            Log.Warning("[KoylulerModu] LivingTownsTradeWidget loaded, but exact TryOpen(player, villager) method was not found.");
            return;
        }

        if (s_patched.Contains(tryOpen))
            return;

        MethodInfo postfix = typeof(VillagerInventoryNamePatch).GetMethod(
            nameof(TryOpenPostfix), StaticFlags);
        s_harmony.Patch(tryOpen, postfix: new HarmonyMethod(postfix));
        s_patched.Add(tryOpen);
        Log.Information("[KoylulerModu] Attached inventory-name postfix to LivingTownsTradeWidget.TryOpen.");
    }

    private static void TryOpenPostfix(object[] __args, bool __result)
    {
        try
        {
            if (!__result || __args == null || __args.Length < 2)
                return;

            object player = __args[0];
            object villager = __args[1];
            if (player == null || villager == null)
                return;

            object gui = GetMemberValue(player, "ComponentGui");
            object panel = GetMemberValue(gui, "ModalPanelWidget");
            if (panel == null)
            {
                Log.Warning("[KoylulerModu] TryOpen succeeded but ModalPanelWidget was empty.");
                return;
            }

            string name = GetOrCreateVillagerName(villager);
            if (string.IsNullOrWhiteSpace(name))
            {
                Log.Warning("[KoylulerModu] Trade panel opened, but villager identity was empty.");
                return;
            }

            object children = GetMemberValue(panel, "Children");
            object label = FindChildLabel(children, "ChestLabel");
            if (label == null)
            {
                Log.Warning("[KoylulerModu] Trade panel opened, but ChestLabel could not be found.");
                return;
            }

            PropertyInfo text = label.GetType().GetProperty("Text", InstanceFlags);
            if (text == null || text.PropertyType != typeof(string) || !text.CanWrite)
            {
                Log.Warning("[KoylulerModu] ChestLabel.Text was not writable.");
                return;
            }

            text.SetValue(label, name + " – Envanteri", null);
            Log.Information("[KoylulerModu] Villager stock label changed to: " + name + " – Envanteri");
        }
        catch (Exception ex)
        {
            Log.Error("[KoylulerModu] Inventory title update failed: " + ex);
        }
    }

    private static string GetOrCreateVillagerName(object villager)
    {
        object entity = GetMemberValue(villager, "Entity");
        if (entity != null)
        {
            foreach (MethodInfo method in entity.GetType().GetMethods(InstanceFlags))
            {
                if (method.Name != "FindComponent" || !method.IsGenericMethodDefinition ||
                    method.GetGenericArguments().Length != 1)
                    continue;

                MethodInfo generic;
                try
                {
                    generic = method.MakeGenericMethod(typeof(ComponentKoyluIdentity));
                }
                catch
                {
                    continue;
                }

                ParameterInfo[] parameters = generic.GetParameters();
                ComponentKoyluIdentity identity = null;
                if (parameters.Length == 0)
                    identity = generic.Invoke(entity, null) as ComponentKoyluIdentity;
                else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                    identity = generic.Invoke(entity, new object[] { false }) as ComponentKoyluIdentity;

                if (identity != null)
                {
                    string ensuredName = identity.EnsureNameNow();
                    if (!string.IsNullOrWhiteSpace(ensuredName))
                        return ensuredName;
                }
            }

            object components = GetMemberValue(entity, "Components");
            if (components is IEnumerable enumerable)
            {
                foreach (object component in enumerable)
                {
                    if (component is ComponentKoyluIdentity identity)
                    {
                        string ensuredName = identity.EnsureNameNow();
                        if (!string.IsNullOrWhiteSpace(ensuredName))
                            return ensuredName;
                    }
                }
            }
        }

        FieldInfo nameField = FindField(villager.GetType(), "m_villagerName");
        return nameField != null ? nameField.GetValue(villager) as string : null;
    }

    private static object FindChildLabel(object children, string name)
    {
        if (children == null)
            return null;

        foreach (MethodInfo method in children.GetType().GetMethods(InstanceFlags))
        {
            if (method.Name != "Find" || !method.IsGenericMethodDefinition ||
                method.GetParameters().Length != 1 ||
                method.GetParameters()[0].ParameterType != typeof(string))
                continue;

            return method.MakeGenericMethod(typeof(LabelWidget))
                .Invoke(children, new object[] { name });
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
