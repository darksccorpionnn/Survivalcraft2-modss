using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game;

/// <summary>
/// Read-only in-game diagnostics for a Living Towns world.
/// It never spawns, damages, moves, edits, or deletes world entities.
/// </summary>
public sealed class LivingTownsTestHarnessModLoader : ModLoader
{
    private const string VillagerComponentName = "ComponentLivingTownsVillager";
    private const string SheepComponentName = "ComponentLivingTownsSheepBehavior";

    private BevelledButtonWidget m_testButton;
    private CanvasWidget m_testCanvas;
    private ContainerWidget m_testCanvasParent;

    public override void __ModInitialize()
    {
        ModsManager.RegisterHook("OnLoadingFinished", this);
        ModsManager.RegisterHook("GuiUpdate", this);
        Log.Information("[LivingTownsTest] Harness initialized. Read-only mode; no world edits are performed.");
    }

    public override void OnLoadingFinished(List<Action> actions)
    {
        Log.Information("[LivingTownsTest] World loading finished. Tap the TEST button in the HUD to scan loaded Living Towns entities.");
    }

    public override void GuiUpdate(ComponentGui componentGui)
    {
        if (componentGui == null)
            return;

        ComponentPlayer player = componentGui.m_componentPlayer;
        if (player == null || player.Project == null)
        {
            if (m_testButton != null)
                m_testButton.IsVisible = false;
            if (m_testCanvas != null)
                m_testCanvas.IsVisible = false;
            return;
        }

        if (m_testButton == null)
        {
            m_testButton = new BevelledButtonWidget
            {
                Text = "TEST",
                DesiredSize = new Engine.Vector2(72f, 48f),
                Margin = Engine.Vector2.Zero,
                HorizontalAlignment = WidgetAlignment.Near,
                VerticalAlignment = WidgetAlignment.Near,
                IsVisible = false
            };
        }

        ContainerWidget parent = componentGui.ControlsContainerWidget;
        if (parent != null && m_testCanvasParent != parent)
        {
            if (m_testCanvas != null && m_testCanvasParent != null)
                m_testCanvasParent.RemoveChildren(m_testCanvas);

            m_testCanvas = new CanvasWidget
            {
                Size = new Engine.Vector2(80f, 56f),
                DesiredSize = new Engine.Vector2(80f, 56f),
                HorizontalAlignment = WidgetAlignment.Near,
                VerticalAlignment = WidgetAlignment.Near,
                Margin = new Engine.Vector2(12f, 12f),
                IsVisible = false
            };
            m_testCanvas.AddChildren(m_testButton);
            parent.AddChildren(m_testCanvas);
            m_testCanvasParent = parent;
        }

        if (m_testCanvas != null)
            CanvasWidget.SetPosition(m_testButton, new Engine.Vector2(4f, 4f));

        bool guiBusy = componentGui.ModalPanelWidget != null || componentGui.IsInventoryVisible();
        bool canShow = !guiBusy && m_testCanvas != null;
        m_testButton.IsVisible = canShow;
        m_testButton.IsEnabled = canShow;
        if (m_testCanvas != null)
            m_testCanvas.IsVisible = canShow;

        if (canShow && m_testButton.IsClicked)
            RunReadOnlyScan(player);
    }

    private static void RunReadOnlyScan(ComponentPlayer player)
    {
        if (player == null || player.Project == null)
            return;

        var report = new StringBuilder();
        int passed = 0;
        int failed = 0;
        int blocked = 0;

        report.AppendLine("=== Living Towns in-game diagnostic ===");
        report.AppendLine("Mode: READ ONLY (no entities spawned or modified)");
        report.AppendLine("UTC: " + DateTime.UtcNow.ToString("O"));

        try
        {
            SubsystemBodies bodies = player.Project.FindSubsystem<SubsystemBodies>(throwOnError: true);
            int bodyCount = 0;
            int villagerCount = 0;
            int sheepCount = 0;
            int invalidLivingTownsHealth = 0;
            int duplicateVillagerControllers = 0;

            foreach (ComponentBody body in bodies.Bodies)
            {
                if (body == null || body.Entity == null)
                    continue;

                bodyCount++;
                int villagerComponents = 0;
                bool hasSheepBehavior = false;

                foreach (GameEntitySystem.Component component in body.Entity.Components)
                {
                    if (component == null)
                        continue;

                    string typeName = component.GetType().Name;
                    if (typeName == VillagerComponentName)
                        villagerComponents++;
                    if (typeName == SheepComponentName)
                        hasSheepBehavior = true;
                }

                bool isVillager = villagerComponents > 0;
                bool isSheep = hasSheepBehavior;

                if (isVillager)
                {
                    villagerCount++;
                    if (villagerComponents > 1)
                        duplicateVillagerControllers++;

                    ComponentCreature creature = body.Entity.FindComponent<ComponentCreature>();
                    if (creature == null || creature.ComponentHealth == null ||
                        creature.ComponentHealth.Health <= 0f)
                        invalidLivingTownsHealth++;
                }

                if (isSheep)
                {
                    sheepCount++;
                    ComponentCreature creature = body.Entity.FindComponent<ComponentCreature>();
                    if (creature == null || creature.ComponentHealth == null ||
                        creature.ComponentHealth.Health <= 0f)
                        invalidLivingTownsHealth++;
                }
            }

            report.AppendLine("Loaded body count: " + bodyCount);
            report.AppendLine("Living Towns villagers detected: " + villagerCount);
            report.AppendLine("Living Towns sheep/ram/lamb behaviors detected: " + sheepCount);

            AddResult(report, "World entity scan", true,
                "SubsystemBodies was read successfully (" + bodyCount + " bodies).", ref passed, ref failed, ref blocked);

            if (villagerCount > 0)
                AddResult(report, "Villager presence", true, villagerCount + " villager controller(s) detected.", ref passed, ref failed, ref blocked);
            else
                AddResult(report, "Villager presence", null, "No loaded Living Towns villagers found. Load/spawn a town and run TEST again.", ref passed, ref failed, ref blocked);

            if (sheepCount > 0)
                AddResult(report, "Sheep-family presence", true, sheepCount + " sheep/ram/lamb behavior component(s) detected.", ref passed, ref failed, ref blocked);
            else
                AddResult(report, "Sheep-family presence", null, "No loaded Living Towns sheep-family entities found. Spawn or load them and run TEST again.", ref passed, ref failed, ref blocked);

            AddResult(report, "Living Towns health integrity", invalidLivingTownsHealth == 0,
                invalidLivingTownsHealth + " identified Living Towns entity/entities had missing, absent, or zero health.",
                ref passed, ref failed, ref blocked);

            AddResult(report, "Duplicate villager controller", duplicateVillagerControllers == 0,
                duplicateVillagerControllers + " villager entity/entities had more than one Living Towns controller.",
                ref passed, ref failed, ref blocked);

            AddResult(report, "Villager jobs and schedules", null,
                "Requires observing NPC task changes over game time; this safe scan does not simulate or alter AI.",
                ref passed, ref failed, ref blocked);
            AddResult(report, "Homes, doors and beds", null,
                "Requires a controlled town test scenario and path/interaction instrumentation.",
                ref passed, ref failed, ref blocked);
            AddResult(report, "Trade and stock persistence", null,
                "Requires opening trade and checking saved inventory/stock before and after reload.",
                ref passed, ref failed, ref blocked);
            AddResult(report, "Animal combat and retaliation", null,
                "Requires observing real predator/prey attacks and comparing both health values over time.",
                ref passed, ref failed, ref blocked);
            AddResult(report, "Save/load regression", null,
                "Requires a save/reload cycle; not performed by this non-destructive scan.",
                ref passed, ref failed, ref blocked);
        }
        catch (Exception ex)
        {
            AddResult(report, "World entity scan", false,
                ex.GetType().Name + ": " + ex.Message, ref passed, ref failed, ref blocked);
        }

        report.AppendLine();
        report.AppendLine("SUMMARY: PASS=" + passed + " FAIL=" + failed + " BLOCKED=" + blocked);
        report.AppendLine("A BLOCKED result means the scenario was not executed; it is not a pass.");
        report.AppendLine("=== End Living Towns diagnostic ===");

        foreach (string line in report.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (!string.IsNullOrWhiteSpace(line))
                Log.Information("[LivingTownsTest] " + line);
        }

        Log.Information("[LivingTownsTest] Scan complete. PASS=" + passed + " FAIL=" + failed + " BLOCKED=" + blocked);
    }

    private static void AddResult(
        StringBuilder report,
        string name,
        bool? passedResult,
        string detail,
        ref int passed,
        ref int failed,
        ref int blocked)
    {
        string status;
        if (!passedResult.HasValue)
        {
            status = "BLOCKED";
            blocked++;
        }
        else if (passedResult.Value)
        {
            status = "PASS";
            passed++;
        }
        else
        {
            status = "FAIL";
            failed++;
        }

        report.AppendLine("[" + status + "] " + name + ": " + detail);
    }
}
