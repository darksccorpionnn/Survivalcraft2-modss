using System;
using System.Linq;
using System.Reflection;
using Game;

var game = typeof(Block).Assembly;
Console.WriteLine("GAME ASSEMBLY " + game.FullName);
Type[] types;
try { types = game.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t=>t!=null).ToArray()!; }
var blocks = types.Where(t=>t.IsClass && !t.IsAbstract && typeof(Block).IsAssignableFrom(t))
    .OrderBy(t=>t.Name).Select(t=>t.Name).ToArray();
Console.WriteLine("TOTAL_BLOCK_CLASSES="+blocks.Length);
foreach(var t in blocks.Where(n=>new[]{"grass","dirt","gravel","stone","sand","wood","plank","log","flower","leaves","plant","cobble","brick","bush"}.Any(q=>n.Contains(q,StringComparison.OrdinalIgnoreCase))))
    Console.WriteLine("BLOCK "+t);
Console.WriteLine("LOADER-HOOK "+typeof(ModLoader).GetMethod("TerrainContentsGenerator24Initialize",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic));
Console.WriteLine("GENERATOR-SEED "+typeof(TerrainContentsGenerator24).GetField("m_seed",BindingFlags.NonPublic|BindingFlags.Instance)?.FieldType);
