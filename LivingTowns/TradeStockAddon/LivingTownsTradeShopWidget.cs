using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Engine;
using GameEntitySystem;

namespace Game
{
    internal sealed class LivingTownsTradeShopOffer
    {
        public int StockSlotIndex { get; }
        public int ItemValue { get; }
        public int PriceSilver { get; }
        public string ItemName { get; }
        public int Stock { get; set; }

        public LivingTownsTradeShopOffer(
            int stockSlotIndex,
            int itemValue,
            int priceSilver,
            string itemName,
            int stock)
        {
            StockSlotIndex = stockSlotIndex;
            ItemValue = itemValue;
            PriceSilver = priceSilver;
            ItemName = itemName;
            Stock = stock;
        }
    }

    internal sealed class LivingTownsTradeShopDisplayInventory : IInventory
    {
        private readonly Project m_project;
        private readonly IList<LivingTownsTradeShopOffer> m_offers;

        public LivingTownsTradeShopDisplayInventory(
            Project project,
            IList<LivingTownsTradeShopOffer> offers)
        {
            m_project = project;
            m_offers = offers;
        }

        public Project Project => m_project;
        public int SlotsCount => m_offers.Count;
        public int VisibleSlotsCount { get => SlotsCount; set { } }
        public int ActiveSlotIndex { get => -1; set { } }

        public int GetSlotValue(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= m_offers.Count)
                return 0;

            return m_offers[slotIndex].ItemValue;
        }

        public int GetSlotCount(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= m_offers.Count)
                return 0;

            return Math.Max(0, m_offers[slotIndex].Stock);
        }

        // The shop window is strictly display-only. No item can be dragged in or out.
        public int GetSlotCapacity(int slotIndex, int value) => 0;
        public int GetSlotProcessCapacity(int slotIndex, int value) => 0;
        public void AddSlotItems(int slotIndex, int value, int count) { }

        public void ProcessSlotItems(
            int slotIndex,
            int value,
            int count,
            int processCount,
            out int processedValue,
            out int processedCount)
        {
            processedValue = 0;
            processedCount = 0;
        }

        public int RemoveSlotItems(int slotIndex, int count) => 0;
        public void DropAllItems(Vector3 position) { }
    }

    internal sealed class LivingTownsTradeShopSlotWidget : InventorySlotWidget
    {
        public Action PurchaseAction { get; set; }

        public override void Update()
        {
            WidgetInput input = Input;
            if (input.Click.HasValue
                && HitTestGlobal(input.Click.Value.Start) == this
                && HitTestGlobal(input.Click.Value.End) == this)
            {
                input.Clear();
                PurchaseAction?.Invoke();
            }
        }

        public override void DragDrop(Widget dragWidget, object data)
        {
            // Prevent the native inventory drag/drop path from changing villager stock.
        }
    }

    public sealed class LivingTownsTradeShopWidget : CanvasWidget
    {
        private readonly ComponentPlayer m_player;
        private readonly object m_villager;
        private readonly IInventory m_stockInventory;
        private readonly IList<LivingTownsTradeShopOffer> m_offers;
        private readonly LivingTownsTradeShopDisplayInventory m_displayInventory;
        private readonly List<LabelWidget> m_offerLabels = new List<LabelWidget>();
        private readonly BevelledButtonWidget m_closeButton;

        public LivingTownsTradeShopWidget(
            ComponentPlayer player,
            object villager,
            IInventory stockInventory,
            string job,
            string villagerName)
        {
            m_player = player;
            m_villager = villager;
            m_stockInventory = stockInventory;
            m_offers = BuildOffers(player, stockInventory, job);
            m_displayInventory = new LivingTownsTradeShopDisplayInventory(player.Project, m_offers);

            LoadContents(this, ContentManager.Get<XElement>("Widgets/ChestWidget"));

            LabelWidget tradeTitle = Children.Find<LabelWidget>("ChestLabel");
            LabelWidget inventoryTitle = Children.Find<LabelWidget>("InventoryLabel");
            GridPanelWidget tradeGrid = Children.Find<GridPanelWidget>("ChestGrid");
            GridPanelWidget inventoryGrid = Children.Find<GridPanelWidget>("InventoryGrid");

            if (tradeTitle != null)
            {
                string titleName = string.IsNullOrWhiteSpace(villagerName) ? "Köylü" : villagerName;
                tradeTitle.Text = titleName + " - Dükkan";
            }

            if (inventoryTitle != null)
                inventoryTitle.Text = "Envanterin";

            int columns = 5;
            tradeGrid.ColumnsCount = columns;
            tradeGrid.RowsCount = Math.Max(1, (m_offers.Count + columns - 1) / columns);

            for (int i = 0; i < m_offers.Count; i++)
            {
                LivingTownsTradeShopOffer offer = m_offers[i];
                CanvasWidget tile = new CanvasWidget { Size = new Vector2(90f, 98f) };

                int offerIndex = i;
                LivingTownsTradeShopSlotWidget itemSlot = new LivingTownsTradeShopSlotWidget
                {
                    Size = new Vector2(64f, 64f)
                };
                itemSlot.AssignInventorySlot(m_displayInventory, i);
                itemSlot.PurchaseAction = () => CompletePurchase(offerIndex);
                tile.Children.Add(itemSlot);
                CanvasWidget.SetPosition(itemSlot, new Vector2(13f, 0f));

                LabelWidget priceLabel = new LabelWidget
                {
                    Text = BuildOfferText(offer),
                    HorizontalAlignment = WidgetAlignment.Center,
                    VerticalAlignment = WidgetAlignment.Center,
                    FontScale = 0.62f,
                    Color = Color.White
                };
                tile.Children.Add(priceLabel);
                CanvasWidget.SetPosition(priceLabel, new Vector2(0f, 67f));

                m_offerLabels.Add(priceLabel);
                tradeGrid.Children.Add(tile);
                tradeGrid.SetWidgetCell(tile, new Point2(i % columns, i / columns));
            }

            if (m_offers.Count == 0)
            {
                LabelWidget emptyLabel = new LabelWidget
                {
                    Text = "Bu köylünün satılabilir ürünü yok.",
                    HorizontalAlignment = WidgetAlignment.Center,
                    VerticalAlignment = WidgetAlignment.Center,
                    FontScale = 0.8f,
                    Color = Color.White
                };
                tradeGrid.Children.Add(emptyLabel);
                tradeGrid.SetWidgetCell(emptyLabel, new Point2(0, 0));
            }

            IInventory playerInventory = player.ComponentMiner?.Inventory;
            if (playerInventory != null)
            {
                int visibleIndex = 0;
                // The native full inventory uses slots 10-25 for the 4x4 backpack grid.
                for (int slotIndex = 10; slotIndex < 26 && slotIndex < playerInventory.SlotsCount; slotIndex++)
                {
                    InventorySlotWidget slot = new InventorySlotWidget();
                    slot.AssignInventorySlot(playerInventory, slotIndex);
                    inventoryGrid.Children.Add(slot);
                    inventoryGrid.SetWidgetCell(slot, new Point2(visibleIndex % 4, visibleIndex / 4));
                    visibleIndex++;
                }
            }

            m_closeButton = new BevelledButtonWidget
            {
                Text = "Kapat",
                Size = new Vector2(72f, 32f),
                FontScale = 0.75f
            };
            Children.Add(m_closeButton);
            CanvasWidget.SetPosition(m_closeButton, new Vector2(530f, 344f));
        }

        private static List<LivingTownsTradeShopOffer> BuildOffers(
            ComponentPlayer player,
            IInventory stockInventory,
            string job)
        {
            List<LivingTownsTradeShopOffer> offers = new List<LivingTownsTradeShopOffer>();
            SubsystemTerrain terrain = player.Project.FindSubsystem<SubsystemTerrain>(true);

            for (int slot = 0; slot < stockInventory.SlotsCount; slot++)
            {
                int count = stockInventory.GetSlotCount(slot);
                if (count <= 0)
                    continue;

                int value = stockInventory.GetSlotValue(slot);
                int contents = Terrain.ExtractContents(value);
                if (contents < 0 || contents >= BlocksManager.Blocks.Length)
                    continue;

                Block block = BlocksManager.Blocks[contents];
                if (block == null)
                    continue;

                string blockName = block.GetType().Name;
                int price = GetPriceInSilver(job, blockName);
                if (price <= 0)
                    continue;

                string itemName = block.GetDisplayName(terrain, value);
                if (string.IsNullOrWhiteSpace(itemName))
                    itemName = blockName;

                offers.Add(new LivingTownsTradeShopOffer(slot, value, price, itemName, count));
            }

            return offers;
        }

        private static int GetPriceInSilver(string job, string blockName)
        {
            string profession = (job ?? string.Empty).Trim().ToLowerInvariant();

            switch (profession)
            {
                case "farmer":
                    switch (blockName)
                    {
                        case "SeedsBlock": return 1;
                        case "RyeBlock": return 1;
                        case "BreadBlock": return 2;
                        case "CottonBlock": return 2;
                        case "PumpkinBlock": return 3;
                        case "PumpkinSoupBucketBlock": return 6;
                        case "CookedMeatBlock": return 5;
                        case "TorchBlock": return 2;
                    }
                    break;

                case "lumberjack":
                    switch (blockName)
                    {
                        case "OakWoodBlock": return 2;
                        case "BirchWoodBlock": return 2;
                        case "PlanksBlock": return 2;
                        case "StickBlock": return 1;
                        case "TorchBlock": return 2;
                        case "CoalChunkBlock": return 3;
                        case "BreadBlock": return 2;
                    }
                    break;

                case "miner":
                    switch (blockName)
                    {
                        case "StoneChunkBlock": return 1;
                        case "CoalChunkBlock": return 2;
                        case "IronOreChunkBlock": return 3;
                        case "CopperOreBlock": return 3;
                        case "CopperIngotBlock": return 6;
                        case "IronIngotBlock": return 8;
                        case "TorchBlock": return 2;
                        case "BreadBlock": return 2;
                    }
                    break;

                case "blacksmith":
                    switch (blockName)
                    {
                        case "IronIngotBlock": return 8;
                        case "CopperIngotBlock": return 6;
                        case "IronPickaxeBlock": return 30;
                        case "CopperPickaxeBlock": return 24;
                        case "IronAxeBlock": return 26;
                        case "CopperAxeBlock": return 20;
                        case "IronSpearBlock": return 28;
                        case "TorchBlock": return 2;
                    }
                    break;

                case "guard":
                    switch (blockName)
                    {
                        case "IronSpearBlock": return 35;
                        case "IronPickaxeBlock": return 30;
                        case "IronIngotBlock": return 8;
                        case "CookedMeatBlock": return 5;
                        case "BreadBlock": return 2;
                        case "TorchBlock": return 2;
                        case "CoalChunkBlock": return 3;
                    }
                    break;

                case "healer":
                    switch (blockName)
                    {
                        case "CookedMeatBlock": return 5;
                        case "BreadBlock": return 2;
                        case "SeedsBlock": return 1;
                        case "CottonBlock": return 2;
                        case "PumpkinSoupBucketBlock": return 6;
                        case "PumpkinBlock": return 3;
                        case "TorchBlock": return 2;
                    }
                    break;

                case "merchant":
                    switch (blockName)
                    {
                        case "BreadBlock": return 2;
                        case "TorchBlock": return 2;
                        case "PlanksBlock": return 2;
                        case "CoalChunkBlock": return 3;
                        case "IronIngotBlock": return 8;
                        case "SeedsBlock": return 1;
                        case "CookedMeatBlock": return 5;
                        case "CottonBlock": return 2;
                    }
                    break;
            }

            // Generic stock for villagers without a profession (for example, ordinary male/female villagers).
            switch (blockName)
            {
                case "StickBlock": return 1;
                case "BreadBlock": return 2;
                case "TorchBlock": return 2;
                case "StoneChunkBlock": return 1;
                case "SeedsBlock": return 1;
            }

            return 0;
        }

        private void CompletePurchase(int offerIndex)
        {
            if (m_player == null || m_stockInventory == null
                || offerIndex < 0 || offerIndex >= m_offers.Count)
                return;

            LivingTownsTradeShopOffer offer = m_offers[offerIndex];
            IInventory playerInventory = m_player.ComponentMiner?.Inventory;
            if (playerInventory == null)
                return;

            if (m_stockInventory.GetSlotCount(offer.StockSlotIndex) <= 0)
            {
                RefreshOfferLabels();
                m_player.ComponentGui?.DisplaySmallMessage("Bu ürünün stoğu tükendi.", Color.White, true, false);
                return;
            }

            if (GetAvailableCapacity(playerInventory, offer.ItemValue) < 1)
            {
                m_player.ComponentGui?.DisplaySmallMessage("Envanterinde ürün için yer yok.", Color.White, true, false);
                return;
            }

            int silverIndex = BlocksManager.GetBlockIndex("SilverCoinBlock");
            int goldIndex = BlocksManager.GetBlockIndex("GoldCoinBlock");
            if (silverIndex < 0 || goldIndex < 0
                || silverIndex >= BlocksManager.Blocks.Length
                || goldIndex >= BlocksManager.Blocks.Length
                || BlocksManager.Blocks[silverIndex] == null
                || BlocksManager.Blocks[goldIndex] == null)
            {
                m_player.ComponentGui?.DisplaySmallMessage("Gümüş/altın para eşyaları bulunamadı.", Color.White, true, false);
                return;
            }

            int silverValue = Terrain.MakeBlockValue(silverIndex, 0, 0);
            int goldValue = Terrain.MakeBlockValue(goldIndex, 0, 0);

            List<RemovedCoin> removedCoins = new List<RemovedCoin>();
            int change;
            if (!TryRemovePayment(playerInventory, silverValue, goldValue, offer.PriceSilver, removedCoins, out change))
            {
                m_player.ComponentGui?.DisplaySmallMessage(
                    "Yeterli paran yok. Fiyat: " + offer.PriceSilver + " gümüş.", Color.White, true, false);
                return;
            }

            if (change > 0)
            {
                int changeLeftover = ComponentInventoryBase.AcquireItems(playerInventory, silverValue, change);
                if (changeLeftover > 0)
                {
                    RemoveItems(playerInventory, silverValue, change - changeLeftover);
                    RestoreRemovedCoins(playerInventory, removedCoins);
                    m_player.ComponentGui?.DisplaySmallMessage(
                        "Para üstü için envanterinde yer yok.", Color.White, true, false);
                    return;
                }
            }

            int removedFromStock = m_stockInventory.RemoveSlotItems(offer.StockSlotIndex, 1);
            if (removedFromStock != 1)
            {
                if (change > 0)
                    RemoveItems(playerInventory, silverValue, change);
                RestoreRemovedCoins(playerInventory, removedCoins);
                m_player.ComponentGui?.DisplaySmallMessage("Stok değişti; satın alma iptal edildi.", Color.White, true, false);
                RefreshOfferLabels();
                return;
            }

            int productLeftover = ComponentInventoryBase.AcquireItems(playerInventory, offer.ItemValue, 1);
            if (productLeftover > 0)
            {
                RemoveItems(playerInventory, offer.ItemValue, 1 - productLeftover);
                m_stockInventory.AddSlotItems(offer.StockSlotIndex, offer.ItemValue, 1);
                if (change > 0)
                    RemoveItems(playerInventory, silverValue, change);
                RestoreRemovedCoins(playerInventory, removedCoins);
                m_player.ComponentGui?.DisplaySmallMessage(
                    "Satın alma güvenli şekilde iptal edildi; ürün için yer kalmadı.", Color.White, true, false);
                RefreshOfferLabels();
                return;
            }

            offer.Stock = m_stockInventory.GetSlotCount(offer.StockSlotIndex);
            RefreshOfferLabels();
            m_player.ComponentGui?.DisplaySmallMessage(
                offer.ItemName + " satın alındı. Ödenen: " + offer.PriceSilver + " gümüş.",
                Color.White, false, true);
        }

        private sealed class RemovedCoin
        {
            public int SlotIndex;
            public int Value;
            public int Count;
        }

        private static bool TryRemovePayment(
            IInventory inventory,
            int silverValue,
            int goldValue,
            int priceSilver,
            List<RemovedCoin> removed,
            out int change)
        {
            change = 0;
            int silverCount = CountItems(inventory, silverValue);
            int goldCount = CountItems(inventory, goldValue);

            int bestGold = -1;
            int bestSilver = 0;
            int bestChange = int.MaxValue;
            int maxGold = Math.Min(goldCount, (priceSilver + 9) / 10);

            for (int goldToUse = 0; goldToUse <= maxGold; goldToUse++)
            {
                int silverToUse = Math.Max(0, priceSilver - goldToUse * 10);
                if (silverToUse > silverCount)
                    continue;

                int overpayment = goldToUse * 10 + silverToUse - priceSilver;
                if (overpayment < bestChange)
                {
                    bestChange = overpayment;
                    bestGold = goldToUse;
                    bestSilver = silverToUse;
                }
            }

            if (bestGold < 0)
                return false;

            if (!RemoveItemsTracked(inventory, silverValue, bestSilver, removed)
                || !RemoveItemsTracked(inventory, goldValue, bestGold, removed))
            {
                RestoreRemovedCoins(inventory, removed);
                removed.Clear();
                return false;
            }

            change = bestChange;
            return true;
        }

        private static bool RemoveItemsTracked(
            IInventory inventory,
            int value,
            int count,
            List<RemovedCoin> removed)
        {
            int remaining = count;
            for (int slot = 0; slot < inventory.SlotsCount && remaining > 0; slot++)
            {
                if (inventory.GetSlotCount(slot) <= 0 || inventory.GetSlotValue(slot) != value)
                    continue;

                int take = Math.Min(remaining, inventory.GetSlotCount(slot));
                int actuallyRemoved = inventory.RemoveSlotItems(slot, take);
                if (actuallyRemoved > 0)
                {
                    removed.Add(new RemovedCoin
                    {
                        SlotIndex = slot,
                        Value = value,
                        Count = actuallyRemoved
                    });
                    remaining -= actuallyRemoved;
                }
            }

            return remaining == 0;
        }

        private static void RestoreRemovedCoins(IInventory inventory, IList<RemovedCoin> removed)
        {
            for (int i = 0; i < removed.Count; i++)
            {
                RemovedCoin coin = removed[i];
                if (coin.SlotIndex >= 0 && coin.SlotIndex < inventory.SlotsCount)
                    inventory.AddSlotItems(coin.SlotIndex, coin.Value, coin.Count);
            }
        }

        private static int CountItems(IInventory inventory, int value)
        {
            int total = 0;
            for (int i = 0; i < inventory.SlotsCount; i++)
            {
                if (inventory.GetSlotCount(i) > 0 && inventory.GetSlotValue(i) == value)
                    total += inventory.GetSlotCount(i);
            }

            return total;
        }

        private static int GetAvailableCapacity(IInventory inventory, int value)
        {
            int total = 0;
            for (int i = 0; i < inventory.SlotsCount; i++)
            {
                int count = inventory.GetSlotCount(i);
                int slotValue = inventory.GetSlotValue(i);
                int capacity = inventory.GetSlotCapacity(i, value);
                if (capacity > 0 && (count == 0 || slotValue == value))
                    total += Math.Max(0, capacity - count);
            }

            return total;
        }

        private static void RemoveItems(IInventory inventory, int value, int count)
        {
            int remaining = count;
            for (int i = 0; i < inventory.SlotsCount && remaining > 0; i++)
            {
                if (inventory.GetSlotCount(i) <= 0 || inventory.GetSlotValue(i) != value)
                    continue;

                int removed = inventory.RemoveSlotItems(i, Math.Min(remaining, inventory.GetSlotCount(i)));
                remaining -= removed;
            }
        }

        private string BuildOfferText(LivingTownsTradeShopOffer offer)
        {
            return offer.Stock > 0
                ? offer.PriceSilver + " Gümüş | Stok: " + offer.Stock
                : "Stok tükendi";
        }

        private void RefreshOfferLabels()
        {
            for (int i = 0; i < m_offers.Count; i++)
            {
                LivingTownsTradeShopOffer offer = m_offers[i];
                offer.Stock = Math.Max(0, m_stockInventory.GetSlotCount(offer.StockSlotIndex));
                m_offerLabels[i].Text = BuildOfferText(offer);
            }
        }

        public override void Update()
        {
            base.Update();

            if (m_closeButton.IsClicked
                || m_player == null
                || !m_player.IsAddedToProject
                || m_villager == null
                || !IsVillagerStillAdded())
            {
                if (m_player?.ComponentGui != null
                    && m_player.ComponentGui.ModalPanelWidget == this)
                {
                    m_player.ComponentGui.ModalPanelWidget = null;
                }
            }
        }

        private bool IsVillagerStillAdded()
        {
            try
            {
                PropertyInfoAccessor accessor = new PropertyInfoAccessor(m_villager);
                return accessor.IsAddedToProject;
            }
            catch
            {
                return true;
            }
        }

        private sealed class PropertyInfoAccessor
        {
            private readonly object m_instance;

            public PropertyInfoAccessor(object instance)
            {
                m_instance = instance;
            }

            public bool IsAddedToProject
            {
                get
                {
                    System.Reflection.PropertyInfo property =
                        m_instance.GetType().GetProperty("IsAddedToProject");
                    if (property == null)
                        return true;
                    object value = property.GetValue(m_instance, null);
                    return value is bool result && result;
                }
            }
        }
    }
}