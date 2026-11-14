using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using HarmonyLib;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game
{
    internal static class LivingTownsTradeStockPatch
    {
        private const string InitializedKey = "LivingTownsTradeStockInitialized_0_4_61";

        private sealed class SeedState
        {
            public bool Initialized;
        }

        private readonly struct StockItem
        {
            public readonly string BlockName;
            public readonly int Data;
            public readonly int Count;

            public StockItem(string blockName, int data, int count)
            {
                BlockName = blockName;
                Data = data;
                Count = count;
            }
        }

        private static readonly ConditionalWeakTable<object, SeedState> s_states =
            new ConditionalWeakTable<object, SeedState>();

        public static void Install(Harmony harmony)
        {
            Type villagerType = AccessTools.TypeByName("Game.ComponentLivingTownsVillager");
            if (villagerType == null)
            {
                Log.Warning("[LivingTowns Trade Shop] ComponentLivingTownsVillager was not found; base mod must load first.");
                return;
            }

            PatchMethod(harmony, villagerType, "Load", nameof(LoadPostfix));
            PatchMethod(harmony, villagerType, "Save", nameof(SavePostfix));

            // Intercept the native player hit before the legacy villager transfer prefix.
            // For villagers, open this shop and return false so the old free-transfer panel never runs.
            MethodInfo hit = AccessTools.Method(typeof(ComponentMiner), nameof(ComponentMiner.Hit),
                new Type[] { typeof(ComponentBody), typeof(Engine.Vector3), typeof(Engine.Vector3) });
            MethodInfo hitReplacement = typeof(LivingTownsTradeStockPatch).GetMethod(
                nameof(TradeScreenHitPrefix), BindingFlags.Static | BindingFlags.NonPublic);

            if (hit != null && hitReplacement != null)
            {
                HarmonyMethod hitPatch = new HarmonyMethod(hitReplacement);
                hitPatch.priority = Priority.First;
                harmony.Patch(hit, prefix: hitPatch);
                Log.Information("[LivingTowns Trade Shop] Installed high-priority player-hit interception.");
            }
            else
            {
                Log.Warning("[LivingTowns Trade Shop] Could not locate ComponentMiner.Hit.");
            }

            Log.Information("[LivingTowns Trade Shop] Currency shop patch installation completed.");
        }

        private static void PatchMethod(Harmony harmony, Type type, string targetName, string postfixName)
        {
            MethodInfo target = AccessTools.Method(type, targetName);
            MethodInfo postfix = typeof(LivingTownsTradeStockPatch).GetMethod(
                postfixName, BindingFlags.Static | BindingFlags.NonPublic);

            if (target == null || postfix == null)
            {
                Log.Warning("[LivingTowns Trade Shop] Could not patch " + targetName + ".");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        }

        private static SeedState GetState(object villager)
        {
            return s_states.GetValue(villager, _ => new SeedState());
        }

        private static void LoadPostfix(object __instance, object[] __args)
        {
            if (__instance == null || __args == null || __args.Length == 0)
                return;

            ValuesDictionary values = __args[0] as ValuesDictionary;
            if (values == null)
                return;

            try
            {
                GetState(__instance).Initialized = values.GetValue<bool>(InitializedKey, false);
            }
            catch
            {
                GetState(__instance).Initialized = false;
            }
        }

        private static void SavePostfix(object __instance, object[] __args)
        {
            if (__instance == null || __args == null || __args.Length == 0)
                return;

            ValuesDictionary values = __args[0] as ValuesDictionary;
            if (values == null)
                return;

            try
            {
                values.SetValue(InitializedKey, GetState(__instance).Initialized);
            }
            catch (Exception ex)
            {
                Log.Warning("[LivingTowns Trade Shop] Could not save stock initialization state: " + ex.Message);
            }
        }

        private static bool TradeScreenHitPrefix(
            ComponentMiner __instance,
            ComponentBody componentBody,
            Engine.Vector3 hitPoint,
            Engine.Vector3 hitDirection)
        {
            return OpenShop(__instance, componentBody);
        }

        private static bool OpenShop(ComponentMiner miner, ComponentBody componentBody)
        {
            if (miner?.ComponentPlayer == null || componentBody?.Entity == null)
                return true;

            Type villagerType = AccessTools.TypeByName("Game.ComponentLivingTownsVillager");
            if (villagerType == null)
                return true;

            object villager = componentBody.Entity.FindComponent(villagerType, null, false);
            if (villager == null)
                return true;

            ComponentPlayer player = miner.ComponentPlayer;
            try
            {
                SeedTradeInventory(villager);
                IInventory stock = GetTradeInventory(villager);
                if (stock == null)
                {
                    player.ComponentGui?.DisplaySmallMessage(
                        "Köylünün ticaret stoğu okunamadı.", Color.White, true, false);
                    return false;
                }

                string job = GetJobName(villager);
                string name = GetVillagerName(villager);
                LivingTownsTradeShopWidget shop =
                    new LivingTownsTradeShopWidget(player, villager, stock, job, name);

                player.ComponentGui.ModalPanelWidget = shop;
            }
            catch (Exception ex)
            {
                Log.Warning("[LivingTowns Trade Shop] Could not open shop: " + ex);
                player.ComponentGui?.DisplaySmallMessage(
                    "Ticaret ekranı açılamadı. Hata günlüğünü kontrol et.", Color.White, true, false);
            }

            // Suppress the old panel even if shop initialization fails: no free stock transfer.
            return false;
        }

        private static void HitPostfix(
            ComponentMiner __instance,
            ComponentBody componentBody,
            Engine.Vector3 hitPoint,
            Engine.Vector3 hitDirection)
        {
            if (componentBody?.Entity == null)
                return;

            Type villagerType = AccessTools.TypeByName("Game.ComponentLivingTownsVillager");
            if (villagerType == null)
                return;

            object villager = componentBody.Entity.FindComponent(villagerType, null, false);
            if (villager != null)
                SeedTradeInventory(villager);
        }

        private static void SeedTradeInventory(object villager)
        {
            if (villager == null)
                return;

            SeedState state = GetState(villager);
            if (state.Initialized)
                return;

            try
            {
                IInventory inventory = GetTradeInventory(villager);
                if (inventory == null || inventory.SlotsCount <= 0)
                {
                    Log.Warning("[LivingTowns Trade Shop] Trade inventory was not available.");
                    return;
                }

                if (HasAnyItems(inventory))
                {
                    state.Initialized = true;
                    return;
                }

                string job = GetJobName(villager);
                StockItem[] stock = GetStockForJob(job);
                int addedTypes = 0;

                foreach (StockItem item in stock)
                {
                    if (AddItem(inventory, item))
                        addedTypes++;
                }

                if (addedTypes > 0)
                {
                    state.Initialized = true;
                    Log.Information("[LivingTowns Trade Shop] Seeded " + addedTypes
                        + " stock item types for profession " + job + ".");
                }
                else
                {
                    Log.Warning("[LivingTowns Trade Shop] No valid stock items were found for profession " + job + ".");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[LivingTowns Trade Shop] Stock initialization failed: " + ex);
            }
        }

        private static IInventory GetTradeInventory(object villager)
        {
            Type type = villager.GetType();
            MethodInfo getter = AccessTools.Method(type, "GetTradeInventory", Type.EmptyTypes);
            if (getter != null)
            {
                object result = getter.Invoke(villager, null);
                if (result is IInventory inventory)
                    return inventory;
            }

            FieldInfo field = AccessTools.Field(type, "m_tradeInventory");
            if (field != null && field.GetValue(villager) is IInventory fieldInventory)
                return fieldInventory;

            return null;
        }

        private static string GetJobName(object villager)
        {
            PropertyInfo property = AccessTools.Property(villager.GetType(), "Job");
            object job = property?.GetValue(villager, null);
            if (job != null)
                return job.ToString();

            FieldInfo field = AccessTools.Field(villager.GetType(), "m_job");
            return field?.GetValue(villager)?.ToString() ?? "Unemployed";
        }

        private static string GetVillagerName(object villager)
        {
            PropertyInfo property = AccessTools.Property(villager.GetType(), "VillagerName");
            object name = property?.GetValue(villager, null);
            return name?.ToString() ?? string.Empty;
        }

        private static bool HasAnyItems(IInventory inventory)
        {
            for (int i = 0; i < inventory.SlotsCount; i++)
            {
                if (inventory.GetSlotCount(i) > 0)
                    return true;
            }

            return false;
        }

        private static bool AddItem(IInventory inventory, StockItem item)
        {
            int blockIndex = BlocksManager.GetBlockIndex(item.BlockName);
            if (blockIndex < 0 || blockIndex >= BlocksManager.Blocks.Length
                || BlocksManager.Blocks[blockIndex] == null)
            {
                Log.Warning("[LivingTowns Trade Shop] Skipping unavailable item: " + item.BlockName);
                return false;
            }

            int value = Terrain.MakeBlockValue(blockIndex, 0, item.Data);
            int remaining = item.Count;

            for (int slot = 0; slot < inventory.SlotsCount && remaining > 0; slot++)
            {
                int currentCount = inventory.GetSlotCount(slot);
                int currentValue = inventory.GetSlotValue(slot);
                if (currentCount > 0 && currentValue != value)
                    continue;

                int capacity = inventory.GetSlotCapacity(slot, value);
                int available = Math.Max(0, capacity - currentCount);
                int add = Math.Min(remaining, available);
                if (add <= 0)
                    continue;

                inventory.AddSlotItems(slot, value, add);
                remaining -= add;
            }

            return remaining < item.Count;
        }

        private static StockItem[] GetStockForJob(string job)
        {
            switch ((job ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "farmer":
                    return new[]
                    {
                        new StockItem("SeedsBlock", 5, 16),
                        new StockItem("RyeBlock", 0, 10),
                        new StockItem("BreadBlock", 0, 8),
                        new StockItem("CottonBlock", 0, 8),
                        new StockItem("PumpkinBlock", 0, 5),
                        new StockItem("PumpkinSoupBucketBlock", 0, 2),
                        new StockItem("CookedMeatBlock", 0, 4),
                        new StockItem("TorchBlock", 0, 6)
                    };

                case "lumberjack":
                    return new[]
                    {
                        new StockItem("OakWoodBlock", 0, 12),
                        new StockItem("BirchWoodBlock", 0, 12),
                        new StockItem("PlanksBlock", 0, 16),
                        new StockItem("StickBlock", 0, 16),
                        new StockItem("TorchBlock", 0, 8),
                        new StockItem("CoalChunkBlock", 0, 6),
                        new StockItem("BreadBlock", 0, 4)
                    };

                case "miner":
                    return new[]
                    {
                        new StockItem("StoneChunkBlock", 0, 16),
                        new StockItem("CoalChunkBlock", 0, 12),
                        new StockItem("IronOreChunkBlock", 0, 8),
                        new StockItem("CopperOreBlock", 0, 8),
                        new StockItem("CopperIngotBlock", 0, 4),
                        new StockItem("IronIngotBlock", 0, 3),
                        new StockItem("TorchBlock", 0, 8),
                        new StockItem("BreadBlock", 0, 4)
                    };

                case "blacksmith":
                    return new[]
                    {
                        new StockItem("IronIngotBlock", 0, 8),
                        new StockItem("CopperIngotBlock", 0, 8),
                        new StockItem("IronPickaxeBlock", 0, 1),
                        new StockItem("CopperPickaxeBlock", 0, 1),
                        new StockItem("IronAxeBlock", 0, 1),
                        new StockItem("CopperAxeBlock", 0, 1),
                        new StockItem("IronSpearBlock", 0, 1),
                        new StockItem("TorchBlock", 0, 8)
                    };

                case "guard":
                    return new[]
                    {
                        new StockItem("IronSpearBlock", 0, 1),
                        new StockItem("IronPickaxeBlock", 0, 1),
                        new StockItem("IronIngotBlock", 0, 4),
                        new StockItem("CookedMeatBlock", 0, 8),
                        new StockItem("BreadBlock", 0, 8),
                        new StockItem("TorchBlock", 0, 8),
                        new StockItem("CoalChunkBlock", 0, 6)
                    };

                case "healer":
                    return new[]
                    {
                        new StockItem("CookedMeatBlock", 0, 8),
                        new StockItem("BreadBlock", 0, 10),
                        new StockItem("SeedsBlock", 5, 12),
                        new StockItem("CottonBlock", 0, 8),
                        new StockItem("PumpkinSoupBucketBlock", 0, 3),
                        new StockItem("PumpkinBlock", 0, 5),
                        new StockItem("TorchBlock", 0, 4)
                    };

                case "merchant":
                    return new[]
                    {
                        new StockItem("BreadBlock", 0, 8),
                        new StockItem("TorchBlock", 0, 8),
                        new StockItem("PlanksBlock", 0, 12),
                        new StockItem("CoalChunkBlock", 0, 8),
                        new StockItem("IronIngotBlock", 0, 4),
                        new StockItem("SeedsBlock", 5, 12),
                        new StockItem("CookedMeatBlock", 0, 6),
                        new StockItem("CottonBlock", 0, 8)
                    };

                default:
                    return new[]
                    {
                        new StockItem("BreadBlock", 0, 6),
                        new StockItem("StickBlock", 0, 8),
                        new StockItem("TorchBlock", 0, 8),
                        new StockItem("StoneChunkBlock", 0, 8),
                        new StockItem("SeedsBlock", 5, 8)
                    };
            }
        }
    }
}