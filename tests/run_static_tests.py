#!/usr/bin/env python3
"""Static validation for the Survivalcraft 2 mod repository.

This does not launch Survivalcraft. Missing main-mod source is reported as BLOCKED,
not treated as a passing in-game test.
"""
from pathlib import Path
import json
import sys
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]
checks = []

def check(name, passed, detail):
    checks.append({"name": name, "status": "PASS" if passed else "FAIL", "detail": detail})

def blocked(name, detail):
    checks.append({"name": name, "status": "BLOCKED", "detail": detail})

sheep_loader = ROOT / "LivingTownsSheepAddon/LivingTownsSheepAddonModLoader.cs"
sheep_behavior = ROOT / "LivingTownsSheepAddon/ComponentLivingTownsSheepBehavior.cs"
sheep_project = ROOT / "LivingTownsSheepAddon/LivingTownsSheepAddon.csproj"
town_project = ROOT / "TownBootstrap/TownBootstrap.csproj"

check("sheep_addon_source_present", sheep_loader.is_file() and sheep_behavior.is_file(),
      "Sheep addon loader and behavior source files are present.")
check("sheep_addon_targets_net10", sheep_project.is_file() and "net10.0" in sheep_project.read_text(encoding="utf-8"),
      "Sheep addon project targets .NET 10.")
check("town_bootstrap_project_present", town_project.is_file(),
      "TownBootstrap project is present.")

if sheep_loader.is_file():
    source = sheep_loader.read_text(encoding="utf-8")
    for hook in ("OnLoadingFinished", "GuiUpdate", "ProcessAttackment",
                 "CalculateCreatureInjuryAmount", "InitializeCreatureTypes"):
        check("sheep_hook_" + hook, hook in source,
              "Expected hook registration/name appears in the sheep addon source.")
else:
    blocked("sheep_hook_registration", "Sheep addon source is missing.")

if sheep_behavior.is_file():
    source = sheep_behavior.read_text(encoding="utf-8")
    for token in ("public override void Save(", "public override void Load(",
                  "TryTame()", "TryMilk()", "UpdateReproduction()"):
        check("sheep_behavior_" + token.replace(" ", "_").replace("(", ""),
              token in source, "Expected sheep behavior/persistence feature appears in source.")
else:
    blocked("sheep_behavior_features", "Sheep behavior source is missing.")

# The repository currently contains TownBootstrap and a sheep addon, not the
# main Living Towns NPC/job/trade/home implementation.
main_mod_candidates = [
    ROOT / "LivingTowns" / "LivingTowns.csproj",
    ROOT / "LivingTowns" / "ModInfo.json",
    ROOT / "LivingTowns-Source.scmod",
]
main_mod_found = any(p.exists() for p in main_mod_candidates)
if main_mod_found:
    check("main_living_towns_source_present", True,
          "A main Living Towns source/package candidate is present; deeper integration tests are still required.")
else:
    blocked("villagers_jobs_homes_doors_trade_save_load",
            "The repository does not contain the main Living Towns source/package. Upload the exact current .scmod or source project to enable these tests.")

# Actual game runtime tests always remain pending until an in-game harness is integrated.
blocked("android_in_game_ai_and_world_tests",
        "Static CI cannot launch the Android game or observe live NPC/animal AI. Requires an in-game test harness and a dedicated test world.")

report = {
    "suite": "Survivalcraft 2 Living Towns - initial static suite",
    "scope": "Repository-visible source only; not an in-game certification",
    "summary": {
        "passed": sum(x["status"] == "PASS" for x in checks),
        "failed": sum(x["status"] == "FAIL" for x in checks),
        "blocked": sum(x["status"] == "BLOCKED" for x in checks),
    },
    "checks": checks,
}
out = ROOT / "test-results"
out.mkdir(exist_ok=True)
(out / "LivingTowns-TestReport.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
lines = [report["suite"], report["scope"], "",
         f"PASS: {report['summary']['passed']}  FAIL: {report['summary']['failed']}  BLOCKED: {report['summary']['blocked']}", ""]
for item in checks:
    lines.append(f"[{item['status']}] {item['name']}: {item['detail']}")
(out / "LivingTowns-TestReport.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
print("\n".join(lines))
if report["summary"]["failed"]:
    sys.exit(1)
