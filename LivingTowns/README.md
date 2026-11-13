# Living Towns 0.4.40

- Survivalcraft 2.4.0.0 / Survivalcraft API 1.9.5.1.
- Each villager is assigned a unique home door and named wall-sign sleeping position.
- At dusk villagers stop working, return to their home, and remain awake until night; at night they sleep and wake at dawn.
- If the named sign cannot be resolved, villagers search for an indoor home rest position first, then a safe position at the home elevation. A remote work position is never accepted as a fake home.
- Doorway navigation has its own no-progress watchdog and retries a direct path when the doorway sequence stalls.
- Lumberjacks search for grounded trees within 100 blocks of home, lock onto one tree at a time, and harvest connected wood and leaves.
- Lumberjacks use a wider collision-safe standing perimeter around trunks. Pathfinder range, progress tracking and harvest range are synchronized; random route recovery is disabled during lumberjack movement.
- Failed work approaches are temporarily rejected so the villager can try a different side rather than orbiting a blocked trunk.
- Personal storage uses the villager's own named wall sign to locate the closest chest on either side of the sign. Gathered items transfer during the home/night routine; job tools and seeds remain in the villager inventory.
- The build workflow verifies the C# assembly, modinfo version and packaged output.

Build validation confirms compilation and package integrity. Actual Android in-game behavior still requires testing in the user's world.
