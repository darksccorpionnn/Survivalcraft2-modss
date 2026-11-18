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
    "ComponentCreature", "ComponentPlayer", "ComponentPathfinding", "PathfindingResult", "ComponentBody", "ComponentLocomotion", "PlayerInput", "ViewWidget", "CameraWidget", "ComponentAimingSights", "Component", "Entity"
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


foreach (string hookName in new[] { "OnMinerHit", "OnMinerHit2", "OnPlayerInputHit" })
{
    MethodInfo hook = modLoaderType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
        .FirstOrDefault(m => m.Name == hookName);
    if (hook == null) continue;
    output.AppendLine();
    output.AppendLine("===== PARAMETER METADATA " + hookName + " =====");
    foreach (ParameterInfo p in hook.GetParameters())
        output.AppendLine($"PARAM {p.Position} {p.Name} type={p.ParameterType.FullName} in={p.IsIn} out={p.IsOut} required={string.Join(",", p.GetRequiredCustomModifiers().Select(t => t.FullName))} optional={string.Join(",", p.GetOptionalCustomModifiers().Select(t => t.FullName))}");
}


foreach (Type parent in allTypes.Where(t => t.FullName == "Game.ComponentMiner" || t.FullName == "Game.ComponentPlayer"))
{
    output.AppendLine();
    output.AppendLine("===== NESTED TYPES " + parent.FullName + " =====");
    foreach (Type nested in parent.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
    {
        output.AppendLine("NESTED " + nested.FullName);
        const BindingFlags nf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (MethodInfo method in nested.GetMethods(nf).OrderBy(m => m.Name))
        {
            output.AppendLine("NESTED_METHOD " + method.ReturnType.FullName + " " + method.Name + "(" +
                string.Join(", ", method.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ")");
            if (method.Name.Contains("DoMeleeHit", StringComparison.Ordinal) ||
                method.Name.Contains("Update>b__8", StringComparison.Ordinal))
                DumpIl(nested.FullName, method.Name);
        }
    }
}


// Focused HUD and touch-widget API inspection for proximity interaction buttons.
var widgetTypeNames = new HashSet<string>(StringComparer.Ordinal)
{
    "Widget", "ContainerWidget", "CanvasWidget", "ButtonWidget", "BevelledButtonWidget",
    "LabelWidget", "TextWidget", "StackPanelWidget", "GridPanelWidget", "TouchInputWidget",
    "ViewWidget", "GameWidget", "SubsystemGameWidgets", "ComponentGui", "ComponentInput",
    "PlayerInput", "ComponentPlayer", "SubsystemInput", "Screen", "ScreensManager",
    "InventoryScreen", "InventoryWidget", "Dialog", "BitmapButtonWidget"
};
foreach (Type type in allTypes.Where(t => widgetTypeNames.Contains(t.Name)).OrderBy(t => t.FullName))
{
    output.AppendLine();
    output.AppendLine("===== HUD WIDGET TYPE " + type.FullName + " =====");
    const BindingFlags wf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(wf))
        output.AppendLine("CTOR " + ctor + " public=" + ctor.IsPublic);
    foreach (var p in type.GetProperties(wf).OrderBy(p => p.Name))
        output.AppendLine("PROP " + p.PropertyType.FullName + " " + p.Name + " get=" + (p.GetMethod?.IsPublic ?? false) + " set=" + (p.SetMethod?.IsPublic ?? false));
    foreach (var f in type.GetFields(wf).OrderBy(f => f.Name))
        output.AppendLine("FIELD " + f.FieldType.FullName + " " + f.Name + " public=" + f.IsPublic);
    foreach (var m in type.GetMethods(wf).OrderBy(m => m.Name))
        output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ") public=" + m.IsPublic + " virtual=" + m.IsVirtual);
}


DumpIl("Game.ComponentGui", "HandleInput");
DumpIl("Game.ComponentGui", "Update");
DumpIl("Game.ComponentInput", "Update");
Type modsManagerType = allTypes.FirstOrDefault(t => t.Name == "ModsManager");
if (modsManagerType != null)
{
    output.AppendLine("\\n===== MODS MANAGER METHODS " + modsManagerType.FullName + " =====");
    const BindingFlags mmf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var method in modsManagerType.GetMethods(mmf).OrderBy(m => m.Name))
        output.AppendLine("METHOD " + method.ReturnType.FullName + " " + method.Name + "(" + string.Join(", ", method.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ") public=" + method.IsPublic + " static=" + method.IsStatic);
    foreach (var field in modsManagerType.GetFields(mmf).OrderBy(f => f.Name))
        output.AppendLine("FIELD " + field.FieldType.FullName + " " + field.Name + " public=" + field.IsPublic + " static=" + field.IsStatic);
    DumpIl(modsManagerType.FullName, "HookAction");
}


var aiTypesToInspect = new HashSet<string>(StringComparer.Ordinal)
{
    "Game.ComponentChaseBehavior", "Game.ComponentCreature", "Game.ComponentPathfinding", "Game.ComponentPilot", "Game.ComponentBody", "Game.ComponentLocomotion",
    "Game.ComponentHerdBehavior", "Game.ComponentFlyAwayBehavior",
    "Game.ComponentFindPlayerBehavior", "Game.ComponentBehaviorSelector",
    "Game.ComponentCreatureSounds", "Game.ComponentMiner"
};
foreach (Type type in allTypes.Where(t => aiTypesToInspect.Contains(t.FullName)).OrderBy(t => t.FullName))
{
    output.AppendLine();
    output.AppendLine("===== AI DEEP INSPECTION " + type.FullName + " =====");
    const BindingFlags af = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(af))
        output.AppendLine("CTOR " + ctor);
    foreach (var p in type.GetProperties(af).OrderBy(p => p.Name))
        output.AppendLine("PROP " + p.PropertyType.FullName + " " + p.Name);
    foreach (var f in type.GetFields(af).OrderBy(f => f.Name))
        output.AppendLine("FIELD " + f.FieldType.FullName + " " + f.Name);
    foreach (var m in type.GetMethods(af).OrderBy(m => m.Name))
    {
        output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" +
            string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ")");
        if (m.GetMethodBody() != null)
            DumpIl(type.FullName, m.Name);
    }
}


foreach (Type type in allTypes.Where(t =>
    t.FullName == "Game.Injury" ||
    t.FullName == "Game.ComponentHealth" ||
    t.FullName == "Game.ComponentCreature" ||
    t.FullName == "Game.MeleeAttackment" ||
    t.FullName == "Game.Attackment").OrderBy(t => t.FullName))
{
    output.AppendLine();
    output.AppendLine("===== INJURY PIPELINE DETAIL " + type.FullName + " =====");
    const BindingFlags ix = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(ix))
        output.AppendLine("CTOR " + ctor + " | " + string.Join(", ", ctor.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)));
    foreach (var p in type.GetProperties(ix).OrderBy(p => p.Name))
        output.AppendLine("PROP " + p.PropertyType.FullName + " " + p.Name + " get=" + (p.GetMethod?.IsPublic ?? false) + " set=" + (p.SetMethod?.IsPublic ?? false));
    foreach (var field in type.GetFields(ix).OrderBy(f => f.Name))
        output.AppendLine("FIELD " + field.FieldType.FullName + " " + field.Name + " public=" + field.IsPublic);
    foreach (var method in type.GetMethods(ix).OrderBy(m => m.Name))
        output.AppendLine("METHOD " + method.ReturnType.FullName + " " + method.Name + "(" +
            string.Join(", ", method.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ")");
}
DumpIl("Game.ComponentHealth", "Update");
DumpIl("Game.ComponentHealth", "Injure");
DumpIl("Game.ComponentHealth", "ApplyDamage");
DumpIl("Game.Injury", ".ctor");
DumpIl("Game.Injury", "Process");
DumpIl("Game.MeleeAttackment", "ProcessAttackment");
DumpIl("Game.Attackment", "ProcessAttackment");
DumpIl("Game.Attackment", "ProcessAttackmentToCreature");

// Flat Nature: inspect the real world/terrain generation API before implementing any patches.
var worldTypeTokens = new[]
{
    "WorldGenerator", "TerrainGenerator", "TerrainContentsGenerator", "WorldSettings",
    "SubsystemTerrain", "TerrainChunk", "TerrainContents", "Flat", "Biome",
    "WorldInfo", "GameInfo", "WorldsManager", "WorldDirectory", "Chunk",
    "Lake", "Ocean", "River", "Heightmap", "TerrainSerializer", "BlockGeometryGenerator"
};
var worldMemberTokens = new[]
{
    "Generate", "Initialize", "Create", "Load", "Terrain", "World", "Chunk",
    "Flat", "Biome", "Height", "Contents", "Ocean", "Lake", "River",
    "Mountain", "Surface", "Column", "Cell", "Block", "Generator", "Seed"
};
var worldCandidates = allTypes
    .Where(t => worldTypeTokens.Any(token => t.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
    .OrderBy(t => t.FullName)
    .ToList();
output.AppendLine();
output.AppendLine("===== FLAT NATURE WORLD GENERATION API CANDIDATES =====");
output.AppendLine("Candidate type count: " + worldCandidates.Count);
foreach (Type type in worldCandidates)
{
    output.AppendLine();
    output.AppendLine("===== WORLD TYPE " + type.FullName + " | " + type.Assembly.GetName().Name + " =====");
    output.AppendLine("BASE " + (type.BaseType?.FullName ?? "<none>"));
    output.AppendLine("INTERFACES " + string.Join(", ", type.GetInterfaces().Select(i => i.FullName)));
    const BindingFlags worldFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(worldFlags))
        output.AppendLine("CTOR " + ctor + " public=" + ctor.IsPublic);
    foreach (var property in type.GetProperties(worldFlags).OrderBy(p => p.Name))
    {
        if (worldMemberTokens.Any(token => property.Name.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
            worldTypeTokens.Any(token => property.PropertyType.Name.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
            type.Name.Contains("Generator", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("WorldSettings", StringComparison.OrdinalIgnoreCase))
            output.AppendLine("PROP " + property.PropertyType.FullName + " " + property.Name +
                " get=" + (property.GetMethod?.IsPublic ?? false) + " set=" + (property.SetMethod?.IsPublic ?? false));
    }
    foreach (var field in type.GetFields(worldFlags).OrderBy(f => f.Name))
    {
        if (worldMemberTokens.Any(token => field.Name.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
            worldTypeTokens.Any(token => field.FieldType.Name.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
            type.Name.Contains("Generator", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("WorldSettings", StringComparison.OrdinalIgnoreCase))
            output.AppendLine("FIELD " + field.FieldType.FullName + " " + field.Name + " public=" + field.IsPublic);
    }
    foreach (var method in type.GetMethods(worldFlags).OrderBy(m => m.Name))
    {
        bool coreType = type.Name.Contains("Generator", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("WorldSettings", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("TerrainChunk", StringComparison.OrdinalIgnoreCase) ||
            type.Name == "SubsystemTerrain" || type.Name == "Terrain";
        bool relevant = worldMemberTokens.Any(token => method.Name.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
            worldTypeTokens.Any(token => method.ReturnType.Name.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
            method.GetParameters().Any(p => worldTypeTokens.Any(token =>
                p.ParameterType.Name.Contains(token, StringComparison.OrdinalIgnoreCase)));
        if (coreType || relevant)
            output.AppendLine("METHOD " + method.ReturnType.FullName + " " + method.Name + "(" +
                string.Join(", ", method.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) +
                ") public=" + method.IsPublic + " static=" + method.IsStatic + " virtual=" + method.IsVirtual);
    }
}

var worldSupportTypes = new[]
{
    "Game.WorldSettings", "Game.SubsystemGameInfo", "Game.SubsystemTerrain",
    "Game.Terrain", "Game.TerrainChunk", "Game.TerrainContentsGenerator",
    "Game.TerrainContentsGeneratorFlat", "Game.WorldsManager", "Game.WorldInfo",
    "Game.TerrainSerializer", "Game.BlockGeometryGenerator"
};
foreach (string typeName in worldSupportTypes)
{
    Type type = allTypes.FirstOrDefault(t => t.FullName == typeName);
    if (type == null)
    {
        output.AppendLine("WORLD SUPPORT TYPE MISSING " + typeName);
        continue;
    }
    output.AppendLine("WORLD SUPPORT TYPE FOUND " + type.FullName);
}


output.AppendLine();
output.AppendLine("===== FLAT NATURE TARGETED IL =====");
foreach (string methodName in new[]
{
    "GenerateChunkContentsPass1", "GenerateChunkContentsPass2",
    "GenerateChunkContentsPass3", "GenerateChunkContentsPass4",
    "CalculateHeight", "FindCoarseSpawnPosition"
})
    DumpIl("Game.TerrainContentsGeneratorFlat", methodName);
DumpIl("Game.WorldsManager", "CreateWorld");
DumpIl("Game.WorldsManager", "ChangeWorld");
DumpIl("Game.WorldsManager", "GetUnusedWorldDirectoryName");
DumpIl("Game.ModsManager", "RegisterHook");
DumpIl("Game.ModsManager", "HookAction");
Type generationModeType = allTypes.FirstOrDefault(t => t.Name == "TerrainGenerationMode" && t.IsEnum);
if (generationModeType != null)
{
    output.AppendLine("===== TERRAIN GENERATION MODE ENUM =====");
    foreach (string name in Enum.GetNames(generationModeType))
        output.AppendLine("VALUE " + name + "=" + Convert.ToInt32(Enum.Parse(generationModeType, name)));
}


output.AppendLine();
output.AppendLine("===== FLAT NATURE WORLD IDENTITY API =====");
foreach (Type type in allTypes.Where(t =>
    new[] { "PlayScreen", "GameManager", "GameDatabase", "Project", "GameInfo", "WorldInfo", "WorldSettings", "ModLoader", "ModsManager" }
        .Contains(t.Name) ||
    t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Any(f =>
        f.Name.Contains("WorldDirectory", StringComparison.OrdinalIgnoreCase) ||
        f.Name.Contains("DirectoryName", StringComparison.OrdinalIgnoreCase)))
    .OrderBy(t => t.FullName))
{
    output.AppendLine();
    output.AppendLine("===== IDENTITY TYPE " + type.FullName + " =====");
    const BindingFlags idFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(idFlags)) output.AppendLine("CTOR " + ctor);
    foreach (var p in type.GetProperties(idFlags).OrderBy(p => p.Name))
        if (new[] { "World", "Directory", "Project", "Name", "Info", "Game" }.Any(s => p.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
            output.AppendLine("PROP " + p.PropertyType.FullName + " " + p.Name + " get=" + (p.GetMethod?.IsPublic ?? false) + " set=" + (p.SetMethod?.IsPublic ?? false));
    foreach (var field in type.GetFields(idFlags).OrderBy(f => f.Name))
        if (new[] { "World", "Directory", "Project", "Name", "Info", "Game" }.Any(s => field.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
            output.AppendLine("FIELD " + field.FieldType.FullName + " " + field.Name + " public=" + field.IsPublic);
    foreach (var m in type.GetMethods(idFlags).OrderBy(m => m.Name))
        if (new[] { "World", "Directory", "Project", "Name", "Info", "Load", "Start", "Play", "Create" }.Any(s => m.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
            output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ") public=" + m.IsPublic + " static=" + m.IsStatic);
}
Type gameInfoType = allTypes.FirstOrDefault(t => t.FullName == "Game.SubsystemGameInfo");
if (gameInfoType != null) DumpIl(gameInfoType.FullName, "Load");
Type modsType = allTypes.FirstOrDefault(t => t.Name == "ModsManager");
if (modsType != null)
{
    output.AppendLine("===== MODS MANAGER HOOK METHODS =====");
    const BindingFlags hookFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var m in modsType.GetMethods(hookFlags).OrderBy(m => m.Name))
        output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ")");
    foreach (string name in new[] { "HookAction", "CallHook", "OnTerrainContentsGenerated", "OnLoadingFinished" })
        DumpIl(modsType.FullName, name);
}


output.AppendLine();
output.AppendLine("===== FLAT NATURE STORAGE AND TERRAIN MUTATION API =====");
foreach (Type type in allTypes.Where(t =>
    t.FullName == "Engine.Storage" ||
    t.FullName == "Game.Terrain" ||
    t.FullName == "Game.TerrainChunk" ||
    t.FullName == "Game.SubsystemTerrain" ||
    t.FullName == "Game.ModLoader" ||
    t.FullName == "Game.PlantsManager" ||
    t.FullName == "Game.BlocksManager" ||
    t.FullName == "Game.Block" ||
    t.FullName == "Game.TerrainBrush" ||
    t.FullName == "Game.TerrainBrushCell" ||
    t.FullName == "Game.WorldsManager").OrderBy(t => t.FullName))
{
    output.AppendLine();
    output.AppendLine("===== MUTATION TYPE " + type.FullName + " =====");
    const BindingFlags mf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(mf))
        output.AppendLine("CTOR " + ctor + " public=" + ctor.IsPublic);
    foreach (var p in type.GetProperties(mf).OrderBy(p => p.Name))
        output.AppendLine("PROP " + p.PropertyType.FullName + " " + p.Name + " get=" + (p.GetMethod?.IsPublic ?? false) + " set=" + (p.SetMethod?.IsPublic ?? false));
    foreach (var field in type.GetFields(mf).OrderBy(f => f.Name))
        output.AppendLine("FIELD " + field.FieldType.FullName + " " + field.Name + " public=" + field.IsPublic + " static=" + field.IsStatic);
    foreach (var m in type.GetMethods(mf).OrderBy(m => m.Name))
        output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ") public=" + m.IsPublic + " static=" + m.IsStatic + " virtual=" + m.IsVirtual);
}


output.AppendLine();
output.AppendLine("===== FLAT NATURE FINAL HOOK ORDER =====");
foreach (string typeName in new[]
{
    "Game.TerrainUpdater", "GameEntitySystem.Project", "Game.WorldsManager",
    "Game.SubsystemGameInfo", "Game.TerrainContentsGeneratorFlat"
})
{
    Type type = allTypes.FirstOrDefault(t => t.FullName == typeName);
    if (type == null) { output.AppendLine("TYPE MISSING " + typeName); continue; }
    output.AppendLine("TYPE " + type.FullName);
    const BindingFlags hf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var m in type.GetMethods(hf).OrderBy(m => m.Name))
        if (typeName == "Game.TerrainUpdater" ||
            new[] { "FindSubsystem", "GetSubsystem", "Load", "GenerateChunkContents", "UpdateChunkSingleStep", "get_WorldDirectoryName", "CreateWorld", "GetUnusedWorldDirectoryName", "OnTerrainContentsGenerated" }.Any(s => m.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
            output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ") public=" + m.IsPublic + " virtual=" + m.IsVirtual);
}
DumpIl("Game.TerrainUpdater", "UpdateChunkSingleStep");
DumpIl("Game.TerrainUpdater", "SynchronousUpdateFunction");
DumpIl("Game.TerrainUpdater", "ThreadUpdateFunction");
DumpIl("Game.SubsystemGameInfo", "Load");
DumpIl("Game.WorldsManager", "GetUnusedWorldDirectoryName");


output.AppendLine();
output.AppendLine("===== TREE CAPITATOR API INSPECTION =====");
foreach (string typeName in new[]
{
    "Game.ComponentMiner", "Game.SubsystemTerrain", "Game.Block", "Game.BlocksManager",
    "Game.AxeBlock", "Game.WoodBlock", "Game.LogBlock", "Game.Terrain",
    "Game.SubsystemParticles", "Game.SubsystemAudio", "Game.SubsystemTime"
})
{
    Type type = allTypes.FirstOrDefault(t => t.FullName == typeName);
    if (type == null) { output.AppendLine("TYPE MISSING " + typeName); continue; }
    output.AppendLine();
    output.AppendLine("===== TREE TYPE " + type.FullName + " =====");
    const BindingFlags tf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(tf)) output.AppendLine("CTOR " + ctor + " public=" + ctor.IsPublic);
    foreach (var p in type.GetProperties(tf).OrderBy(p => p.Name))
        output.AppendLine("PROP " + p.PropertyType.FullName + " " + p.Name + " getPublic=" + (p.GetMethod?.IsPublic ?? false) + " setPublic=" + (p.SetMethod?.IsPublic ?? false));
    foreach (var f in type.GetFields(tf).OrderBy(f => f.Name))
        output.AppendLine("FIELD " + f.FieldType.FullName + " " + f.Name + " public=" + f.IsPublic + " static=" + f.IsStatic);
    foreach (var m in type.GetMethods(tf).OrderBy(m => m.Name))
        output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ") public=" + m.IsPublic + " static=" + m.IsStatic + " virtual=" + m.IsVirtual);
}
foreach (string typeName in new[] { "Game.ComponentMiner", "Game.AxeBlock", "Game.Block", "Game.SubsystemTerrain" })
{
    Type type = allTypes.FirstOrDefault(t => t.FullName == typeName);
    if (type == null) continue;
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(m => new[] { "Hit", "Dig", "Destroy", "Remove", "Drop", "GetDrop", "GetTool", "GetDig", "Use", "Calculate" }.Any(n => m.Name.Contains(n, StringComparison.OrdinalIgnoreCase))))
        DumpIl(type.FullName, method.Name);
}


output.AppendLine();
output.AppendLine("===== TREE CAPITATOR FOLLOW-UP =====");
foreach (string typeName in new[] { "Game.TerrainRaycastResult", "Game.CellFace", "Game.BlockDropValue", "Game.SubsystemPickables", "Game.Pickable", "Game.IInventory", "Game.ComponentMiner" })
{
    Type type = allTypes.FirstOrDefault(t => t.FullName == typeName);
    if (type == null) { output.AppendLine("TYPE MISSING " + typeName); continue; }
    output.AppendLine("TYPE " + type.FullName);
    const BindingFlags qf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(qf)) output.AppendLine("CTOR " + ctor);
    foreach (var p in type.GetProperties(qf).OrderBy(p => p.Name)) output.AppendLine("PROP " + p.PropertyType.FullName + " " + p.Name);
    foreach (var f in type.GetFields(qf).OrderBy(f => f.Name)) output.AppendLine("FIELD " + f.FieldType.FullName + " " + f.Name);
    foreach (var m in type.GetMethods(qf).OrderBy(m => m.Name)) output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ")");
}
Type woodBase = allTypes.FirstOrDefault(t => t.FullName == "Game.WoodBlock");
if (woodBase != null)
{
    output.AppendLine("WOOD BLOCK TYPES:");
    foreach (var type in allTypes.Where(t => t != woodBase && woodBase.IsAssignableFrom(t)).OrderBy(t => t.FullName))
        output.AppendLine("WOODTYPE " + type.FullName);
}
DumpIl("Game.SubsystemTerrain", "DestroyCell");
DumpIl("Game.SubsystemTerrain", "ChangeCell");
DumpIl("Game.ModsManager", "HookAction");
DumpIl("Game.ModsManager", "RegisterHook");

string result = output.ToString();
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "api-inspection.txt"), result);
Console.WriteLine(result);

// Focused pathfinding diagnostics: expose whether this game API provides a terrain-aware route API.
foreach (string typeName in new[] { "Game.ComponentPathfinding", "Game.ComponentPilot", "Game.ComponentBody", "Game.ComponentLocomotion" })
{
    Type type = allTypes.FirstOrDefault(t => t.FullName == typeName);
    if (type == null) { output.AppendLine("PATHFINDING API TYPE MISSING " + typeName); continue; }
    output.AppendLine("\\n===== PATHFINDING API DETAIL " + type.FullName + " =====");
    const BindingFlags pf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(pf)) output.AppendLine("CTOR " + ctor);
    foreach (var p in type.GetProperties(pf).OrderBy(p => p.Name)) output.AppendLine("PROP " + p.PropertyType.FullName + " " + p.Name);
    foreach (var f in type.GetFields(pf).OrderBy(f => f.Name)) output.AppendLine("FIELD " + f.FieldType.FullName + " " + f.Name);
    foreach (var m in type.GetMethods(pf).OrderBy(m => m.Name)) output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ")");
}


// Stove model, block registration, fire, heat and rendering API inspection.
var stoveTypeNames = new HashSet<string>(StringComparer.Ordinal)
{
    "Game.BlockMesh", "Game.BlockMeshVertex", "Game.Block", "Game.FurnaceBlock",
    "Game.FireBlock", "Game.SubsystemFurnaceBlockBehavior", "Game.SubsystemFireBlockBehavior",
    "Game.SubsystemBlockBehaviors", "Game.SubsystemBlockEntities", "Game.SubsystemBlocksManager",
    "Game.BlockAllocateData", "Game.ModEntity", "Game.BlockGeometryGenerator",
    "Game.TerrainGeometry", "Game.TerrainGeometrySubset", "Game.ComponentVitalStats",
    "Game.ComponentPlayer", "Game.ComponentHealth", "Game.SubsystemTime",
    "Engine.Graphics.Texture2D", "Engine.Graphics.PrimitivesRenderer3D",
    "Engine.Graphics.TexturedBatch3D", "Engine.Graphics.VertexBuffer",
    "Engine.Graphics.Model", "Engine.Graphics.ModelMesh", "Engine.Graphics.ModelMeshPart"
};
foreach (Type type in allTypes.Where(t => stoveTypeNames.Contains(t.FullName)).OrderBy(t => t.FullName))
{
    output.AppendLine();
    output.AppendLine("===== STOVE DEEP TYPE " + type.FullName + " =====");
    output.AppendLine("BASE " + (type.BaseType?.FullName ?? "<none>"));
    output.AppendLine("INTERFACES " + string.Join(", ", type.GetInterfaces().Select(i => i.FullName)));
    const BindingFlags sf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(sf))
        output.AppendLine("CTOR " + ctor + " public=" + ctor.IsPublic);
    foreach (var p in type.GetProperties(sf).OrderBy(p => p.Name))
        output.AppendLine("PROP " + p.PropertyType.FullName + " " + p.Name + " get=" + (p.GetMethod?.IsPublic ?? false) + " set=" + (p.SetMethod?.IsPublic ?? false));
    foreach (var f in type.GetFields(sf).OrderBy(f => f.Name))
        output.AppendLine("FIELD " + f.FieldType.FullName + " " + f.Name + " public=" + f.IsPublic + " static=" + f.IsStatic);
    foreach (var m in type.GetMethods(sf).OrderBy(m => m.Name))
        output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" +
            string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) +
            ") public=" + m.IsPublic + " virtual=" + m.IsVirtual);
}
DumpIl("Game.FurnaceBlock", "DrawBlock");
DumpIl("Game.FireBlock", "DrawBlock");
DumpIl("Game.SubsystemFurnaceBlockBehavior", "OnBlockAdded");
DumpIl("Game.SubsystemFurnaceBlockBehavior", "OnBlockGenerated");
DumpIl("Game.SubsystemFireBlockBehavior", "OnBlockAdded");
DumpIl("Game.SubsystemFireBlockBehavior", "OnNeighborBlockChanged");
DumpIl("Game.BlocksManager", "InitializeBlocks");
DumpIl("Game.SubsystemBlocksManager", "CallAllocate");
DumpIl("Game.SubsystemBlocksManager", "Initialize");

// Rewrite the artifact after the appended focused stove diagnostics above.
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "api-inspection.txt"), output.ToString());
Console.WriteLine("STOVE API DIAGNOSTICS APPENDED.");


// Additional stove behavior and mesh collection internals.
foreach (string typeName in new[]
{
    "Game.SubsystemFurnaceBlockBehavior", "Game.SubsystemEntityBlockBehavior",
    "Game.ComponentBlockEntity", "Game.ComponentInventory", "Game.Inventory",
    "Game.SubsystemBlockBehavior", "Game.SubsystemFireBlockBehavior",
    "Game.SubsystemMetersBlockBehavior", "Game.BlockMesh",
    "Engine.DynamicArray\u00601", "Game.ModEntity"
})
{
    Type type = allTypes.FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);
    if (type == null) { output.AppendLine("STOVE EXTRA TYPE MISSING " + typeName); continue; }
    output.AppendLine();
    output.AppendLine("===== STOVE EXTRA TYPE " + type.FullName + " =====");
    const BindingFlags ef = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(ef)) output.AppendLine("CTOR " + ctor + " public=" + ctor.IsPublic);
    foreach (var p in type.GetProperties(ef).OrderBy(p => p.Name)) output.AppendLine("PROP " + p.PropertyType.FullName + " " + p.Name + " get=" + (p.GetMethod?.IsPublic ?? false) + " set=" + (p.SetMethod?.IsPublic ?? false));
    foreach (var f in type.GetFields(ef).OrderBy(f => f.Name)) output.AppendLine("FIELD " + f.FieldType.FullName + " " + f.Name + " public=" + f.IsPublic);
    foreach (var m in type.GetMethods(ef).OrderBy(m => m.Name)) output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ") public=" + m.IsPublic + " virtual=" + m.IsVirtual);
}
DumpIl("Game.FurnaceBlock", "Initialize");
DumpIl("Game.SubsystemFurnaceBlockBehavior", "get_HandledBlocks");
DumpIl("Game.SubsystemFurnaceBlockBehavior", "InteractBlockEntity");
DumpIl("Game.SubsystemFurnaceBlockBehavior", "Load");
DumpIl("Game.SubsystemFurnaceBlockBehavior", "AddFire");
DumpIl("Game.SubsystemFurnaceBlockBehavior", "OnBlockRemoved");
DumpIl("Game.SubsystemEntityBlockBehavior", "OnBlockAdded");
DumpIl("Game.SubsystemEntityBlockBehavior", "OnBlockRemoved");
DumpIl("Game.SubsystemEntityBlockBehavior", "Load");
DumpIl("Game.ComponentBlockEntity", "Load");
DumpIl("Game.ComponentBlockEntity", "Save");
DumpIl("Game.SubsystemBlockBehaviors", "Load");

File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "api-inspection.txt"), output.ToString());


// Furnace component, ambient player temperature, and nearby fire calculations.
foreach (string typeName in new[]
{
    "Game.ComponentFurnace", "Game.ComponentVitalStats",
    "Game.SubsystemMetersBlockBehavior", "Game.SubsystemFireBlockBehavior",
    "Game.SubsystemParticles", "Game.SubsystemAmbientSounds",
    "Game.SubsystemBlockBehaviors", "Game.ComponentBlockEntity"
})
{
    Type type = allTypes.FirstOrDefault(t => t.FullName == typeName);
    if (type == null) { output.AppendLine("STOVE TEMP TYPE MISSING " + typeName); continue; }
    output.AppendLine();
    output.AppendLine("===== STOVE TEMP TYPE " + type.FullName + " =====");
    output.AppendLine("BASE " + (type.BaseType?.FullName ?? "<none>"));
    const BindingFlags tf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(tf)) output.AppendLine("CTOR " + ctor + " public=" + ctor.IsPublic);
    foreach (var p in type.GetProperties(tf).OrderBy(p => p.Name)) output.AppendLine("PROP " + p.PropertyType.FullName + " " + p.Name + " get=" + (p.GetMethod?.IsPublic ?? false) + " set=" + (p.SetMethod?.IsPublic ?? false));
    foreach (var f in type.GetFields(tf).OrderBy(f => f.Name)) output.AppendLine("FIELD " + f.FieldType.FullName + " " + f.Name + " public=" + f.IsPublic);
    foreach (var m in type.GetMethods(tf).OrderBy(m => m.Name)) output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ") public=" + m.IsPublic + " virtual=" + m.IsVirtual);
}
DumpIl("Game.ComponentFurnace", "Update");
DumpIl("Game.ComponentFurnace", "OnEntityAdded");
DumpIl("Game.ComponentFurnace", "OnEntityRemoved");
DumpIl("Game.ComponentFurnace", "OnBlockAdded");
DumpIl("Game.ComponentFurnace", "OnBlockRemoved");
DumpIl("Game.ComponentFurnace", "SimulateThermometer");
DumpIl("Game.ComponentVitalStats", "UpdateTemperature");
DumpIl("Game.ComponentVitalStats", "get_EnvironmentTemperature");
DumpIl("Game.SubsystemMetersBlockBehavior", "GetTemperature");
DumpIl("Game.SubsystemMetersBlockBehavior", "Update");
DumpIl("Game.SubsystemFireBlockBehavior", "IsCellOnFire");
DumpIl("Game.SubsystemFireBlockBehavior", "SetCellOnFire");

File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "api-inspection.txt"), output.ToString());


// Final fuel/heat details and value mutation API.
DumpIl("Game.ComponentFurnace", "UseFuel");
DumpIl("Game.ComponentFurnace", "Load");
DumpIl("Game.ComponentFurnace", "Save");
DumpIl("Game.Block", "GetHeat");
DumpIl("Game.SubsystemMetersBlockBehavior", "GetHeat");
DumpIl("Game.SubsystemMetersBlockBehavior", "CalculateTemperature");
DumpIl("Game.ComponentBlockEntity", "set_BlockValue");
DumpIl("Game.Terrain", "ReplaceData");
DumpIl("Game.Terrain", "ExtractData");
DumpIl("Game.BlockMesh", "GenerateSidesData");
DumpIl("Game.BlockMesh", "AppendModelMeshPart");
DumpIl("Game.BlockMesh", "GetVertexData");
DumpIl("Game.BlockGeometryGenerator", "GenerateMeshVertices");

File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "api-inspection.txt"), output.ToString());


// Enumerate native smoke/fire effects available for the Stove implementation.
foreach (Type type in allTypes.Where(t =>
    t.FullName.Contains("ParticleSystem", StringComparison.OrdinalIgnoreCase) &&
    (t.FullName.Contains("Fire", StringComparison.OrdinalIgnoreCase) ||
     t.FullName.Contains("Smoke", StringComparison.OrdinalIgnoreCase) ||
     t.FullName.Contains("Steam", StringComparison.OrdinalIgnoreCase)))
    .OrderBy(t => t.FullName))
{
    output.AppendLine();
    output.AppendLine("===== STOVE PARTICLE TYPE " + type.FullName + " =====");
    const BindingFlags particleFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(particleFlags)) output.AppendLine("CTOR " + ctor + " public=" + ctor.IsPublic);
    foreach (var f in type.GetFields(particleFlags).OrderBy(f => f.Name)) output.AppendLine("FIELD " + f.FieldType.FullName + " " + f.Name + " public=" + f.IsPublic);
    foreach (var m in type.GetMethods(particleFlags).OrderBy(m => m.Name)) output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ") public=" + m.IsPublic);
}

File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "api-inspection.txt"), output.ToString());


// SmokeTrailParticleSystem constructor behavior and native call sites.
Type smokeType = allTypes.FirstOrDefault(t => t.FullName == "Game.SmokeTrailParticleSystem");
if (smokeType != null)
{
    output.AppendLine();
    output.AppendLine("===== SMOKE TRAIL CONSTRUCTOR IL =====");
    foreach (var ctor in smokeType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
    {
        output.AppendLine("CTOR " + ctor + " params=" + string.Join(", ", ctor.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)));
        var bytes = ctor.GetMethodBody()?.GetILAsByteArray();
        if (bytes != null) output.AppendLine("ILBYTES " + Convert.ToHexString(bytes));
    }
    foreach (var method in smokeType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        output.AppendLine("SMOKE METHOD " + method + " IL=" + (method.GetMethodBody()?.GetILAsByteArray() == null ? "" : Convert.ToHexString(method.GetMethodBody().GetILAsByteArray())));
}

File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "api-inspection.txt"), output.ToString());


Type colorType = allTypes.FirstOrDefault(t => t.FullName == "Engine.Color");
if (colorType != null)
{
    output.AppendLine();
    output.AppendLine("===== ENGINE COLOR API =====");
    const BindingFlags cf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in colorType.GetConstructors(cf))
        output.AppendLine("COLOR CTOR " + ctor + " public=" + ctor.IsPublic);
    foreach (var f in colorType.GetFields(cf).OrderBy(f => f.Name))
        output.AppendLine("COLOR FIELD " + f.FieldType.FullName + " " + f.Name + " public=" + f.IsPublic + " static=" + f.IsStatic);
    foreach (var p in colorType.GetProperties(cf).OrderBy(p => p.Name))
        output.AppendLine("COLOR PROP " + p.PropertyType.FullName + " " + p.Name);
}

File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "api-inspection.txt"), output.ToString());


// Focused inspection for colored torch fire particle APIs.
var particleTypeNames = new HashSet<string>(StringComparer.Ordinal)
{
    "FireParticleSystem",
    "ParticleSystemBase",
    "SubsystemParticles",
    "SubsystemTorchBlockBehavior",
    "TorchBlock"
};
foreach (Type type in allTypes.Where(t => particleTypeNames.Contains(t.Name)).OrderBy(t => t.FullName))
{
    output.AppendLine();
    output.AppendLine("===== PARTICLE/TORCH API " + type.FullName + " | BASE=" + type.BaseType?.FullName + " | SEALED=" + type.IsSealed + " =====");
    const BindingFlags particleFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(particleFlags))
        output.AppendLine("CTOR " + ctor + " public=" + ctor.IsPublic);
    foreach (var p in type.GetProperties(particleFlags).OrderBy(p => p.Name))
        output.AppendLine("PROP " + p.PropertyType.FullName + " " + p.Name + " get=" + (p.GetMethod?.IsPublic ?? false) + " set=" + (p.SetMethod?.IsPublic ?? false));
    foreach (var f in type.GetFields(particleFlags).OrderBy(f => f.Name))
        output.AppendLine("FIELD " + f.FieldType.FullName + " " + f.Name + " public=" + f.IsPublic);
    foreach (var m in type.GetMethods(particleFlags).OrderBy(m => m.Name))
        output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" +
            string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) +
            ") public=" + m.IsPublic + " virtual=" + m.IsVirtual + " final=" + m.IsFinal);
}
DumpIl("Game.SubsystemTorchBlockBehavior", "OnBlockAdded");
DumpIl("Game.SubsystemTorchBlockBehavior", "OnBlockGenerated");
DumpIl("Game.SubsystemTorchBlockBehavior", "OnBlockModified");
DumpIl("Game.SubsystemTorchBlockBehavior", "OnBlockRemoved");
DumpIl("Game.SubsystemTorchBlockBehavior", "OnNeighborBlockChanged");
DumpIl("Game.FireParticleSystem", ".ctor");

File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "api-inspection.txt"), output.ToString());


foreach (Type type in allTypes.Where(t => t.FullName == "Game.FireParticleSystem" || t.FullName.StartsWith("Game.ParticleSystem", StringComparison.Ordinal)))
{
    output.AppendLine();
    output.AppendLine("===== PARTICLE BASE DETAIL " + type.FullName + " | BASE=" + type.BaseType?.FullName + " =====");
    const BindingFlags detailFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var ctor in type.GetConstructors(detailFlags))
        output.AppendLine("CTOR " + ctor + " public=" + ctor.IsPublic);
    foreach (var f in type.GetFields(detailFlags).OrderBy(f => f.Name))
        output.AppendLine("FIELD " + f.FieldType.FullName + " " + f.Name + " public=" + f.IsPublic);
    foreach (var p in type.GetProperties(detailFlags).OrderBy(p => p.Name))
        output.AppendLine("PROP " + p.PropertyType.FullName + " " + p.Name + " get=" + (p.GetMethod?.IsPublic ?? false) + " set=" + (p.SetMethod?.IsPublic ?? false));
    foreach (var m in type.GetMethods(detailFlags).OrderBy(m => m.Name))
        output.AppendLine("METHOD " + m.ReturnType.FullName + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ") public=" + m.IsPublic + " virtual=" + m.IsVirtual);
}
DumpIl("Game.FireParticleSystem", "Draw");
DumpIl("Game.FireParticleSystem", "Simulate");
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "api-inspection.txt"), output.ToString());


Type fireParticleType = allTypes.FirstOrDefault(t => t.FullName == "Game.FireParticleSystem");
Type fireNestedParticle = fireParticleType?.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault();
if (fireNestedParticle != null)
{
    output.AppendLine();
    output.AppendLine("===== FIRE NESTED PARTICLE DETAIL " + fireNestedParticle.FullName + " | BASE=" + fireNestedParticle.BaseType?.FullName + " | PUBLIC=" + fireNestedParticle.IsPublic + " | NESTEDPUBLIC=" + fireNestedParticle.IsNestedPublic + " =====");
    const BindingFlags nestedFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var f in fireNestedParticle.GetFields(nestedFlags).OrderBy(f => f.Name))
        output.AppendLine("FIELD " + f.FieldType.FullName + " " + f.Name + " public=" + f.IsPublic);
    foreach (var p in fireNestedParticle.GetProperties(nestedFlags).OrderBy(p => p.Name))
        output.AppendLine("PROP " + p.PropertyType.FullName + " " + p.Name + " get=" + (p.GetMethod?.IsPublic ?? false) + " set=" + (p.SetMethod?.IsPublic ?? false));
    for (Type b = fireNestedParticle.BaseType; b != null; b = b.BaseType)
    {
        output.AppendLine("PARTICLE BASE " + b.FullName + " public=" + b.IsPublic + " nestedpublic=" + b.IsNestedPublic);
        foreach (var f in b.GetFields(nestedFlags).OrderBy(f => f.Name))
            output.AppendLine("BASEFIELD " + f.FieldType.FullName + " " + f.Name + " public=" + f.IsPublic);
    }
}
DumpIl("Game.SubsystemTorchBlockBehavior", "AddTorch");
DumpIl("Game.SubsystemTorchBlockBehavior", "RemoveTorch");
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "api-inspection.txt"), output.ToString());


DumpIl("Game.SubsystemTorchBlockBehavior", "AddTorch");
DumpIl("Game.SubsystemTorchBlockBehavior", "RemoveTorch");
DumpIl("Game.TorchBlock", "GetPlacementValue");
DumpIl("Game.TorchBlock", "Initialize");
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "api-inspection.txt"), output.ToString());
