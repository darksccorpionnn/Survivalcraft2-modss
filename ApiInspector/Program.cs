using System.Reflection;
using System.Text;

var output = new StringBuilder();
var loaded = new List<Assembly>();
foreach (string path in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
{
    try { loaded.Add(Assembly.LoadFrom(path)); }
    catch (Exception ex) { output.AppendLine($"LOAD ERROR {Path.GetFileName(path)}: {ex.GetType().Name} {ex.Message}"); }
}
output.AppendLine("LOADED ASSEMBLIES:");
foreach (Assembly assembly in loaded) output.AppendLine(" - " + assembly.GetName().Name);

var wanted = new[]
{
    "ComponentMiner", "SubsystemSeasons", "WorldSettings", "SubsystemGameInfo",
    "ComponentCow", "Cow", "Milk", "Bucket", "Wheat", "SubsystemCreatureSpawn",
    "ComponentCreature", "ComponentPlayer", "Component", "Entity"
};
var types = new List<Type>();
foreach (Assembly assembly in loaded)
{
    try { types.AddRange(assembly.GetTypes().Where(t => wanted.Any(w => t.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))); }
    catch (ReflectionTypeLoadException ex) { types.AddRange(ex.Types.Where(t => t != null && wanted.Any(w => t.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))); }
}
foreach (Type type in types.OrderBy(t => t.FullName))
{
    output.AppendLine();
    output.AppendLine("===== TYPE " + type.FullName + " | " + type.Assembly.GetName().Name + " =====");
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var property in type.GetProperties(flags).OrderBy(p => p.Name))
    {
        if (wanted.Any(w => property.Name.Contains(w, StringComparison.OrdinalIgnoreCase)) ||
            type.Name.Contains("Miner") || type.Name.Contains("Seasons") || type.Name.Contains("WorldSettings") ||
            type.Name.Contains("GameInfo") || type.Name.Contains("Cow") || type.Name.Contains("Spawn"))
            output.AppendLine("PROP " + property.PropertyType.Name + " " + property.Name +
                " get=" + (property.GetMethod != null) + " set=" + (property.SetMethod != null));
    }
    foreach (var field in type.GetFields(flags).OrderBy(f => f.Name))
    {
        if (wanted.Any(w => field.Name.Contains(w, StringComparison.OrdinalIgnoreCase)) ||
            type.Name.Contains("Miner") || type.Name.Contains("Seasons") || type.Name.Contains("WorldSettings") ||
            type.Name.Contains("GameInfo") || type.Name.Contains("Cow") || type.Name.Contains("Spawn"))
            output.AppendLine("FIELD " + field.FieldType.Name + " " + field.Name);
    }
    foreach (var method in type.GetMethods(flags).OrderBy(m => m.Name))
    {
        if (wanted.Any(w => method.Name.Contains(w, StringComparison.OrdinalIgnoreCase)) ||
            type.Name.Contains("Miner") || type.Name.Contains("Seasons") || type.Name.Contains("WorldSettings") ||
            type.Name.Contains("GameInfo") || type.Name.Contains("Cow") || type.Name.Contains("Spawn") ||
            (type.Name == "Component" && method.Name == "Save"))
        {
            string parameters = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
            output.AppendLine("METHOD " + method.ReturnType.Name + " " + method.Name + "(" + parameters + ")");
        }
    }
}
string result = output.ToString();
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "api-inspection.txt"), result);
Console.WriteLine(result);
