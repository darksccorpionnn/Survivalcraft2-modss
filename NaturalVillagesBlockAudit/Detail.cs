using System;
using System.Linq;
using System.Reflection;
using Game;
public static class Detail {
 public static void Main() {
  var ass=typeof(Block).Assembly;
  var ts=ass.GetTypes().Where(t=>t.Name.Contains("Glass",StringComparison.OrdinalIgnoreCase)||t.Name.Contains("Door",StringComparison.OrdinalIgnoreCase)||t.Name.Contains("Leaves",StringComparison.OrdinalIgnoreCase)||t.Name.Contains("Wood",StringComparison.OrdinalIgnoreCase)||t.Name.Contains("Window",StringComparison.OrdinalIgnoreCase)||t.Name.Contains("Planks",StringComparison.OrdinalIgnoreCase)||t.Name.Contains("Brick",StringComparison.OrdinalIgnoreCase)).OrderBy(t=>t.Name);
  foreach(var t in ts) {
   Console.WriteLine("TYPE "+t.FullName);
   if(!t.Name.Contains("Door") && !t.Name.Contains("Glass"))continue;
   var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly;
   foreach(var m in t.GetMethods(flags).Where(m=>m.Name.Contains("Data")||m.Name.Contains("Rotation")||m.Name.Contains("Face")||m.Name.Contains("Door")||m.Name.Contains("Place")||m.Name.Contains("Value")||m.Name.Contains("Hinge")||m.Name.Contains("Open")||m.Name.Contains("Create")))
    Console.WriteLine("METHOD "+m);
   foreach(var f in t.GetFields(flags)) Console.WriteLine("FIELD "+f);
  }
 }
}
