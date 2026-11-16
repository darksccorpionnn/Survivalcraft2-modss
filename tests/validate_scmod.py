#!/usr/bin/env python3
"""Validate a Living Towns .scmod archive without launching the game."""
import argparse, csv, hashlib, io, json, sys, xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile, BadZipFile

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("package", help="Path to LivingTowns .scmod")
    args = ap.parse_args()
    path = Path(args.package)
    checks = []
    def add(name, ok, detail):
        checks.append((name, "PASS" if ok else "FAIL", detail))
    try:
        with ZipFile(path) as z:
            bad = z.testzip()
            add("archive_crc", bad is None, "All ZIP members pass CRC." if bad is None else f"Corrupt member: {bad}")
            names = set(z.namelist())
            required = ["modinfo.json","LivingTowns.dll","LivingTownsTradeStockAddon.dll",
                        "LivingTownsSheepAddon.dll","Assets/LivingTownsDatabase.xdb",
                        "Assets/LivingTownsBlocksData.csv","Assets/Lang/en-US.json","Assets/Lang/tr-TR.json"]
            for item in required:
                add("required_"+item.replace("/","_"), item in names, "Required archive entry present." if item in names else "Required archive entry missing.")
            if "modinfo.json" in names:
                info=json.loads(z.read("modinfo.json"))
                add("version_0_4_144", info.get("Version")=="0.4.144", f"Version={info.get('Version')}")
                add("api_version", info.get("ApiVersion")=="1.9.5.1", f"ApiVersion={info.get('ApiVersion')}")
                add("game_version", info.get("ScVersion")=="2.4.0.0", f"ScVersion={info.get('ScVersion')}")
                add("package_name", info.get("PackageName")=="darksccorpionnn.LivingTowns", f"PackageName={info.get('PackageName')}")
            if "Assets/LivingTownsDatabase.xdb" in names:
                root=ET.fromstring(z.read("Assets/LivingTownsDatabase.xdb"))
                entities={e.attrib.get("Name"):e for e in root.iter("EntityTemplate")}
                expected=["LivingTowns_MaleVillager","LivingTowns_FemaleVillager","LivingTowns_Farmer",
                          "LivingTowns_Blacksmith","LivingTowns_Merchant","LivingTowns_Healer",
                          "LivingTowns_Guard","LivingTowns_Hunter","LivingTowns_Shepherd",
                          "LivingTowns_Sheep","LivingTowns_Ram","LivingTowns_Lamb"]
                for entity in expected:
                    add("entity_"+entity, entity in entities, "Entity template present." if entity in entities else "Entity template missing.")
                for entity in expected[:9]:
                    members={m.attrib.get("Name") for m in entities.get(entity, ET.Element("x")).findall("MemberComponentTemplate")}
                    add("villager_controller_"+entity, "LivingTownsVillagerController" in members,
                        "Villager controller registered." if "LivingTownsVillagerController" in members else "Villager controller missing.")
                for entity in expected[9:]:
                    members={m.attrib.get("Name") for m in entities.get(entity, ET.Element("x")).findall("MemberComponentTemplate")}
                    add("sheep_behavior_"+entity, "LivingTownsSheepBehavior" in members,
                        "Sheep behavior registered." if "LivingTownsSheepBehavior" in members else "Sheep behavior missing.")
            for lang in ["Assets/Lang/en-US.json","Assets/Lang/tr-TR.json"]:
                if lang in names:
                    json.loads(z.read(lang))
                    add("json_"+lang.replace("/","_"), True, "Localization JSON parses.")
            anims=[n for n in names if n.startswith("Assets/Animations/") and n.endswith(".json")]
            for name in anims:
                json.loads(z.read(name))
            add("animation_json", len(anims)>=12, f"{len(anims)} animation JSON files parse.")
            if "Assets/LivingTownsBlocksData.csv" in names:
                rows=list(csv.DictReader(io.StringIO(z.read("Assets/LivingTownsBlocksData.csv").decode("utf-8-sig")), delimiter=";"))
                classes={r.get("Class Name") for r in rows}
                add("block_registry_rows", len(rows)>0, f"{len(rows)} CSV rows.")
            for dll in ["LivingTowns.dll","LivingTownsTradeStockAddon.dll","LivingTownsSheepAddon.dll"]:
                if dll in names:
                    data=z.read(dll)
                    add("dll_nonempty_"+dll, len(data)>0, f"{len(data)} bytes; SHA256={hashlib.sha256(data).hexdigest()}")
    except (BadZipFile, KeyError, json.JSONDecodeError, ET.ParseError, UnicodeDecodeError) as exc:
        print("PACKAGE VALIDATION ERROR:", repr(exc))
        return 2
    for name,status,detail in checks:
        print(f"[{status}] {name}: {detail}")
    passed=sum(s=="PASS" for _,s,_ in checks)
    failed=sum(s=="FAIL" for _,s,_ in checks)
    print(f"\nSUMMARY: PASS={passed} FAIL={failed}")
    return 1 if failed else 0
if __name__=="__main__":
    sys.exit(main())
