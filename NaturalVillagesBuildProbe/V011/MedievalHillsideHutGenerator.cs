using System;
using System.Collections.Generic;
using Engine;
using Game;

namespace NaturalVillagesMedieval {
 /// <summary>
 /// Backup 5x5 medieval cottage on rugged dry terrain.
 /// Entirely confined to one 16x16 chunk; water or unknown ground is rejected.
 /// </summary>
 public static class MedievalHillsideHutGenerator {
  const int Width = 5, Depth = 5, MaxGradeDifference = 24;
  struct Plot { public int X,Z,Grade,Low,Score; public int[,] Heights; }
  struct Write { public int X,Y,Z,Value;
   public Write(int x,int y,int z,int v){X=x;Y=y;Z=z;Value=v;}
  }
  static void Add(List<Write> writes,int x,int y,int z,int block){
   if(x<0||x>15||z<0||z>15||y<1||y>254)
    throw new InvalidOperationException("Hillside hut out-of-chunk write");
   writes.Add(new Write(x,y,z,Terrain.MakeBlockValue(block,0,0)));
  }
  static Plot? ChooseSite(TerrainChunk chunk){
   int[,] ground = new int[16,16];
   for(int x=1;x<15;x++)for(int z=1;z<15;z++)
    ground[x,z]=VillageTerrainHelper.GroundY(chunk,x,z);
   Plot? best=null;
   for(int sx=1;sx<=10;sx++)for(int sz=5;sz<=10;sz++){
    bool bad=false;int low=255,high=0;int[,] heights=new int[Width,Depth];
    for(int x=0;x<Width&&!bad;x++)for(int z=0;z<Depth;z++){
     int h=ground[sx+x,sz+z];
     if(h<3){bad=true;break;}
     heights[x,z]=h;low=Math.Min(low,h);high=Math.Max(high,h);
    }
    if(bad||high-low>MaxGradeDifference||high>234)continue;
    int grade=high-Math.Min(4,(high-low)/3);
    for(int x=0;x<Width&&!bad;x++)for(int z=0;z<Depth;z++){
     if(!VillageTerrainHelper.CanClear(chunk,sx+x,sz+z,
       heights[x,z]+1,grade+9)){bad=true;break;}
    }
    if(bad)continue;
    int score=(high-low)*16+Math.Abs(sx-5)*4+Math.Abs(sz-5)*4;
    if(!best.HasValue||score<best.Value.Score)
     best=new Plot{X=sx,Z=sz,Grade=grade,Low=low,Score=score,Heights=heights};
   }
   return best;
  }
  public static bool TryGenerate(TerrainChunk chunk,int slotIndex){
   try{
    int stone=VillageTerrainHelper.FindBlock("StoneBrickBlock","CobblestoneBlock");
    int timber=VillageTerrainHelper.FindBlock("OakWoodBlock","SpruceWoodBlock");
    int wall=VillageTerrainHelper.FindBlock("PlanksBlock","BricksBlock");
    int glass=VillageTerrainHelper.FindBlock("FramedGlassBlock","GlassBlock");
    int door=VillageTerrainHelper.FindBlock("WoodenDoorBlock");
    int light=VillageTerrainHelper.FindBlock("WickerLampBlock","JackOLanternBlock");
    int chest=VillageTerrainHelper.FindBlock("ChestBlock");
    int fence=VillageTerrainHelper.FindBlock("WoodenFenceBlock");
    int gate=VillageTerrainHelper.FindBlock("WoodenFenceGateBlock");
    if(stone<=0||timber<=0||wall<=0||glass<=0||door<=0||fence<=0){
     Log.Error("[NaturalVillages] V0.11 fallback palette missing");return false;
    }
    Plot? site=ChooseSite(chunk);
    if(!site.HasValue)return false;
    Plot p=site.Value;var writes=new List<Write>(640);
    int sx=p.X,sz=p.Z,g=p.Grade;
    for(int x=0;x<Width;x++)for(int z=0;z<Depth;z++){
     int floor=p.Heights[x,z];
     for(int y=floor+1;y<=g+1;y++)Add(writes,sx+x,y,sz+z,stone);
     Add(writes,sx+x,g+1,sz+z,stone);
     for(int d=2;d<=5;d++){
      bool edge=x==0||x==Width-1||z==0||z==Depth-1;
      bool corner=(x==0||x==Width-1)&&(z==0||z==Depth-1);
      Add(writes,sx+x,g+d,sz+z,edge?(corner||d==5?timber:wall):0);
     }
    }
    Add(writes,sx+2,g+2,sz,door);
    Add(writes,sx+2,g+3,sz,0);
    Add(writes,sx+1,g+4,sz,glass);
    Add(writes,sx+3,g+4,sz,glass);
    Add(writes,sx,g+4,sz+2,glass);
    Add(writes,sx+Width-1,g+4,sz+2,glass);
    if(chest>0)Add(writes,sx+1,g+2,sz+3,chest);
    if(light>0)Add(writes,sx+3,g+2,sz+3,light);
    foreach(int z in new[]{sz,sz+Depth-1}){
     for(int x=0;x<Width;x++)Add(writes,sx+x,g+6,z,wall);
     for(int x=1;x<Width-1;x++)Add(writes,sx+x,g+7,z,wall);
    }
    for(int rise=0;rise<3;rise++)for(int z=sz-1;z<=sz+Depth;z++){
     Add(writes,sx-1+rise,g+6+rise,z,stone);
     Add(writes,sx+Width-rise,g+6+rise,z,stone);
    }
    for(int z=sz-1;z<=sz+Depth;z++)Add(writes,sx+2,g+8,z,stone);
    int xMin=Math.Max(1,sx-1),xMax=Math.Min(14,sx+Width);
    int zMin=sz-1,zMax=Math.Min(14,sz+Depth);
    for(int x=xMin;x<=xMax;x++)for(int z=zMin;z<=zMax;z++){
     if(x!=xMin&&x!=xMax&&z!=zMin&&z!=zMax)continue;
     int y=VillageTerrainHelper.GroundY(chunk,x,z);
     if(y<3||y>248)continue;
     if(!VillageTerrainHelper.CanClear(chunk,x,z,y+1,y+2))continue;
     if(z==zMin&&x==sx+2){
      if(gate>0)Add(writes,x,y+1,z,gate);
     }else Add(writes,x,y+1,z,fence);
    }
    for(int z=1;z<sz;z++){
     int x=sx+2;
     if(x<1||x>14)continue;
     int y=VillageTerrainHelper.GroundY(chunk,x,z);
     if(y>=3&&y<=245&&VillageTerrainHelper.CanClear(chunk,x,z,y+1,y+2))
      Add(writes,x,y,z,stone);
    }
    VillageTerrainHelper.ClearVegetation(chunk,
     Math.Max(0,sx-1),Math.Min(15,sx+Width),Math.Max(0,sz-1),Math.Min(15,sz+Depth),
     p.Low+1,Math.Min(254,g+16));
    foreach(Write w in writes)chunk.SetCellValueFast(w.X,w.Y,w.Z,w.Value);
    Log.Information("[NaturalVillages] V0.11 compact hillside hut placed at slot "+slotIndex
      +" chunk "+chunk.Coords+" altitude="+g);
    return true;
   }catch(Exception ex){Log.Error("[NaturalVillages] V0.11 hillside hut failed: "+ex);return false;}
  }
 }
}
