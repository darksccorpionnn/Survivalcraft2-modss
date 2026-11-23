using System;
using System.Collections.Generic;
using Engine;
using Game;

namespace NaturalVillagesMedieval {
 public static class VillageRoadGenerator {
  struct Write {
   public int X,Y,Z,V;
   public Write(int x,int y,int z,int v){X=x;Y=y;Z=z;V=v;}
  }
  // New chunks only. Paths are painted inside their owning chunk.
  // Tall vegetation can be removed; rock, water, unknown objects are protected.
  public static void TryGenerate(TerrainChunk chunk,int seed){
   try{
    bool[,] mask=VillageRoadPlanner.MaskForChunk(seed,chunk.Coords.X,chunk.Coords.Y);
    bool any=false;
    for(int x=0;x<16&&!any;x++)
     for(int z=0;z<16;z++)if(mask[x,z]){any=true;break;}
    if(!any)return;
    int road=VillageTerrainHelper.FindBlock("GravelBlock","CobblestoneBlock");
    if(road<=0){Log.Error("[NaturalVillages] Road palette missing.");return;}
    var writes=new List<Write>();
    int[,] h=new int[16,16];
    bool[,] valid=new bool[16,16];
    int blocked=0,steepEdges=0;
    // Complete a read-only survey BEFORE any chunk writes.
    for(int x=0;x<16;x++)
     for(int z=0;z<16;z++){
      if(!mask[x,z])continue;
      int y=VillageTerrainHelper.GroundY(chunk,x,z);
      h[x,z]=y;
      if(y<3||y>245||!VillageTerrainHelper.CanClear(chunk,x,z,y+1,y+3)){
       blocked++;continue;
      }
      valid[x,z]=true;
     }
    for(int x=0;x<16;x++)
     for(int z=0;z<16;z++){
      if(!valid[x,z])continue;
      int y=h[x,z];
      if((x>0&&valid[x-1,z]&&Math.Abs(y-h[x-1,z])>1)
       ||(z>0&&valid[x,z-1]&&Math.Abs(y-h[x,z-1])>1))
        steepEdges++;
      // Do not delete safe road cells just because the adjacent grade differs.
      // These steep edges are logged for a future stairs/ramp path planner.
      writes.Add(new Write(x,y,z,Terrain.MakeBlockValue(road,0,0)));
     }
    foreach(Write w in writes){
     for(int y=w.Y+1;y<=Math.Min(254,w.Y+9);y++)
      if(VillageTerrainHelper.IsVegetation(VillageTerrainHelper.BlockName(
       chunk.GetCellValueFast(w.X,y,w.Z))))
       chunk.SetCellValueFast(w.X,y,w.Z,0);
     chunk.SetCellValueFast(w.X,w.Y,w.Z,w.V);
    }
    if(blocked>0||steepEdges>0)
     Log.Information("[NaturalVillages] V0.9 road chunk "+chunk.Coords
       +" blocked="+blocked+" steepEdges="+steepEdges);
   }catch(Exception ex){Log.Error("[NaturalVillages] V0.9 road error: "+ex);}
  }
 }
}
