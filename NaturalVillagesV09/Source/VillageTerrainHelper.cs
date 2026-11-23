using System;
using Engine;
using Game;

namespace NaturalVillagesMedieval {
 public static class VillageTerrainHelper {
  public static string BlockName(int value) {
   int id=Terrain.ExtractContents(value);
   if(id==0)return "";
   if(id<0 || id>=BlocksManager.Blocks.Length)return "?";
   Block b=BlocksManager.Blocks[id];
   return b==null ? "?" : b.GetType().Name;
  }
  public static bool IsVegetation(string name) {
   if(name.Length==0)return false;
   return name.IndexOf("Leaves",StringComparison.OrdinalIgnoreCase)>=0
    || name.EndsWith("WoodBlock",StringComparison.OrdinalIgnoreCase)
    || name.IndexOf("TallGrass",StringComparison.OrdinalIgnoreCase)>=0
    || name.IndexOf("Flower",StringComparison.OrdinalIgnoreCase)>=0
    || name.IndexOf("DryBush",StringComparison.OrdinalIgnoreCase)>=0;
  }
  public static bool IsGround(string name) {
   return name=="GrassBlock" || name=="DirtBlock" || name=="GravelBlock"
    || name=="SandBlock" || name=="SandstoneBlock" || name=="LimestoneBlock"
    || name=="BasaltBlock" || name=="GraniteBlock" || name=="CobblestoneBlock"
    || name=="StoneBrickBlock";
  }
  public static int GroundY(TerrainChunk chunk,int x,int z) {
   if(x<0||x>15||z<0||z>15)return -1;
   for(int y=254;y>=3;y--) {
    string name=BlockName(chunk.GetCellValueFast(x,y,z));
    if(name.Length==0||IsVegetation(name))continue;
    return IsGround(name)?y:-1;
   }
   return -1;
  }
  public static bool CanClear(TerrainChunk chunk,int x,int z,int fromY,int toY) {
   if(x<0||x>15||z<0||z>15)return false;
   for(int y=Math.Max(1,fromY);y<=Math.Min(254,toY);y++) {
    string name=BlockName(chunk.GetCellValueFast(x,y,z));
    if(name.Length!=0 && !IsVegetation(name))return false;
   }
   return true;
  }
  public static void ClearVegetation(TerrainChunk chunk,int minX,int maxX,int minZ,int maxZ,int minY,int maxY) {
   if(minX<0||maxX>15||minZ<0||maxZ>15||minY<1||maxY>254)
    throw new InvalidOperationException("Vegetation clear exceeded chunk bounds.");
   for(int x=minX;x<=maxX;x++)
    for(int z=minZ;z<=maxZ;z++)
     for(int y=minY;y<=maxY;y++)
      if(IsVegetation(BlockName(chunk.GetCellValueFast(x,y,z))))
       chunk.SetCellValueFast(x,y,z,0);
  }
  public static int FindBlock(params string[] names) {
   foreach(string name in names)
    for(int i=1;i<BlocksManager.Blocks.Length;i++) {
     Block b=BlocksManager.Blocks[i];
     if(b!=null && string.Equals(b.GetType().Name,name,StringComparison.OrdinalIgnoreCase)) return i;
    }
   return -1;
  }
 }
}
