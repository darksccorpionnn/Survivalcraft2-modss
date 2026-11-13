using System;
using System.Linq;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game
{
    public sealed class SubsystemLivingTownsDoorBehavior : SubsystemBlockBehavior
    {
        private SubsystemCreatureSpawn m_creatureSpawn;
        private static readonly System.Random s_random = new System.Random();
        private static readonly string[] s_templates =
        {
            "LivingTowns_MaleVillager",
            "LivingTowns_FemaleVillager",
            "LivingTowns_Farmer"
        };

        public override int[] HandledBlocks => BlocksManager.Blocks
            .Select((block, index) => new { block, index })
            .Where(item => item.block is DoorBlock)
            .Select(item => item.index)
            .ToArray();

        public override void Load(ValuesDictionary valuesDictionary)
        {
            base.Load(valuesDictionary);
            m_creatureSpawn = Project.FindSubsystem<SubsystemCreatureSpawn>(true);
            Log.Information("[LivingTowns] Door behavior ready.");
        }

        public override void OnBlockAdded(int value, int oldValue, int x, int y, int z)
        {
            int contents = Terrain.ExtractContents(value);
            if (contents == 0 || contents >= BlocksManager.Blocks.Length || BlocksManager.Blocks[contents] is not DoorBlock)
                return;
            if (Terrain.ExtractContents(oldValue) == contents)
                return;
            if (DoorBlock.IsTopPart(SubsystemTerrain.Terrain, x, y, z))
                return;

            TrySpawnForDoor(x, y, z);
        }

        public override void OnChunkInitialized(TerrainChunk chunk)
        {
            for (int lx = 0; lx < TerrainChunk.Size; lx++)
            {
                for (int lz = 0; lz < TerrainChunk.Size; lz++)
                {
                    int x = chunk.Origin.X + lx;
                    int z = chunk.Origin.Y + lz;
                    for (int y = 0; y < TerrainChunk.Height; y++)
                    {
                        int contents = chunk.GetCellContentsFast(lx, y, lz);
                        if (contents > 0 && contents < BlocksManager.Blocks.Length && BlocksManager.Blocks[contents] is DoorBlock)
                        {
                            if (!DoorBlock.IsTopPart(SubsystemTerrain.Terrain, x, y, z))
                                TrySpawnForDoor(x, y, z);
                        }
                    }
                }
            }
        }

        private void TrySpawnForDoor(int x, int y, int z)
        {
            if (HasVillagerForDoor(x, y, z))
                return;

            if (!TryFindSpawnPosition(x, y, z, out Vector3 position))
            {
                Log.Information($"[LivingTowns] Door at {x},{y},{z} has no safe adjacent spawn cell yet.");
                return;
            }

            string template = s_templates[s_random.Next(s_templates.Length)];
            Entity entity = m_creatureSpawn.SpawnCreature(template, position, true);
            if (entity == null)
                return;

            ComponentLivingTownsVillager villager = entity.FindComponent<ComponentLivingTownsVillager>();
            if (villager != null)
                villager.SetHomeDoor(x, y, z);
            else
                Log.Error("[LivingTowns] Spawned villager is missing ComponentLivingTownsVillager. Check the mod database.");
        }

        private bool HasVillagerForDoor(int x, int y, int z)
        {
            foreach (ComponentCreature creature in m_creatureSpawn.Creatures)
            {
                ComponentLivingTownsVillager villager = creature.Entity.FindComponent<ComponentLivingTownsVillager>();
                if (villager != null && villager.HasHomeDoor &&
                    villager.HomeDoor.X == x && villager.HomeDoor.Y == y && villager.HomeDoor.Z == z)
                    return true;
            }
            return false;
        }

        private bool TryFindSpawnPosition(int doorX, int doorY, int doorZ, out Vector3 position)
        {
            Point3[] offsets =
            {
                new Point3(1,0,0), new Point3(-1,0,0), new Point3(0,0,1), new Point3(0,0,-1),
                new Point3(2,0,0), new Point3(-2,0,0), new Point3(0,0,2), new Point3(0,0,-2),
                new Point3(1,0,1), new Point3(-1,0,1), new Point3(1,0,-1), new Point3(-1,0,-1)
            };

            foreach (Point3 offset in offsets)
            {
                int x = doorX + offset.X;
                int y = doorY + offset.Y;
                int z = doorZ + offset.Z;
                int ground = SubsystemTerrain.Terrain.GetCellValue(x, y - 1, z);
                int feet = SubsystemTerrain.Terrain.GetCellValue(x, y, z);
                int head = SubsystemTerrain.Terrain.GetCellValue(x, y + 1, z);
                if (Terrain.ExtractContents(ground) != 0 &&
                    Terrain.ExtractContents(feet) == 0 && Terrain.ExtractContents(head) == 0)
                {
                    position = new Vector3(x + 0.5f, y, z + 0.5f);
                    return true;
                }
            }

            position = Vector3.Zero;
            return false;
        }
    }
}
