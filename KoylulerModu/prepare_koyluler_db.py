#!/usr/bin/env python3
"""Prepare the standalone villager DB after restoring the compressed source."""
import sys
import uuid
import xml.etree.ElementTree as ET
from pathlib import Path

DB = Path(sys.argv[1] if len(sys.argv) > 1 else "KoylulerModu/Assets/KoylulerDatabase.xdb")
NAMES = ["MaleVillager", "FemaleVillager", "Farmer", "Blacksmith", "Merchant", "Healer", "Guard", "Hunter", "Shepherd"]

def guid(key):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "darksccorpionnn.KoylulerModu/0.2.1/" + key))

root = ET.parse(DB).getroot()

def find(tag, name):
    return next((e for e in root if e.tag == tag and e.attrib.get("Name") == name), None)

identity = find("ComponentTemplate", "KoylulerIdentity")
if identity is None:
    identity = ET.Element("ComponentTemplate", {
        "Name": "KoylulerIdentity", "Guid": guid("Component/KoylulerIdentity"),
        "InheritanceParent": "b05700ed-7e4e-4679-98f5-b597f421496b"})
    ET.SubElement(identity, "Parameter", {
        "Name": "Class", "Guid": guid("Component/KoylulerIdentity/Class"),
        "Value": "Game.ComponentKoyluIdentity", "Type": "string"})
    root.insert(0, identity)

controller = find("ComponentTemplate", "LivingTownsVillagerController")
if controller is None:
    controller = ET.Element("ComponentTemplate", {
        "Name": "LivingTownsVillagerController", "Guid": guid("Component/LivingTownsVillagerController"),
        "InheritanceParent": "b05700ed-7e4e-4679-98f5-b597f421496b"})
    ET.SubElement(controller, "Parameter", {
        "Name": "Class", "Guid": guid("Component/LivingTownsVillagerController/Class"),
        "Value": "Game.ComponentLivingTownsVillager", "Type": "string"})
    root.insert(1, controller)

primary = {}
for name in NAMES:
    entity_name = "KoylulerModu_" + name
    entity = find("EntityTemplate", entity_name)
    if entity is None:
        raise SystemExit("Missing base template: " + entity_name)
    primary[entity_name] = entity
    if not any(c.tag == "MemberComponentTemplate" and c.attrib.get("Name") == "LivingTownsVillagerController" for c in entity):
        member = ET.SubElement(entity, "MemberComponentTemplate", {
            "Name": "LivingTownsVillagerController", "Guid": guid(entity_name + "/Controller"),
            "InheritanceParent": controller.attrib["Guid"]})
        ET.SubElement(member, "Parameter", {
            "Name": "IsOptional", "Guid": guid(entity_name + "/Controller/IsOptional"),
            "Value": "False", "Type": "bool"})
    if not any(c.tag == "MemberComponentTemplate" and c.attrib.get("Name") == "KoylulerIdentity" for c in entity):
        ET.SubElement(entity, "MemberComponentTemplate", {
            "Name": "KoylulerIdentity", "Guid": guid(entity_name + "/Identity"),
            "InheritanceParent": identity.attrib["Guid"]})


# Add the nine distinct creature eggs (indices 700-708) to the player-facing templates.
egg_data = {
    "MaleVillager": (163, "184,126,82", 700, "Erkek Köylü Yumurtası"),
    "FemaleVillager": (164, "205,155,130", 701, "Kadın Köylü Yumurtası"),
    "Farmer": (165, "166,119,74", 702, "Çiftçi Yumurtası"),
    "Blacksmith": (163, "72,72,78", 703, "Demirci Yumurtası"),
    "Merchant": (163, "52,94,160", 704, "Tüccar Yumurtası"),
    "Healer": (163, "110,160,120", 705, "Şifacı Yumurtası"),
    "Guard": (163, "100,108,120", 706, "Muhafız Yumurtası"),
    "Hunter": (163, "116,126,145", 707, "Avcı Yumurtası"),
    "Shepherd": (165, "170,145,112", 708, "Çoban Yumurtası"),
}
for short_name, (texture_slot, color, egg_index, _label) in egg_data.items():
    entity = primary["KoylulerModu_" + short_name]
    egg = next((x for x in entity.findall("ParameterSet")
                if x.attrib.get("Name") == "CreatureEggData"), None)
    if egg is None:
        egg = ET.SubElement(entity, "ParameterSet", {
            "Name": "CreatureEggData", "Guid": guid("Entity/KoylulerModu_" + short_name + "/CreatureEggData")})
    parameters = {p.attrib.get("Name"): p for p in egg.findall("Parameter")}
    values = {
        "TextureSlot": str(texture_slot),
        "Color": color,
        "EggTypeIndex": str(egg_index),
        "DisplayName": "[DisplayName:KoylulerModu_" + short_name + "Egg]",
        "ShowEgg": "True",
        "SwapUV": "True",
    }
    for key, value in values.items():
        expected_types = {
            "TextureSlot": "int",
            "Color": "Color",
            "EggTypeIndex": "int",
            "DisplayName": "string",
            "ShowEgg": "bool",
            "SwapUV": "bool",
        }
        if key in parameters:
            parameters[key].attrib["Value"] = value
            parameters[key].attrib["Type"] = expected_types[key]
        else:
            ET.SubElement(egg, "Parameter", {
                "Name": key, "Guid": guid("Entity/KoylulerModu_" + short_name + "/CreatureEggData/" + key),
                "Value": value, "Type": expected_types[key]})

# The door spawner in LivingTowns.dll requests these historical template names.
# Aliases use the new models and controller, and intentionally do not duplicate eggs.
for name in NAMES:
    alias_name = "LivingTowns_" + name
    if find("EntityTemplate", alias_name) is not None:
        continue
    source = primary["KoylulerModu_" + name]
    alias = ET.fromstring(ET.tostring(source, encoding="unicode"))
    alias.attrib["Name"] = alias_name
    alias.attrib["Guid"] = guid("Entity/" + alias_name)
    for child in list(alias):
        if child.tag == "ParameterSet" and child.attrib.get("Name") == "CreatureEggData":
            alias.remove(child)
            continue
        if child.tag == "MemberComponentTemplate":
            component_name = child.attrib.get("Name", "component")
            child.attrib["Guid"] = guid(alias_name + "/" + component_name)
            for param in child.findall("Parameter"):
                param.attrib["Guid"] = guid(alias_name + "/" + component_name + "/" + param.attrib.get("Name", "parameter"))
        elif child.tag == "ParameterSet":
            set_name = child.attrib.get("Name", "parameters")
            child.attrib["Guid"] = guid(alias_name + "/" + set_name)
            for param in child.findall("Parameter"):
                param.attrib["Guid"] = guid(alias_name + "/" + set_name + "/" + param.attrib.get("Name", "parameter"))
    root.append(alias)

# Register the door behavior as a project subsystem; the standalone loader does
# not add random surface spawning.
project = find("ProjectTemplate", "Project")
if project is None:
    project = ET.Element("ProjectTemplate", {
        "Name": "Project", "Guid": "85023bf8-1c90-4dd1-9442-e6c13691d078"})
    root.append(project)
if not any(s.tag == "MemberSubsystemTemplate" and s.attrib.get("Name") == "LivingTownsDoorBehavior" for s in project):
    subsystem = ET.SubElement(project, "MemberSubsystemTemplate", {
        "Name": "LivingTownsDoorBehavior", "Guid": "49474126-82a0-4f88-9b22-2890af9322d2",
        "InheritanceParent": "fefb9590-4972-4893-b02a-76063611b745"})
    ET.SubElement(subsystem, "Parameter", {
        "Name": "Class", "Guid": "feff26ac-36a9-4ad6-99c0-907a2ac8340d",
        "Value": "Game.SubsystemLivingTownsDoorBehavior", "Type": "string"})
    ET.SubElement(subsystem, "Parameter", {
        "Name": "Dependencies", "Guid": "3bff6c5c-3ad3-48c8-ae61-bdb812cc9e13",
        "Value": "Terrain, CreatureSpawn", "Type": "string"})

ET.indent(root, space="  ")
ET.ElementTree(root).write(DB, encoding="utf-8", xml_declaration=True)

check = ET.parse(DB).getroot()
entities = check.findall("EntityTemplate")
actual = {e.attrib.get("Name") for e in entities}
expected = {"KoylulerModu_" + n for n in NAMES} | {"LivingTowns_" + n for n in NAMES}
if len(entities) != 18 or actual != expected:
    raise SystemExit(f"Unexpected entities: count={len(entities)}, missing={expected-actual}, extra={actual-expected}")
project = next((e for e in check if e.tag == "ProjectTemplate" and e.attrib.get("Name") == "Project"), None)
if project is None or not any(s.attrib.get("Name") == "LivingTownsDoorBehavior" for s in project.findall("MemberSubsystemTemplate")):
    raise SystemExit("Door subsystem was not added")
for entity in entities:
    comps = {c.attrib.get("Name") for c in entity.findall("MemberComponentTemplate")}
    if not {"HumanModel", "Creature", "Locomotion", "LivingTownsVillagerController", "KoylulerIdentity"}.issubset(comps):
        raise SystemExit("Missing components in " + entity.attrib["Name"])
print("PASS: 9 player templates + 9 LivingTowns door-spawn aliases; identity/controller on all 18; door subsystem registered.")