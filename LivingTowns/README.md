# Living Towns 0.4.53

- Survivalcraft 2.4.0.0 / Survivalcraft API 1.9.5.1.
- Each villager is assigned a unique home door and named wall-sign sleeping position.
- At dusk villagers stop working, return to their home, and remain awake until night; at night they sleep and wake at dawn.
- If the named sign cannot be resolved, villagers search for an indoor home rest position first, then a safe position at the home elevation. A remote work position is never accepted as a fake home.
- Doorway navigation has its own no-progress watchdog and retries a direct path when the doorway sequence stalls.
- Lumberjacks search for grounded trees within 100 blocks of home, lock onto one tree at a time, and harvest connected wood and leaves.
- Lumberjacks use a wider 3–5 block standing perimeter around trunks. Pathfinder range, progress tracking and harvest range are synchronized; random route recovery is disabled during lumberjack movement.
- Home-return progress is measured against the active doorway waypoint rather than the final bed. A villager waiting for a shared doorway reservation no longer triggers a false stuck-route bypass.
- Failed work approaches are temporarily rejected so the villager can try a different side rather than orbiting a blocked trunk.
- Personal storage uses the villager's own named wall sign to locate the closest chest on either side of the sign. Gathered items transfer during the home/night routine; job tools and seeds remain in the villager inventory.
- The GitHub Actions workflow compiles and verifies the C# assembly and modinfo version. The distributable package contains the compiled assembly and modinfo.json.

Build validation confirms compilation and package integrity. Actual Android in-game behavior still requires testing in the user's world.

- Version 0.4.46 resolves the assigned door bottom-half coordinate, aligns entry/exit waypoints to villager standing height, and fails safely instead of routing directly through a wall when the home door cannot be resolved.


## Version 0.4.53

- Profession detection now matches custom model route names case-insensitively across folder/file names, common aliases, file extensions and either slash direction.
- If a custom model publishes its route after the villager component loads, the controller retries role detection once per second until a profession is recognized.
- Farmer, lumberjack, guard, miner, blacksmith, merchant and healer role IDs are mapped to their existing AI routines. Healer currently patrols; guard currently patrols and does not yet have combat AI.
- The shared villager day/night, home-return and sleep routines remain unchanged.
- This source repository currently contains C# source and text notes, not the new GLB/XDB model templates. The workflow compiles and uploads the assembly only; model-template packaging and in-game behavior still need confirmation in the target world.
