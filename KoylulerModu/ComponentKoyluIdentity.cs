using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

/// <summary>
/// Gives each villager a stable Turkish identity and mirrors it into the
/// Living Towns controller. The name is shown in the villager inventory title,
/// not as a floating world label.
/// </summary>
public sealed class ComponentKoyluIdentity : Component, IUpdateable
{
    private static int s_highestAssignedNumber;
    private string m_name;
    private bool m_applied;
    private float m_retryTimer;

    public UpdateOrder UpdateOrder => UpdateOrder.Default;
    public string Name => m_name;

    public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap)
    {
        base.Load(valuesDictionary, idToEntityMap);
        m_name = valuesDictionary.GetValue<string>("Name", null);
        int savedNumber = ParseNumber(m_name);
        if (savedNumber > s_highestAssignedNumber)
            s_highestAssignedNumber = savedNumber;
    }

    public override void Save(ValuesDictionary valuesDictionary, EntityToIdMap entityToIdMap)
    {
        base.Save(valuesDictionary, entityToIdMap);
        if (!string.IsNullOrEmpty(m_name))
            valuesDictionary.SetValue("Name", m_name);
    }

    public void Update(float dt)
    {
        if (m_applied)
            return;
        m_retryTimer -= dt;
        if (m_retryTimer > 0f)
            return;
        m_retryTimer = 0.5f;
        ApplyNameWhenComponentsAreReady();
    }

    /// <summary>
    /// Ensures a persistent Köylü### identity immediately, even if the
    /// villager inventory is opened before the first scheduled component update.
    /// </summary>
    public string EnsureNameNow()
    {
        object controller = FindComponentByTypeName("Game.ComponentLivingTownsVillager");

        // Always create the identity independently of controller readiness.
        // This makes the inventory title available even if LivingTowns attaches late.
        if (string.IsNullOrEmpty(m_name))
        {
            string storedName = controller != null ? ReadControllerName(controller) : null;
            if (ParseNumber(storedName) > 0)
                m_name = storedName;
            else
            {
                s_highestAssignedNumber++;
                m_name = FormatName(s_highestAssignedNumber);
            }
        }

        int number = ParseNumber(m_name);
        if (number > s_highestAssignedNumber)
            s_highestAssignedNumber = number;

        if (controller != null)
        {
            bool controllerUpdated = SetStringMember(controller, "m_villagerName", "Name", m_name);
            if (controllerUpdated && !m_applied)
            {
                m_applied = true;
                Log.Information("[KoylulerModu] Villager identity assigned: " + m_name +
                    " (saved on villager; displayed in inventory title only).");
            }
        }

        return m_name;
    }

    private void ApplyNameWhenComponentsAreReady()
    {
        EnsureNameNow();
    }

    private static string FormatName(int number)
        => "Köylü" + number.ToString("000", CultureInfo.InvariantCulture);

    private static int ParseNumber(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith("Köylü", StringComparison.Ordinal))
            return 0;
        int number;
        return int.TryParse(value.Substring("Köylü".Length), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out number) ? number : 0;
    }

    private static string ReadControllerName(object controller)
    {
        FieldInfo field = FindStringField(controller.GetType(), "m_villagerName");
        if (field != null)
            return field.GetValue(controller) as string;
        PropertyInfo property = controller.GetType().GetProperty("Name",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property != null && property.PropertyType == typeof(string)
            ? property.GetValue(controller, null) as string : null;
    }

    private object FindComponentByTypeName(string fullName)
    {
        if (Entity == null)
            return null;
        Type componentType = null;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            componentType = assembly.GetType(fullName, throwOnError: false);
            if (componentType != null)
                break;
        }
        if (componentType == null)
            return null;

        foreach (MethodInfo method in Entity.GetType().GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.Name != "FindComponent" || !method.IsGenericMethodDefinition ||
                method.GetGenericArguments().Length != 1)
                continue;
            MethodInfo generic = method.MakeGenericMethod(componentType);
            ParameterInfo[] parameters = generic.GetParameters();
            if (parameters.Length == 0)
                return generic.Invoke(Entity, null);
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                return generic.Invoke(Entity, new object[] { false });
        }

        PropertyInfo componentsProperty = Entity.GetType().GetProperty("Components",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        IEnumerable components = componentsProperty != null
            ? componentsProperty.GetValue(Entity, null) as IEnumerable : null;
        if (components != null)
            foreach (object component in components)
                if (component != null && component.GetType() == componentType)
                    return component;
        return null;
    }

    private static bool SetStringMember(object target, string fieldName, string propertyName, string value)
    {
        if (target == null)
            return false;
        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.PropertyType == typeof(string) && property.CanWrite)
        {
            property.SetValue(target, value, null);
            return true;
        }
        FieldInfo field = FindStringField(type, fieldName);
        if (field == null)
            return false;
        field.SetValue(target, value);
        return true;
    }

    private static FieldInfo FindStringField(Type type, string preferredName)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo exact = current.GetField(preferredName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (exact != null && exact.FieldType == typeof(string))
                return exact;
            foreach (FieldInfo field in current.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (field.FieldType == typeof(string) &&
                    field.Name.IndexOf(preferredName.Replace("m_", ""), StringComparison.OrdinalIgnoreCase) >= 0)
                    return field;
        }
        return null;
    }
}
