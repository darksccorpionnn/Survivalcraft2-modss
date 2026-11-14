using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Engine;
using GameEntitySystem;

namespace Game
{
    public sealed class LivingTownsTradeOffer
    {
        public int CostValue { get; }
        public int CostCount { get; }
        public int ResultValue { get; }
        public int ResultCount { get; }
        public int InitialStock { get; }
        public int Stock { get; set; }

        public LivingTownsTradeOffer(int costValue, int costCount, int resultValue, int resultCount, int stock)
        {
            CostValue = costValue;
            CostCount = costCount;
            ResultValue = resultValue;
            ResultCount = resultCount;
            InitialStock = stock;
            Stock = stock;
        }
    }

    internal sealed class LivingTownsTradeDisplayInventory : IInventory
    {
        private readonly Project m_project;
        private readonly IList<LivingTownsTradeOffer> m_offers;

        public LivingTownsTradeDisplayInventory(Project project, IList<LivingTownsTradeOffer> offers)
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
            return slotIndex >= 0 && slotIndex < m_offers.Count ? m_offers[slotIndex].ResultValue : 0;
        }

        public int GetSlotCount(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= m_offers.Count)
                return 0;

            LivingTownsTradeOffer offer = m_offers[slotIndex];
            return offer.Stock > 0 ? offer.ResultCount : 0;
        }

        // This inventory is display-only. Purchases are processed atomically by the villager.
        public int GetSlotCapacity(int slotIndex, int value) => 0;
        public int GetSlotProcessCapacity(int slotIndex, int value) => 0;
        public void AddSlotItems(int slotIndex, int value, int count) { }
        public void ProcessSlotItems(int slotIndex, int value, int count, int processCount, out int processedValue, out int processedCount)
        {
            processedValue = 0;
            processedCount = 0;
        }
        public int RemoveSlotItems(int slotIndex, int count) => 0;
        public void DropAllItems(Vector3 position) { }
    }

    internal sealed class LivingTownsTradeOfferSlotWidget : InventorySlotWidget
    {
        public Action TradeAction { get; set; }

        public override void Update()
        {
            WidgetInput input = Input;
            if (input.Click.HasValue
                && HitTestGlobal(input.Click.Value.Start) == this
                && HitTestGlobal(input.Click.Value.End) == this)
            {
                input.Clear();
                TradeAction?.Invoke();
            }
        }

        public override void DragDrop(Widget dragWidget, object data)
        {
            // Items can only leave the offer through a validated trade.
        }
    }

    public sealed class LivingTownsTradeWidget : CanvasWidget
    {
        private readonly ComponentPlayer m_player;
        private readonly ComponentLivingTownsVillager m_villager;
        private readonly IList<LivingTownsTradeOffer> m_offers;
        private readonly LivingTownsTradeDisplayInventory m_offerInventory;
        private readonly List<LabelWidget> m_offerLabels = new List<LabelWidget>();
        private readonly BevelledButtonWidget m_closeButton;

        public LivingTownsTradeWidget(
            ComponentPlayer player,
            ComponentLivingTownsVillager villager,
            IList<LivingTownsTradeOffer> offers)
        {
            m_player = player;
            m_villager = villager;
            m_offers = offers;
            m_offerInventory = new LivingTownsTradeDisplayInventory(player.Project, offers);

            // Reuse Survivalcraft's native chest/inventory layout and its real inventory slots.
            LoadContents(this, ContentManager.Get<XElement>("Widgets/ChestWidget"));

            LabelWidget tradeTitle = Children.Find<LabelWidget>("ChestLabel");
            LabelWidget inventoryTitle = Children.Find<LabelWidget>("InventoryLabel");
            GridPanelWidget tradeGrid = Children.Find<GridPanelWidget>("ChestGrid");
            GridPanelWidget inventoryGrid = Children.Find<GridPanelWidget>("InventoryGrid");

            tradeTitle.Text = (string.IsNullOrWhiteSpace(villager.VillagerName) ? "Köylü" : villager.VillagerName) + " - Takas";
            inventoryTitle.Text = "Envanterin";

            tradeGrid.ColumnsCount = 2;
            tradeGrid.RowsCount = 2;

            for (int i = 0; i < offers.Count && i < 4; i++)
            {
                LivingTownsTradeOffer offer = offers[i];
                CanvasWidget tile = new CanvasWidget { Size = new Vector2(136f, 132f) };

                LivingTownsTradeOfferSlotWidget resultSlot = new LivingTownsTradeOfferSlotWidget();
                resultSlot.AssignInventorySlot(m_offerInventory, i);
                resultSlot.TradeAction = () => CompleteTrade(i);
                tile.Children.Add(resultSlot);
                CanvasWidget.SetPosition(resultSlot, new Vector2(32f, 0f));

                LabelWidget costLabel = new LabelWidget
                {
                    Text = BuildOfferText(offer),
                    HorizontalAlignment = WidgetAlignment.Center,
                    VerticalAlignment = WidgetAlignment.Center,
                    FontScale = 0.72f,
                    Color = Color.White
                };
                tile.Children.Add(costLabel);
                CanvasWidget.SetPosition(costLabel, new Vector2(0f, 76f));

                m_offerLabels.Add(costLabel);
                tradeGrid.Children.Add(tile);
                tradeGrid.SetWidgetCell(tile, new Point2(i % 2, i / 2));
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

        private void CompleteTrade(int offerIndex)
        {
            if (m_villager == null || offerIndex < 0 || offerIndex >= m_offers.Count)
                return;

            if (m_villager.TryCompleteTrade(m_player, m_offers[offerIndex], offerIndex))
                RefreshOfferLabels();
            else
                RefreshOfferLabels();
        }

        private void RefreshOfferLabels()
        {
            for (int i = 0; i < m_offerLabels.Count && i < m_offers.Count; i++)
                m_offerLabels[i].Text = BuildOfferText(m_offers[i]);
        }

        private string BuildOfferText(LivingTownsTradeOffer offer)
        {
            string costName = GetItemName(offer.CostValue);
            return offer.Stock > 0
                ? "Ver: " + offer.CostCount + " x " + costName + "\nStok: " + offer.Stock + " | Dokun: Takas"
                : "Stok tükendi";
        }

        private string GetItemName(int value)
        {
            int contents = Terrain.ExtractContents(value);
            if (contents < 0 || contents >= BlocksManager.Blocks.Length)
                return "Eşya";

            Block block = BlocksManager.Blocks[contents];
            if (block == null)
                return "Eşya";

            string name = block.GetDisplayName(
                m_player.Project.FindSubsystem<SubsystemTerrain>(true),
                value);
            return string.IsNullOrWhiteSpace(name) ? block.GetType().Name : name;
        }

        public override void Update()
        {
            base.Update();

            if (m_closeButton.IsClicked
                || m_player == null
                || !m_player.IsAddedToProject
                || m_villager == null
                || !m_villager.IsAddedToProject)
            {
                if (m_player?.ComponentGui != null
                    && m_player.ComponentGui.ModalPanelWidget == this)
                {
                    m_player.ComponentGui.ModalPanelWidget = null;
                }
            }
        }
    }
}
