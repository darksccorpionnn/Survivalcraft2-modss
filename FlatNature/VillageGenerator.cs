using System;
using System.Collections.Generic;

namespace Game;

/// <summary>
/// Deterministic villages: eight cottage-style homes in a 4x2 layout,
/// eight clear blocks between house walls, connected roads, blue-torch
/// street lamps and a ring of trees. Village centers repeat every 512 blocks.
/// </summary>
internal static class VillageGenerator
{
    private const int ChunkSize = 16;
    private const int VillageSpacing = 512;
    private const int HouseSize = 9;

    internal static bool GenerateChunk(TerrainChunk chunk, FlatNatureWorldContext context)
    {
        if (chunk?.Cells == null || context == null) return false;

        int chunkX = FloorDiv(chunk.Origin.X, ChunkSize);
        int chunkZ = FloorDiv(chunk.Origin.Y, ChunkSize);
        int minVillageX = FloorDiv(chunk.Origin.X - 90, VillageSpacing);
        int maxVillageX = FloorDiv(chunk.Origin.X + 15 + 90, VillageSpacing);
        int minVillageZ = FloorDiv(chunk.Origin.Y - 90, VillageSpacing);
        int maxVillageZ = FloorDiv(chunk.Origin.Y + 15 + 90, VillageSpacing);
        bool changed = false;

        int plank = Find("WoodPlanks", "Planks", "OakPlanks", "PinePlanks");
        int log = Find("OakWood", "PineWood", "BirchWood", "AcaciaWood", "SpruceWood", "Wood");
        int stone = Find("Cobblestone", "StoneBricks", "StoneBrick", "Stone");
        int glass = Find("Glass", "GlassBlock");
        int door = FindType("WoodenDoorBlock");
        if (door <= 0) door = FindType("DoorBlock");
        int fence = Find("WoodenFence", "WoodFence", "WoodenFenceBlock");
        if (plank <= 0) plank = log;
        if (log <= 0) log = plank;
        if (stone <= 0) stone = plank;
        if (plank <= 0 || log <= 0 || stone <= 0 || door <= 0) return false;

        int blueTorch = BlocksManager.GetBlockIndex<BlueTorchBlock>();
        int roadValue = Terrain.MakeBlockValue(stone);
        int plankValue = Terrain.MakeBlockValue(plank);
        int logValue = Terrain.MakeBlockValue(log);
        int glassValue = Terrain.MakeBlockValue(glass > 0 ? glass : plank);
        int doorValue = Terrain.MakeBlockValue(door);
        int fenceValue = fence > 0 ? Terrain.MakeBlockValue(fence) : 0;

        for (int vx=minVillageX; vx<=maxVillageX; vx++)
        for (int vz=minVillageZ; vz<=maxVillageZ; vz++)
        {
            int centerX = vx * VillageSpacing;
            int centerZ = vz * VillageSpacing;
            // The first village is centered on the new world's origin.
            // Each further village appears on a deterministic 512-block grid.
            int baseX = centerX - 30; // local chunk offsets 2,3,4,5
            int baseZ = centerZ - 15; // local chunk offsets 1,2
            int villageMinX = baseX - 8;
            int villageMaxX = baseX + 4 * 17 - 17 + 8;
            int villageMinZ = baseZ - 20;
            int villageMaxZ = baseZ + 17 + 9 + 20;

            if (chunk.Origin.X > villageMaxX || chunk.Origin.X + 15 < villageMinX ||
                chunk.Origin.Y > villageMaxZ || chunk.Origin.Y + 15 < villageMinZ)
                continue;

            // Each house occupies one chunk. Adjacent house origins are 17 blocks
            // apart, so their 9x9 walls have exactly eight clear blocks between them.
            for (int row=0; row<2; row++)
            for (int col=0; col<4; col++)
            {
                int hx = baseX + col * 17;
                int hz = baseZ + row * 17;
                if (FloorDiv(hx,16)!=chunkX || FloorDiv(hz,16)!=chunkZ) continue;
                int sx = hx - chunk.Origin.X;
                int sz = hz - chunk.Origin.Y;
                if (sx<0 || sx+HouseSize>16 || sz<0 || sz+HouseSize>16) continue;
                int ground = FindHouseGround(chunk,sx,sz,context);
                if (ground<2 || ground>238 || !CanPlaceHouse(chunk,sx,sz,ground,context.WaterIndex)) continue;
                BuildHouse(chunk,sx,ground+1,sz,plankValue,logValue,roadValue,glassValue,doorValue,context);
                changed = true;
                Console.WriteLine("[FlatNature] Village cottage generated at "+hx+","+ground+","+hz);
            }

            // Main north-south street, cross street and short paths from house doors.
            for (int x=0;x<16;x++)
            for (int z=0;z<16;z++)
            {
                int wx=chunk.Origin.X+x, wz=chunk.Origin.Y+z;
                bool road =
                    (Math.Abs(wz-(centerZ-18))<=1 && wx>=centerX-42 && wx<=centerX+42) ||
                    (Math.Abs(wx-(centerX-2))<=1 && wz>=centerZ-34 && wz<=centerZ+25) ||
                    (Math.Abs(wz-(centerZ-1))==0 && wx>=centerX-31 && wx<=centerX+30);
                // Connect every house front to the nearest street.
                if (!road)
                {
                    for(int row=0;row<2&&!road;row++)
                    for(int col=0;col<4&&!road;col++)
                    {
                        int hx=baseX+col*17, hz=baseZ+row*17;
                        if(wx==hx+4 && wz>=hz-8 && wz<hz) road=true;
                    }
                }
                if (!road) continue;
                int y=Surface(chunk,x,z);
                if(y>1 && y<245)
                {
                    Put(chunk,x,y,z,roadValue);
                    changed=true;
                }
            }

            // Blue Torch is our custom-designed blue lantern. Place it on a
            // four-block stone post beside the village roads at regular intervals.
            if (blueTorch>0)
            {
                for(int x=0;x<16;x++)
                for(int z=0;z<16;z++)
                {
                    int wx=chunk.Origin.X+x, wz=chunk.Origin.Y+z;
                    bool lamp =
                        (wz==centerZ-21 && (wx-centerX+42)%16==0 && wx>=centerX-42 && wx<=centerX+42) ||
                        (wx==centerX+2 && (wz-centerZ+34)%16==0 && wz>=centerZ-34 && wz<=centerZ+25);
                    if(!lamp) continue;
                    int y=Surface(chunk,x,z);
                    if(y<2||y>238) continue;
                    for(int py=1;py<=4;py++) Put(chunk,x,y+py,z,roadValue);
                    Put(chunk,x,y+5,z,Terrain.ReplaceData(Terrain.MakeBlockValue(blueTorch),4));
                    changed=true;
                }
            }

            // A planted tree belt around the settlement; deterministic 8-block spacing.
            for(int x=0;x<16;x++)
            for(int z=0;z<16;z++)
            {
                int wx=chunk.Origin.X+x, wz=chunk.Origin.Y+z;
                bool onWest=Math.Abs(wx-(centerX-52))<=1 && (wz-(centerZ-40))%8==0 && wz>=centerZ-40 && wz<=centerZ+40;
                bool onEast=Math.Abs(wx-(centerX+52))<=1 && (wz-(centerZ-40))%8==0 && wz>=centerZ-40 && wz<=centerZ+40;
                bool onNorth=Math.Abs(wz-(centerZ-40))<=1 && (wx-(centerX-52))%8==0 && wx>=centerX-52 && wx<=centerX+52;
                bool onSouth=Math.Abs(wz-(centerZ+40))<=1 && (wx-(centerX-52))%8==0 && wx>=centerX-52 && wx<=centerX+52;
                if (!(onWest||onEast||onNorth||onSouth)) continue;
                if(context.WoodIndex<=0||context.LeavesIndex<=0) continue;
                int y=Surface(chunk,x,z);
                if(y<2||y>235) continue;
                BuildVillageTree(chunk,x,y,z,context);
                changed=true;
            }
        }

        return changed;
    }

    private static void BuildHouse(TerrainChunk c,int sx,int floorY,int sz,int p,int l,int stone,int glass,int door,FlatNatureWorldContext context)
    {
        Action<int,int,int,int> put=(x,y,z,v)=>Put(c,sx+x,y,sz+z,v);
        int ground=floorY-1;
        for(int x=0;x<9;x++) for(int z=0;z<9;z++)
        {
            bool edge=x==0||x==8||z==0||z==8;
            put(x,ground,z,stone); put(x,floorY,z,edge?stone:p);
        }
        for(int y=floorY+1;y<=floorY+3;y++)
        for(int q=1;q<8;q++)
        {
            bool opening=q==4&&y<=floorY+2;
            if(!opening) put(q,y,0,(y==floorY+2&&(q==2||q==6))?glass:p);
            put(q,y,8,(y==floorY+2&&(q==2||q==6))?glass:p);
            put(0,y,q,(y==floorY+2&&(q==3||q==5))?glass:p);
            put(8,y,q,(y==floorY+2&&(q==3||q==5))?glass:p);
        }
        for(int y=floorY+1;y<=floorY+3;y++)
        {
            put(0,y,0,l);put(8,y,0,l);put(0,y,8,l);put(8,y,8,l);
        }
        for(int q=0;q<9;q++){put(q,floorY+3,0,l);put(q,floorY+3,8,l);}
        for(int x=1;x<8;x++)
        {
            int rise=4-Math.Abs(4-x);
            for(int y=floorY+4;y<floorY+4+rise;y++){put(x,y,0,p);put(x,y,8,p);}
        }
        put(4,floorY+1,0,door);put(4,floorY+2,0,door);
        for(int x=0;x<9;x++)
        {
            int rise=4-Math.Abs(4-x);
            int roofY=floorY+4+rise;
            for(int z=-1;z<=9;z++) put(x,roofY,z,stone);
        }
        for(int z=0;z<9;z++) put(4,floorY+8,z,l);

        int table=FindType("CraftingTableBlock"), stove=FindType("WoodStoveBlock");
        int chest=FindType("ChestBlock"), bed=FindType("BedBlock"), torch=FindType("TorchBlock");
        if(table>0) put(2,floorY+1,2,Terrain.MakeBlockValue(table));
        if(stove>0) put(6,floorY+1,6,Terrain.MakeBlockValue(stove));
        if(chest>0) put(6,floorY+1,2,Terrain.MakeBlockValue(chest));
        if(bed>0) put(2,floorY+1,6,Terrain.MakeBlockValue(bed));
        if(torch>0)
        {
            int tv=Terrain.MakeBlockValue(torch);
            put(1,floorY+2,4,Terrain.ReplaceData(tv,1));
            put(7,floorY+2,4,Terrain.ReplaceData(tv,3));
        }
    }

    private static void BuildVillageTree(TerrainChunk c,int x,int ground,int z,FlatNatureWorldContext ctx)
    {
        int wood=Terrain.MakeBlockValue(ctx.WoodIndex), leaves=Terrain.MakeBlockValue(ctx.LeavesIndex);
        for(int y=1;y<=6;y++) Put(c,x,ground+y,z,wood);
        for(int y=3;y<=7;y++)
        {
            int r=y<5?3:(y==5?2:1);
            for(int dx=-r;dx<=r;dx++) for(int dz=-r;dz<=r;dz++)
            {
                if(dx*dx+dz*dz>r*r+1 || (dx==0&&dz==0)) continue;
                Put(c,x+dx,ground+y,z+dz,leaves);
            }
        }
    }

    private static int FindHouseGround(TerrainChunk c,int sx,int sz,FlatNatureWorldContext ctx)
    {
        int min=999,max=0;
        for(int x=sx;x<sx+9;x++) for(int z=sz;z<sz+9;z++)
        {
            int y=255;
            while(y>1)
            {
                int id=Terrain.ExtractContents(c.GetCellValueFast(x,y,z));
                if(id==ctx.GrassIndex||id==ctx.DirtIndex||id==ctx.SandIndex) break;
                y--;
            }
            int v=c.GetCellValueFast(x,y,z),idAtGround=Terrain.ExtractContents(v);
            if(y<2||idAtGround==ctx.WaterIndex) return -1;
            Block b=idAtGround>0&&idAtGround<BlocksManager.Blocks.Length?BlocksManager.Blocks[idAtGround]:null;
            if(b==null||!b.IsCollidable_(v)) return -1;
            min=Math.Min(min,y);max=Math.Max(max,y);
        }
        return max-min<=3?max:-1;
    }

    private static bool CanPlaceHouse(TerrainChunk c,int sx,int sz,int ground,int water)
    {
        for(int x=sx;x<sx+9;x++) for(int z=sz;z<sz+9;z++)
        {
            int y=Surface(c,x,z),id=Terrain.ExtractContents(c.GetCellValueFast(x,y,z));
            if(y<ground-2||y>ground||id==water) return false;
            // Clear pre-existing grassland trees and shrubs from the house footprint
            // so vegetation cannot randomly suppress one of the eight village homes.
            for(int yy=y+1;yy<=ground+12;yy++)
                if(Terrain.ExtractContents(c.GetCellValueFast(x,yy,z))!=0)
                    c.SetCellValueFast(x,yy,z,0);
        }
        return true;
    }

    private static int Surface(TerrainChunk c,int x,int z)
    {
        int y=c.CalculateTopmostCellHeight(x,z);
        if(y>255)y=255;
        while(y>1&&Terrain.ExtractContents(c.GetCellValueFast(x,y,z))==0)y--;
        return y;
    }
    private static void Put(TerrainChunk c,int x,int y,int z,int v)
    {
        if(x<0||x>=16||z<0||z>=16||y<1||y>=255||v==0)return;
        if(c.GetCellValueFast(x,y,z)!=v)c.SetCellValueFast(x,y,z,v);
    }
    private static int Find(params string[] names)
    {
        foreach(string n in names) foreach(KeyValuePair<string,int> kv in BlocksManager.BlockNameToIndex)
            if(kv.Value>0&&string.Equals(kv.Key,n,StringComparison.OrdinalIgnoreCase))return kv.Value;
        foreach(string n in names) foreach(KeyValuePair<string,int> kv in BlocksManager.BlockNameToIndex)
            if(kv.Value>0&&kv.Key.IndexOf(n,StringComparison.OrdinalIgnoreCase)>=0)return kv.Value;
        return 0;
    }
    private static int FindType(string token)
    {
        foreach(KeyValuePair<Type,int> kv in BlocksManager.BlockTypeToIndex)
            if(kv.Value>0&&kv.Key.Name.IndexOf(token,StringComparison.OrdinalIgnoreCase)>=0)return kv.Value;
        return 0;
    }
    private static int FloorDiv(int v,int d){int q=v/d,r=v%d;return r<0?q-1:q;}
}
