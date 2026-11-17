using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Engine;
using HarmonyLib;

namespace Game;

/// <summary>
/// Rewrites the exact hard-coded title in LivingTownsTradeWidget's constructor.
/// The original source line:
/// tradeLabel.Text = "Köylünün Takas Stoğu";
/// becomes:
/// tradeLabel.Text = VillagerInventoryNameBridge.ResolveStockLabel(villager);
/// </summary>
internal static class VillagerInventoryNamePatch
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Harmony s_harmony;
    private static bool s_listening;
    private static readonly HashSet<MethodBase> s_patched = new HashSet<MethodBase>();

    public static void Install(Harmony harmony)
    {
        if (harmony == null)
            return;

        s_harmony = harmony;
        if (!s_listening)
        {
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            s_listening = true;
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            TryPatchAssembly(assembly);
    }

    private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
    {
        try
        {
            TryPatchAssembly(args.LoadedAssembly);
        }
        catch (Exception ex)
        {
            Log.Error("[KoylulerModu] Assembly-load patch failed: " + ex);
        }
    }

    private static void TryPatchAssembly(Assembly assembly)
    {
        if (assembly == null || s_harmony == null)
            return;

        Type widgetType = assembly.GetType("Game.LivingTownsTradeWidget", false);
        if (widgetType == null)
            return;

        MethodInfo transpiler = typeof(VillagerInventoryNamePatch).GetMethod(
            nameof(ReplaceStockCaption), BindingFlags.Static | BindingFlags.Public);
        int patched = 0;

        foreach (ConstructorInfo constructor in widgetType.GetConstructors(InstanceFlags))
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            if (parameters.Length != 4 ||
                parameters[0].ParameterType.FullName != "Game.ComponentPlayer" ||
                parameters[1].ParameterType.FullName != "Game.ComponentLivingTownsVillager")
                continue;

            if (s_patched.Contains(constructor))
                continue;

            s_harmony.Patch(constructor, transpiler: new HarmonyMethod(transpiler));
            s_patched.Add(constructor);
            patched++;
        }

        if (patched > 0)
            Log.Information("[KoylulerModu] Direct stock-caption transpiler installed on " + patched + " LivingTownsTradeWidget constructor(s).");
    }

    public static IEnumerable<CodeInstruction> ReplaceStockCaption(IEnumerable<CodeInstruction> instructions)
    {
        bool replaced = false;
        MethodInfo resolver = AccessTools.Method(
            typeof(VillagerInventoryNameBridge),
            nameof(VillagerInventoryNameBridge.ResolveStockLabel),
            new Type[] { typeof(object) });

        foreach (CodeInstruction instruction in instructions)
        {
            if (!replaced &&
                instruction.opcode == OpCodes.Ldstr &&
                instruction.operand is string literal &&
                literal.IndexOf("Takas Stoğu", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Constructor arguments: 0=this, 1=player, 2=villager.
                yield return new CodeInstruction(OpCodes.Ldarg_2);
                yield return new CodeInstruction(OpCodes.Call, resolver);
                replaced = true;
            }
            else
            {
                yield return instruction;
            }
        }

        if (!replaced)
            Log.Error("[KoylulerModu] The exact 'Köylünün Takas Stoğu' text was not found in the trade-widget constructor; no caption was changed.");
        else
            Log.Information("[KoylulerModu] Replaced hard-coded trade-stock caption with the villager-name resolver.");
    }
}
