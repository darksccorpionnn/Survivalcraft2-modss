# Living Towns 0.4.54

- Target: Survivalcraft 2.4.0.0 / Survivalcraft API 1.9.5.1.
- Original male, female and farmer villagers keep their existing daily routines.
- Blacksmith, merchant, healer and guard templates use the same villager controller, body, locomotion, human model, spawn and inventory components.
- All seven villager templates can be selected by the existing door-based random spawn system.
- Profession routes are matched case-insensitively across folder/file names, aliases, extensions and either slash direction. If a custom model publishes its route after the controller loads, profession detection retries once per second until resolved.
- At dusk all villagers stop work and return home; at night they sleep; at dawn they leave through their own door. The shared bed, home-door and obstacle-navigation systems were not rewritten for this release.
- Healers seek injured LivingTowns villagers within range and restore health. They do not heal players or unrelated creatures.
- Guards patrol and defend against nearby land predators. They do not target players or LivingTowns villagers.
- Blacksmith and merchant keep their existing profession routines.
- The four supplied GLB models, animation configs, localization files and XDB templates are retained in the full .scmod package. The XDB has seven entity templates and 328 unique GUIDs with no duplicates.
- GitHub Actions compiles and uploads the C# assembly. The full .scmod package is assembled by preserving the prior model/template archive and replacing its DLL and metadata.
- Build succeeded with Survivalcraft API 1.9.5.1. Actual Android in-game behavior still requires testing in the user's world.


## Version 0.4.54

- Obstacle detour search radius increased from 3 to 6 blocks.
- Standing-cell search now checks floor levels up to 2 blocks above/below the villager.
- Detour scoring favors open cells with a meaningful side-step around walls and corners.
- Unreachable detour waypoints are temporarily blacklisted for 12 seconds; the villager then searches for a different route instead of retrying the same point indefinitely.
- Detour pathfinding budget increased to 1,800 positions.
- Shared villager home, sleep, work and profession routines remain unchanged.
