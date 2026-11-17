# KoylulerDoorStages (0.2.14 navigation add-on)

Build target: Survivalcraft 2.4.0.0 / API 1.9.5.1 / .NET 10.

This add-on is intended to be placed beside the original 0.2.14 package contents in the same .scmod archive.

Behavior:
- Night entry: route to the assigned home door first.
- The prefix suppresses the original rest routine (and therefore the first bed/name-sign search) until the villager is clearly on the indoor side of the door and its native crossing route has ended.
- Once inside, the untouched 0.2.14 rest routine resumes and resolves the personal named sign and assigned bed.
- Dawn exit and work-sign search are not patched. The 0.2.14 update flow already waits for TickHomeExitRoutine before TickWorkSignRoutine.
- Combat, health, animal damage, professions, inventory, trades, all 0.2.14 database templates, and both original DLLs are intended to remain unchanged.

Safety:
- If reflection or door geometry cannot be resolved, the prefix fails open and allows the original 0.2.14 behavior.
- Compilation / archive validation does not prove runtime movement. Android in-game testing is required.
