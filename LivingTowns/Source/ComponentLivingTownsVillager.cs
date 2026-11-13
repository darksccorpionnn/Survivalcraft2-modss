using System;
using System.Collections.Generic;
using Engine;
using Engine.Animation;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game
{
    public enum LivingTownsJob { Unemployed = 0, Farmer = 1, Lumberjack = 2, Guard = 3, Miner = 4, Blacksmith = 5, Merchant = 6 }

    public sealed class ComponentLivingTownsVillager : Component, IUpdateable
    {
        private ComponentBody m_body;
        private ComponentPathfinding m_pathfinding;
        private ComponentInventory m_inventory;
        private ComponentModel m_model;
        private SubsystemTerrain m_terrain;
        private SubsystemTimeOfDay m_timeOfDay;
        private SubsystemFurnitureBlockBehavior m_furniture;
        private double m_nextThinkTime;
        private Point3 m_bedCell;
        private bool m_hasBedCell;
        private bool m_sleepAnimation;
        private LivingTownsJob m_job = LivingTownsJob.Unemployed;
        private Point3 m_homeDoor;
        private bool m_hasHomeDoor;
        private const int WorkRadius = 64;
        private const int FarmCellsPerScan = 512;
        private int m_farmSearchRadius, m_farmSearchDx, m_farmSearchDz;

        public UpdateOrder UpdateOrder => UpdateOrder.Default;
        public LivingTownsJob Job => m_job;
        public bool HasHomeDoor => m_hasHomeDoor;
        public Point3 HomeDoor => m_homeDoor;
        public ComponentInventory Inventory => m_inventory;

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap)
        {
            m_body = Entity.FindComponent<ComponentBody>(true);
            m_pathfinding = Entity.FindComponent<ComponentPathfinding>();
            m_inventory = Entity.FindComponent<ComponentInventory>();
            m_model = Entity.FindComponent<ComponentModel>();
            m_terrain = Project.FindSubsystem<SubsystemTerrain>(true);
            m_timeOfDay = Project.FindSubsystem<SubsystemTimeOfDay>(true);
            m_furniture = Project.FindSubsystem<SubsystemFurnitureBlockBehavior>();
            m_job = (LivingTownsJob)valuesDictionary.GetValue<int>("Job", 0);
            m_hasHomeDoor = valuesDictionary.GetValue<bool>("HasHomeDoor", false);
            m_homeDoor = new Point3(valuesDictionary.GetValue<int>("HomeDoorX", 0), valuesDictionary.GetValue<int>("HomeDoorY", 0), valuesDictionary.GetValue<int>("HomeDoorZ", 0));
            m_bedCell = new Point3(valuesDictionary.GetValue<int>("BedX", 0), valuesDictionary.GetValue<int>("BedY", 0), valuesDictionary.GetValue<int>("BedZ", 0));
            m_hasBedCell = valuesDictionary.GetValue<bool>("HasBed", false);
            Log.Information("[LivingTowns] Villager controller loaded.");
        }

        public override void Save(ValuesDictionary valuesDictionary, EntityToIdMap entityToIdMap)
        {
            valuesDictionary.SetValue("Job", (int)m_job);
            valuesDictionary.SetValue("HasHomeDoor", m_hasHomeDoor);
            valuesDictionary.SetValue("HomeDoorX", m_homeDoor.X);
            valuesDictionary.SetValue("HomeDoorY", m_homeDoor.Y);
            valuesDictionary.SetValue("HomeDoorZ", m_homeDoor.Z);
            valuesDictionary.SetValue("HasBed", m_hasBedCell);
            valuesDictionary.SetValue("BedX", m_bedCell.X);
            valuesDictionary.SetValue("BedY", m_bedCell.Y);
            valuesDictionary.SetValue("BedZ", m_bedCell.Z);
        }

        public void SetHomeDoor(int x, int y, int z) { m_homeDoor = new Point3(x, y, z); m_hasHomeDoor = true; }

        public void Update(float dt)
        {
            m_nextThinkTime -= dt;
            if (m_nextThinkTime > 0.0 || m_body == null) return;
            m_nextThinkTime = 1.0;
            if (IsNight()) { TickSleepRoutine(); return; }
            SetSleepingAnimation(false);
            if (m_job == LivingTownsJob.Farmer) TickFarmer();
        }

        public void ShowJobMenu(ComponentPlayer player)
        {
            if (player == null) return;
            List<string> options = new List<string> {
                "Meslek: İşsiz", "Meslek: Çiftçi", "Oduncu (hazırlanıyor)",
                "Muhafız (hazırlanıyor)", "Madenci (hazırlanıyor)",
                "Demirci (hazırlanıyor)", "Tüccar (hazırlanıyor)",
                "Eldeki eşyayı NPC'ye ver"
            };
            DialogsManager.ShowDialog(null, new ListSelectionDialog("Köylü Görevleri", options, 60f,
                item => item == null ? string.Empty : item.ToString(),
                item => HandleMenuChoice(player, item == null ? string.Empty : item.ToString())));
        }

        private void HandleMenuChoice(ComponentPlayer player, string choice)
        {
            switch (choice)
            {
                case "Meslek: İşsiz": SetJob(LivingTownsJob.Unemployed, player); break;
                case "Meslek: Çiftçi": SetJob(LivingTownsJob.Farmer, player); break;
                case "Oduncu (hazırlanıyor)":
                case "Muhafız (hazırlanıyor)":
                case "Madenci (hazırlanıyor)":
                case "Demirci (hazırlanıyor)":
                case "Tüccar (hazırlanıyor)":
                    player.ComponentGui?.DisplaySmallMessage("Bu meslek henüz etkin değil. Şimdilik çiftçi görevini kullanabilirsin.", Color.White, true, false);
                    break;
                case "Eldeki eşyayı NPC'ye ver": GiveHeldItems(player); break;
            }
        }

        private void SetJob(LivingTownsJob job, ComponentPlayer player)
        {
            m_job = job; m_farmSearchRadius = 0; m_farmSearchDx = 0; m_farmSearchDz = 0;
            m_pathfinding?.Stop();
            player.ComponentGui?.DisplaySmallMessage("NPC görevi: " + JobName(job), Color.White, true, false);
        }

        private static string JobName(LivingTownsJob job)
        {
            return job switch {
                LivingTownsJob.Farmer => "Çiftçi", LivingTownsJob.Lumberjack => "Oduncu",
                LivingTownsJob.Guard => "Muhafız", LivingTownsJob.Miner => "Madenci",
                LivingTownsJob.Blacksmith => "Demirci", LivingTownsJob.Merchant => "Tüccar",
                _ => "İşsiz"
            };
        }

        private void GiveHeldItems(ComponentPlayer player)
        {
            if (m_inventory == null || player?.ComponentMiner?.Inventory == null) {
                player?.ComponentGui?.DisplaySmallMessage("NPC envanteri bulunamadı.", Color.White, true, false); return;
            }
            IInventory source = player.ComponentMiner.Inventory;
            int active = source.ActiveSlotIndex;
            if (active < 0 || active >= source.SlotsCount || source.GetSlotCount(active) <= 0) {
                player.ComponentGui?.DisplaySmallMessage("Elinde verilecek eşya yok.", Color.White, true, false); return;
            }
            int value = source.GetSlotValue(active);
            int targetSlot = ComponentInventoryBase.FindAcquireSlotForItem(m_inventory, value);
            if (targetSlot < 0) { player.ComponentGui?.DisplaySmallMessage("NPC envanteri dolu.", Color.White, true, false); return; }
            int free = m_inventory.GetSlotCapacity(targetSlot, value) - m_inventory.GetSlotCount(targetSlot);
            int amount = Math.Min(source.GetSlotCount(active), free);
            if (amount <= 0) return;
            source.RemoveSlotItems(active, amount);
            m_inventory.AddSlotItems(targetSlot, value, amount);
            player.ComponentGui?.DisplaySmallMessage("NPC'ye " + amount + " adet eşya verildi.", Color.White, true, false);
        }

        private bool IsNight()
        {
            return m_timeOfDay != null && m_timeOfDay.TimeOfDayEnabled &&
                IntervalUtils.IsBetween(m_timeOfDay.TimeOfDay, m_timeOfDay.NightStart, m_timeOfDay.DawnStart);
        }

        private void TickSleepRoutine()
        {
            if (!m_hasBedCell && !TryFindNamedBed(out m_bedCell)) return;
            m_hasBedCell = true;
            Vector3 bedPosition = new Vector3(m_bedCell.X + 0.5f, m_bedCell.Y, m_bedCell.Z + 0.5f);
            if (Vector3.Distance(m_body.Position, bedPosition) <= 1.5f) {
                m_pathfinding?.Stop(); SetSleepingAnimation(true);
            } else {
                SetSleepingAnimation(false);
                m_pathfinding?.SetDestination(bedPosition, 1.0f, 1.2f, 128, false, false, false, null);
            }
        }

        private bool TryFindNamedBed(out Point3 bed)
        {
            bed = new Point3(0, 0, 0);
            if (m_furniture == null || m_terrain == null) return false;
            int cx = m_hasHomeDoor ? m_homeDoor.X : Terrain.ToCell(m_body.Position.X);
            int cy = m_hasHomeDoor ? m_homeDoor.Y : Terrain.ToCell(m_body.Position.Y);
            int cz = m_hasHomeDoor ? m_homeDoor.Z : Terrain.ToCell(m_body.Position.Z);
            int bestDistance = int.MaxValue;
            for (int x = cx - 16; x <= cx + 16; x++)
            for (int z = cz - 16; z <= cz + 16; z++)
            for (int y = Math.Max(0, cy - 3); y <= Math.Min(254, cy + 5); y++) {
                int value = m_terrain.Terrain.GetCellValue(x, y, z);
                if (Terrain.ExtractContents(value) != FurnitureBlock.Index) continue;
                FurnitureDesign design = m_furniture.GetDesign(FurnitureBlock.GetDesignIndex(Terrain.ExtractData(value)));
                string name = design?.Name ?? string.Empty;
                if (!name.Contains("bed", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("yatak", StringComparison.OrdinalIgnoreCase)) continue;
                int distance = Math.Abs(x-cx) + Math.Abs(y-cy) + Math.Abs(z-cz);
                if (distance < bestDistance) { bestDistance = distance; bed = new Point3(x,y,z); }
            }
            return bestDistance != int.MaxValue;
        }

        private void SetSleepingAnimation(bool sleeping)
        {
            if (m_sleepAnimation == sleeping) return;
            m_sleepAnimation = sleeping;
            if (m_model?.AnimationController != null) m_model.AnimationController.Parameters.SetBool("IsSleeping", sleeping);
        }

        private void TickFarmer()
        {
            if (m_inventory == null || m_terrain == null || m_pathfinding == null) return;
            int rakeSlot = FindItemSlot(BlocksManager.GetBlockIndex<IronRakeBlock>(), null);
            int seedsSlot = FindItemSlot(BlocksManager.GetBlockIndex<SeedsBlock>(), 5);
            if (rakeSlot < 0 || seedsSlot < 0) { m_pathfinding.Stop(); return; }
            if (!TryFindFarmTask(out Point3 target, out int task)) { m_pathfinding.Stop(); return; }
            Vector3 targetPosition = task == 4
                ? new Vector3(target.X + 1.5f, target.Y, target.Z + 0.5f)
                : new Vector3(target.X + 0.5f, target.Y + 1f, target.Z + 0.5f);
            if (Vector3.Distance(m_body.Position, targetPosition) > 1.8f) {
                m_pathfinding.SetDestination(targetPosition, 1.0f, 1.3f, 200, false, false, false, null); return;
            }
            m_pathfinding.Stop();
            if (task == 1) {
                m_terrain.ChangeCell(target.X,target.Y,target.Z,Terrain.ReplaceContents(m_terrain.Terrain.GetCellValue(target.X,target.Y,target.Z),168));
                DamageRake(rakeSlot);
            } else if (task == 2) {
                m_terrain.ChangeCell(target.X,target.Y,target.Z,Terrain.ReplaceContents(m_terrain.Terrain.GetCellValue(target.X,target.Y,target.Z),2));
                DamageRake(rakeSlot);
            } else if (task == 3) {
                int rye = BlocksManager.GetBlockIndex<RyeBlock>();
                int cropData = RyeBlock.SetSize(RyeBlock.SetIsWild(0,false),0);
                m_terrain.ChangeCell(target.X,target.Y+1,target.Z,Terrain.MakeBlockValue(rye,0,cropData));
                m_inventory.RemoveSlotItems(seedsSlot,1);
            } else if (task == 4) HarvestCrop(target.X,target.Y,target.Z);
        }

        private bool TryFindFarmTask(out Point3 target, out int task)
        {
            target = new Point3(0,0,0); task = 0;
            int centerX = m_hasHomeDoor ? m_homeDoor.X : Terrain.ToCell(m_body.Position.X);
            int centerZ = m_hasHomeDoor ? m_homeDoor.Z : Terrain.ToCell(m_body.Position.Z);
            int rye = BlocksManager.GetBlockIndex<RyeBlock>();
            for (int scanned=0; scanned<FarmCellsPerScan; scanned++) {
                if (m_farmSearchRadius > WorkRadius) { m_farmSearchRadius=0; m_farmSearchDx=0; m_farmSearchDz=0; return false; }
                int radius=m_farmSearchRadius, dx=m_farmSearchDx, dz=m_farmSearchDz;
                AdvanceFarmSearchPosition();
                if (Math.Max(Math.Abs(dx),Math.Abs(dz)) != radius) continue;
                int x=centerX+dx, z=centerZ+dz;
                int y=m_terrain.Terrain.GetTopHeight(x,z);
                if (y<1 || y>253) continue;
                int groundValue=m_terrain.Terrain.GetCellValue(x,y,z);
                int ground=Terrain.ExtractContents(groundValue);
                int aboveValue=m_terrain.Terrain.GetCellValue(x,y+1,z);
                int above=Terrain.ExtractContents(aboveValue);
                if (ground==rye && RyeBlock.GetSize(Terrain.ExtractData(groundValue))>=7 &&
                    Terrain.ExtractContents(m_terrain.Terrain.GetCellValue(x,y-1,z))==168) {
                    target=new Point3(x,y,z); task=4; return true;
                }
                if (ground==168 && above==0) { target=new Point3(x,y,z); task=3; return true; }
                if (above==0 && ground==2) { target=new Point3(x,y,z); task=1; return true; }
                if (above==0 && ground==8) { target=new Point3(x,y,z); task=2; return true; }
            }
            return false;
        }

        private void AdvanceFarmSearchPosition()
        {
            int radius=m_farmSearchRadius;
            m_farmSearchDz++;
            if (m_farmSearchDz>radius) { m_farmSearchDz=-radius; m_farmSearchDx++; }
            if (m_farmSearchDx>radius) { m_farmSearchRadius++; m_farmSearchDx=-m_farmSearchRadius; m_farmSearchDz=-m_farmSearchRadius; }
        }

        private int FindItemSlot(int content, int? data)
        {
            if (content<=0 || m_inventory==null) return -1;
            for (int i=0;i<m_inventory.SlotsCount;i++) {
                int value=m_inventory.GetSlotValue(i);
                if (m_inventory.GetSlotCount(i)>0 && Terrain.ExtractContents(value)==content &&
                    (!data.HasValue || Terrain.ExtractData(value)==data.Value)) return i;
            }
            return -1;
        }

        private void DamageRake(int slot)
        {
            int value=m_inventory.GetSlotValue(slot), content=Terrain.ExtractContents(value);
            Block block=BlocksManager.Blocks[content];
            int durability=block.GetDurability(value), damage=block.GetDamage(value)+1;
            m_inventory.RemoveSlotItems(slot,1);
            if (durability<=0 || damage<durability) m_inventory.AddSlotItems(slot,block.SetDamage(value,damage),1);
        }

        private void HarvestCrop(int x,int y,int z)
        {
            int cropValue=m_terrain.Terrain.GetCellValue(x,y,z), content=Terrain.ExtractContents(cropValue);
            Block cropBlock=BlocksManager.Blocks[content];
            List<BlockDropValue> drops=new List<BlockDropValue>();
            cropBlock.GetDropValues(m_terrain,cropValue,0,0,drops,out bool showDebris);
            Vector3 dropPosition=new Vector3(x+0.5f,y+0.25f,z+0.5f);
            SubsystemPickables pickables=Project.FindSubsystem<SubsystemPickables>(true);
            foreach(BlockDropValue drop in drops) {
                if(drop.Count<=0 || Terrain.ExtractContents(drop.Value)==0) continue;
                int remainder=ComponentInventoryBase.AcquireItems(m_inventory,drop.Value,drop.Count);
                if(remainder>0) pickables.AddPickable(drop.Value,remainder,dropPosition,null,null);
            }
            m_terrain.ChangeCell(x,y,z,0);
        }
    }
}
