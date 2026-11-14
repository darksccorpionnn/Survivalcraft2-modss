using System;
using System.Xml.Linq;
using Engine;

namespace Game
{
    /// <summary>
    /// A native chest-style inventory panel for one villager's persistent trade stock.
    /// </summary>
    public sealed class LivingTownsTradeWidget : CanvasWidget
    {
        private readonly ComponentLivingTownsVillager m_villager;

        public LivingTownsTradeWidget(
            ComponentPlayer player,
            ComponentLivingTownsVillager villager,
            IInventory playerInventory,
            IInventory tradeInventory)
        {
            m_villager = villager;

            XElement node = ContentManager.Get<XElement>("Widgets/ChestWidget");
            if (node == null)
                throw new InvalidOperationException("Native ChestWidget layout could not be loaded.");
            LoadContents(this, node);

            GridPanelWidget tradeGrid = Children.Find<GridPanelWidget>("ChestGrid");
            GridPanelWidget playerGrid = Children.Find<GridPanelWidget>("InventoryGrid");
            LabelWidget tradeLabel = Children.Find<LabelWidget>("ChestLabel");
            LabelWidget playerLabel = Children.Find<LabelWidget>("InventoryLabel");
            if (tradeGrid == null || playerGrid == null)
                throw new InvalidOperationException("Native inventory grids could not be found.");

            if (tradeLabel != null)
                tradeLabel.Text = (string.IsNullOrEmpty(villager.VillagerName) ? "Köylü" : villager.VillagerName) + " - Takas Stoğu";
            if (playerLabel != null)
                playerLabel.Text = "Oyuncu Envanteri";

            tradeGrid.Children.Clear();
            tradeGrid.ColumnsCount = 5;
            tradeGrid.RowsCount = 2;
            int tradeSlot = 0;
            for (int row = 0; row < tradeGrid.RowsCount; row++)
            {
                for (int column = 0; column < tradeGrid.ColumnsCount; column++)
                {
                    InventorySlotWidget slot = new InventorySlotWidget();
                    slot.AssignInventorySlot(tradeInventory, tradeSlot);
                    tradeGrid.Children.Add(slot);
                    tradeGrid.SetWidgetCell(slot, new Point2(column, row));
                    tradeSlot++;
                }
            }

            playerGrid.Children.Clear();
            int playerSlot = 10;
            for (int row = 0; row < playerGrid.RowsCount; row++)
            {
                for (int column = 0; column < playerGrid.ColumnsCount; column++)
                {
                    if (playerSlot >= playerInventory.SlotsCount)
                        break;
                    InventorySlotWidget slot = new InventorySlotWidget();
                    slot.AssignInventorySlot(playerInventory, playerSlot);
                    playerGrid.Children.Add(slot);
                    playerGrid.SetWidgetCell(slot, new Point2(column, row));
                    playerSlot++;
                }
            }
        }

        public override void Update()
        {
            if (m_villager == null || !m_villager.IsAddedToProject)
            {
                if (ParentWidget != null)
                    ParentWidget.Children.Remove(this);
            }
        }

        public static bool TryOpen(ComponentPlayer player, ComponentLivingTownsVillager villager)
        {
            if (player?.ComponentGui == null || player.ComponentMiner?.Inventory == null || villager == null)
                return false;

            try
            {
                IInventory tradeInventory = villager.GetTradeInventory();
                if (tradeInventory == null)
                    return false;

                player.ComponentGui.ModalPanelWidget = new LivingTownsTradeWidget(
                    player, villager, player.ComponentMiner.Inventory, tradeInventory);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[LivingTowns] Could not open villager trade inventory: " + ex);
                return false;
            }
        }
    }
}
