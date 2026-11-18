import math, os
import numpy as np
import trimesh
from PIL import Image, ImageDraw
from trimesh.visual.texture import TextureVisuals
from trimesh.visual.material import PBRMaterial
out="FlatNature/Assets/Models/IronKettle/IronKettle.glb"
os.makedirs(os.path.dirname(out),exist_ok=True)
img=Image.new("RGB",(256,256),(112,118,123)); pix=img.load()
for y in range(256):
 for x in range(256):
  n=((x*17+y*31+(x*y)%23)%13)-6; v=max(0,min(255,112+n+int(5*math.sin(x/8))))
  pix[x,y]=(v,min(255,v+2),min(255,v+4))
d=ImageDraw.Draw(img)
for y in range(0,256,32): d.line((0,y,256,y),fill=(130,136,140),width=1)
mat=PBRMaterial(name="Brushed iron",baseColorTexture=img,metallicFactor=.82,roughnessFactor=.34)
parts=[]
def add(m):
 m.visual=TextureVisuals(uv=np.tile([.5,.5],(len(m.vertices),1)),material=mat); parts.append(m)
profile=[(0,.12),(.04,.12),(.12,.14),(.19,.20),(.25,.27),(.27,.34),(.25,.42),(.21,.47),(.16,.49),(0,.49)]
v=[]; f=[]; seg=48
for r,y in profile:
 for i in range(seg):
  a=2*math.pi*i/seg; v.append([r*math.cos(a),y,r*math.sin(a)])
for j in range(len(profile)-1):
 for i in range(seg):
  a=j*seg+i;b=j*seg+(i+1)%seg;c=(j+1)*seg+(i+1)%seg;e=(j+1)*seg+i;f.extend([[a,b,c],[a,c,e]])
add(trimesh.Trimesh(vertices=np.array(v),faces=np.array(f),process=True))
add(trimesh.creation.cylinder(radius=.165,height=.025,sections=48,transform=trimesh.transformations.translation_matrix([0,.50,0])))
add(trimesh.creation.icosphere(subdivisions=2,radius=.10,transform=trimesh.transformations.translation_matrix([0,.535,0])))
add(trimesh.creation.cylinder(radius=.035,height=.065,sections=20,transform=trimesh.transformations.translation_matrix([0,.625,0])))
add(trimesh.creation.uv_sphere(radius=.045,count=[16,12],transform=trimesh.transformations.translation_matrix([0,.675,0])))
path=np.array([[.20,.34,0],[.29,.36,0],[.36,.41,0],[.43,.49,0],[.49,.56,0]]); radii=[.075,.065,.052,.043,.038]
v=[];f=[];n=16
for k,p in enumerate(path):
 t=path[min(k+1,len(path)-1)]-path[max(k-1,0)];t/=np.linalg.norm(t)
 s=np.cross(t,[0,0,1.]);s/=np.linalg.norm(s);u=np.cross(t,s);u/=np.linalg.norm(u)
 for i in range(n):
  a=2*math.pi*i/n;v.append(p+radii[k]*(math.cos(a)*s+math.sin(a)*u))
for k in range(len(path)-1):
 for i in range(n):
  a=k*n+i;b=k*n+(i+1)%n;c=(k+1)*n+(i+1)%n;e=(k+1)*n+i;f.extend([[a,b,c],[a,c,e]])
add(trimesh.Trimesh(vertices=np.array(v),faces=np.array(f),process=True))
h=trimesh.creation.torus(major_radius=.17,minor_radius=.028,major_sections=32,minor_sections=10)
h.apply_transform(trimesh.transformations.rotation_matrix(math.pi/2,[0,1,0]));h.apply_translation([-.29,.34,0]);add(h)
for y in [.23,.45]: add(trimesh.creation.cylinder(radius=.055,height=.075,sections=20,transform=trimesh.transformations.translation_matrix([-.245,y,0])))
add(trimesh.creation.cylinder(radius=.18,height=.025,sections=48,transform=trimesh.transformations.translation_matrix([0,.13,0])))
scene=trimesh.Scene()
for i,m in enumerate(parts): scene.add_geometry(m,node_name=f"IronKettlePart{i}",geom_name=f"IronKettlePart{i}")
scene.export(out);print(out,os.path.getsize(out))
