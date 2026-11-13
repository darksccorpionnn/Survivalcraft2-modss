using System;
using System.Collections.Generic;
using Engine;
using Engine.Animation;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game
{
    public enum LivingTownsJob
    {
        Unemployed = 0,
        Farmer = 1,
        Lumberjack = 2,
        Guard = 3,
        Miner = 4,
        Blacksmith = 5,
        Merchant = 6
    }

    public sealed class ComponentLivingTownsVillager : Component, IUpdateable
    {
        private static readonly System.Random s_random = new System.Random();
        private static readonly Point3[] s_doorScanOffsets =
        {
            new Point3(-1,0,-1), new Point3(0,0,-1), new Point3(1,0,-1),
            new Point3(-1,0,0),  new Point3(0,0,0),  new Point3(1,0,0),
            new Point3(-1,0,1),  new Point3(0,0,1),  new Point3(1,0,1)
        };

        private ComponentBody m_body;
        private ComponentPathfinding m_pathfinding;
        private ComponentInventory m_inventory;
        private ComponentModel m_model;
        private ComponentLocomotion m_locomotion;
        private SubsystemTerrain m_terrain;
        private SubsystemTimeOfDay m_timeOfDay;
        private SubsystemCreatureSpawn m_creatureSpawn;
        private SubsystemDoorBlockBehavior m_doorBehavior;
        private SubsystemPickables m_pickables;

        private LivingTownsJob m_job = LivingTownsJob.Unemployed;
        private Point3 m_homeDoor;
        private bool m_hasHomeDoor;
        private Point3 m_bedCell;
        private bool m_hasBedCell;
        private Vector3 m_bedFramePosition;
        private double m_nextBedTryTime;
        private double m_nextThinkTime;
        private double m_nextDoorCheckTime;
        private double m_nextWorkSearchTime;
        private double m_chatCooldown;
        private double m_chatTimeRemaining;
        private double m_patrolPause;
        private bool m_isTalking;
        private ComponentLivingTownsVillager m_chatPartner;
        private bool m_sleepAnimation;
        private bool m_wasGravityEnabled;
        private bool m_sleepPhysicsSaved;
        private readonly List<Point3> m_autoOpenedDoors = new List<Point3>();
        private Point3 m_workTarget;
        private bool m_hasWorkTarget;
        private bool m_farmerKitGranted;

        private const float VillagerWalkSpeed = 3.15f;
        private const float WorkMoveSpeed = 2.8f;
        private const int FarmerWorkRadius = 32;
        private const int FarmCellsPerScan = 512;

        public UpdateOrder UpdateOrder => UpdateOrder.Default;
        public LivingTownsJob Job => m_job;
        public bool HasHomeDoor => m_hasHomeDoor;
        public Point3 HomeDoor => m_homeDoor;
        public ComponentInventory Inventory => m_inventory;
        public bool IsTalking => m_isTalking;

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap)
        {
            m_body = Entity.FindComponent<ComponentBody>(true);
            m_pathfinding = Entity.FindComponent<ComponentPathfinding>();
            m_inventory = Entity.FindComponent<ComponentInventory>();
            m_model = Entity.FindComponent<ComponentModel>();
            m_locomotion = Entity.FindComponent<ComponentLocomotion>();
            m_terrain = Project.FindSubsystem<SubsystemTerrain>(true);
            m_timeOfDay = Project.FindSubsystem<SubsystemTimeOfDay>(true);
            m_creatureSpawn = Project.FindSubsystem<SubsystemCreatureSpawn>(true);
            m_doorBehavior = Project.FindSubsystem<SubsystemDoorBlockBehavior>(true);
            m_pickables = Project.FindSubsystem<SubsystemPickables>(true);

            m_job = (LivingTownsJob)valuesDictionary.GetValue<int>("Job", 0);
            m_hasHomeDoor = valuesDictionary.GetValue<bool>("HasHomeDoor", false);
            m_homeDoor = new Point3(
                valuesDictionary.GetValue<int>("HomeDoorX", 0),
                valuesDictionary.GetValue<int>("HomeDoorY", 0),
                valuesDictionary.GetValue<int>("HomeDoorZ", 0));
            m_bedCell = new Point3(
                valuesDictionary.GetValue<int>("BedX", 0),
                valuesDictionary.GetValue<int>("BedY", 0),
                valuesDictionary.GetValue<int>("BedZ", 0));
            m_hasBedCell = valuesDictionary.GetValue<bool>("HasBed", false);
            m_farmerKitGranted = valuesDictionary.GetValue<bool>("FarmerKitGranted", false);

            if (m_locomotion != null)
                m_locomotion.WalkSpeed = Math.Max(m_locomotion.WalkSpeed, VillagerWalkSpeed);
            if (m_body != null)
                m_body.IsGravityEnabled = true;

            Log.Information("[LivingTowns] Villager controller loaded; speed=" + VillagerWalkSpeed + ", job=" + m_job);
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
            valuesDictionary.SetValue("FarmerKitGranted", m_farmerKitGranted);
        }

        public void SetHomeDoor(int x, int y, int z)
        {
            m_homeDoor = new Point3(x, y, z);
            m_hasHomeDoor = true;
            m_hasBedCell = false;
            m_nextBedTryTime = 0.0;
        }

        public void Update(float dt)
        {
            if (m_body == null)
                return;

            m_nextDoorCheckTime -= dt;
            if (m_nextDoorCheckTime <= 0.0)
            {
                m_nextDoorCheckTime = 0.10;
                TickAutomaticDoors();
            }

            if (IsNight())
            {
                EndConversation(false);
                TickSleepRoutine();
                return;
            }

            WakeFromBedIfNeeded();
            m_chatCooldown -= dt;

            if (m_isTalking)
            {
                m_chatTimeRemaining -= dt;
                if (m_chatTimeRemaining <= 0.0)
                    EndConversation(true);
                else
                {
                    FaceChatPartner();
                    return;
                }
            }

            m_nextThinkTime -= dt;
            if (m_nextThinkTime > 0.0)
                return;
            m_nextThinkTime = 0.35;

            if (TryStartConversation())
                return;
            TickJob();
        }

        public void ShowJobMenu(ComponentPlayer player)
        {
            if (player == null)
                return;
            List<string> options = new List<string>
            {
                "Meslek: İşsiz (ev çevresinde dolaş)",
                "Meslek: Çiftçi (otomatik tarım)",
                "Meslek: Oduncu (ağaç kes)",
                "Meslek: Muhafız (devriye gez)",
                "Meslek: Madenci (cevher çıkar)",
                "Meslek: Demirci (fırın yanında çalış)",
                "Meslek: Tüccar (köylülerle buluş)",
                "Eldeki eşyayı NPC'ye ver"
            };
            DialogsManager.ShowDialog(null, new ListSelectionDialog(
                "Köylü Görevleri",
                options,
                60f,
                item => item == null ? string.Empty : item.ToString(),
                item => HandleMenuChoice(player, item == null ? string.Empty : item.ToString())
            ));
        }

        private void HandleMenuChoice(ComponentPlayer player, string choice)
        {
            switch (choice.Trim())
            {
                case "Meslek: İşsiz (ev çevresinde dolaş)": SetJob(LivingTownsJob.Unemployed, player); break;
                case "Meslek: Çiftçi (otomatik tarım)": SetJob(LivingTownsJob.Farmer, player); break;
                case "Meslek: Oduncu (ağaç kes)": SetJob(LivingTownsJob.Lumberjack, player); break;
                case "Meslek: Muhafız (devriye gez)": SetJob(LivingTownsJob.Guard, player); break;
                case "Meslek: Madenci (cevher çıkar)": SetJob(LivingTownsJob.Miner, player); break;
                case "Meslek: Demirci (fırın yanında çalış)": SetJob(LivingTownsJob.Blacksmith, player); break;
                case "Meslek: Tüccar (köylülerle buluş)": SetJob(LivingTownsJob.Merchant, player); break;
                case "Eldeki eşyayı NPC'ye ver": GiveHeldItems(player); break;
            }
        }

        private void SetJob(LivingTownsJob job, ComponentPlayer player)
        {
            m_job = job;
            m_hasWorkTarget = false;
            m_nextWorkSearchTime = 0.0;
            m_patrolPause = 0.0;
            m_pathfinding?.Stop();

            if (job == LivingTownsJob.Farmer)
                EnsureFarmerKit();

            string message = "NPC görevi: " + JobName(job);
            if (job == LivingTownsJob.Farmer)
                message += " — demir tırmık ve çavdar tohumu verildi.";
            player.ComponentGui?.DisplaySmallMessage(message, Color.White, true, false);
        }

        private static string JobName(LivingTownsJob job)
        {
            switch (job)
            {
                case LivingTownsJob.Farmer: return "Çiftçi";
                case LivingTownsJob.Lumberjack: return "Oduncu";
                case LivingTownsJob.Guard: return "Muhafız";
                case LivingTownsJob.Miner: return "Madenci";
                case LivingTownsJob.Blacksmith: return "Demirci";
                case LivingTownsJob.Merchant: return "Tüccar";
                default: return "İşsiz";
            }
        }

        private void GiveHeldItems(ComponentPlayer player)
        {
            if (m_inventory == null || player?.ComponentMiner?.Inventory == null)
            {
                player?.ComponentGui?.DisplaySmallMessage("NPC envanteri bulunamadı.", Color.White, true, false);
                return;
            }

            IInventory source = player.ComponentMiner.Inventory;
            int active = source.ActiveSlotIndex;
            if (active < 0 || active >= source.SlotsCount || source.GetSlotCount(active) <= 0)
            {
                player.ComponentGui?.DisplaySmallMessage("Elinde verilecek eşya yok.", Color.White, true, false);
                return;
            }

            int value = source.GetSlotValue(active);
            int targetSlot = ComponentInventoryBase.FindAcquireSlotForItem(m_inventory, value);
            if (targetSlot < 0)
            {
                player.ComponentGui?.DisplaySmallMessage("NPC envanteri dolu.", Color.White, true, false);
                return;
            }

            int free = m_inventory.GetSlotCapacity(targetSlot, value) - m_inventory.GetSlotCount(targetSlot);
            int amount = Math.Min(source.GetSlotCount(active), free);
            if (amount <= 0)
                return;

            source.RemoveSlotItems(active, amount);
            m_inventory.AddSlotItems(targetSlot, value, amount);
            player.ComponentGui?.DisplaySmallMessage("NPC'ye " + amount + " adet eşya verildi.", Color.White, true, false);
        }

        private bool EnsureAssignedBed()
        {
            if (!m_hasHomeDoor)
            {
                m_homeDoor = new Point3(Terrain.ToCell(m_body.Position.X), Terrain.ToCell(m_body.Position.Y), Terrain.ToCell(m_body.Position.Z));
                m_hasHomeDoor = true;
            }

            foreach (Entity entity in Project.Entities)
            {
                ComponentLivingTownsBed bed = entity.FindComponent<ComponentLivingTownsBed>();
                if (bed == null || !bed.IsOwnedBy(m_homeDoor))
                    continue;
                ComponentFrame frame = entity.FindComponent<ComponentFrame>();
                if (frame == null)
                    continue;
                m_bedFramePosition = frame.Position;
                m_bedCell = new Point3(Terrain.ToCell(frame.Position.X), Terrain.ToCell(frame.Position.Y - 1.736f), Terrain.ToCell(frame.Position.Z));
                m_hasBedCell = true;
                return true;
            }

            m_nextBedTryTime -= 0.35;
            if (m_nextBedTryTime > 0.0)
                return false;
            m_nextBedTryTime = 4.0;

            if (!TryFindBedPlacement(out Point3 floorCell, out Vector3 modelPosition))
                return false;

            try
            {
                ValuesDictionary values = DatabaseManager.FindEntityValuesDictionary("LivingTowns_CelticBed", true);
                Entity bedEntity = Project.CreateEntity(values);
                bedEntity.FindComponent<ComponentFrame>(true).Position = modelPosition;
                bedEntity.FindComponent<ComponentLivingTownsBed>(true).SetOwnerDoor(m_homeDoor);
                Project.AddEntity(bedEntity);

                m_bedFramePosition = modelPosition;
                m_bedCell = floorCell;
                m_hasBedCell = true;
                Log.Information("[LivingTowns] Dedicated Celtic bed created for " + m_homeDoor.X + "," + m_homeDoor.Y + "," + m_homeDoor.Z);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[LivingTowns] Unable to create NPC bed: " + ex);
                return false;
            }
        }

        private bool TryFindBedPlacement(out Point3 floorCell, out Vector3 modelPosition)
        {
            floorCell = new Point3(0, 0, 0);
            modelPosition = Vector3.Zero;
            int bestScore = int.MinValue;
            bool found = false;

            for (int radius = 2; radius <= 6; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius)
                        continue;
                    int x = m_homeDoor.X + dx;
                    int z = m_homeDoor.Z + dz;
                    for (int yOffset = -2; yOffset <= 1; yOffset++)
                    {
                        int y = m_homeDoor.Y - 1 + yOffset;
                        if (y < 1 || y > 252)
                            continue;
                        int groundValue = m_terrain.Terrain.GetCellValue(x, y, z);
                        int groundContent = Terrain.ExtractContents(groundValue);
                        if (groundContent == 0 || !BlocksManager.Blocks[groundContent].IsCollidable_(groundValue))
                            continue;
                        int feetValue = m_terrain.Terrain.GetCellValue(x, y + 1, z);
                        int headValue = m_terrain.Terrain.GetCellValue(x, y + 2, z);
                        if (Terrain.ExtractContents(feetValue) != 0 || Terrain.ExtractContents(headValue) != 0)
                            continue;

                        Vector3 candidate = new Vector3(x + 0.5f, y + 1.736f, z + 0.5f);
                        if (IsBedTooClose(candidate))
                            continue;
                        int score = 100 - (Math.Abs(dx) + Math.Abs(dz)) * 3 - Math.Abs(yOffset) * 4;
                        if (Terrain.ExtractContents(m_terrain.Terrain.GetCellValue(x, y + 3, z)) != 0)
                            score += 24;
                        for (int nx = -1; nx <= 1; nx++)
                        for (int nz = -1; nz <= 1; nz++)
                        {
                            if (nx == 0 && nz == 0)
                                continue;
                            if (Terrain.ExtractContents(m_terrain.Terrain.GetCellValue(x + nx, y + 1, z + nz)) != 0)
                                score += 2;
                        }
                        if (!found || score > bestScore)
                        {
                            found = true;
                            bestScore = score;
                            floorCell = new Point3(x, y, z);
                            modelPosition = candidate;
                        }
                    }
                }
            }
            return found;
        }

        private bool IsBedTooClose(Vector3 candidate)
        {
            foreach (Entity entity in Project.Entities)
            {
                if (entity.FindComponent<ComponentLivingTownsBed>() == null)
                    continue;
                ComponentFrame frame = entity.FindComponent<ComponentFrame>();
                if (frame != null && Vector3.Distance(frame.Position, candidate) < 2.1f)
                    return true;
            }
            return false;
        }

        private Vector3 GetBedSleepPosition()
        {
            return new Vector3(m_bedFramePosition.X, m_bedFramePosition.Y - 1.28f, m_bedFramePosition.Z);
        }

        private bool IsNight()
        {
            if (m_timeOfDay == null || !m_timeOfDay.TimeOfDayEnabled)
                return false;
            return IntervalUtils.IsBetween(m_timeOfDay.TimeOfDay, m_timeOfDay.NightStart, m_timeOfDay.DawnStart);
        }

        private void TickSleepRoutine()
        {
            if (!EnsureAssignedBed())
                return;
            Vector3 target = GetBedSleepPosition();
            if (Vector3.Distance(m_body.Position, target) <= 1.15f)
            {
                m_pathfinding?.Stop();
                if (!m_sleepPhysicsSaved)
                {
                    m_wasGravityEnabled = m_body.IsGravityEnabled;
                    m_sleepPhysicsSaved = true;
                    m_body.IsGravityEnabled = false;
                }
                m_body.Velocity = Vector3.Zero;
                m_body.Position = target;
                SetSleepingAnimation(true);
            }
            else
            {
                SetSleepingAnimation(false);
                m_pathfinding?.SetDestination(target, 1.35f, 0.7f, 180, false, false, false, null);
            }
        }

        private void WakeFromBedIfNeeded()
        {
            if (m_sleepPhysicsSaved)
            {
                m_body.IsGravityEnabled = m_wasGravityEnabled;
                m_sleepPhysicsSaved = false;
                m_body.Velocity = Vector3.Zero;
            }
            SetSleepingAnimation(false);
        }

        private void SetSleepingAnimation(bool sleeping)
        {
            if (m_sleepAnimation == sleeping)
                return;
            m_sleepAnimation = sleeping;
            if (m_model?.AnimationController != null)
                m_model.AnimationController.Parameters.SetBool("IsSleeping", sleeping);
        }

        private void TickAutomaticDoors()
        {
            if (m_doorBehavior == null || m_terrain == null)
            {
                CloseDoorsFarBehind();
                return;
            }

            if (m_pathfinding?.Destination.HasValue == true)
            {
                Vector3 direction = m_pathfinding.Destination.Value - m_body.Position;
                direction.Y = 0f;
                if (direction.LengthSquared() > 0.04f)
                {
                    direction = Vector3.Normalize(direction);
                    int cx = Terrain.ToCell(m_body.Position.X);
                    int cy = Terrain.ToCell(m_body.Position.Y + 0.5f);
                    int cz = Terrain.ToCell(m_body.Position.Z);

                    for (int dy = -1; dy <= 1; dy++)
                    for (int i = 0; i < s_doorScanOffsets.Length; i++)
                    {
                        int x = cx + s_doorScanOffsets[i].X;
                        int y = cy + dy;
                        int z = cz + s_doorScanOffsets[i].Z;
                        int value = m_terrain.Terrain.GetCellValue(x, y, z);
                        int content = Terrain.ExtractContents(value);
                        if (content <= 0 || content >= BlocksManager.Blocks.Length || !(BlocksManager.Blocks[content] is DoorBlock))
                            continue;
                        if (!DoorBlock.IsBottomPart(m_terrain.Terrain, x, y, z))
                            continue;

                        Vector3 toDoor = new Vector3(x + 0.5f, m_body.Position.Y, z + 0.5f) - m_body.Position;
                        toDoor.Y = 0f;
                        float distance = toDoor.Length();
                        if (distance > 1.8f || distance < 0.01f || Vector3.Dot(toDoor, direction) <= 0.05f)
                            continue;

                        int currentValue = m_terrain.Terrain.GetCellValue(x, y, z);
                        if (!DoorBlock.GetOpen(Terrain.ExtractData(currentValue)) &&
                            !m_doorBehavior.IsDoorElectricallyConnected(x, y, z))
                        {
                            if (m_doorBehavior.OpenCloseDoor(x, y, z, true) && !ContainsDoor(m_autoOpenedDoors, x, y, z))
                                m_autoOpenedDoors.Add(new Point3(x, y, z));
                        }
                    }
                }
            }
            CloseDoorsFarBehind();
        }

        private void CloseDoorsFarBehind()
        {
            for (int i = m_autoOpenedDoors.Count - 1; i >= 0; i--)
            {
                Point3 p = m_autoOpenedDoors[i];
                Vector3 center = new Vector3(p.X + 0.5f, p.Y, p.Z + 0.5f);
                if (Vector3.Distance(m_body.Position, center) <= 2.2f || IsAnotherVillagerNearDoor(p))
                    continue;

                int value = m_terrain.Terrain.GetCellValue(p.X, p.Y, p.Z);
                int content = Terrain.ExtractContents(value);
                if (content > 0 && content < BlocksManager.Blocks.Length &&
                    BlocksManager.Blocks[content] is DoorBlock &&
                    DoorBlock.GetOpen(Terrain.ExtractData(value)))
                    m_doorBehavior?.OpenCloseDoor(p.X, p.Y, p.Z, false);
                m_autoOpenedDoors.RemoveAt(i);
            }
        }

        private bool IsAnotherVillagerNearDoor(Point3 door)
        {
            Vector3 center = new Vector3(door.X + 0.5f, door.Y, door.Z + 0.5f);
            foreach (ComponentCreature creature in m_creatureSpawn.Creatures)
            {
                if (creature.Entity == Entity || creature.Entity.FindComponent<ComponentLivingTownsVillager>() == null)
                    continue;
                ComponentBody body = creature.Entity.FindComponent<ComponentBody>();
                if (body != null && Vector3.Distance(body.Position, center) < 2.2f)
                    return true;
            }
            return false;
        }

        private static bool ContainsDoor(List<Point3> list, int x, int y, int z)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].X == x && list[i].Y == y && list[i].Z == z)
                    return true;
            return false;
        }

        private bool TryStartConversation()
        {
            if (m_isTalking || m_chatCooldown > 0.0 || m_creatureSpawn == null || IsNight() || s_random.NextDouble() > 0.18)
                return false;

            foreach (ComponentCreature creature in m_creatureSpawn.Creatures)
            {
                if (creature.Entity == Entity || creature.Entity.Id <= 0 || Entity.Id <= 0 || Entity.Id > creature.Entity.Id)
                    continue;
                ComponentLivingTownsVillager other = creature.Entity.FindComponent<ComponentLivingTownsVillager>();
                if (other == null || other.m_isTalking || other.m_chatCooldown > 0.0 || other.m_body == null)
                    continue;
                if (Vector3.Distance(m_body.Position, other.m_body.Position) > 3.6f)
                    continue;

                double duration = 4.5 + s_random.NextDouble() * 4.0;
                BeginConversation(other, duration);
                other.BeginConversation(this, duration);
                return true;
            }
            return false;
        }

        private void BeginConversation(ComponentLivingTownsVillager partner, double duration)
        {
            m_chatPartner = partner;
            m_isTalking = true;
            m_chatTimeRemaining = duration;
            m_pathfinding?.Stop();
            SetTalkingAnimation(true);
            FaceChatPartner();
        }

        private void EndConversation(bool applyCooldown)
        {
            if (!m_isTalking)
                return;
            m_isTalking = false;
            m_chatPartner = null;
            m_chatTimeRemaining = 0.0;
            SetTalkingAnimation(false);
            if (applyCooldown)
                m_chatCooldown = 8.0 + s_random.NextDouble() * 7.0;
        }

        private void FaceChatPartner()
        {
            if (m_chatPartner?.m_body == null)
                return;
            Vector3 direction = m_chatPartner.m_body.Position - m_body.Position;
            direction.Y = 0f;
            if (direction.LengthSquared() < 0.01f)
                return;
            float yaw = MathF.Atan2(direction.X, direction.Z);
            m_body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        }

        private void SetTalkingAnimation(bool talking)
        {
            if (m_model?.AnimationController != null)
                m_model.AnimationController.Parameters.SetBool("IsTalking", talking);
        }

        private void EnsureFarmerKit()
        {
            if (m_inventory == null || m_farmerKitGranted)
                return;
            int rake = BlocksManager.GetBlockIndex<IronRakeBlock>();
            if (rake > 0 && FindItemSlot(rake, null) < 0)
                AddItemToInventory(Terrain.MakeBlockValue(rake), 1);

            int seedContent = BlocksManager.GetBlockIndex<SeedsBlock>();
            int seedSlot = FindItemSlot(seedContent, 5);
            if (seedContent > 0 && seedSlot < 0)
                AddItemToInventory(Terrain.MakeBlockValue(seedContent, 0, 5), 32);
            m_farmerKitGranted = true;
        }

        private void AddItemToInventory(int value, int count)
        {
            int remaining = ComponentInventoryBase.AcquireItems(m_inventory, value, count);
            if (remaining > 0 && m_pickables != null)
                m_pickables.AddPickable(value, remaining, m_body.Position + new Vector3(0f, 0.5f, 0f), null, null);
        }

        private void TickFarmer()
        {
            if (m_inventory == null || m_terrain == null || m_pathfinding == null)
                return;
            int rakeSlot = FindItemSlot(BlocksManager.GetBlockIndex<IronRakeBlock>(), null);
            int seedsSlot = FindItemSlot(BlocksManager.GetBlockIndex<SeedsBlock>(), 5);
            if (rakeSlot < 0 || seedsSlot < 0)
            {
                m_pathfinding.Stop();
                return;
            }
            if (!TryFindFarmTask(out Point3 target, out int task))
            {
                m_pathfinding.Stop();
                return;
            }

            Vector3 targetPosition = task == 4
                ? new Vector3(target.X + 1.5f, target.Y, target.Z + 0.5f)
                : new Vector3(target.X + 0.5f, target.Y + 1f, target.Z + 0.5f);
            if (Vector3.Distance(m_body.Position, targetPosition) > 1.45f)
            {
                m_pathfinding.SetDestination(targetPosition, WorkMoveSpeed, 1.2f, 220, false, false, false, null);
                return;
            }

            m_pathfinding.Stop();
            if (task == 1)
            {
                int old = m_terrain.Terrain.GetCellValue(target.X, target.Y, target.Z);
                m_terrain.ChangeCell(target.X, target.Y, target.Z, Terrain.ReplaceContents(old, 168));
                DamageRake(rakeSlot);
            }
            else if (task == 2)
            {
                int old = m_terrain.Terrain.GetCellValue(target.X, target.Y, target.Z);
                m_terrain.ChangeCell(target.X, target.Y, target.Z, Terrain.ReplaceContents(old, 2));
                DamageRake(rakeSlot);
            }
            else if (task == 3)
            {
                int rye = BlocksManager.GetBlockIndex<RyeBlock>();
                int cropData = RyeBlock.SetSize(RyeBlock.SetIsWild(0, false), 0);
                m_terrain.ChangeCell(target.X, target.Y + 1, target.Z, Terrain.MakeBlockValue(rye, 0, cropData));
                m_inventory.RemoveSlotItems(seedsSlot, 1);
            }
            else if (task == 4)
                HarvestCrop(target.X, target.Y, target.Z);
        }

        private bool TryFindFarmTask(out Point3 target, out int task)
        {
            target = new Point3(0, 0, 0);
            task = 0;
            int centerX = m_hasHomeDoor ? m_homeDoor.X : Terrain.ToCell(m_body.Position.X);
            int centerZ = m_hasHomeDoor ? m_homeDoor.Z : Terrain.ToCell(m_body.Position.Z);
            int rye = BlocksManager.GetBlockIndex<RyeBlock>();

            for (int scanned = 0; scanned < FarmCellsPerScan; scanned++)
            {
                if (m_farmSearchRadius > FarmerWorkRadius)
                {
                    m_farmSearchRadius = 0;
                    m_farmSearchDx = 0;
                    m_farmSearchDz = 0;
                    return false;
                }

                int radius = m_farmSearchRadius;
                int dx = m_farmSearchDx;
                int dz = m_farmSearchDz;
                AdvanceFarmSearchPosition();
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius)
                    continue;

                int x = centerX + dx;
                int z = centerZ + dz;
                int y = m_terrain.Terrain.GetTopHeight(x, z);
                if (y < 1 || y > 253)
                    continue;

                int groundValue = m_terrain.Terrain.GetCellValue(x, y, z);
                int ground = Terrain.ExtractContents(groundValue);
                int aboveValue = m_terrain.Terrain.GetCellValue(x, y + 1, z);
                int above = Terrain.ExtractContents(aboveValue);

                if (ground == rye && RyeBlock.GetSize(Terrain.ExtractData(groundValue)) >= 7 &&
                    Terrain.ExtractContents(m_terrain.Terrain.GetCellValue(x, y - 1, z)) == 168)
                {
                    target = new Point3(x, y, z); task = 4; return true;
                }
                if (ground == 168 && above == 0)
                {
                    target = new Point3(x, y, z); task = 3; return true;
                }
                if (above == 0 && ground == 2)
                {
                    target = new Point3(x, y, z); task = 1; return true;
                }
                if (above == 0 && ground == 8)
                {
                    target = new Point3(x, y, z); task = 2; return true;
                }
            }
            return false;
        }

        private void AdvanceFarmSearchPosition()
        {
            int radius = m_farmSearchRadius;
            m_farmSearchDz++;
            if (m_farmSearchDz > radius)
            {
                m_farmSearchDz = -radius;
                m_farmSearchDx++;
            }
            if (m_farmSearchDx > radius)
            {
                m_farmSearchRadius++;
                m_farmSearchDx = -m_farmSearchRadius;
                m_farmSearchDz = -m_farmSearchRadius;
            }
        }

        private void TickLumberjack()
        {
            if (m_terrain == null || m_pathfinding == null)
                return;
            if (!m_hasWorkTarget || m_nextWorkSearchTime <= 0.0 ||
                Terrain.ExtractContents(m_terrain.Terrain.GetCellValue(m_workTarget.X, m_workTarget.Y, m_workTarget.Z)) == 0)
            {
                if (m_nextWorkSearchTime > 0.0)
                    m_nextWorkSearchTime -= 0.35;
                if (m_nextWorkSearchTime <= 0.0)
                {
                    m_nextWorkSearchTime = 3.0;
                    m_hasWorkTarget = TryFindWorkBlock(false, out m_workTarget);
                }
            }

            if (!m_hasWorkTarget)
            {
                TickPatrol(4f, 8f);
                return;
            }

            Vector3 target = new Vector3(m_workTarget.X + 1.35f, m_workTarget.Y, m_workTarget.Z + 0.5f);
            if (Vector3.Distance(m_body.Position, target) > 1.6f)
                m_pathfinding.SetDestination(target, WorkMoveSpeed, 1.2f, 220, false, false, false, null);
            else
            {
                m_pathfinding.Stop();
                HarvestBlock(m_workTarget.X, m_workTarget.Y, m_workTarget.Z, 5);
                m_hasWorkTarget = false;
                m_nextWorkSearchTime = 0.0;
            }
        }

        private void TickMiner()
        {
            if (m_terrain == null || m_pathfinding == null)
                return;
            if (!m_hasWorkTarget || m_nextWorkSearchTime <= 0.0 ||
                IsAir(m_terrain.Terrain.GetCellValue(m_workTarget.X, m_workTarget.Y, m_workTarget.Z)))
            {
                if (m_nextWorkSearchTime > 0.0)
                    m_nextWorkSearchTime -= 0.35;
                if (m_nextWorkSearchTime <= 0.0)
                {
                    m_nextWorkSearchTime = 3.5;
                    m_hasWorkTarget = TryFindWorkBlock(true, out m_workTarget);
                }
            }

            if (!m_hasWorkTarget)
            {
                TickPatrol(3f, 6f);
                return;
            }

            Vector3 target = new Vector3(m_workTarget.X + 1.4f, m_workTarget.Y + 0.25f, m_workTarget.Z + 0.5f);
            if (Vector3.Distance(m_body.Position, target) > 1.6f)
                m_pathfinding.SetDestination(target, WorkMoveSpeed, 1.2f, 240, false, true, false, null);
            else
            {
                m_pathfinding.Stop();
                HarvestBlock(m_workTarget.X, m_workTarget.Y, m_workTarget.Z, 5);
                m_hasWorkTarget = false;
                m_nextWorkSearchTime = 0.0;
            }
        }

        private bool TryFindWorkBlock(bool ore, out Point3 target)
        {
            target = new Point3(0, 0, 0);
            int cx = m_hasHomeDoor ? m_homeDoor.X : Terrain.ToCell(m_body.Position.X);
            int cy = m_hasHomeDoor ? m_homeDoor.Y : Terrain.ToCell(m_body.Position.Y);
            int cz = m_hasHomeDoor ? m_homeDoor.Z : Terrain.ToCell(m_body.Position.Z);
            int bestDistance = int.MaxValue;
            for (int x = cx - 12; x <= cx + 12; x++)
            for (int z = cz - 12; z <= cz + 12; z++)
            for (int y = Math.Max(1, cy - (ore ? 20 : 1)); y <= Math.Min(250, cy + (ore ? 5 : 14)); y++)
            {
                int value = m_terrain.Terrain.GetCellValue(x, y, z);
                int content = Terrain.ExtractContents(value);
                if (content == 0 || content >= BlocksManager.Blocks.Length)
                    continue;
                Block block = BlocksManager.Blocks[content];
                bool matches = ore
                    ? block.GetType().Name.IndexOf("Ore", StringComparison.OrdinalIgnoreCase) >= 0
                    : block is WoodBlock;
                if (!matches)
                    continue;
                int distance = Math.Abs(x - cx) + Math.Abs(z - cz) + Math.Abs(y - cy);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    target = new Point3(x, y, z);
                }
            }
            return bestDistance != int.MaxValue;
        }

        private void HarvestBlock(int x, int y, int z, int toolLevel)
        {
            int value = m_terrain.Terrain.GetCellValue(x, y, z);
            int content = Terrain.ExtractContents(value);
            if (content == 0 || content >= BlocksManager.Blocks.Length)
                return;

            Block block = BlocksManager.Blocks[content];
            List<BlockDropValue> drops = new List<BlockDropValue>();
            block.GetDropValues(m_terrain, value, 0, toolLevel, drops, out bool showDebris);
            Vector3 dropPosition = new Vector3(x + 0.5f, y + 0.35f, z + 0.5f);
            foreach (BlockDropValue drop in drops)
            {
                if (drop.Count <= 0 || Terrain.ExtractContents(drop.Value) == 0)
                    continue;
                int remainder = m_inventory == null ? drop.Count : ComponentInventoryBase.AcquireItems(m_inventory, drop.Value, drop.Count);
                if (remainder > 0)
                    m_pickables?.AddPickable(drop.Value, remainder, dropPosition, null, null);
            }
            m_terrain.DestroyCell(toolLevel, x, y, z, 0, true, false);
        }

        private void TickPatrol(float minRadius, float maxRadius)
        {
            if (m_pathfinding == null || m_terrain == null)
                return;
            if (m_patrolPause > 0.0)
            {
                m_patrolPause -= 0.35;
                return;
            }
            if (m_pathfinding.Destination.HasValue && Vector3.Distance(m_body.Position, m_pathfinding.Destination.Value) > 1.25f)
                return;

            int cx = m_hasHomeDoor ? m_homeDoor.X : Terrain.ToCell(m_body.Position.X);
            int cz = m_hasHomeDoor ? m_homeDoor.Z : Terrain.ToCell(m_body.Position.Z);
            float angle = (float)(s_random.NextDouble() * Math.PI * 2.0);
            float radius = minRadius + (float)s_random.NextDouble() * (maxRadius - minRadius);
            int x = cx + Terrain.ToCell(MathF.Cos(angle) * radius);
            int z = cz + Terrain.ToCell(MathF.Sin(angle) * radius);
            int y = m_terrain.Terrain.GetTopHeight(x, z);
            Vector3 destination = new Vector3(x + 0.5f, y + 1f, z + 0.5f);
            m_pathfinding.SetDestination(destination, WorkMoveSpeed, 1.1f, 180, false, false, false, null);
            m_patrolPause = 0.5 + s_random.NextDouble() * 1.0;
        }

        private void TickBlacksmith()
        {
            if (m_terrain == null || m_pathfinding == null)
                return;
            if (!m_hasWorkTarget || m_nextWorkSearchTime <= 0.0 ||
                Terrain.ExtractContents(m_terrain.Terrain.GetCellValue(m_workTarget.X, m_workTarget.Y, m_workTarget.Z)) == 0)
            {
                if (m_nextWorkSearchTime > 0.0)
                    m_nextWorkSearchTime -= 0.35;
                if (m_nextWorkSearchTime <= 0.0)
                {
                    m_nextWorkSearchTime = 5.0;
                    m_hasWorkTarget = TryFindNamedBlock("Furnace", out m_workTarget);
                }
            }
            if (!m_hasWorkTarget)
            {
                TickPatrol(2f, 5f);
                return;
            }
            Vector3 target = new Vector3(m_workTarget.X + 1.5f, m_workTarget.Y, m_workTarget.Z + 0.5f);
            if (Vector3.Distance(m_body.Position, target) > 1.6f)
                m_pathfinding.SetDestination(target, WorkMoveSpeed, 1.2f, 180, false, false, false, null);
            else
                m_pathfinding.Stop();
        }

        private bool TryFindNamedBlock(string namePart, out Point3 target)
        {
            target = new Point3(0, 0, 0);
            int cx = m_hasHomeDoor ? m_homeDoor.X : Terrain.ToCell(m_body.Position.X);
            int cy = m_hasHomeDoor ? m_homeDoor.Y : Terrain.ToCell(m_body.Position.Y);
            int cz = m_hasHomeDoor ? m_homeDoor.Z : Terrain.ToCell(m_body.Position.Z);
            int bestDistance = int.MaxValue;
            for (int x = cx - 12; x <= cx + 12; x++)
            for (int z = cz - 12; z <= cz + 12; z++)
            for (int y = Math.Max(1, cy - 3); y <= Math.Min(250, cy + 4); y++)
            {
                int value = m_terrain.Terrain.GetCellValue(x, y, z);
                int content = Terrain.ExtractContents(value);
                if (content <= 0 || content >= BlocksManager.Blocks.Length)
                    continue;
                if (BlocksManager.Blocks[content].GetType().Name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                int distance = Math.Abs(x - cx) + Math.Abs(y - cy) + Math.Abs(z - cz);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    target = new Point3(x, y, z);
                }
            }
            return bestDistance != int.MaxValue;
        }

        private void TickMerchant()
        {
            if (m_creatureSpawn == null || m_pathfinding == null)
            {
                TickPatrol(3f, 7f);
                return;
            }
            ComponentLivingTownsVillager nearest = null;
            float best = 12f;
            foreach (ComponentCreature creature in m_creatureSpawn.Creatures)
            {
                if (creature.Entity == Entity)
                    continue;
                ComponentLivingTownsVillager other = creature.Entity.FindComponent<ComponentLivingTownsVillager>();
                if (other == null || other.m_body == null)
                    continue;
                float distance = Vector3.Distance(m_body.Position, other.m_body.Position);
                if (distance < best)
                {
                    best = distance;
                    nearest = other;
                }
            }
            if (nearest == null)
            {
                TickPatrol(3f, 7f);
                return;
            }
            if (best > 2.8f)
            {
                Vector3 direction = m_body.Position - nearest.m_body.Position;
                direction.Y = 0f;
                if (direction.LengthSquared() < 0.01f)
                    direction = Vector3.UnitX;
                direction = Vector3.Normalize(direction);
                m_pathfinding.SetDestination(nearest.m_body.Position + direction * 1.8f, WorkMoveSpeed, 1.2f, 180, false, false, false, null);
            }
        }

        private int FindItemSlot(int content, int? data)
        {
            if (content <= 0 || m_inventory == null)
                return -1;
            for (int i = 0; i < m_inventory.SlotsCount; i++)
            {
                int value = m_inventory.GetSlotValue(i);
                if (m_inventory.GetSlotCount(i) > 0 && Terrain.ExtractContents(value) == content &&
                    (!data.HasValue || Terrain.ExtractData(value) == data.Value))
                    return i;
            }
            return -1;
        }

        private void DamageRake(int slot)
        {
            int value = m_inventory.GetSlotValue(slot);
            int content = Terrain.ExtractContents(value);
            Block block = BlocksManager.Blocks[content];
            int durability = block.GetDurability(value);
            int damage = block.GetDamage(value) + 1;
            m_inventory.RemoveSlotItems(slot, 1);
            if (durability <= 0 || damage < durability)
                m_inventory.AddSlotItems(slot, block.SetDamage(value, damage), 1);
        }

        private void HarvestCrop(int x, int y, int z)
        {
            int cropValue = m_terrain.Terrain.GetCellValue(x, y, z);
            int content = Terrain.ExtractContents(cropValue);
            if (content <= 0 || content >= BlocksManager.Blocks.Length)
                return;
            Block cropBlock = BlocksManager.Blocks[content];
            List<BlockDropValue> drops = new List<BlockDropValue>();
            cropBlock.GetDropValues(m_terrain, cropValue, 0, 0, drops, out bool showDebris);
            Vector3 dropPosition = new Vector3(x + 0.5f, y + 0.25f, z + 0.5f);
            foreach (BlockDropValue drop in drops)
            {
                if (drop.Count <= 0 || Terrain.ExtractContents(drop.Value) == 0)
                    continue;
                int remainder = ComponentInventoryBase.AcquireItems(m_inventory, drop.Value, drop.Count);
                if (remainder > 0)
                    m_pickables?.AddPickable(drop.Value, remainder, dropPosition, null, null);
            }
            m_terrain.ChangeCell(x, y, z, 0);
        }

        private static bool IsAir(int value) => Terrain.ExtractContents(value) == 0;
    }
}
