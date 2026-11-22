from pathlib import Path
import json
import sys

root=Path(sys.argv[1])
assert root.name=="NaturalVillages-Medieval-V0.8"

def patch(path,old,new):
    p=root/path
    data=p.read_text(encoding="utf-8")
    if old not in data:
        raise RuntimeError(f"expected anchor missing from {path}: {old[:80]}")
    p.write_text(data.replace(old,new),encoding="utf-8")

(root/"Source"/"WorldSeedResolver.cs").write_text("""using System;
using System.Reflection;

namespace NaturalVillagesMedieval {
    /// <summary>
    /// Read the real generator's world seed including private fields
    /// declared in base classes. Never substitute a fixed default seed.
    /// </summary>
    public static class WorldSeedResolver {
        private static readonly string[] Names = {
            "WorldSeed", "m_worldSeed", "Seed", "m_seed"
        };
        public static bool TryRead(object instance, out int result) {
            result = 0;
            if (instance == null) return false;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            for (Type type = instance.GetType(); type != null; type = type.BaseType) {
                foreach (string name in Names) {
                    try {
                        PropertyInfo property = type.GetProperty(name, flags);
                        if (property != null && property.GetIndexParameters().Length == 0
                            && TryConvert(property.GetValue(instance), out result)) return true;
                        FieldInfo field = type.GetField(name, flags);
                        if (field != null && TryConvert(field.GetValue(instance), out result)) return true;
                    }
                    catch (Exception) { /* Try parent or next name. */ }
                }
            }
            return false;
        }
        private static bool TryConvert(object value, out int result) {
            result = 0;
            if (value is int integer) { result = integer; return true; }
            if (value is long number && number >= int.MinValue && number <= int.MaxValue) {
                result = (int)number; return true;
            }
            return false;
        }
    }
}
""",encoding="utf-8")

patch("Source/MedievalHouseGenerator.cs",
      "if (h < 0) return;",
      '''if (h < 0) {
                            Log.Information("[NaturalVillages] Cottage skipped: unsuitable ground at slot " + slot.Index);
                            return;
                        }''')
patch("Source/MedievalHouseGenerator.cs",
      "if (highest - lowest > 2 || highest < 5 || highest > 243) return;",
      '''if (highest - lowest > 2 || highest < 5 || highest > 243) {
                    Log.Information("[NaturalVillages] Cottage skipped: excessive slope/height at slot " + slot.Index);
                    return;
                }''')
patch("Source/MedievalHouseGenerator.cs",
      "if (Terrain.ExtractContents(value) != 0) return;",
      '''if (Terrain.ExtractContents(value) != 0) {
                                Log.Information("[NaturalVillages] Cottage skipped: blocked roof clearance at slot " + slot.Index);
                                return;
                            }''')
patch("Source/MedievalWellGenerator.cs",
      "if (!TryGetSurface(chunk, x + 5, z + 5, out h)) return;",
      '''if (!TryGetSurface(chunk, x + 5, z + 5, out h)) {
                            Log.Information("[NaturalVillages] Well skipped: unsupported surface in " + chunk.Coords);
                            return;
                        }''')
patch("Source/MedievalWellGenerator.cs",
      "if (maximum - minimum > 2 || maximum < 5 || maximum > 245) return;",
      '''if (maximum - minimum > 2 || maximum < 5 || maximum > 245) {
                    Log.Information("[NaturalVillages] Well skipped: steep/unsafe grade in " + chunk.Coords);
                    return;
                }''')
patch("Source/MedievalWellGenerator.cs",
      "if (Terrain.ExtractContents(chunk.GetCellValueFast(x, y, z)) != 0) return;",
      '''if (Terrain.ExtractContents(chunk.GetCellValueFast(x, y, z)) != 0) {
                                Log.Information("[NaturalVillages] Well skipped: canopy obstruction in " + chunk.Coords);
                                return;
                            }''')
patch("Source/NaturalVillagesLoader.cs","V0.7 experimental","V0.8 experimental")
patch("Source/NaturalVillagesLoader.cs","V0.7 chunk-based","V0.8 chunk-based")
meta=root/"modinfo.json"
m=json.loads(meta.read_text());m["Version"]="0.8.0"
m["Description"]="EXPERIMENTAL V0.8: inherited world seed lookup and terrain placement diagnostics; not Android-tested. New test worlds only."
meta.write_text(json.dumps(m,ensure_ascii=False,indent=2)+"\n")
patch("package_mod.py","V0.7.scmod","V0.8.scmod")
for name in ("Tests/test_v04.py","Tests/test_v05_build.py"):
    p=root/name
    s=p.read_text().replace("'0.7.0'","'0.8.0'").replace("V0.7.scmod","V0.8.scmod")
    s=s.replace("self.assertIn('if (h < 0) return;',src)","self.assertIn('if (h < 0)',src)")
    p.write_text(s)

(root/"Tests"/"test_v08.py").write_text("""import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parent.parent
class V08StabilityTests(unittest.TestCase):
    def test_world_seed_searches_base_types(self):
        s=(ROOT/'Source'/'WorldSeedResolver.cs').read_text()
        self.assertIn('type = type.BaseType',s)
        self.assertIn('BindingFlags.DeclaredOnly',s)
        self.assertIn('"m_seed"',s)
    def test_missing_seed_fails_closed(self):
        s=(ROOT/'Source'/'NaturalVillagesLoader.cs').read_text()
        self.assertIn('if (!WorldSeedResolver.TryRead(',s)
        self.assertIn('generator disabled',s)
    def test_house_rejection_logging(self):
        s=(ROOT/'Source'/'MedievalHouseGenerator.cs').read_text()
        for t in ('unsuitable ground','excessive slope/height','blocked roof clearance'): self.assertIn(t,s)
    def test_well_rejection_logging(self):
        s=(ROOT/'Source'/'MedievalWellGenerator.cs').read_text()
        for t in ('unsupported surface','steep/unsafe grade','canopy obstruction'): self.assertIn(t,s)
    def test_version_matches_package(self):
        import json
        m=json.loads((ROOT/'modinfo.json').read_text())
        self.assertEqual(m['Version'],'0.8.0')
        self.assertIn('V0.8.scmod',(ROOT/'package_mod.py').read_text())
""")
(root/"README.md").write_text("""# Natural Villages Medieval Edition V0.8

Independent Survivalcraft 2.4.0.0 / SCAPI 1.9.5.1 experimental mod.

**V0.8 reliability fixes:** world seed lookup traverses inherited private class fields; house/well terrain rejection now logs specific reasons. Confirmed relevant block types in SDK metadata. 400-block nominal village candidate grid (300–500 user-selected range). 6–12 planned slots, not guaranteed placed buildings.

Run: python -m unittest discover -s Tests -q

Build: dotnet restore NaturalVillagesMedieval.csproj --configfile nuget.config && dotnet build -c Release --no-restore && python package_mod.py

**TEST WORLD ONLY.** This was compiled and tested outside the Android game; runtime village appearance and route continuity need device verification. Keep Living Towns separate.
""")
probe=root/"RuntimeProbe"
probe.mkdir(exist_ok=True)
(probe/"SeedProbe.csproj").write_text("""<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
<ItemGroup><ProjectReference Include="../NaturalVillagesMedieval.csproj" /></ItemGroup>
</Project>
""")
(probe/"Program.cs").write_text("""using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Game;
using NaturalVillagesMedieval;

Type genType=typeof(TerrainContentsGenerator24);
FieldInfo field=null;
for (Type t=genType;t!=null;t=t.BaseType) {
    Console.WriteLine("BASE="+t.FullName);
    field=t.GetField("m_seed",BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly);
    if(field!=null) { Console.WriteLine("SEED_FIELD="+t.FullName+" type="+field.FieldType.FullName); break; }
}
if(field==null || field.FieldType!=typeof(int)) throw new Exception("SCAPI seed field missing or not int");
var generator=RuntimeHelpers.GetUninitializedObject(genType);
field.SetValue(generator,246801);
if(!WorldSeedResolver.TryRead(generator,out int seed) || seed!=246801)
    throw new Exception("WorldSeedResolver returned wrong seed: "+seed);
Console.WriteLine("SEED_RESOLVER_RUNTIME_PROBE=PASS value="+seed);
""")
print("Applied V0.8 source and runtime tests.")
