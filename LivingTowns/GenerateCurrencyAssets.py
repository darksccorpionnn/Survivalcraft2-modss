from pathlib import Path
from PIL import Image, ImageDraw
import math, csv, xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent
MODELS = ROOT / "Assets/Models/LivingTownsCoins"
TEXTURES = ROOT / "Assets/Textures/LivingTownsCoins"
MODELS.mkdir(parents=True, exist_ok=True)
TEXTURES.mkdir(parents=True, exist_ok=True)

def make_texture(path, metal):
    silver = metal == "silver"
    base = (177, 190, 202) if silver else (202, 151, 51)
    dark = (62, 78, 94, 255) if silver else (105, 62, 9, 255)
    mid = (111, 132, 150, 255) if silver else (155, 103, 22, 255)
    bright = (235, 241, 246, 255) if silver else (255, 224, 137, 255)
    pale = (205, 218, 229, 255) if silver else (237, 190, 86, 255)
    im = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.ellipse((7, 7, 248, 248), fill=base, outline=dark, width=8)
    d.ellipse((13, 13, 242, 242), outline=bright, width=3)
    d.ellipse((22, 22, 233, 233), outline=mid, width=4)
    d.ellipse((31, 31, 224, 224), outline=bright, width=2)
    d.ellipse((45, 45, 210, 210), outline=dark, width=4)
    d.ellipse((73, 73, 182, 182), outline=bright, width=4)
    for i in range(48):
        a = 2 * math.pi * i / 48
        x1, y1 = 127.5 + math.cos(a) * 102, 127.5 + math.sin(a) * 102
        x2, y2 = 127.5 + math.cos(a) * 111, 127.5 + math.sin(a) * 111
        d.line((x1, y1, x2, y2), fill=bright if i % 2 == 0 else dark, width=2)
    if silver:
        pts = []
        for i in range(16):
            a = -math.pi / 2 + i * math.pi / 8
            r = 43 if i % 2 == 0 else 20
            pts.append((127.5 + math.cos(a) * r, 126 + math.sin(a) * r))
        d.polygon([(x + 2, y + 3) for x, y in pts], fill=dark)
        d.polygon(pts, fill=pale)
        d.line((127, 84, 127, 168), fill=bright, width=3)
        d.line((85, 126, 170, 126), fill=bright, width=3)
    else:
        for i in range(12):
            a = 2 * math.pi * i / 12
            d.line((127.5 + math.cos(a)*40, 127.5 + math.sin(a)*40,
                    127.5 + math.cos(a)*52, 127.5 + math.sin(a)*52), fill=bright, width=3)
        crown = [(88,138),(93,100),(111,116),(127,86),(145,116),(163,100),(168,138)]
        d.polygon([(x+3,y+3) for x,y in crown], fill=dark)
        d.polygon(crown, fill=pale)
        d.line(crown + [crown[0]], fill=bright, width=3)
        d.rectangle((92,145,164,155), fill=bright, outline=dark, width=2)
    d.ellipse((116, 190, 139, 213), outline=bright, width=3)
    im.save(path, "PNG", optimize=True)

def make_model(path, label):
    nseg = 48
    pos, normals, uvs = [], [], []
    def tri(a,b,c,na,nb,nc,ua,ub,uc):
        for p,n,uv in ((a,na,ua),(b,nb,ub),(c,nc,uc)):
            pos.append(p); normals.append(n); uvs.append(uv)
    def uv(p, radius=.48):
        return (.5 + p[0]/(2*radius), .5 + p[2]/(2*radius))
    specs = ((.435,-.08),(.478,-.07),(.5,-.04),(.5,.04),(.478,.07),(.435,.08))
    rings = [[(r*math.cos(2*math.pi*i/nseg), y, r*math.sin(2*math.pi*i/nseg))
              for i in range(nseg)] for r,y in specs]
    for k in range(len(rings)-1):
        low, high = rings[k], rings[k+1]
        for i in range(nseg):
            j = (i+1) % nseg
            a,b,c,d = low[i],low[j],high[j],high[i]
            ang = 2*math.pi*(i+.5)/nseg
            normal = (math.cos(ang),0,math.sin(ang))
            v0, v1 = .025+k*.009, .034+k*.009
            tri(a,d,c,normal,normal,normal,(i/nseg,v0),(i/nseg,v1),(j/nseg,v1))
            tri(a,c,b,normal,normal,normal,(i/nseg,v0),(j/nseg,v1),(j/nseg,v0))
    front, back = (0,.08,0),(0,-.08,0)
    for i in range(nseg):
        j=(i+1)%nseg
        p,q=rings[-1][i],rings[-1][j]
        tri(front,q,p,(0,1,0),(0,1,0),(0,1,0),(.5,.5),uv(q),uv(p))
        p,q=rings[0][i],rings[0][j]
        tri(back,p,q,(0,-1,0),(0,-1,0),(0,-1,0),(.5,.5),uv(p),uv(q))
    crest=[]
    for i in range(16):
        a=-math.pi/2+i*math.pi/8
        r=.205 if i%2==0 else .105
        crest.append((r*math.cos(a),.086,r*math.sin(a)))
    for i in range(16):
        j=(i+1)%16
        tri((0,.086,0),crest[j],crest[i],(0,1,0),(0,1,0),(0,1,0),
            (.5,.5),uv(crest[j]),uv(crest[i]))
    count=len(pos)
    fmt=lambda arr:" ".join(f"{v:.4f}" for row in arr for v in row)
    indices=" ".join(str(i) for k in range(count//3) for i in (3*k,3*k,3*k,3*k+1,3*k+1,3*k+1,3*k+2,3*k+2,3*k+2))
    xml=f'''<?xml version="1.0" encoding="utf-8"?>
<COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">
<asset><contributor><authoring_tool>LivingTowns Coin Mesh Builder</authoring_tool></contributor><created>2026-11-14T00:00:00Z</created><modified>2026-11-14T00:00:00Z</modified><unit name="meter" meter="1"/><up_axis>Y_UP</up_axis></asset>
<library_geometries><geometry id="{label}Geometry" name="{label}"><mesh>
<source id="{label}Positions"><float_array id="{label}PositionsArray" count="{count*3}">{fmt(pos)}</float_array><technique_common><accessor source="#{label}PositionsArray" count="{count}" stride="3"><param name="X" type="float"/><param name="Y" type="float"/><param name="Z" type="float"/></accessor></technique_common></source>
<source id="{label}Normals"><float_array id="{label}NormalsArray" count="{count*3}">{fmt(normals)}</float_array><technique_common><accessor source="#{label}NormalsArray" count="{count}" stride="3"><param name="X" type="float"/><param name="Y" type="float"/><param name="Z" type="float"/></accessor></technique_common></source>
<source id="{label}UVs"><float_array id="{label}UVsArray" count="{count*2}">{fmt(uvs)}</float_array><technique_common><accessor source="#{label}UVsArray" count="{count}" stride="2"><param name="S" type="float"/><param name="T" type="float"/></accessor></technique_common></source>
<vertices id="{label}Vertices"><input semantic="POSITION" source="#{label}Positions"/></vertices>
<triangles count="{count//3}"><input semantic="VERTEX" source="#{label}Vertices" offset="0"/><input semantic="NORMAL" source="#{label}Normals" offset="1"/><input semantic="TEXCOORD" source="#{label}UVs" offset="2" set="0"/><p>{indices}</p></triangles>
</mesh></geometry></library_geometries>
<library_visual_scenes><visual_scene id="Scene" name="Scene"><node id="{label}Node" name="{label}"><instance_geometry url="#{label}Geometry"/></node></visual_scene></library_visual_scenes>
<scene><instance_visual_scene url="#Scene"/></scene></COLLADA>'''
    path.write_text(xml, encoding="utf-8")
    ET.parse(path)

for name,metal in (("SilverCoin","silver"),("GoldCoin","gold")):
    make_texture(TEXTURES/f"{name}.png",metal)
    make_model(MODELS/f"{name}.dae",name)

header = "Class Name;DefaultDisplayName;DefaultCategory;Behaviors;DisplayOrder;DefaultIconBlockOffset;DefaultIconViewOffset;DefaultIconViewScale;FirstPersonScale;FirstPersonOffset;FirstPersonRotation;InHandScale;InHandOffset;InHandRotation;CraftingId;DefaultCreativeData;IsCollidable;IsPlaceable;IsDiggingTransparent;IsPlacementTransparent;DefaultIsInteractive;IsEditable;IsNonDuplicable;IsGatherable;HasCollisionBehavior;KillsWhenStuck;IsFluidBlocker;IsTransparent;GenerateFacesForSameNeighbors;DefaultShadowStrength;LightAttenuation;DefaultEmittedLightAmount;ObjectShadowStrength;DefaultDropContent;DefaultDropCount;DefaultExperienceCount;RequiredToolLevel;MaxStacking;SleepSuitability;FrictionFactor;Density;NoAutoJump;NoSmoothRise;FuelHeatLevel;FuelFireDuration;DefaultSoundMaterialName;ShovelPower;QuarryPower;HackPower;DefaultMeleePower;DefaultMeleeHitProbability;DefaultProjectilePower;ToolLevel;PlayerLevelRequired;Durability;IsAimable;IsStickable;AlignToVelocity;ProjectileSpeed;ProjectileDamping;ProjectileTipOffset;DisintegratesOnHit;ProjectileStickProbability;DefaultHeat;FireDuration;ExplosionResilience;DefaultExplosionPressure;DefaultExplosionIncendiary;ExplosionKeepsPickables;DigMethod;DigResilience;ProjectileResilience;DefaultNutritionalValue;DefaultSicknessProbability;FoodType;DefaultRotPeriod;DefaultTextureSlot;DestructionDebrisScale"
fields=header.split(";")
def row(name, display, order, craft):
    d={k:"" for k in fields}
    d.update({"Class Name":name,"DefaultDisplayName":display,"DefaultCategory":"Items","DisplayOrder":str(order),
      "DefaultIconBlockOffset":"0,0,0","DefaultIconViewOffset":"1,1,1","DefaultIconViewScale":"0.95",
      "FirstPersonScale":"0.4","FirstPersonOffset":"0.5,-0.5,-0.6","FirstPersonRotation":"0,40,0",
      "InHandScale":"0.3","InHandOffset":"0,0.2,0","InHandRotation":"60,0,0","CraftingId":craft,
      "DefaultCreativeData":"0","IsCollidable":"FALSE","IsPlaceable":"FALSE","IsDiggingTransparent":"TRUE",
      "IsPlacementTransparent":"TRUE","DefaultIsInteractive":"FALSE","IsEditable":"FALSE","IsNonDuplicable":"FALSE",
      "IsGatherable":"FALSE","HasCollisionBehavior":"FALSE","KillsWhenStuck":"FALSE","IsFluidBlocker":"FALSE",
      "IsTransparent":"FALSE","GenerateFacesForSameNeighbors":"FALSE","DefaultShadowStrength":"15",
      "LightAttenuation":"0","DefaultEmittedLightAmount":"0","ObjectShadowStrength":"0","DefaultDropCount":"1",
      "DefaultExperienceCount":"0","RequiredToolLevel":"0","MaxStacking":"99","SleepSuitability":"0","FrictionFactor":"1",
      "Density":"1","NoAutoJump":"FALSE","NoSmoothRise":"FALSE","FuelHeatLevel":"0","FuelFireDuration":"0",
      "ShovelPower":"1","QuarryPower":"1","HackPower":"1","DefaultMeleePower":"1","DefaultMeleeHitProbability":"0.66",
      "DefaultProjectilePower":"2","ToolLevel":"1","PlayerLevelRequired":"1","Durability":"-1","IsAimable":"FALSE",
      "IsStickable":"FALSE","AlignToVelocity":"FALSE","ProjectileSpeed":"0","ProjectileDamping":"0",
      "ProjectileTipOffset":"0","DisintegratesOnHit":"FALSE","ProjectileStickProbability":"0","DefaultHeat":"0",
      "FireDuration":"0","ExplosionResilience":"0","DefaultExplosionPressure":"0","DefaultExplosionIncendiary":"FALSE",
      "ExplosionKeepsPickables":"FALSE","DigMethod":"Quarry","DigResilience":"1","ProjectileResilience":"1",
      "DefaultTextureSlot":"0","DestructionDebrisScale":"0"})
    return [d[k] for k in fields]
with (ROOT/"Assets/LivingTownsBlocksData.csv").open("w",encoding="utf-8-sig",newline="") as f:
    w=csv.writer(f,delimiter=";",lineterminator="\n")
    w.writerow(fields)
    w.writerow(row("SilverCoinBlock","Silver Coin",500,"livingtowns_silver_coin"))
    w.writerow(row("GoldCoinBlock","Gold Coin",501,"livingtowns_gold_coin"))

notes = """LivingTowns 0.4.58 - Silver and Gold Currency Items

- Adds SilverCoinBlock and GoldCoinBlock as separate dynamic Survivalcraft item blocks.
- Both are real 3D coin models, with individual DAE meshes and PNG face textures.
- Coins are non-placeable inventory items; their meshes are used for item, hand and pickable rendering.
- Both currencies stack to 99.
- Currency value rule for the planned trade system: 1 Gold Coin = 10 Silver Coins.
- This version adds the two item types and visuals only. Exchange UI, trade recipes, NPC payouts and automatic conversion are not included yet.
- Existing villager AI and the other mod assets remain unchanged in the full .scmod package.
"""
(ROOT/"Assets/LivingTowns-0.4.58-Currency-Notes.txt").write_text(notes,encoding="utf-8")
print("Generated SilverCoinBlock and GoldCoinBlock assets.")
