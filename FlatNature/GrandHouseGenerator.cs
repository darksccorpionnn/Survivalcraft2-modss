using System;
using System.Collections.Generic;

namespace Game;

/// <summary>
/// Larger two-room house placed in the chunk immediately south of the first cottage,
/// with its double entrance facing north toward the cottage.
/// </summary>
internal static class GrandHouseGenerator
{
    private const int Size = 15;
    private const int RegionSize = 128;
    private const int ChunkSize = 16;

    internal static bool GenerateChunk(TerrainChunk chunk, FlatNatureWorldContext context)
    {
        if (chunk?.Cells == null || context == null) return false;

        int rx = FloorDiv(chunk.Origin.X, RegionSize);
        int rz = FloorDiv(chunk.Origin.Y, RegionSize);
        uint h = Hash(context.Seed, rx, rz, 7319);
        if (h % 100u >= 65u) return false;

        // Keep the cottage owner at local chunk-Z 1..7 so the grand house
        // can occupy the chunk directly south of it, within the same region.
        int ownerX = rx * 8 + (int)((h >> 8) % 8u);
        int ownerZ = rz * 8 + 1 + (int)((h >> 16) % 7u);
        int thisX = FloorDiv(chunk.Origin.X, ChunkSize);
        int thisZ = FloorDiv(chunk.Origin.Y, ChunkSize);
        if (thisX != ownerX || thisZ != ownerZ - 1) return false;

        int plank = Find("WoodPlanks", "Planks", "OakPlanks", "PinePlanks");
        int log = Find("OakWood", "PineWood", "Wood");
        int stone = Find("StoneBricks", "Cobblestone", "Stone");
        int glass = Find("Glass");
        int door = FindType("WoodenDoorBlock");
        if (door <= 0) door = FindType("DoorBlock");
        int stove = FindType("WoodStoveBlock");
        int bed = FindType("BedBlock");
        int chest = FindType("ChestBlock");
        int table = FindType("CraftingTableBlock");
        int torch = FindType("TorchBlock");
        int fence = Find("WoodenFence", "WoodFence");
        if (plank <= 0) plank = log;
        if (log <= 0) log = plank;
        if (stone <= 0) stone = plank;
        if (plank <= 0 || log <= 0 || stone <= 0 || door <= 0) return false;

        int ground = FindGround(chunk, context.WaterIndex);
        if (ground < 2 || ground > 235 || !CanPlace(chunk, ground, context.WaterIndex)) return false;

        int p = Terrain.MakeBlockValue(plank);
        int l = Terrain.MakeBlockValue(log);
        int s = Terrain.MakeBlockValue(stone);
        int g = Terrain.MakeBlockValue(glass > 0 ? glass : plank);
        int d = Terrain.MakeBlockValue(door);
        int floorY = ground + 1;
        Action<int,int,int,int> put = (x,y,z,v) =>
        {
            if (x >= 0 && x < 16 && z >= 0 && z < 16 && y > 0 && y < 255)
                chunk.SetCellValueFast(x,y,z,v);
        };

        // Stone plinth and polished wooden floor, 15 x 15.
        for (int x=0;x<Size;x++) for (int z=0;z<Size;z++)
        {
            put(x,ground,z,s);
            put(x,floorY,z,(x==0||x==14||z==0||z==14)?s:p);
        }

        // Outer walls, tall corner pillars, broad windows.
        for (int y=floorY+1;y<=floorY+4;y++)
        for (int q=0;q<Size;q++)
        {
            bool windowBand = y==floorY+2;
            int front = (q>=2&&q<=4 || q>=10&&q<=12) && windowBand ? g : p;
            int rear = (q==3||q==11) && windowBand ? g : p;
            int side = (q==4||q==10) && windowBand ? g : p;
            if (!(q==6||q==7) || y>floorY+2)
                put(q,y,0,front);
            if (!(q==6||q==7) || y>floorY+2) put(q,y,14,rear);
            put(0,y,q,side);
            put(14,y,q,side);
        }
        for (int y=floorY+1;y<=floorY+4;y++)
        {
            put(0,y,0,l); put(14,y,0,l); put(0,y,14,l); put(14,y,14,l);
        }
        for (int q=0;q<Size;q++)
        {
            put(q,floorY+4,0,l); put(q,floorY+4,14,l);
            put(0,floorY+4,q,l); put(14,floorY+4,q,l);
        }

        // Double front doors: two adjacent two-block doorways.
        put(6,floorY+1,14,d); put(6,floorY+2,14,d);
        put(7,floorY+1,14,Terrain.ReplaceData(d,1));
        put(7,floorY+2,14,Terrain.ReplaceData(d,1));

        // Two rooms divided by a full-height interior wall, with a wide passage.
        for (int x=1;x<14;x++)
        {
            if (x==6||x==7||x==8) continue;
            put(x,floorY+1,7,p); put(x,floorY+2,7,p); put(x,floorY+3,7,p);
        }
        // High gabled roof with stone trim and a raised central ridge.
        for (int x=0;x<Size;x++)
        {
            int rise = 5 - Math.Abs(7-x)*5/7;
            int roofY = floorY+5+rise;
            for (int z=-1;z<=15;z++) put(x,roofY,z,s);
        }
        for (int z=0;z<Size;z++)
        {
            put(7,floorY+10,z,l);
            put(1,floorY+5,z,s); put(13,floorY+5,z,s);
        }

        // Furnish both rooms independently.
        if (table>0) put(2,floorY+1,3,Terrain.MakeBlockValue(table));
        if (chest>0) put(4,floorY+1,5,Terrain.MakeBlockValue(chest));
        if (stove>0) put(11,floorY+1,3,Terrain.MakeBlockValue(stove));
        if (bed>0) put(11,floorY+1,10,Terrain.MakeBlockValue(bed));
        if (torch>0)
        {
            int tv=Terrain.MakeBlockValue(torch);
            put(1,floorY+3,4,Terrain.ReplaceData(tv,1));
            put(13,floorY+3,4,Terrain.ReplaceData(tv,3));
            put(1,floorY+3,10,Terrain.ReplaceData(tv,1));
            put(13,floorY+3,10,Terrain.ReplaceData(tv,3));
        }

        // Grand front terrace, steps, and low decorative garden border.
        for (int x=4;x<=9;x++) put(x,ground,15,s);
        if (fence>0)
        {
            int fv=Terrain.MakeBlockValue(fence);
            for (int x=1;x<=13;x++) { put(x,floorY,15,fv); }
        }
        Console.WriteLine("[FlatNature] Grand two-room house generated opposite the cottage at "+chunk.Origin.X+","+ground+","+chunk.Origin.Y);
        return true;
    }

    private static int FindGround(TerrainChunk c,int water)
    {
        int min=999,max=0;
        for(int x=0;x<15;x++) for(int z=0;z<15;z++)
        {
            int y=c.CalculateTopmostCellHeight(x,z);
            while(y>1 && Terrain.ExtractContents(c.GetCellValueFast(x,y,z))==0) y--;
            int v=c.GetCellValueFast(x,y,z), id=Terrain.ExtractContents(v);
            if(y<2 || id==water) return -1;
            Block b=id>0&&id<BlocksManager.Blocks.Length?BlocksManager.Blocks[id]:null;
            if(b==null || !b.IsCollidable_(v)) return -1;
            min=Math.Min(min,y); max=Math.Max(max,y);
        }
        return max-min<=2?max:-1;
    }
    private static bool CanPlace(TerrainChunk c,int ground,int water)
    {
        for(int x=0;x<15;x++) for(int z=0;z<15;z++)
        {
            int y=c.CalculateTopmostCellHeight(x,z);
            while(y>1 && Terrain.ExtractContents(c.GetCellValueFast(x,y,z))==0) y--;
            int id=Terrain.ExtractContents(c.GetCellValueFast(x,y,z));
            if(y<ground-2||y>ground||id==water) return false;
            for(int yy=y+1;yy<=ground+16;yy++)
                if(Terrain.ExtractContents(c.GetCellValueFast(x,yy,z))!=0) return false;
        }
        return true;
    }
    private static int Find(params string[] names)
    {
        foreach(string n in names) foreach(KeyValuePair<string,int> kv in BlocksManager.BlockNameToIndex)
            if(kv.Value>0 && string.Equals(kv.Key,n,StringComparison.OrdinalIgnoreCase)) return kv.Value;
        foreach(string n in names) foreach(KeyValuePair<string,int> kv in BlocksManager.BlockNameToIndex)
            if(kv.Value>0 && kv.Key.IndexOf(n,StringComparison.OrdinalIgnoreCase)>=0) return kv.Value;
        return 0;
    }
    private static int FindType(string token)
    {
        foreach(KeyValuePair<Type,int> kv in BlocksManager.BlockTypeToIndex)
            if(kv.Value>0 && kv.Key.Name.IndexOf(token,StringComparison.OrdinalIgnoreCase)>=0) return kv.Value;
        return 0;
    }
    private static int FloorDiv(int v,int d) { int q=v/d,r=v%d; return r<0?q-1:q; }
    private static uint Hash(int seed,int x,int z,int salt)
    {
        unchecked { uint h=(uint)seed; h^=(uint)x*0x9E3779B9u; h=(h^(h>>16))*0x85EBCA6Bu; h^=(uint)z*0xC2B2AE35u; h=(h^(h>>13))*0x27D4EB2Fu; h^=(uint)salt*0x165667B1u; return h^(h>>16); }
    }
}
