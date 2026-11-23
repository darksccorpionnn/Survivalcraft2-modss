from pathlib import Path
import json,sys,shutil
root=Path(sys.argv[1])
def patch(path,old,new):
 p=root/path
 s=p.read_text(encoding="utf-8")
 assert old in s,(path,old[:120])
 p.write_text(s.replace(old,new),encoding="utf-8")
house=root/"Source"/"MedievalHouseGenerator.cs"
s=house.read_text()
assert "const int Width=7, Depth=9, MaxGradeDifference=8;" in s
s=s.replace("const int Width=7, Depth=9, MaxGradeDifference=8;",
            "const int Width=7, Depth=9, MaxGradeDifference=18;")
start=s.index("  static Site? ChooseSite(TerrainChunk chunk){")
end=s.index("  static void Add(List<Write> writes",start)
s=s[:start]+'''  static Site? ChooseSite(TerrainChunk chunk){
   // Survey more dry plots: old code only visited ten candidate origins.
   int[,] ground=new int[16,16];
   for(int x=1;x<15;x++)for(int z=1;z<15;z++)
    ground[x,z]=VillageTerrainHelper.GroundY(chunk,x,z);
   Site? best=null;
   for(int sx=1;sx<=8;sx++)for(int sz=4;sz<=6;sz++){
     int low=255,high=0;bool bad=false;
     int[,] heights=new int[Width,Depth];
     for(int x=0;x<Width&&!bad;x++)for(int z=0;z<Depth;z++){
       int h=ground[sx+x,sz+z];
       if(h<3){bad=true;break;}
       heights[x,z]=h;low=Math.Min(low,h);high=Math.Max(high,h);
     }
     if(bad||high-low>MaxGradeDifference||high>234)continue;
     // Gently cut up to four blocks from a hill, build stone support below.
     int grade=high-Math.Min(4,(high-low)/3);
     // Checking all surrounding rock rejected otherwise buildable sites.
     for(int x=0;x<Width&&!bad;x++)for(int z=0;z<Depth;z++){
       int h=heights[x,z];
       if(!VillageTerrainHelper.CanClear(chunk,sx+x,sz+z,h+1,grade+10)){
         bad=true;break;
       }
     }
     if(bad)continue;
     int score=(high-low)*15+Math.Abs(sx-4)*3+Math.Abs(sz-5)*3;
     if(!best.HasValue||score<best.Value.Score)
       best=new Site{X=sx,Z=sz,Grade=grade,Low=low,Score=score,Heights=heights};
   }
   return best;
  }
''' +s[end:]
old='''    if(!site.HasValue){
     Log.Information("[NaturalVillages] V0.10.1 cottage skipped: unsuitable site chunk "+chunk.Coords);
     return;
    }'''
new='''    if(!site.HasValue){
     Log.Information("[NaturalVillages] V0.11 standard house not possible; trying a compact hillside house slot "
       +slot.Index+" chunk "+chunk.Coords);
     if(!MedievalHillsideHutGenerator.TryGenerate(chunk, slot.Index))
       Log.Information("[NaturalVillages] V0.11 both houses rejected (water, severe cliff or unsafe block) slot "
         +slot.Index+" chunk "+chunk.Coords);
     return;
    }'''
assert old in s
s=s.replace(old,new).replace("V0.10.1","V0.11")
s=s.replace("V0.10: one self-contained 16x16 chunk cottage","V0.11: hillside-aware cottage")
house.write_text(s,encoding="utf-8")
patch("Source/SeededVillagePlanner.cs",'return 6 + (int)(Hash(seed, cellX, cellZ, "count") % 7UL);','return 10 + (int)(Hash(seed, cellX, cellZ, "count") % 3UL);')
for p in ("Source/NaturalVillagesLoader.cs","Source/VillageRoadGenerator.cs","Source/MedievalWellGenerator.cs"):
 file=root/p
 file.write_text(file.read_text(encoding="utf-8").replace("V0.10.1","V0.11").replace("V0.10","V0.11"),encoding="utf-8")
j=json.loads((root/"modinfo.json").read_text(encoding="utf-8"))
assert j["Version"]=="0.10.1"
j["Version"]="0.11.0"
j["Description"]="EXPERIMENTAL V0.11: more hillside-compatible houses; compact fallback cottage; 10-12 scheduled structures, Wicker lamps. Test worlds only."
(root/"modinfo.json").write_text(json.dumps(j,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
patch("package_mod.py","V0.10.1.scmod","V0.11.scmod")
shutil.copyfile("NaturalVillagesBuildProbe/V011/MedievalHillsideHutGenerator.cs",root/"Source"/"MedievalHillsideHutGenerator.cs")
(root/"Tests").mkdir(exist_ok=True)
shutil.copyfile("NaturalVillagesBuildProbe/V011/test_v011.py",root/"Tests"/"test_v011.py")
(root/"README.md").write_text("""# Natural Villages Medieval Edition V0.11
Independent SCAPI 1.9.5.1 / Survivalcraft 2.4 mod.
- 10-12 candidate structures per village (includes well). Actual placed count depends on terrain.
- 24 alternative 7x9 site origins per chunk, shallow terrace, max grade difference 18.
- Smaller 5x5 hillside hut if a standard house cannot fit, 60 alternative prospects.
- Wicker Lamps, stone roofs and cobblestone road plan retained.
- Deterministic seed/chunk schedule; does not edit existing generated chunks.
- Test with NEW WORLD only. Do not use a valuable saved world.
- Android generation behaviour remains unverified after C# compile.
""")
print("V0.11 source patched with robust house fallback")
