# Living Towns Test Harness 0.2.1

Compatibility:
- Survivalcraft 2.4.0.0
- Survivalcraft API 1.9.5.1
- Living Towns 0.4.144

## Wolf versus bull combat observer

This is an observational test. It does not spawn creatures, injure entities, change health, move animals, or edit terrain.

1. Install Living Towns 0.4.144 and this harness as separate mods.
2. Open a disposable test world.
3. Make sure a wolf and a bull are loaded and close enough for their normal AI to interact.
4. Tap **WATCH** before the fight starts.
5. Let the animals fight naturally. The button changes to **STOP** while observation is active.
6. Tap **STOP** and share log lines prefixed with `[LivingTownsCombatTest]`.

The observer records actual melee-hook events, then checks later health values. It reports:
- whether wolf attacks caused real bull health loss;
- whether the bull caused a real wolf health loss after being hit;
- whether each observed hit stayed at or below 12% of the target's remaining health;
- one-hit deaths above the critical-health exception;
- whether another retaliation occurred before the full 2.5-second cooldown expired. The observer waits through the entire remaining cooldown before marking a probe as passed.

Species labels are read from runtime metadata. The harness does not guess species when it cannot identify a wolf or bull.

## Existing diagnostic scan

The same WATCH button first scans loaded bodies, Living Towns villagers, sheep-family components, health integrity and duplicate villager controllers. Results use PASS, FAIL and BLOCKED.

## Limits

This test does not control AI or guarantee that the animals will choose to fight. A missing attack is not evidence that damage handling passed. The in-game fight itself must occur in Survivalcraft; static CI cannot launch the Android game.
