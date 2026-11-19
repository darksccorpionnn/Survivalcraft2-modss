using System;
using System.Collections.Generic;

namespace Game;

/// <summary>
/// Builds a wider, deeper version of the original wooden cottage in the
/// neighboring chunk. It keeps the cottage style, adds a second room and a
/// larger rear garden. The previous grand-house design is no longer used.
/// </summary>
internal static class ExpandedCottageGenerator
{
    private const int RegionSize = 128;
    private const int ChunkSize = 16;
    private const int HouseWidth = 11;
    private const int HouseDepth = 10;

    internal static bool GenerateChunk(TerrainChunk chunk, FlatNatureWorldContext context)
    {
        if (chunk?.Cells == null || context == null) return false;

        int rx = FloorDiv(chunk.Origin.X, RegionSize);
        int rz = FloorDiv(chunk.Origin.Y, RegionSize);
        uint h = Hash(context.Seed, rx, rz, 7319);
        if (h % 100u >= 65u) return false;

        int cottageX = rx * 8 + (int)((h >> 8) % 8u);
        int cottageZ = rz * 8 + 1 + (int)((h >> 16) % 7u);
        int thisX = FloorDiv(chunk.Origin.X, ChunkSize);
        int thisZ = FloorDiv(chunk.Origin.Y, ChunkSize);

        // Place the expanded cottage immediately to the right of the first one.
        // The original cottage ends at local X=9; this house starts at the next
        // chunk's X=0, leaving six complete blocks (10..15) between the houses.
        if (thisX != cottageX + 1 || thisZ != cottageZ) return false;

        int plank = Find("WoodPlanks", "Planks", "OakPlanks", "PinePlanks");
        int log = Find("OakWood", "PineWood", "BirchWood", "AcaciaWood", "SpruceWood", "Wood");
        int stone = Find("Cobblestone", "StoneBricks", "StoneBrick", "Stone");
        int glass = Find("Glass", "GlassBlock");
        int door = FindType("WoodenDoorBlock");
        if (door <= 0) door = FindType("DoorBlock");
        int fence = Find("WoodenFence", "WoodFence", "WoodenFenceBlock");
        if (fence <= 0) fence = FindType("FenceBlock");
        int compost = Find("MoistCompost", "MoistCompostBlock");
        if (compost <= 0) compost = FindType("MoistCompostBlock");
        int table = FindType("CraftingTableBlock");
        int stove = FindType("WoodStoveBlock");
        int chest = FindType("ChestBlock");
        int bed = FindType("BedBlock");
        int torch = FindType("TorchBlock");

        if (plank <= 0) plank = log;
        if (log <= 0) log = plank;
        if (stone <= 0) stone = plank;
        if (plank <= 0 || log <= 0 || stone <= 0 || door <= 0) return false;

        int ground = FindFlatGround(chunk, context.WaterIndex);
        if (ground < 2 || ground > 238 || !CanPlace(chunk, ground, context.WaterIndex))
            return false;

        int p = Terrain.MakeBlockValue(plank);
        int l = Terrain.MakeBlockValue(log);
        int s = Terrain.MakeBlockValue(stone);
        int g = Terrain.MakeBlockValue(glass > 0 ? glass : plank);
        int d = Terrain.MakeBlockValue(door);
        int f = fence > 0 ? Terrain.MakeBlockValue(fence) : 0;
        int soil = compost > 0 ? Terrain.MakeBlockValue(compost) : p;
        int floorY = ground + 1;

        Action<int,int,int,int> put = (x,y,z,v) =>
        {
            if (x >= 0 && x < ChunkSize && z >= 0 && z < ChunkSize && y > 0 && y < 255)
                chunk.SetCellValueFast(x,y,z,v);
        };

        // 11 x 10 cottage-style footprint: stone foundation and plank floor.
        for (int x=0;x<HouseWidth;x++)
        for (int z=1;z<=HouseDepth;z++)
        {
            bool border = x==0 || x==HouseWidth-1 || z==1 || z==HouseDepth;
            put(x,ground,z,s);
            put(x,floorY,z,border?s:p);
        }

        // Outer walls; front door remains a single two-block-high doorway.
        for (int y=floorY+1;y<=floorY+3;y++)
        {
            for (int x=1;x<HouseWidth-1;x++)
            {
                bool doorOpening = x==5 && y<=floorY+2;
                if (!doorOpening)
                {
                    int frontValue = y==floorY+2 && (x==2||x==8) ? g : p;
                    put(x,y,1,frontValue);
                }
                int rearValue = y==floorY+2 && (x==3||x==7) ? g : p;
                put(x,y,HouseDepth,rearValue);
            }
            for (int z=2;z<HouseDepth;z++)
            {
                int sideValue = y==floorY+2 && (z==4||z==8) ? g : p;
                put(0,y,z,sideValue);
                put(HouseWidth-1,y,z,sideValue);
            }
        }

        // Timber corner posts and top beams retain the original cottage look.
        for (int y=floorY+1;y<=floorY+3;y++)
        {
            put(0,y,1,l); put(10,y,1,l);
            put(0,y,10,l); put(10,y,10,l);
        }
        for (int x=0;x<HouseWidth;x++)
        {
            put(x,floorY+3,1,l);
            put(x,floorY+3,10,l);
        }
        for (int z=1;z<=HouseDepth;z++)
        {
            put(0,floorY+3,z,l);
            put(10,floorY+3,z,l);
        }

        // Two rooms separated by a wooden partition with a centered passage.
        for (int x=1;x<=9;x++)
        {
            if (x==5) continue;
            put(x,floorY+1,6,p);
            put(x,floorY+2,6,p);
            put(x,floorY+3,6,p);
        }

        // Single entrance matching the first cottage (two vertical door cells).
        put(5,floorY+1,1,d);
        put(5,floorY+2,1,d);

        // Fully filled pitched roof: build the entire triangular roof volume,
        // not just the outer ridge line. This closes the open gable seen in-game.
        int roofBase=floorY+4;
        for (int x=0;x<HouseWidth;x++)
        {
            int rise=5-Math.Abs(5-x);
            int roofY=roofBase+rise;
            for (int y=roofBase;y<=roofY;y++)
            for (int z=0;z<=11;z++)
                put(x,y,z,s);
        }

        // Close both triangular gable ends beneath the roof with matching planks.
        for (int x=1;x<HouseWidth-1;x++)
        {
            int rise=5-Math.Abs(5-x);
            for (int y=roofBase;y<roofBase+rise;y++)
            {
                put(x,y,0,p);
                put(x,y,11,p);
            }
        }

        // Timber ridge beam runs the full length of the roof.
        for (int z=0;z<=11;z++)
            put(5,roofBase+5,z,l);

        // Separate room furnishings: living/work room and bedroom.
        if (table>0) put(2,floorY+1,3,Terrain.MakeBlockValue(table));
        if (chest>0) put(8,floorY+1,3,Terrain.MakeBlockValue(chest));
        if (stove>0) put(2,floorY+1,8,Terrain.MakeBlockValue(stove));
        if (bed>0) put(8,floorY+1,8,Terrain.MakeBlockValue(bed));
        if (torch>0)
        {
            int tv=Terrain.MakeBlockValue(torch);
            put(1,floorY+2,5,Terrain.ReplaceData(tv,1));
            put(9,floorY+2,5,Terrain.ReplaceData(tv,3));
            put(1,floorY+2,8,Terrain.ReplaceData(tv,1));
            put(9,floorY+2,8,Terrain.ReplaceData(tv,3));
        }

        // Larger rear garden, 11 x 5, fenced and planted at ground level.
        // The central gap is its simple entrance; no decorative fence clutter
        // is placed along the front of the house.
        if (f>0)
        {
            for (int x=0;x<11;x++)
            {
                if (x!=5) put(x,ground+1,11,f);
                put(x,ground+1,15,f);
            }
            for (int z=12;z<15;z++)
            {
                put(0,ground+1,z,f);
                put(10,ground+1,z,f);
            }
        }
        for (int x=1;x<=9;x++)
        for (int z=12;z<=14;z++)
            put(x,ground,z,soil);

        // A short central stone path links the back doorless garden opening
        // to the side of the house without adding another doorway.
        for (int x=4;x<=6;x++) put(x,ground,11,s);

        Console.WriteLine("[FlatNature] Expanded two-room cottage generated beside first cottage at "+chunk.Origin.X+","+ground+","+chunk.Origin.Y);
        return true;
    }

    private static int FindFlatGround(TerrainChunk c,int water)
    {
        int min=999,max=0;
        for(int x=0;x<16;x++) for(int z=0;z<16;z++)
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
        for(int x=0;x<16;x++) for(int z=0;z<16;z++)
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
