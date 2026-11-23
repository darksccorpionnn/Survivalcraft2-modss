from pathlib import Path
import json
import re
import sys
from collections import deque

root=Path(sys.argv[1])
if root.name!="NaturalVillages-Medieval-V0.10":
    raise SystemExit("Bad project path")

def patch(path,old,new):
    p=root/path
    s=p.read_text(encoding="utf-8")
    if old not in s: raise RuntimeError("Missing anchor "+str(path)+" : "+old[:100])
    p.write_text(s.replace(old,new),encoding="utf-8")

patch("Source/VillageChunkSelector.cs",
      "(-3, -2), (0, -3), (3, -2), (-4, 0), (4, 0),\n            (-3, 2), (0, 3), (3, 2), (-2, -4), (2, -4), (-2, 4)",
      "(-2, -1), (2, -1), (-2, 1), (2, 1), (0, -3),\n            (-2, -3), (2, -3), (0, 3), (-2, 3), (2, 3), (-3, 0)")
patch("Source/VillageChunkSelector.cs","MaximumVillageRadiusChunks = 4","MaximumVillageRadiusChunks = 3")
patch("Source/VillageRoadGenerator.cs",
      '"GravelBlock","CobblestoneBlock"','"CobblestoneBlock","StoneBrickBlock","GravelBlock"')
patch("Source/VillageRoadGenerator.cs","V0.9 road","V0.10 road")
patch("Source/MedievalWellGenerator.cs",
      '"PlanksBlock","WoodenStairsBlock"','"StoneBrickBlock","StoneBrickSlabBlock"')
patch("Source/MedievalWellGenerator.cs",
      "    VillageTerrainHelper.ClearVegetation(chunk,s.X-1,s.X+5,s.Z-1,s.Z+5,",
      """    // Warm light on every well roof corner.
    int lamp=VillageTerrainHelper.FindBlock("JackOLanternBlock");
    if(lamp>0)foreach(int x in new[]{s.X,s.X+4})
      foreach(int z in new[]{s.Z,s.Z+4})Add(writes,x,s.Grade+7,z,lamp);
    VillageTerrainHelper.ClearVegetation(chunk,s.X-1,s.X+5,s.Z-1,s.Z+5,""")
patch("Source/MedievalWellGenerator.cs","V0.9 well","V0.10 well")
patch("Source/NaturalVillagesLoader.cs","V0.9","V0.10")
m=root/"modinfo.json"
data=json.loads(m.read_text(encoding="utf-8"))
data["Version"]="0.10.0"
data["Description"]="EXPERIMENTAL V0.10: closer cottages, cobblestone roads, stone roofs, fenced gardens, interior decor and village lights; new test worlds only."
m.write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
patch("package_mod.py","V0.9.scmod","V0.10.scmod")

# Match the current real source and run a pure routing regression for all orientations.
house=(root/"Source/MedievalHouseGenerator.cs").read_text()
assert "AddYard(chunk,s,p,writes)" in house
assert '"WoodenFenceBlock"' in house and '"JackOLanternBlock"' in house
assert '"StoneBrickBlock","CobblestoneBlock"' in house
assert '"ChestBlock"' in house and '"CraftingTableBlock"' in house
assert 'V0.10.scmod' in (root/"package_mod.py").read_text()
offsets=[(0,0),(-2,-1),(2,-1),(-2,1),(2,1),(0,-3),(-2,-3),(2,-3),(0,3),(-2,3),(2,3),(-3,0)]
def rotate(p,r):
    x,z=p
    for _ in range(r): x,z=-z,x
    return x,z
def reachable(slots,start,goal):
    occupied=set(slots)
    if start in occupied or goal in occupied:return False
    q=deque([start]);visited={start}
    while q:
        x,z=q.popleft()
        if (x,z)==goal:return True
        for p in ((x-1,z),(x+1,z),(x,z-1),(x,z+1)):
            if p not in occupied and p not in visited and max(abs(p[0]),abs(p[1]))<=7:
                visited.add(p);q.append(p)
    return False
checks=0
for rotation in range(4):
    for count in range(6,13):
        slots=[rotate(p,rotation) for p in offsets[:count]]
        assert len(set(slots))==count
        for x,z in slots[1:]:
            assert reachable(slots,(0,-1),(x,z-1)),(rotation,count,x,z)
            checks+=1
print("Natural Villages V0.10 patch + route regression PASS:",checks,"paths")
