using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Engine;
using HarmonyLib;

namespace Game;

/// <summary>
/// Late-binds to LivingTownsTradeWidget after its assembly loads.
/// The constructor postfix changes the actual ChestLabel after the native
/// inventory layout has been created. TryOpen postfix is a second safety net.
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

        MethodInfo constructorPostfix = typeof(VillagerInventoryNamePatch).GetMethod(
            nameof(ConstructorPostfix), StaticFlags);
        MethodInfo tryOpenPostfix = typeof(VillagerInventoryNamePatch).GetMethod(
            nameof(TryOpenPostfix), StaticFlags);

        int constructorCount = 0;
        foreach (ConstructorInfo constructor in widgetType.GetConstructors(InstanceFlags))
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            if (parameters.Length != 4 ||
                parameters[0].ParameterType.FullName != "Game.ComponentPlayer" ||
                parameters[1].ParameterType.FullName != "Game.ComponentLivingTownsVillager")
                continue;

            if (s_patched.Contains(constructor))
                continue;

            s_harmony.Patch(constructor, postfix: new HarmonyMethod(constructorPostfix));
            s_patched.Add(constructor);
            constructorCount++;
        }

        int tryOpenCount = 0;
        foreach (MethodInfo method in widgetType.GetMethods(StaticFlags))
        {
            if (method.Name != "TryOpen" || method.ReturnType != typeof(bool))
                continue;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 2 ||
                parameters[0].ParameterType.FullName != "Game.ComponentPlayer" ||
                parameters[1].ParameterType.FullName != "Game.ComponentLivingTownsVillager")
                continue;

            if (s_patched.Contains(method))
                continue;

            s_harmony.Patch(method, postfix: new HarmonyMethod(tryOpenPostfix));
            s_patched.Add(method);
            tryOpenCount++;
        }

        if (constructorCount + tryOpenCount > 0)
            Log.Information("[KoylulerModu] Inventory name hook attached: constructors=" +
                constructorCount + ", TryOpen=" + tryOpenCount + ".");
    }

    private static void ConstructorPostfix(object __instance)
    {
        try
        {
            if (__instance == null)
                return;

            object villager = GetMemberValue(__instance, "m_villager");
            ApplyNameToInventoryPanel(__instance, villager);
        }
        catch (Exception ex)
        {
            Log.Error("[KoylulerModu] Inventory constructor name update failed: " + ex);
        }
    }

    private static void TryOpenPostfix(object[] __args, bool __result)
    {
        try
        {
            if (!__result || __args == null || __args.Length < 2)
                return;

            object player = __args[0];
            object villager = __args[1];
            object gui = GetMemberValue(player, "ComponentGui");
            object panel = GetMemberValue(gui, "ModalPanelWidget");
            ApplyNameToInventoryPanel(panel, villager);
        }
        catch (Exception ex)
        {
            Log.Error("[KoylulerModu] TryOpen name update failed: " + ex);
        }
    }

    private static void ApplyNameToInventoryPanel(object panel, object villager)
    {
        if (panel == null || villager == null)
            return;

        string name = GetOrCreateVillagerName(villager);
        if (string.IsNullOrWhiteSpace(name))
        {
            Log.Warning("[KoylulerModu] Inventory panel has no readable villager identity.");
            return;
        }

        object children = GetMemberValue(panel, "Children");
        object label = FindChildLabel(children, "ChestLabel");
        if (label == null)
        {
            Log.Warning("[KoylulerModu] Inventory panel ChestLabel was not found.");
            return;
        }

        PropertyInfo text = label.GetType().GetProperty("Text", InstanceFlags);
        if (text == null || text.PropertyType != typeof(string) || !text.CanWrite)
        {
            Log.Warning("[KoylulerModu] Inventory ChestLabel.Text is not writable.");
            return;
        }

        string title = name + " – Envanteri";
        text.SetValue(label, title, null);
        Log.Information("[KoylulerModu] Villager inventory title set: " + title);
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
