using System;
using System.Reflection;
using System.Linq;
using Game;
var assembly = typeof(ComponentPathfinding).Assembly;
foreach(var name in new[]{"Game.ComponentPathfinding","Game.ComponentPilot","Game.ComponentHumanModel","Game.ComponentCreatureModel","Game.ComponentBody","Game.ComponentCreature","Game.ComponentLocomotion","Game.SubsystemTerrain","Game.SubsystemTime","Game.SubsystemBodies","Game.SubsystemCreatureSpawn","Game.Component","Game.ModLoader","Game.ComponentNoEffect","Game.ComponentBehavior","Game.IUpdateable"}){
 Type t=assembly.GetType(name,false);
 if(t==null){Console.WriteLine("MISSING "+name);continue;}
 Console.WriteLine("TYPE "+name+" BASE "+t.BaseType?.FullName);
 const BindingFlags f=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly;
 foreach(var p in t.GetProperties(f))Console.WriteLine("P "+p.PropertyType.FullName+" "+p.Name+" get="+(p.GetMethod!=null)+" set="+(p.SetMethod!=null));
 foreach(var x in t.GetFields(f))Console.WriteLine("F "+x.FieldType.FullName+" "+x.Name);
 foreach(var m in t.GetMethods(f))Console.WriteLine("M "+m.ReturnType.FullName+" "+m.Name+"("+string.Join(",",m.GetParameters().Select(p=>p.ParameterType.Name+" "+p.Name+(p.HasDefaultValue?"="+p.DefaultValue:"")))+")");
}
