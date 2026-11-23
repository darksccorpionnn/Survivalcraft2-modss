from pathlib import Path
import json,sys
root=Path(sys.argv[1])
def patch(relative, old, new):
    f=root/relative
    s=f.read_text(encoding="utf-8")
    assert s.count(old)==1,(relative,old[:100],s.count(old))
    f.write_text(s.replace(old,new),encoding="utf-8")

# Optional dependency: do not refer to villagers DLL at C# compile time.
# If the villager bed mod is disabled, leave every house unchanged.
patch("Source/MedievalHouseGenerator.cs",
    "public int Fence,Gate,Paving,Light,Flower,Dirt,Chest,Workbench,TableSlab;",
    "public int Fence,Gate,Paving,Light,Flower,Dirt,Chest,Workbench,TableSlab,Bed;")
patch("Source/MedievalHouseGenerator.cs",
    'TableSlab=VillageTerrainHelper.FindBlock("StoneBrickSlabBlock","WoodenSlabBlock")',
    'TableSlab=VillageTerrainHelper.FindBlock("StoneBrickSlabBlock","WoodenSlabBlock"),\n    Bed=VillageTerrainHelper.FindBlock("YeniNesilBedBlock")')
patch("Source/MedievalHouseGenerator.cs",
    "   // Interior light far from the door, positioned as a stable self-lit cube.",
    '   // One bed per home; only when the independent V0.4 villagers mod is installed.\n   if(p.Bed>0)Add(writes,sx+1,grade+2,sz+3,p.Bed);\n   // Interior light far from the door, positioned as a stable self-lit cube.')
patch("Source/MedievalHillsideHutGenerator.cs",
    'int chest=VillageTerrainHelper.FindBlock("ChestBlock");',
    'int chest=VillageTerrainHelper.FindBlock("ChestBlock");\n    int bed=VillageTerrainHelper.FindBlock("YeniNesilBedBlock");')
patch("Source/MedievalHillsideHutGenerator.cs",
    'if(chest>0)Add(writes,sx+1,g+2,sz+3,chest);',
    'if(chest>0)Add(writes,sx+1,g+2,sz+3,chest);\n    if(bed>0)Add(writes,sx+1,g+2,sz+2,bed);')
jfile=root/"modinfo.json"
j=json.loads(jfile.read_text())
assert j["Version"]=="0.11.0"
j["Version"]="0.12.0"
j["Description"]="V0.12 optional V0.4 villagers bed support: new standard houses and hillside cottages receive a bed if YeniNesilBedBlock is registered. Old generated houses are not edited. Experimental."
jfile.write_text(json.dumps(j,indent=2,ensure_ascii=False)+"\n")
patch("package_mod.py","V0.11.scmod","V0.12.scmod")
(root/"README.md").write_text("""# Natural Villages Medieval V0.12 - optional villagers beds
This version preserves the V0.11 generation logic.
If Köylüler Yeni Nesil V0.4 is installed, one newly created NPC bed is placed inside each generated full cottage or hillside hut.
Existing previously generated houses do not change.
Bed block is not hard-required: if absent, villages still generate without beds.
Test in a NEW WORLD; Android gameplay not yet confirmed.
""")
print("V0.12 optional bed placement patch applied.")
