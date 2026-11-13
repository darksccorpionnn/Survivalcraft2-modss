using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game
{
    public sealed class ComponentLivingTownsBed : Component
    {
        private int m_ownerDoorX;
        private int m_ownerDoorY;
        private int m_ownerDoorZ;

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap)
        {
            m_ownerDoorX = valuesDictionary.GetValue<int>("OwnerDoorX", 0);
            m_ownerDoorY = valuesDictionary.GetValue<int>("OwnerDoorY", 0);
            m_ownerDoorZ = valuesDictionary.GetValue<int>("OwnerDoorZ", 0);
        }

        public override void Save(ValuesDictionary valuesDictionary, EntityToIdMap entityToIdMap)
        {
            valuesDictionary.SetValue("OwnerDoorX", m_ownerDoorX);
            valuesDictionary.SetValue("OwnerDoorY", m_ownerDoorY);
            valuesDictionary.SetValue("OwnerDoorZ", m_ownerDoorZ);
        }

        public void SetOwnerDoor(Point3 door)
        {
            m_ownerDoorX = door.X;
            m_ownerDoorY = door.Y;
            m_ownerDoorZ = door.Z;
        }

        public bool IsOwnedBy(Point3 door)
        {
            return m_ownerDoorX == door.X && m_ownerDoorY == door.Y && m_ownerDoorZ == door.Z;
        }

        public bool IsUnassigned()
        {
            return m_ownerDoorX == int.MinValue && m_ownerDoorY == int.MinValue && m_ownerDoorZ == int.MinValue;
        }
    }
}
