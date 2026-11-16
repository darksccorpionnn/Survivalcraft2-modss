# Living Towns Test Harness 0.2.2

Compatibility:
- Survivalcraft 2.4.0.0
- Survivalcraft API 1.9.5.1
- Living Towns 0.4.144 family

## Wolf versus bull combat observer

This is a read-only observation test. It does not spawn creatures, injure entities, change health, move animals, or edit terrain.

1. Install the current Living Towns package and this harness as separate mods.
2. Open a disposable test world.
3. Make sure a wolf and a bull are loaded and close enough for their normal AI to interact.
4. Tap **WATCH** before the fight starts.
5. Let the animals fight naturally. The button changes to **STOP** while observation is active.
6. Tap **STOP** and share log lines prefixed with `[LivingTownsCombatTest]`.

The harness now distinguishes these two directions:
- Wolf to bull: it records the engine's native wolf melee event and verifies the bull's health change.
- Bull to wolf: it records a separate bull-to-wolf native `MeleeAttackment` event at the engine strike/hit callback. A later decrease in wolf health is counted as a confirmed bull retaliation only when the matching native bull event for that exact bull and wolf was observed in the correlation window.
- If wolf health decreases after the wolf's hit but no matching bull-to-wolf native melee event was observed, the harness logs `FAIL HEALTH_LOSS_WITHOUT_NATIVE_BULL_MELEE`. A health decrease alone can no longer pass as a counterattack.
- A native bull melee event without a corresponding health decrease is reported separately as `BULL_NATIVE_MELEE_NO_HEALTH_LOSS`.
- It also checks the 12% damage cap, one-hit deaths, and the 2.5-second cooldown between bull native melee events.

**Important:** `ProcessAttackment` is the engine's native melee strike/hit callback. This harness verifies that event and its resulting health change; it does not independently inspect the rendered animation clip. Visual animation playback and synchronization still need an in-game visual check.

Species labels are read from runtime metadata. The harness does not guess species when it cannot identify a wolf or bull.

## Existing diagnostic scan

The WATCH button first scans loaded bodies, Living Towns villagers, sheep-family components, health integrity and duplicate villager controllers. Results use PASS, FAIL and BLOCKED. A BLOCKED result means the required entity was not loaded or the scenario was not available, not that it passed.

## Limits

This test does not control AI or guarantee that the animals will choose to fight. A missing attack is not evidence that damage handling passed. The in-game fight itself must occur in Survivalcraft; static CI cannot launch the Android game.
