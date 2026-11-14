using System;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game
{
    /// <summary>
    /// Dedicated persistent trade stock. Never shares the villager's work inventory.
    /// </summary>
    public sealed class LivingTownsTradeInventory : IInventory
    {
        public const int DefaultSlotsCount = 10;
        private readonly int[] m_values = new int[DefaultSlotsCount];
        private readonly int[] m_counts = new int[DefaultSlotsCount];
        private int m_activeSlotIndex;
        private int m_visibleSlotsCount = DefaultSlotsCount;

        public Project Project { get; }
        public int SlotsCount => DefaultSlotsCount;

        public int VisibleSlotsCount
        {
            get => m_visibleSlotsCount;
            set => m_visibleSlotsCount = Math.Clamp(value, 0, DefaultSlotsCount);
        }

        public int ActiveSlotIndex
        {
            get => m_activeSlotIndex;
            set => m_activeSlotIndex = Math.Clamp(value, 0, Math.Max(0, VisibleSlotsCount - 1));
        }

        public LivingTownsTradeInventory(Project project)
        {
            Project = project ?? throw new ArgumentNullException(nameof(project));
        }

        public int GetSlotValue(int slotIndex)
        {
            return IsValidSlot(slotIndex) && m_counts[slotIndex] > 0 ? m_values[slotIndex] : 0;
        }

        public int GetSlotCount(int slotIndex)
        {
            return IsValidSlot(slotIndex) ? m_counts[slotIndex] : 0;
        }

        public int GetSlotCapacity(int slotIndex, int value)
        {
            if (!IsValidSlot(slotIndex) || value == 0)
                return 0;

            int contents = Terrain.ExtractContents(value);
            if (contents < 0 || contents >= BlocksManager.Blocks.Length || BlocksManager.Blocks[contents] == null)
                return 0;

            return BlocksManager.Blocks[contents].GetMaxStacking(value);
        }

        public int GetSlotProcessCapacity(int slotIndex, int value) => 0;

        public void AddSlotItems(int slotIndex, int value, int count)
        {
            if (count <= 0)
                return;
            if (!IsValidSlot(slotIndex))
                throw new ArgumentOutOfRangeException(nameof(slotIndex));

            int currentCount = m_counts[slotIndex];
            if (currentCount > 0 && m_values[slotIndex] != value)
                throw new InvalidOperationException("Cannot add a different item to an occupied trade slot.");

            int capacity = GetSlotCapacity(slotIndex, value);
            if (capacity <= 0 || currentCount + count > capacity)
                throw new InvalidOperationException("Trade slot capacity exceeded.");

            m_values[slotIndex] = value;
            m_counts[slotIndex] = currentCount + count;
        }

        public void ProcessSlotItems(int slotIndex, int value, int count, int processCount,
            out int processedValue, out int processedCount)
        {
            processedValue = value;
            processedCount = count;
        }

        public int RemoveSlotItems(int slotIndex, int count)
        {
            if (!IsValidSlot(slotIndex) || count <= 0)
                return 0;

            int removed = Math.Min(count, m_counts[slotIndex]);
            m_counts[slotIndex] -= removed;
            if (m_counts[slotIndex] == 0)
                m_values[slotIndex] = 0;
            return removed;
        }

        public void DropAllItems(Vector3 position)
        {
            SubsystemPickables pickables = Project.FindSubsystem<SubsystemPickables>(true);
            for (int i = 0; i < SlotsCount; i++)
            {
                int count = GetSlotCount(i);
                int value = GetSlotValue(i);
                if (count > 0 && RemoveSlotItems(i, count) > 0)
                    pickables.AddPickable(value, count, position, new Vector3(0f, 1f, 0f), null, null);
            }
        }

        public void Load(ValuesDictionary ownerValues)
        {
            if (ownerValues == null)
                return;
            ValuesDictionary savedInventory = ownerValues.GetValue<ValuesDictionary>("TradeInventory", null);
            ValuesDictionary savedSlots = savedInventory?.GetValue<ValuesDictionary>("Slots", null);
            if (savedSlots == null)
                return;

            for (int i = 0; i < SlotsCount; i++)
            {
                ValuesDictionary savedSlot = savedSlots.GetValue<ValuesDictionary>("Slot" + i, null);
                if (savedSlot == null)
                    continue;
                int value = savedSlot.GetValue<int>("Contents", 0);
                int count = savedSlot.GetValue<int>("Count", 0);
                int capacity = count > 0 ? GetSlotCapacity(i, value) : 0;
                if (value > 0 && count > 0 && count <= capacity)
                {
                    m_values[i] = value;
                    m_counts[i] = count;
                }
            }
        }

        public void Save(ValuesDictionary ownerValues)
        {
            if (ownerValues == null)
                return;
            ValuesDictionary savedInventory = new ValuesDictionary();
            ValuesDictionary savedSlots = new ValuesDictionary();
            savedInventory.SetValue("Slots", savedSlots);
            savedInventory.SetValue("SlotsCount", SlotsCount);

            for (int i = 0; i < SlotsCount; i++)
            {
                if (m_counts[i] <= 0)
                    continue;
                ValuesDictionary savedSlot = new ValuesDictionary();
                savedSlot.SetValue("Contents", m_values[i]);
                savedSlot.SetValue("Count", m_counts[i]);
                savedSlots.SetValue("Slot" + i, savedSlot);
            }
            ownerValues.SetValue("TradeInventory", savedInventory);
        }

        private bool IsValidSlot(int slotIndex) => slotIndex >= 0 && slotIndex < SlotsCount;
    }
}
