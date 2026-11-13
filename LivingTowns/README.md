# Living Towns 0.4.32

- Keeps the 0.4.31 named-sign sleeping, shared-house door reservation and shared doorway routing behavior.
- Farmer now holds one selected plot task until it is finished; it no longer replaces its destination every AI tick while walking to another plot.
- Farmer and lumberjack abandon blocked work routes, temporarily blacklist the failed target/approach, and search for another reachable position.
- Lumberjack recognizes grounded vertical trunks, starts at the uppermost connected log and works downward from a safe ground-level position. Floating canopy remnants are not treated as new trees.
- Job route failures use the existing progress watchdog and do not invoke random path recovery into obstacles.
- Build workflow compiles the assembly and packages a directly installable .scmod artifact.
- Build validation is compile/package validation; actual Android in-game behavior still requires testing in the user's world.

Build target branch: `livingtowns-0.4.32-villager-ai`.
