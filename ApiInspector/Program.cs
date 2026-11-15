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
    "ComponentCow", "Cow", "Cattle", "Milk", "Bucket", "Wheat", "WheatSeeds", "Seeds", "Grain", "Crop",
    "SubsystemCreatureSpawn", "CreatureType", "Attackment", "SubsystemBucketBlockBehavior", "BlocksManager", "Block", "ComponentUdder", "BodyRaycastResult", "RyeBlock", "SubsystemTime", "ModLoader", "ValuesDictionary",
    "ComponentCreature", "ComponentPlayer", "PlayerInput", "ViewWidget", "CameraWidget", "ComponentAimingSights", "Component", "Entity"
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
            type.Name.Contains("GameInfo") || type.Name.Contains("Cow") || type.Name.Contains("Cattle") ||
            type.Name.Contains("BucketBlock") || type.Name.Contains("BlocksManager") || type.Name == "Block" ||
            type.Name.Contains("Wheat") || type.Name.Contains("Seed") || type.Name.Contains("Grain") ||
            type.Name.Contains("Crop") || type.Name.Contains("Spawn") || type.Name == "ComponentPlayer" ||
            type.Name == "ModLoader" || type.Name.Contains("Udder") || type.Name == "SubsystemTime")
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
            type.Name.Contains("GameInfo") || type.Name.Contains("Cow") || type.Name.Contains("Cattle") ||
            type.Name.Contains("BucketBlock") || type.Name.Contains("BlocksManager") || type.Name == "Block" ||
            type.Name.Contains("Wheat") || type.Name.Contains("Seed") || type.Name.Contains("Grain") ||
            type.Name.Contains("Crop") || type.Name.Contains("Spawn") ||
            (type.Name == "Component" && method.Name == "Save"))
        {
            string parameters = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
            output.AppendLine("METHOD " + method.ReturnType.Name + " " + method.Name + "(" + parameters + ")");
        }
    }
}
foreach (Type type in loaded.SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); } })
    .Where(t => t.IsEnum && t.Name == "Season"))
{
    output.AppendLine();
    output.AppendLine("===== ENUM " + type.FullName + " =====");
    foreach (var name in Enum.GetNames(type)) output.AppendLine("VALUE " + name + " = " + Convert.ToInt32(Enum.Parse(type, name)));
}

var allTypes = loaded.SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); } }).ToList();
foreach (Type type in allTypes.Where(t =>
    new[] { "IInventory", "Inventory", "RaycastMode", "RaycastResult", "BodyRaycastResult", "ComponentBody", "ComponentPlayer", "ComponentMiner", "ComponentCreature", "ComponentUdder", "RyeBlock", "SubsystemTime", "SubsystemSeasons", "ModLoader", "ValuesDictionary", "Project", "SubsystemInput", "ComponentInput", "PlayerInput", "ViewWidget", "CameraWidget", "ComponentAimingSights", "SubsystemBucketBlockBehavior", "EmptyBucketBlock", "MilkBucketBlock", "SeedsBlock", "WheatBlock" }
    .Contains(t.Name)).OrderBy(t => t.FullName))
{
    output.AppendLine();
    output.AppendLine("===== FOCUSED " + type.FullName + " =====");
    const BindingFlags ff = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var p in type.GetProperties(ff).OrderBy(p => p.Name))
        output.AppendLine("PROP " + p.PropertyType.FullName + " " + p.Name + " get=" + (p.GetMethod != null) + " set=" + (p.SetMethod != null));
    foreach (var f in type.GetFields(ff).OrderBy(f => f.Name))
        output.AppendLine("FIELD " + f.FieldType.FullName + " " + f.Name);
    foreach (var m in type.GetMethods(ff).OrderBy(m => m.Name))
        output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ")");
    if (type.IsEnum)
        foreach (var name in Enum.GetNames(type)) output.AppendLine("VALUE " + name + "=" + Convert.ToInt32(Enum.Parse(type, name)));
}

var opcodeMap = new Dictionary<short, System.Reflection.Emit.OpCode>();
foreach (var field in typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
{
    if (field.GetValue(null) is System.Reflection.Emit.OpCode op) opcodeMap[op.Value] = op;
}
void DumpIl(string typeName, string methodName)
{
    Type type = allTypes.FirstOrDefault(t => t.FullName == typeName);
    if (type == null) { output.AppendLine("IL MISSING TYPE " + typeName); return; }
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => m.Name == methodName))
    {
        output.AppendLine();
        output.AppendLine("===== IL " + typeName + "." + methodName + " =====");
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il == null) { output.AppendLine("NO BODY"); continue; }
        int pos = 0;
        while (pos < il.Length)
        {
            int offset = pos;
            short code = il[pos++];
            if (code == 0xFE && pos < il.Length) code = (short)(0xFE00 | il[pos++]);
            if (!opcodeMap.TryGetValue(code, out var op)) { output.AppendLine(offset.ToString("X4") + ": <unknown>"); break; }
            string operand = "";
            try
            {
                switch (op.OperandType)
                {
                    case System.Reflection.Emit.OperandType.InlineNone: break;
                    case System.Reflection.Emit.OperandType.ShortInlineI: operand = ((sbyte)il[pos++]).ToString(); break;
                    case System.Reflection.Emit.OperandType.InlineI: operand = BitConverter.ToInt32(il, pos).ToString(); pos += 4; break;
                    case System.Reflection.Emit.OperandType.InlineI8: operand = BitConverter.ToInt64(il, pos).ToString(); pos += 8; break;
                    case System.Reflection.Emit.OperandType.ShortInlineR: operand = BitConverter.ToSingle(il, pos).ToString(); pos += 4; break;
                    case System.Reflection.Emit.OperandType.InlineR: operand = BitConverter.ToDouble(il, pos).ToString(); pos += 8; break;
                    case System.Reflection.Emit.OperandType.ShortInlineVar: operand = il[pos++].ToString(); break;
                    case System.Reflection.Emit.OperandType.InlineVar: operand = BitConverter.ToUInt16(il, pos).ToString(); pos += 2; break;
                    case System.Reflection.Emit.OperandType.ShortInlineBrTarget: { sbyte d=(sbyte)il[pos++]; operand="IL_"+(pos+d).ToString("X4"); break; }
                    case System.Reflection.Emit.OperandType.InlineBrTarget: { int d=BitConverter.ToInt32(il,pos); pos+=4; operand="IL_"+(pos+d).ToString("X4"); break; }
                    case System.Reflection.Emit.OperandType.InlineSwitch: { int n=BitConverter.ToInt32(il,pos); pos+=4; var ds=new int[n]; for(int k=0;k<n;k++){ds[k]=BitConverter.ToInt32(il,pos);pos+=4;} operand="switch("+string.Join(",",ds.Select(d=>"IL_"+(pos+d).ToString("X4")))+")"; break; }
                    case System.Reflection.Emit.OperandType.InlineString: { int tok=BitConverter.ToInt32(il,pos);pos+=4;operand=method.Module.ResolveString(tok);break; }
                    case System.Reflection.Emit.OperandType.InlineMethod: { int tok=BitConverter.ToInt32(il,pos);pos+=4;operand=method.Module.ResolveMethod(tok)?.ToString() ?? tok.ToString("X8");break; }
                    case System.Reflection.Emit.OperandType.InlineField: { int tok=BitConverter.ToInt32(il,pos);pos+=4;operand=method.Module.ResolveField(tok)?.ToString() ?? tok.ToString("X8");break; }
                    case System.Reflection.Emit.OperandType.InlineType: { int tok=BitConverter.ToInt32(il,pos);pos+=4;operand=method.Module.ResolveType(tok)?.FullName ?? tok.ToString("X8");break; }
                    case System.Reflection.Emit.OperandType.InlineTok: { int tok=BitConverter.ToInt32(il,pos);pos+=4;operand=method.Module.ResolveMember(tok)?.ToString() ?? tok.ToString("X8");break; }
                    default: operand = "operandType="+op.OperandType; break;
                }
            }
            catch (Exception ex) { operand += " [decode "+ex.GetType().Name+"]"; }
            output.AppendLine(offset.ToString("X4") + ": " + op.Name + (operand.Length>0 ? " " + operand : ""));
        }
    }
}
DumpIl("Game.SubsystemBucketBlockBehavior", "OnUse");
DumpIl("Game.ComponentUdder", "Milk");
DumpIl("Game.ComponentUdder", "Load");
DumpIl("Game.ComponentMiner", "Hit");
DumpIl("Game.ComponentMiner", "<Raycast>b__0");
DumpIl("Game.ComponentMiner", "<Raycast>b__1");
DumpIl("Game.ComponentMiner", "<DoMeleeHit>b__0");
DumpIl("Game.ComponentMiner", "<DoMeleeHit>b__1");
DumpIl("Game.ComponentPlayer", "Update");
DumpIl("Game.ComponentInput", "Update");
DumpIl("Game.ComponentInput", "OnHit");
DumpIl("Game.ComponentInput", "Hit");
DumpIl("Game.ComponentHealth", "Load");
DumpIl("Game.Injury", "Process");
DumpIl("Game.ComponentMiner", "Use");
DumpIl("Game.ComponentMiner", "Raycast");
DumpIl("Game.ComponentMiner", "DoMeleeHit");
DumpIl("Game.ComponentMiner", "AttackBody");
DumpIl("Game.Attackment", "ProcessAttackment");
DumpIl("Game.Attackment", "ProcessAttackmentToCreature");
DumpIl("Game.Attackment", "DisableFriendlyFire");
DumpIl("Game.ComponentHealth", "Injure");
DumpIl("Game.ComponentHealth", "Injure");
DumpIl("Game.ComponentHealth", "ApplyDamage");
DumpIl("Game.SubsystemCreatureSpawn", "InitializeCreatureTypesFromDatabase");
DumpIl("Game.SubsystemCreatureSpawn", "InitializeCreatureTypes");
DumpIl("Game.SubsystemCreatureSpawn", ".cctor");
DumpIl("Game.SubsystemCreatureSpawn+CreatureType", ".ctor");
DumpIl("Game.SubsystemCreatureSpawn", "CalculateSpawnSuitability");
DumpIl("Game.SubsystemCreatureSpawn", "SpawnCreature");


var inspectNames = new HashSet<string>(StringComparer.Ordinal)
{
    "Game.ComponentMiner", "Game.ComponentPlayer", "Game.PlayerInput", "Game.ComponentInput", "Game.ModLoader", "Game.Attackment",
    "Game.SubsystemCreatureSpawn+CreatureType", "Game.SpawnLocationType",
    "Game.CreatureCategory", "Game.SubsystemBodies", "Game.ComponentBody"
};
foreach (Type type in allTypes.Where(t => inspectNames.Contains(t.FullName)).OrderBy(t => t.FullName))
{
    output.AppendLine();
    output.AppendLine("===== DEEP TYPE " + type.FullName + " =====");
    const BindingFlags deep = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(deep))
        output.AppendLine("CTOR " + ctor + " params=" + string.Join(", ", ctor.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + " public=" + ctor.IsPublic);
    foreach (var p in type.GetProperties(deep).OrderBy(p => p.Name))
        output.AppendLine("PROP " + p.PropertyType.FullName + " " + p.Name + " get=" + (p.GetMethod?.IsPublic ?? false) + " set=" + (p.SetMethod?.IsPublic ?? false));
    foreach (var f in type.GetFields(deep).OrderBy(f => f.Name))
        output.AppendLine("FIELD " + f.FieldType.FullName + " " + f.Name + " public=" + f.IsPublic);
    foreach (var m in type.GetMethods(deep).OrderBy(m => m.Name))
        output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" +
            string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) +
            ") public=" + m.IsPublic + " virtual=" + m.IsVirtual + " final=" + m.IsFinal);
}
DumpIl("Game.Attackment", "CalculateInjuryAmount");
DumpIl("Game.Injury", "Process");
DumpIl("Game.SubsystemCreatureSpawn", "SpawnRandomCreature");
DumpIl("Game.SubsystemCreatureSpawn", "TestSpawnPoint");
DumpIl("Game.SubsystemCreatureSpawn", "SpawnCreatures");
DumpIl("Game.ComponentMiner", "GetTargetingRay");
DumpIl("Game.SubsystemCreatureSpawn", "<InitializeCreatureTypes>b__33_9");
DumpIl("Game.SubsystemCreatureSpawn", "<InitializeCreatureTypes>b__33_10");
DumpIl("Game.SubsystemCreatureSpawn", "<InitializeCreatureTypes>b__33_11");
DumpIl("Game.SubsystemCreatureSpawn", "<InitializeCreatureTypes>b__33_12");


Type modLoaderType = allTypes.FirstOrDefault(t => t.FullName == "Game.ModLoader");
if (modLoaderType != null)
{
    var method = modLoaderType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
        .FirstOrDefault(m => m.Name == "OnPlayerInputHit");
    if (method != null)
    {
        output.AppendLine();
        output.AppendLine("===== ON PLAYER INPUT HIT PARAMETER METADATA =====");
        foreach (var p in method.GetParameters())
            output.AppendLine($"PARAM {p.Position} {p.Name} type={p.ParameterType.FullName} in={p.IsIn} out={p.IsOut} required={string.Join(",", p.GetRequiredCustomModifiers().Select(t=>t.FullName))} optional={string.Join(",", p.GetOptionalCustomModifiers().Select(t=>t.FullName))}");
    }
}
DumpIl("Game.ModsManager", "RegisterHook");
DumpIl("Game.ModsManager", "HookAction");

string result = output.ToString();
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "api-inspection.txt"), result);
Console.WriteLine(result);
