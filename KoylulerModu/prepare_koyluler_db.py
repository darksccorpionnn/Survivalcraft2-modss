#!/usr/bin/env python3
"""Prepare the standalone villager DB with one coordinated movement owner."""
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

def set_component_parameter(entity, component_name, parameter_name, value, value_type):
    component = next((c for c in entity.findall("MemberComponentTemplate")
                      if c.attrib.get("Name") == component_name), None)
    if component is None:
        raise SystemExit(f"{entity.attrib.get('Name')}: missing {component_name}")
    parameter = next((p for p in component.findall("Parameter")
                      if p.attrib.get("Name") == parameter_name), None)
    if parameter is None:
        parameter = ET.SubElement(component, "Parameter", {
            "Name": parameter_name,
            "Guid": guid(f"Entity/{entity.attrib.get('Name')}/{component_name}/{parameter_name}"),
            "Value": str(value),
            "Type": value_type,
        })
    else:
        parameter.attrib["Value"] = str(value)
        parameter.attrib["Type"] = value_type

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
    identity_member = next((c for c in entity.findall("MemberComponentTemplate")
                            if c.attrib.get("Name") == "KoylulerIdentity"), None)
    if identity_member is None:
        identity_member = ET.SubElement(entity, "MemberComponentTemplate", {
            "Name": "KoylulerIdentity",
            "Guid": guid(entity_name + "/Identity"),
            "InheritanceParent": identity.attrib["Guid"]})
    else:
        # Custom component members must inherit the ComponentTemplate that
        # declares their runtime Class. Without this link Entity construction
        # fails with "Required value Class not found in values dictionary".
        identity_member.attrib["InheritanceParent"] = identity.attrib["Guid"]

# Add the nine distinct creature eggs (indices 700-708) to player-facing templates.
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
    expected_types = {
        "TextureSlot": "int", "Color": "Color", "EggTypeIndex": "int",
        "DisplayName": "string", "ShowEgg": "bool", "SwapUV": "bool",
    }
    for key, value in values.items():
        if key in parameters:
            parameters[key].attrib["Value"] = value
            parameters[key].attrib["Type"] = expected_types[key]
        else:
            ET.SubElement(egg, "Parameter", {
                "Name": key, "Guid": guid("Entity/KoylulerModu_" + short_name + "/CreatureEggData/" + key),
                "Value": value, "Type": expected_types[key]})

# The LivingTowns door spawner requests these historical template names.
# Aliases reuse the same models, identity and movement configuration; they do not duplicate eggs.
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

# Every entity member component must explicitly declare IsOptional.
# Missing this value makes Entity construction fail at world load.
for entity in [e for e in root.findall("EntityTemplate")
               if e.attrib.get("Name", "").startswith(("KoylulerModu_", "LivingTowns_"))]:
    for member in entity.findall("MemberComponentTemplate"):
        optional = next((p for p in member.findall("Parameter")
                         if p.attrib.get("Name") == "IsOptional"), None)
        if optional is None:
            ET.SubElement(member, "Parameter", {
                "Name": "IsOptional",
                "Guid": guid("Entity/" + entity.attrib["Name"] + "/" + member.attrib.get("Name", "Component") + "/IsOptional"),
                "Value": "False",
                "Type": "bool",
            })
        else:
            optional.attrib["Value"] = "False"
            optional.attrib["Type"] = "bool"

# Coordinate movement: LivingTownsVillagerController is the sole destination owner.
# The inherited vanilla WalkAroundBehavior otherwise picks independent roam targets,
# which can overwrite a job/home/door/bed destination while the controller is routing.
all_villagers = [e for e in root.findall("EntityTemplate")
                 if e.attrib.get("Name", "").startswith(("KoylulerModu_", "LivingTowns_"))]
expected_names = {prefix + name for prefix in ("KoylulerModu_", "LivingTowns_") for name in NAMES}
if {e.attrib.get("Name") for e in all_villagers} != expected_names or len(all_villagers) != 18:
    raise SystemExit("Expected exactly 18 villager templates before movement normalization")

for entity in all_villagers:
    set_component_parameter(entity, "WalkAroundBehavior", "Class", "Game.ComponentNoEffect", "string")
    set_component_parameter(entity, "Locomotion", "WalkSpeed", "2.2", "float")
    set_component_parameter(entity, "Locomotion", "TurnSpeed", "8", "float")
    set_component_parameter(entity, "Locomotion", "AccelerationFactor", "1.5", "float")
    set_component_parameter(entity, "Body", "MaxSmoothRiseHeight", "0.5", "float")

# Register the door behavior as a project subsystem; the standalone loader does not
# add random surface spawning.
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
if len(entities) != 18 or actual != expected_names:
    raise SystemExit(f"Unexpected entities: count={len(entities)}, missing={expected_names-actual}, extra={actual-expected_names}")
for entity in entities:
    name = entity.attrib.get("Name")
    comps = {c.attrib.get("Name"): c for c in entity.findall("MemberComponentTemplate")}
    if not {"HumanModel", "Creature", "Locomotion", "LivingTownsVillagerController", "KoylulerIdentity", "WalkAroundBehavior"}.issubset(comps):
        raise SystemExit("Missing required component in " + name)
    if comps["KoylulerIdentity"].attrib.get("InheritanceParent") != identity.attrib["Guid"]:
        raise SystemExit("KoylulerIdentity has no valid Class template link in " + name)
    walk_class = next((p.attrib.get("Value") for p in comps["WalkAroundBehavior"].findall("Parameter")
                       if p.attrib.get("Name") == "Class"), None)
    if walk_class != "Game.ComponentNoEffect":
        raise SystemExit("Conflicting WalkAroundBehavior remains active in " + name)
    locomotion = {p.attrib.get("Name"): p.attrib.get("Value") for p in comps["Locomotion"].findall("Parameter")}
    if locomotion.get("WalkSpeed") != "2.2" or locomotion.get("TurnSpeed") != "8" or locomotion.get("AccelerationFactor") != "1.5":
        raise SystemExit("Movement tuning mismatch in " + name)
    body = {p.attrib.get("Name"): p.attrib.get("Value") for p in comps["Body"].findall("Parameter")}
    if body.get("MaxSmoothRiseHeight") != "0.5":
        raise SystemExit("Step/rise tuning mismatch in " + name)
project = next((e for e in check if e.tag == "ProjectTemplate" and e.attrib.get("Name") == "Project"), None)
if project is None or not any(s.attrib.get("Name") == "LivingTownsDoorBehavior" for s in project.findall("MemberSubsystemTemplate")):
    raise SystemExit("Door subsystem was not added")
print("PASS: 9 player templates + 9 door-spawn aliases; saved identities; single movement owner; common speed/turn/rise settings; door subsystem.")
