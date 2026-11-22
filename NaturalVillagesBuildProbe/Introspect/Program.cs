using System;
using System.Linq;
using System.Reflection;
using Game;

public static class ApiIntrospect
{
    public static void Main()
    {
        Type[] types = { typeof(ModLoader), typeof(TerrainContentsGenerator24), typeof(ChunkGenerationStep), typeof(TerrainChunk), typeof(BlocksManager) };
        foreach (Type type in types)
        {
            Console.WriteLine("=== " + type.FullName + " ===");
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name.Contains("Terrain", StringComparison.OrdinalIgnoreCase)
                         || m.Name.Contains("Chunk", StringComparison.OrdinalIgnoreCase)
                         || m.Name.Contains("Initialize", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("METHOD " + method);
            }
            foreach (MemberInfo member in type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name.Contains("Seed", StringComparison.OrdinalIgnoreCase)
                         || m.Name.Contains("ChunkGeneration", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("MEMBER " + member.MemberType + " " + member.Name);
            }
        }
    }
}
