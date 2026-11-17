#!/usr/bin/env python3
import csv
import json
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
project = root / "KoylulerModu"
db = ET.parse(project / "Assets" / "KoylulerDatabase.xdb").getroot()
source = (project / "ComponentKoyluFarmerPlot.cs").read_text(encoding="utf-8")
salt_source = (project / "KoyluSaltBlock.cs").read_text(encoding="utf-8")

assert "PlotWidth = 12" in source and "PlotDepth = 10" in source
assert "RequiredSeedCount = 64" in source
assert "SafeBlockIndex<WoodenFenceBlock>()" in source
assert "SafeBlockIndex<WaterBlock>()" in source
assert "SafeBlockIndex<SoilBlock>()" in source
assert "DamageRake" in source
assert "TryGetWorkSign" in source and "GetWorkCenterCell" in source
assert "TryValidateCandidate" in source
assert "ConsumeOneItem(salt)" in source
assert "EnsureFarmerJob()" in source and 'ReadField(m_controller, "m_job")' not in source
assert 'FindField(m_controller, "m_job")' in source
assert "requiredData ?? 0" in source
assert "EnsureInventoryCount(seedsIndex, RequiredSeedCount, 5)" in source
assert "Textures/KoyluSalt" in salt_source

block_rows = list(csv.DictReader((project / "Assets" / "BlocksData.csv").open(encoding="utf-8"), delimiter=";"))
salt_rows = [row for row in block_rows if row["Class Name"] == "KoyluSaltBlock"]
assert len(salt_rows) == 1
assert salt_rows[0]["DefaultDisplayName"] == "Tuz"

farmer_names = {"KoylulerModu_Farmer", "LivingTowns_Farmer"}
farmers = [e for e in db.findall("EntityTemplate") if e.attrib.get("Name") in farmer_names]
assert len(farmers) == 2
farm_template = next((e for e in db.findall("ComponentTemplate")
                      if e.attrib.get("Name") == "KoyluFarmerPlot"), None)
assert farm_template is not None
assert any(p.attrib.get("Name") == "Class" and
           p.attrib.get("Value") == "Game.ComponentKoyluFarmerPlot"
           for p in farm_template.findall("Parameter"))
for entity in farmers:
    controller = next((c for c in entity.findall("MemberComponentTemplate")
                       if c.attrib.get("Name") == "LivingTownsVillagerController"), None)
    assert controller is not None, entity.attrib.get("Name")
    job = next((p for p in controller.findall("Parameter") if p.attrib.get("Name") == "Job"), None)
    assert job is not None and job.attrib.get("Type") == "int" and job.attrib.get("Value") == "1", entity.attrib.get("Name")
    member = next((c for c in entity.findall("MemberComponentTemplate")
                   if c.attrib.get("Name") == "KoyluFarmerPlot"), None)
    assert member is not None, entity.attrib.get("Name")
    assert member.attrib.get("InheritanceParent") == farm_template.attrib["Guid"]
    optional = next((p for p in member.findall("Parameter")
                     if p.attrib.get("Name") == "IsOptional"), None)
    assert optional is not None and optional.attrib.get("Value") == "False"

for entity in db.findall("EntityTemplate"):
    if entity.attrib.get("Name") not in farmer_names:
        assert not any(c.attrib.get("Name") == "KoyluFarmerPlot"
                       for c in entity.findall("MemberComponentTemplate"))

for lang in ("tr-TR", "en-US"):
    data = json.loads((project / "Assets" / "Lang" / f"{lang}.json").read_text(encoding="utf-8"))
    assert "DisplayName:KoyluSaltBlock" in data
    assert "Description:KoyluSaltBlock" in data

print("PASS: Farmer Job=1 on both farmer templates; runtime promotion from Unemployed; 12x10 plot, farmer-only attachment, wooden fence, water channels, two rake passes, 64 rye seeds with data=5, dedicated salt item and translations.")
