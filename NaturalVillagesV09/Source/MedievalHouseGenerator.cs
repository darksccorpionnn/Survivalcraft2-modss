using System;
using System.Collections.Generic;
using Engine;
using Game;

namespace NaturalVillagesMedieval {
 public static class MedievalHouseGenerator {
  const int Width=7,Depth=9,MaxGradeDifference=8;
  struct Write {
   public int X,Y,Z,Value;
   public Write(int x,int y,int z,int v){X=x;Y=y;Z=z;Value=v;}
  }
  struct Site {
   public int X,Z,Grade,Low,Score;
   public int[,] Heights;
  }
  struct Palette {
   public int Stone,Timber,Wall,Roof,Glass,Door;
   public bool Complete => Stone>0&&Timber>0&&Wall>0&&Roof>0&&Glass>0&&Door>0;
  }
  static Palette GetPalette(){
   return new Palette {
    Stone=VillageTerrainHelper.FindBlock("CobblestoneBlock","StoneBrickBlock"),
    Timber=VillageTerrainHelper.FindBlock("OakWoodBlock","SpruceWoodBlock","WoodBlock"),
    Wall=VillageTerrainHelper.FindBlock("PlanksBlock","BricksBlock"),
    Roof=VillageTerrainHelper.FindBlock("PlanksBlock","WoodenStairsBlock"),
    Glass=VillageTerrainHelper.FindBlock("FramedGlassBlock","GlassBlock"),
    Door=VillageTerrainHelper.FindBlock("WoodenDoorBlock")
   };
  }
  static Site? ChooseSite(TerrainChunk chunk) {
   int[,] ground=new int[16,16];
   for(int x=1;x<15;x++)
    for(int z=1;z<15;z++)ground[x,z]=VillageTerrainHelper.GroundY(chunk,x,z);
   Site? best=null;
   foreach(int sx in new[]{4,3,5,2,6,7})
    foreach(int sz in new[]{4,5,6}) {
     int low=255,high=0;bool bad=false;
     int[,] heights=new int[Width,Depth];
     for(int x=0;x<Width&&!bad;x++)
      for(int z=0;z<Depth;z++){
       int h=ground[sx+x,sz+z];
       if(h<3){bad=true;break;}
       heights[x,z]=h;low=Math.Min(low,h);high=Math.Max(high,h);
      }
     if(bad||high-low>MaxGradeDifference||high>234)continue;
     for(int x=sx-1;x<=sx+Width&&!bad;x++)
      for(int z=sz-1;z<=sz+Depth;z++)
       if(!VillageTerrainHelper.CanClear(chunk,x,z,high+2,high+10)){
        bad=true;break;
       }
     if(bad)continue;
     int score=(high-low)*30+Math.Abs(sx-4)*4+(sz-4)*5;
     if(!best.HasValue||score<best.Value.Score)
      best=new Site{X=sx,Z=sz,Grade=high,Low=low,Score=score,Heights=heights};
    }
   return best;
  }
  static void Add(List<Write> writes,int x,int y,int z,int contents,int data=0) {
   if(x<0||x>15||z<0||z>15||y<1||y>254)
    throw new InvalidOperationException("House attempted an out-of-chunk write.");
   writes.Add(new Write(x,y,z,Terrain.MakeBlockValue(contents,0,data)));
  }
  static List<Write> Build(Site s,Palette p){
   var writes=new List<Write>(1000);
   int sx=s.X,sz=s.Z,grade=s.Grade;
   for(int x=0;x<Width;x++)
    for(int z=0;z<Depth;z++){
     for(int y=s.Heights[x,z]+1;y<=grade+1;y++)
      Add(writes,sx+x,y,sz+z,p.Stone);
     for(int dy=2;dy<=5;dy++){
      bool edge=x==0||x==Width-1||z==0||z==Depth-1;
      bool corner=(x==0||x==Width-1)&&(z==0||z==Depth-1);
      int block=!edge?0:(corner||dy==5?p.Timber:p.Wall);
      Add(writes,sx+x,grade+dy,sz+z,block);
     }
    }
   // Door is a real WoodenDoorBlock (not a pair of missing air blocks).
   Add(writes,sx+3,grade+2,sz,p.Door);
   Add(writes,sx+3,grade+3,sz,0);
   foreach(int x in new[]{sx+1,sx+5})Add(writes,x,grade+4,sz,p.Glass);
   foreach(int z in new[]{sz+2,sz+Depth-3}){
    Add(writes,sx,grade+4,z,p.Glass);
    Add(writes,sx+Width-1,grade+4,z,p.Glass);
   }
   for(int rise=0;rise<4;rise++)
    for(int z=sz-1;z<=sz+Depth;z++){
     Add(writes,sx-1+rise,grade+6+rise,z,p.Roof);
     Add(writes,sx+Width-rise,grade+6+rise,z,p.Roof);
    }
   foreach(int z in new[]{sz,sz+Depth-1})
    for(int dy=6;dy<=8;dy++){
     int margin=dy-6;
     for(int x=margin;x<Width-margin;x++)
      Add(writes,sx+x,grade+dy,z,p.Wall);
    }
   for(int z=sz-1;z<=sz+Depth;z++)
    Add(writes,sx+3,grade+9,z,p.Roof);
   return writes;
  }
  public static void TryGenerate(TerrainChunk chunk,int seed){
   if(!VillageChunkSelector.TryFind(chunk.Coords.X,chunk.Coords.Y,seed,out var slot))return;
   if(slot.IsWell){MedievalWellGenerator.TryGenerate(chunk);return;}
   try{
    Palette p=GetPalette();
    if(!p.Complete){Log.Error("[NaturalVillages] Missing door/glass/timber palette.");return;}
    Site? site=ChooseSite(chunk);
    if(!site.HasValue){
     Log.Information("[NaturalVillages] V0.9 cottage skipped: unsuitable site chunk "+chunk.Coords);
     return;
    }
    Site s=site.Value;
    List<Write> writes=Build(s,p);
    VillageTerrainHelper.ClearVegetation(chunk,s.X-1,s.X+Width,s.Z-1,s.Z+Depth,
     s.Low+1,Math.Min(254,s.Grade+20));
    foreach(Write w in writes)chunk.SetCellValueFast(w.X,w.Y,w.Z,w.Value);
    Log.Information("[NaturalVillages] V0.9 door+glass cottage placed slot "+slot.Index
      +"/"+slot.PlannedCount+" chunk "+chunk.Coords);
   }catch(Exception ex){Log.Error("[NaturalVillages] V0.9 house error: "+ex);}
  }
 }
}
