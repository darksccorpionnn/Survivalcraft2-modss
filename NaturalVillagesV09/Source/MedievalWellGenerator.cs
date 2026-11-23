using System;
using System.Collections.Generic;
using Engine;
using Game;

namespace NaturalVillagesMedieval {
 public static class MedievalWellGenerator {
  struct Write { public int X,Y,Z,V;
   public Write(int x,int y,int z,int v){X=x;Y=y;Z=z;V=v;}
  }
  struct Site {public int X,Z,Low,Grade,Score;public int[,] Heights;}
  static void Add(List<Write> writes,int x,int y,int z,int b){
   if(x<0||x>15||z<0||z>15||y<1||y>254)throw new InvalidOperationException("Well attempted out-of-chunk write.");
   writes.Add(new Write(x,y,z,Terrain.MakeBlockValue(b,0,0)));
  }
  static Site? ChooseSite(TerrainChunk chunk){
   Site? best=null;
   foreach(int sx in new[]{5,4,6})
    foreach(int sz in new[]{5,4,6}){
     int low=255,high=0;bool bad=false;
     int[,] h=new int[5,5];
     for(int x=0;x<5&&!bad;x++)
      for(int z=0;z<5;z++){
       int y=VillageTerrainHelper.GroundY(chunk,sx+x,sz+z);
       if(y<3){bad=true;break;}
       h[x,z]=y;low=Math.Min(low,y);high=Math.Max(high,y);
      }
     if(bad||high-low>6||high>235)continue;
     for(int x=sx-1;x<=sx+5&&!bad;x++)
      for(int z=sz-1;z<=sz+5;z++)
       if(!VillageTerrainHelper.CanClear(chunk,x,z,high+2,high+7)){bad=true;break;}
     if(bad)continue;
     int score=(high-low)*20+Math.Abs(sx-5)*5+Math.Abs(sz-5)*5;
     if(!best.HasValue||score<best.Value.Score)
      best=new Site{X=sx,Z=sz,Low=low,Grade=high,Score=score,Heights=h};
    }
   return best;
  }
  public static void TryGenerate(TerrainChunk chunk){
   try{
    int stone=VillageTerrainHelper.FindBlock("CobblestoneBlock","StoneBrickBlock");
    int wood=VillageTerrainHelper.FindBlock("OakWoodBlock","SpruceWoodBlock","WoodBlock");
    int roof=VillageTerrainHelper.FindBlock("PlanksBlock","WoodenStairsBlock");
    if(stone<=0||wood<=0||roof<=0){Log.Error("[NaturalVillages] Missing well palette.");return;}
    Site? site=ChooseSite(chunk);
    if(!site.HasValue){Log.Information("[NaturalVillages] V0.9 well skipped chunk "+chunk.Coords);return;}
    Site s=site.Value;var writes=new List<Write>(200);
    for(int x=0;x<5;x++)
     for(int z=0;z<5;z++){
      for(int y=s.Heights[x,z]+1;y<=s.Grade+1;y++)Add(writes,s.X+x,y,s.Z+z,stone);
      if(x==0||x==4||z==0||z==4)Add(writes,s.X+x,s.Grade+2,s.Z+z,stone);
     }
    foreach(int x in new[]{s.X,s.X+4})
     foreach(int z in new[]{s.Z,s.Z+4})
      for(int y=s.Grade+3;y<=s.Grade+5;y++)Add(writes,x,y,z,wood);
    for(int x=s.X-1;x<=s.X+5;x++)
     for(int z=s.Z-1;z<=s.Z+5;z++)Add(writes,x,s.Grade+6,z,roof);
    VillageTerrainHelper.ClearVegetation(chunk,s.X-1,s.X+5,s.Z-1,s.Z+5,
     s.Low+1,Math.Min(254,s.Grade+18));
    foreach(Write w in writes)chunk.SetCellValueFast(w.X,w.Y,w.Z,w.V);
    Log.Information("[NaturalVillages] V0.9 well generated chunk "+chunk.Coords);
   }catch(Exception ex){Log.Error("[NaturalVillages] V0.9 well error: "+ex);}
  }
 }
}
