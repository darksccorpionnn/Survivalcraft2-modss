from pathlib import Path
import json

root=Path(__file__).resolve().parents[1] / "NaturalVillages-Medieval-V0.10.1"
assert (root/"Source"/"MedievalHouseGenerator.cs").is_file()

for name in ("MedievalHouseGenerator.cs", "MedievalWellGenerator.cs"):
    p=root/"Source"/name
    s=p.read_text(encoding="utf-8")
    old='VillageTerrainHelper.FindBlock("JackOLanternBlock")'
    assert s.count(old)==1, (name,s.count(old))
    s=s.replace(old,'VillageTerrainHelper.FindBlock("WickerLampBlock","JackOLanternBlock")')
    s=s.replace("V0.10 ", "V0.10.1 ")
    p.write_text(s,encoding="utf-8")

info=root/"modinfo.json"
j=json.loads(info.read_text(encoding="utf-8"))
assert j["Version"]=="0.10.0"
j["Version"]="0.10.1"
j["Description"]="EXPERIMENTAL V0.10.1: Wicker Lamp is preferred for house, garden, entrance and well lights; all V0.10 compact village features retained."
info.write_text(json.dumps(j,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")

pkg=root/"package_mod.py"
s=pkg.read_text(encoding="utf-8")
assert "V0.10.scmod" in s
pkg.write_text(s.replace("V0.10.scmod","V0.10.1.scmod"),encoding="utf-8")

h=(root/"Source"/"MedievalHouseGenerator.cs").read_text(encoding="utf-8")
w=(root/"Source"/"MedievalWellGenerator.cs").read_text(encoding="utf-8")
assert 'Light=VillageTerrainHelper.FindBlock("WickerLampBlock","JackOLanternBlock")' in h
assert 'int lamp=VillageTerrainHelper.FindBlock("WickerLampBlock","JackOLanternBlock")' in w
assert '"WoodenFenceGateBlock"' in h and '"StoneBrickBlock"' in h
assert '"CobblestoneBlock"' in (root/"Source"/"VillageRoadGenerator.cs").read_text()
print("Wicker lamp source patch and regression checks PASS")
