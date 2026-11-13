# Living Towns 0.4.26

- Door-created villagers, one villager per lower door half.
- Automatic opening and closing of normal (non-electrically controlled) doors while pathfinding.
- 2.2 m/s villager walk speed, with jump and ladder locomotion enabled for obstacle traversal.
- Farmer gets an initial private Iron Rake + Rye Seeds kit once, then tills, seeds, grows and harvests Rye.
- Lumberjack fells WoodBlock trees; Miner harvests nearby ore blocks; Guard patrols; Blacksmith seeks a furnace; Merchant approaches villagers.
- Villagers stop near each other and play the configured social animation.
- Each villager receives a persistent, exclusive Celtic bed associated with their home door. They go to it at night, play their own sleep clip, and restore gravity at dawn.
- Player sleep code is not modified.
- Blacksmith currently seeks and waits at a furnace; it does not smelt or craft. Merchant approaches and chats but does not exchange inventory.

Build validation uses the public Survivalcraft API source mirror with its optional FLAC codec stubbed only in the temporary compile-reference checkout. Android in-game runtime still needs testing.

Latest build: 0.4.26 — reduced door toggling and forced home-door closure before sleep.


## Version 0.4.26 door control fixes

- When no bed can be placed, the villager now searches for a safe indoor rest cell near its registered home door and navigates there instead of stopping outside all night.
- Indoor fallback positions require a solid floor and two clear body cells; roofed cells are preferred, with a nearby safe door-area cell as a last resort.
- Bed search cooldown now uses real frame delta rather than subtracting a fixed 0.35 per update.
- Patrol targets are sampled and validated for a solid floor and headroom before the villager walks toward them.
- Work approach search extends 12 blocks below tall tree trunks and scores candidates to prefer reachable ground-level cells.
- Human locomotion uses 2.2 walk speed and enables jump / ladder capabilities, following the movement profile used by Tribalcraft human NPCs.
- Measures progress by decreasing distance to the destination, not by raw body movement; circular movement around trees or obstacles no longer counts as progress.
- Restarts a route after 10 seconds without meaningful approach to the target, including when the villager keeps moving sideways or circles an obstacle.
- Retains the engine pathfinder's route-search and random-movement recovery as a fallback.
- Raises path search budget and permits the engine's random-movement recovery after a stuck result.
- Opens doors earlier in a wider forward travel corridor and only closes them after villagers clear the doorway.
- Night transition cancels work destinations before bed navigation starts.
- Replaces older outdoor-assigned Celtic beds when an enclosed indoor sleeping position is available.
- Indoor bed placement requires a roof and nearby wall structure; no exposed yard positions are selected.
- Farmer approaches prepared soil and crops from a validated adjacent standing cell.


## Version 0.4.26 door control fixes

- Narrows automatic door detection to a close forward corridor, reducing accidental opening of nearby side doors.
- Adds closing hysteresis: doors opened by a villager stay open until it is clearly away from the doorway.
- Keeps a door open while another LivingTowns villager is within 3.5 blocks, reducing one villager closing a door in front of another.
- When a villager reaches its bed or night rest position, it closes doors it opened and explicitly checks its registered home door, even if that door was opened by another villager.
- Electrically connected doors are not forced shut.
- Door close calls are guarded by the actual open bit because the game's OpenCloseDoor method plays a sound even when asked to close an already-closed door.
