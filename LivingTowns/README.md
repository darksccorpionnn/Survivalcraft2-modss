# Living Towns 0.4.23

- Door-created villagers, one villager per lower door half.
- Automatic opening and closing of normal (non-electrically controlled) doors while pathfinding.
- 3.15 m/s villager walk speed.
- Farmer gets an initial private Iron Rake + Rye Seeds kit once, then tills, seeds, grows and harvests Rye.
- Lumberjack fells WoodBlock trees; Miner harvests nearby ore blocks; Guard patrols; Blacksmith seeks a furnace; Merchant approaches villagers.
- Villagers stop near each other and play the configured social animation.
- Each villager receives a persistent, exclusive Celtic bed associated with their home door. They go to it at night, play their own sleep clip, and restore gravity at dawn.
- Player sleep code is not modified.
- Blacksmith currently seeks and waits at a furnace; it does not smelt or craft. Merchant approaches and chats but does not exchange inventory.

Build validation uses the public Survivalcraft API source mirror with its optional FLAC codec stubbed only in the temporary compile-reference checkout. Android in-game runtime still needs testing.

Latest test build: 0.4.4 — job control, visible held items, and slower walking.


## Version 0.4.23 navigation fixes

- Detects a pathfinder that is stuck while its destination is still populated and restarts the route search after a no-progress timeout.
- Raises path search budget and permits the engine's random-movement recovery after a stuck result.
- Opens doors earlier in a wider forward travel corridor and only closes them after villagers clear the doorway.
- Night transition cancels work destinations before bed navigation starts.
- Replaces older outdoor-assigned Celtic beds when an enclosed indoor sleeping position is available.
- Indoor bed placement requires a roof and nearby wall structure; no exposed yard positions are selected.
- Farmer approaches prepared soil and crops from a validated adjacent standing cell.
