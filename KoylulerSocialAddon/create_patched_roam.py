from pathlib import Path

source=Path("KoylulerModu/VillagerBedRoamingPatch.cs").read_text(encoding="utf-8")
replacements=[
('private static FieldInfo s_bedCellField;', '''private static FieldInfo s_bedCellField;
    [ThreadStatic] private static object s_patrolOwner;'''),
('s_harmony.Patch(patrol, prefix: new HarmonyMethod(patrolPrefix));',
 '''s_harmony.Patch(patrol,
                prefix: new HarmonyMethod(patrolPrefix),
                postfix: new HarmonyMethod(typeof(VillagerBedRoamingPatch).GetMethod(
                    nameof(TickPatrolPostfix), BindingFlags.Static | BindingFlags.NonPublic)),
                finalizer: new HarmonyMethod(typeof(VillagerBedRoamingPatch).GetMethod(
                    nameof(TickPatrolFinalizer), BindingFlags.Static | BindingFlags.NonPublic)));'''),
('''        // Do not allow free roaming until this villager has its own saved bed.
        if (!TryGetBedCell(__instance, out _))
            return false;

        minRadius = MinimumRoamRadius;''',
 '''        // Never suppress the original patrol if a villager has no assigned bed.
        // The native controller will fall back to its own work/home/position center.
        s_patrolOwner = __instance;
        minRadius = MinimumRoamRadius;'''),
('''    private static bool GetWorkCenterCellPrefix(object __instance, ref Point3 __result)
    {
        if (!TryGetBedCell(__instance, out Point3 bedCell))''',
 '''    private static void TickPatrolPostfix() { s_patrolOwner = null; }

    private static Exception TickPatrolFinalizer(Exception __exception)
    {
        s_patrolOwner = null;
        return __exception;
    }

    private static bool GetWorkCenterCellPrefix(object __instance, ref Point3 __result)
    {
        // Restrict the bed-centered override to the idle-patrol call only.
        // Work routing still receives its original work-center result.
        if (!ReferenceEquals(s_patrolOwner, __instance))
            return true;
        if (!TryGetBedCell(__instance, out Point3 bedCell))'''),
('Idle patrol radius set to 2.5-25 blocks around each villager\'s own bed.',
 'Idle patrol radius extended; no-bed villagers keep native roaming.'),
('Idle patrol centre now uses the villager\'s assigned bed cell.',
 'Idle patrol is bed-centered only during TickPatrol; work routing preserved.')
]
for old,new in replacements:
    if source.count(old)!=1:raise AssertionError("Expected unique patch anchor: "+old[:65])
    source=source.replace(old,new)
out=Path("KoylulerSocialAddon/VillagerBedRoamingPatched.cs")
out.write_text(source,encoding="utf-8")
assert "return false;" not in source.split("private static bool TickPatrolPrefix")[1].split("private static void TickPatrolPostfix")[0]
assert "ThreadStatic" in source and "ReferenceEquals(s_patrolOwner, __instance)" in source
print("Patching roaming source: PASS")
