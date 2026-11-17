using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using Engine;

namespace Game;

/// <summary>
/// Returns the villager's actual saved name for the trade stock label.
/// If an older villager has no name yet, assigns a stable Köylü### fallback
/// directly to LivingTowns' persisted m_villagerName field.
/// </summary>
public static class VillagerInventoryNameBridge
{
    private static int s_highestNumber;
    private static readonly object s_lock = new object();
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static string ResolveStockLabel(object villager)
    {
        if (villager == null)
            return "Köylü";

        FieldInfo nameField = FindField(villager.GetType(), "m_villagerName");
        string name = nameField != null ? nameField.GetValue(villager) as string : null;

        if (string.IsNullOrWhiteSpace(name))
            name = TryReadIdentityComponent(villager);

        if (string.IsNullOrWhiteSpace(name))
            name = AllocateName();

        if (nameField != null)
        {
            try
            {
                nameField.SetValue(villager, name);
            }
            catch (Exception ex)
            {
                Log.Warning("[KoylulerModu] Villager name could not be written back to LivingTowns: " + ex.Message);
            }
        }

        UpdateCounterFromName(name);
        Log.Information("[KoylulerModu] Trade-stock header text: " + name);
        return name;
    }

    private static string TryReadIdentityComponent(object villager)
    {
        object entity = GetMemberValue(villager, "Entity");
        if (entity == null)
            return null;

        object components = GetMemberValue(entity, "Components");
        if (!(components is IEnumerable enumerable))
            return null;

        foreach (object component in enumerable)
        {
            if (component == null || component.GetType().FullName != "Game.ComponentKoyluIdentity")
                continue;

            MethodInfo ensure = component.GetType().GetMethod(
                "EnsureNameNow", Flags);
            if (ensure == null)
                continue;

            string value = ensure.Invoke(component, null) as string;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string AllocateName()
    {
        lock (s_lock)
        {
            s_highestNumber++;
            return "Köylü" + s_highestNumber.ToString("000", CultureInfo.InvariantCulture);
        }
    }

    private static void UpdateCounterFromName(string name)
    {
        if (string.IsNullOrEmpty(name) || !name.StartsWith("Köylü", StringComparison.Ordinal))
            return;

        int parsed;
        if (!int.TryParse(name.Substring("Köylü".Length), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out parsed))
            return;

        lock (s_lock)
        {
            if (parsed > s_highestNumber)
                s_highestNumber = parsed;
        }
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(name, Flags | BindingFlags.DeclaredOnly);
            if (field != null && field.FieldType == typeof(string))
                return field;
        }
        return null;
    }

    private static object GetMemberValue(object target, string name)
    {
        if (target == null)
            return null;

        PropertyInfo property = target.GetType().GetProperty(name, Flags);
        if (property != null && property.GetIndexParameters().Length == 0)
            return property.GetValue(target, null);

        FieldInfo field = FindField(target.GetType(), name);
        if (field != null)
            return field.GetValue(target);

        return null;
    }
}
