# Living Towns Test Harness 0.1.0

Compatibility:
- Survivalcraft 2.4.0.0
- Survivalcraft API 1.9.5.1

## Behavior

This is a separate read-only diagnostics mod. It performs one scan automatically when the player HUD becomes available. The TEST button can repeat the scan later.

The scan:
- inspects loaded world bodies;
- detects Living Towns villagers and sheep-family entities by their runtime components;
- checks health components, duplicate villager controllers, core/add-on assemblies, and selected runtime types;
- writes PASS, FAIL, and BLOCKED lines to the Survivalcraft log.

It does not spawn, injure, move, edit, or delete entities or terrain.

## Limits

A PASS only means the specific inspection succeeded. It does not certify jobs, sleep, home/door routing, trade transactions, animal AI/combat, or save/load behavior. Those scenarios are reported as BLOCKED until their dedicated controlled tests are implemented.

## Install

1. Keep Living Towns 0.4.144 installed.
2. Install LivingTownsTestHarness.scmod as a separate mod; do not merge it into the main package.
3. Start a disposable test world and load it.
4. The initial scan runs automatically; use TEST in the HUD for another scan.
5. Share log lines prefixed with [LivingTownsTest].
