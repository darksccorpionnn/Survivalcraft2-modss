using System;
using System.Linq;
using Game;
public static class FurnitureAudit {
 public static void Main() {
   var names=typeof(Block).Assembly.GetTypes().Where(t=>typeof(Block).IsAssignableFrom(t))
    .Select(t=>t.Name).Where(n=>new[]{"Glass","Torch","Lamp","Lantern","Chest","Bed","Table","Fence","Fire","Flower","Cobble","Brick","Wood","Stairs","Slab","Gate","Grass","Rye","Dirt"}.Any(q=>n.Contains(q,StringComparison.OrdinalIgnoreCase))).OrderBy(n=>n);
   foreach(var n in names)Console.WriteLine("BLOCK_TYPE "+n);
 }
}