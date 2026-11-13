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
        private ComponentMiner m_miner;
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
        private Vector3 m_workApproachPosition;
        private bool m_hasWorkApproach;
        private double m_nextTradeTime;
        private bool m_farmerKitGranted;
        private int m_farmSearchRadius;
        private int m_farmSearchDx;
        private int m_farmSearchDz;
        private Vector3 m_managedDestination;
        private bool m_hasManagedDestination;
        private double m_destinationRetryCooldown;
        private Vector3 m_managedProgressPosition;
        private Vector3 m_managedProgressTarget;
        private double m_managedNoProgressTime;
        private bool m_hasManagedProgressSample;
        private bool m_wasNight;

        private const float VillagerWalkSpeed = 1.85f;
        private const float WorkMoveSpeed = 1.55f;
        private const int FarmerWorkRadius = 60;
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
            m_miner = Entity.FindComponent<ComponentMiner>();
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
                m_locomotion.WalkSpeed = VillagerWalkSpeed;
            if (m_body != null)
                m_body.IsGravityEnabled = true;

            Log.Information("[LivingTowns] Villager controller loaded; speed=" + VillagerWalkSpeed + ", miner=" + (m_miner != null) + ", job=" + m_job);
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

            m_destinationRetryCooldown -= dt;
            if (m_destinationRetryCooldown < 0.0)
                m_destinationRetryCooldown = 0.0;
            UpdateManagedPathProgress(dt);

            m_nextDoorCheckTime -= dt;
            if (m_nextDoorCheckTime <= 0.0)
            {
                m_nextDoorCheckTime = 0.10;
                TickAutomaticDoors();
            }

            bool night = IsNight();
            if (night)
            {
                if (!m_wasNight)
                {
                    m_wasNight = true;
                    EndConversation(false);
                    m_hasWorkTarget = false;
                    m_hasWorkApproach = false;
                    m_nextWorkSearchTime = 0.0;
                    StopManagedPath();
                    Log.Information("[LivingTowns] Night routine started; work movement stopped for villager.");
                }
                TickSleepRoutine();
                return;
            }

            if (m_wasNight)
            {
                m_wasNight = false;
                StopManagedPath();
                Log.Information("[LivingTowns] Dawn routine started; villager leaving bed.");
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
            try
            {
                switch ((choice ?? string.Empty).Trim())
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
            catch (Exception ex)
            {
                Log.Error("[LivingTowns] Villager job menu action failed safely: " + ex);
                player?.ComponentGui?.DisplaySmallMessage("Meslek atanırken hata oluştu; ayrıntı günlüğe yazıldı.", Color.White, true, false);
            }
        }

        private void SetJob(LivingTownsJob job, ComponentPlayer player)
        {
            m_job = job;
            m_hasWorkTarget = false;
            m_hasWorkApproach = false;
            m_nextWorkSearchTime = 0.0;
            m_patrolPause = 0.0;
            EndConversation(false);
            m_chatCooldown = 12.0;
            m_nextThinkTime = 0.0;
            m_hasManagedDestination = false;
            m_destinationRetryCooldown = 0.0;
            StopManagedPath();

            if (job == LivingTownsJob.Farmer)
            {
                try
                {
                    EnsureFarmerKit();
                }
                catch (Exception ex)
                {
                    Log.Error("[LivingTowns] Farmer job assigned, but starter kit failed safely: " + ex);
                }
            }

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

        private void TickJob()
        {
            switch (m_job)
            {
                case LivingTownsJob.Farmer: TickFarmer(); break;
                case LivingTownsJob.Lumberjack: TickLumberjack(); break;
                case LivingTownsJob.Guard: TickPatrol(7f, 12f); break;
                case LivingTownsJob.Miner: TickMiner(); break;
                case LivingTownsJob.Blacksmith: TickBlacksmith(); break;
                case LivingTownsJob.Merchant: TickMerchant(); break;
                default: TickPatrol(2.5f, 5.0f); break;
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
            // ComponentHumanModel.DrawExtras reads the active slot through ComponentMiner.
            // Selecting the received slot makes the block/item render with the game's normal hand pose.
            m_inventory.ActiveSlotIndex = targetSlot;
            m_miner?.Poke(true);
            player.ComponentGui?.DisplaySmallMessage("NPC'ye " + amount + " adet eşya verildi; eline aldı.", Color.White, true, false);
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
                Point3 existingFloor = new Point3(
                    Terrain.ToCell(frame.Position.X),
                    Terrain.ToCell(frame.Position.Y - 1.736f),
                    Terrain.ToCell(frame.Position.Z));
                if (IsIndoorBedSpot(existingFloor.X, existingFloor.Y, existingFloor.Z, out _))
                {
                    m_bedFramePosition = frame.Position;
                    m_bedCell = existingFloor;
                    m_hasBedCell = true;
                    return true;
                }

                // Older builds could assign a bed outside the house. Release that
                // assignment so this villager can receive an indoor bed instead.
                bed.SetOwnerDoor(new Point3(int.MinValue, int.MinValue, int.MinValue));
                Log.Information("[LivingTowns] Replacing an outdoor bed assignment with an indoor bed.");

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
                        if (IsBedTooClose(candidate) || !IsIndoorBedSpot(x, y, z, out int wallDirections))
                            continue;
                        int score = 220 - (Math.Abs(dx) + Math.Abs(dz)) * 3 - Math.Abs(yOffset) * 4;
                        score += wallDirections * 18;
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

        private bool IsIndoorBedSpot(int x, int y, int z, out int wallDirections)
        {
            wallDirections = 0;
            if (m_terrain == null || y < 1 || y > 248)
                return false;

            bool hasRoof = false;
            for (int roofY = y + 3; roofY <= y + 5; roofY++)
            {
                int roofValue = m_terrain.Terrain.GetCellValue(x, roofY, z);
                int roofContent = Terrain.ExtractContents(roofValue);
                if (roofContent > 0 && roofContent < BlocksManager.Blocks.Length
                    && BlocksManager.Blocks[roofContent].IsCollidable_(roofValue))
                {
                    hasRoof = true;
                    break;
                }
            }
            if (!hasRoof)
                return false;

            Point3[] directions =
            {
                new Point3(1, 0, 0), new Point3(-1, 0, 0),
                new Point3(0, 0, 1), new Point3(0, 0, -1)
            };
            for (int i = 0; i < directions.Length; i++)
            {
                bool hasWall = false;
                for (int distance = 1; distance <= 4 && !hasWall; distance++)
                {
                    int wx = x + directions[i].X * distance;
                    int wz = z + directions[i].Z * distance;
                    for (int wy = y + 1; wy <= y + 2; wy++)
                    {
                        int wallValue = m_terrain.Terrain.GetCellValue(wx, wy, wz);
                        int wallContent = Terrain.ExtractContents(wallValue);
                        if (wallContent > 0 && wallContent < BlocksManager.Blocks.Length
                            && BlocksManager.Blocks[wallContent].IsCollidable_(wallValue))
                        {
                            hasWall = true;
                            break;
                        }
                    }
                }
                if (hasWall)
                    wallDirections++;
            }
            return wallDirections >= 1;
        }

        private bool IsBedTooClose(Vector3 candidate)
        {
            foreach (Entity entity in Project.Entities)
            {
                ComponentLivingTownsBed bed = entity.FindComponent<ComponentLivingTownsBed>();
                if (bed == null || bed.IsUnassigned())
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
            {
                StopManagedPath();
                SetSleepingAnimation(false);
                return;
            }
            Vector3 target = GetBedSleepPosition();
            if (Vector3.Distance(m_body.Position, target) <= 1.15f)
            {
                StopManagedPath();
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
                SetManagedDestination(target, 1.35f, 0.7f, 700, false, false, false, null);
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
            if (m_doorBehavior == null || m_terrain == null || m_body == null)
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

                    // Scan a wider corridor in front of the villager. Open only doors
                    // that lie close to the current travel direction, not doors behind it.
                    for (int dx = -3; dx <= 3; dx++)
                    for (int dz = -3; dz <= 3; dz++)
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        int x = cx + dx;
                        int y = cy + dy;
                        int z = cz + dz;
                        int value = m_terrain.Terrain.GetCellValue(x, y, z);
                        int content = Terrain.ExtractContents(value);
                        if (content <= 0 || content >= BlocksManager.Blocks.Length
                            || !(BlocksManager.Blocks[content] is DoorBlock)
                            || !DoorBlock.IsBottomPart(m_terrain.Terrain, x, y, z))
                            continue;

                        Vector3 toDoor = new Vector3(x + 0.5f - m_body.Position.X, 0f,
                            z + 0.5f - m_body.Position.Z);
                        float distance = toDoor.Length();
                        if (distance < 0.05f || distance > 3.8f)
                            continue;

                        float forwardDistance = Vector3.Dot(toDoor, direction);
                        float lateralDistance = Math.Abs(toDoor.X * direction.Z - toDoor.Z * direction.X);
                        if (forwardDistance < 0.1f || forwardDistance > 3.6f || lateralDistance > 1.15f
                            || Math.Abs((y + 0.5f) - m_body.Position.Y) > 2.0f)
                            continue;

                        int currentValue = m_terrain.Terrain.GetCellValue(x, y, z);
                        if (!DoorBlock.GetOpen(Terrain.ExtractData(currentValue))
                            && !m_doorBehavior.IsDoorElectricallyConnected(x, y, z))
                        {
                            if (m_doorBehavior.OpenCloseDoor(x, y, z, true)
                                && !ContainsDoor(m_autoOpenedDoors, x, y, z))
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
            if (m_creatureSpawn == null)
                return false;
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
            if (m_job != LivingTownsJob.Unemployed || m_isTalking || m_chatCooldown > 0.0 || m_creatureSpawn == null || IsNight() || s_random.NextDouble() > 0.18)
                return false;

            foreach (ComponentCreature creature in m_creatureSpawn.Creatures)
            {
                if (creature.Entity == Entity || creature.Entity.Id <= 0 || Entity.Id <= 0 || Entity.Id > creature.Entity.Id)
                    continue;
                ComponentLivingTownsVillager other = creature.Entity.FindComponent<ComponentLivingTownsVillager>();
                if (other == null || other.m_job != LivingTownsJob.Unemployed || other.m_isTalking || other.m_chatCooldown > 0.0 || other.m_body == null)
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
            StopManagedPath();
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

            try
            {
                int rake = BlocksManager.GetBlockIndex<IronRakeBlock>();
                if (rake > 0 && FindItemSlot(rake, null) < 0)
                    AddItemToInventory(Terrain.MakeBlockValue(rake), 1);

                int seedContent = BlocksManager.GetBlockIndex<SeedsBlock>();
                int seedSlot = FindItemSlot(seedContent, 5);
                if (seedContent > 0 && seedSlot < 0)
                    AddItemToInventory(Terrain.MakeBlockValue(seedContent, 0, 5), 32);

                // Do not change ActiveSlotIndex from the menu callback. The inventory
                // can contain non-visible slots; TickFarmer selects a checked visible slot.
                m_farmerKitGranted = true;
            }
            catch (Exception ex)
            {
                m_farmerKitGranted = false;
                Log.Error("[LivingTowns] Could not grant farmer starter kit: " + ex);
            }
        }

        private void AddItemToInventory(int value, int count)
        {
            int remaining = ComponentInventoryBase.AcquireItems(m_inventory, value, count);
            if (remaining > 0 && m_pickables != null)
                m_pickables.AddPickable(value, remaining, m_body.Position + new Vector3(0f, 0.5f, 0f), null, null);
        }

        private void SelectInventorySlot(int slot)
        {
            if (m_inventory == null || slot < 0 || slot >= m_inventory.VisibleSlotsCount || m_inventory.GetSlotCount(slot) <= 0)
                return;
            m_inventory.ActiveSlotIndex = slot;
        }

        private int SelectBestToolForBlock(int blockValue)
        {
            if (m_inventory == null)
                return 0;

            int bestValue = m_miner != null ? m_miner.FindBestInventoryToolForDigging(blockValue) : 0;
            int bestSlot = -1;
            if (bestValue != 0)
            {
                for (int i = 0; i < m_inventory.VisibleSlotsCount; i++)
                    if (m_inventory.GetSlotCount(i) > 0 && m_inventory.GetSlotValue(i) == bestValue)
                    {
                        bestSlot = i;
                        break;
                    }
            }

            // Fallback for custom tools/blocks whose dig-time calculation is not finite.
            if (bestSlot < 0)
            {
                Block target = BlocksManager.Blocks[Terrain.ExtractContents(blockValue)];
                BlockDigMethod method = target.GetBlockDigMethod(blockValue);
                float bestPower = 0f;
                for (int i = 0; i < m_inventory.VisibleSlotsCount; i++)
                {
                    int candidateValue = m_inventory.GetSlotValue(i);
                    if (m_inventory.GetSlotCount(i) <= 0 || candidateValue == 0)
                        continue;
                    Block tool = BlocksManager.Blocks[Terrain.ExtractContents(candidateValue)];
                    float power = method == BlockDigMethod.Hack ? tool.GetHackPower(candidateValue)
                        : method == BlockDigMethod.Quarry ? tool.GetQuarryPower(candidateValue)
                        : method == BlockDigMethod.Shovel ? tool.GetShovelPower(candidateValue) : 0f;
                    if (power > bestPower)
                    {
                        bestPower = power;
                        bestSlot = i;
                    }
                }
            }

            if (bestSlot >= 0)
            {
                SelectInventorySlot(bestSlot);
                return m_inventory.GetSlotValue(bestSlot);
            }
            return 0;
        }

        private void UpdateManagedPathProgress(float dt)
        {
            if (m_pathfinding?.Destination.HasValue != true || m_body == null)
            {
                m_managedNoProgressTime = 0.0;
                m_hasManagedProgressSample = false;
                return;
            }

            Vector3 target = m_pathfinding.Destination.Value;
            if (!m_hasManagedProgressSample
                || Vector3.DistanceSquared(target, m_managedProgressTarget) > 0.25f)
            {
                m_managedProgressTarget = target;
                m_managedProgressPosition = m_body.Position;
                m_managedNoProgressTime = 0.0;
                m_hasManagedProgressSample = true;
                return;
            }

            if (Vector3.DistanceSquared(m_body.Position, m_managedProgressPosition) > 0.09f)
            {
                m_managedProgressPosition = m_body.Position;
                m_managedNoProgressTime = 0.0;
            }
            else if (Vector3.DistanceSquared(m_body.Position, target) > 2.25f)
            {
                m_managedNoProgressTime += dt;
            }
            else
            {
                m_managedNoProgressTime = 0.0;
            }
        }

        private void SetManagedDestination(Vector3 destination, float speed, float range, int maxPathfindingPositions,
            bool useRandomMovements, bool ignoreHeightDifference, bool raycastDestination, ComponentBody doNotAvoidBody)
        {
            if (m_pathfinding == null)
                return;

            Vector3? current = m_pathfinding.Destination;
            bool sameRequestedTarget = m_hasManagedDestination
                && Vector3.DistanceSquared(m_managedDestination, destination) <= 0.0625f;
            bool sameCurrentTarget = current.HasValue
                && Vector3.DistanceSquared(current.Value, destination) <= 0.0625f;
            bool stuck = m_pathfinding.IsStuck;
            bool stalled = sameRequestedTarget && m_managedNoProgressTime >= 7.0;

            // Important: a stuck pathfinder keeps Destination set. Never treat that as
            // proof that the route is still working; explicitly restart the route search.
            if (sameCurrentTarget && !stuck && !stalled)
                return;
            if (!current.HasValue && sameRequestedTarget && m_destinationRetryCooldown > 0.0
                && !stuck && !stalled)
                return;

            if (stuck || stalled)
                m_pathfinding.Stop();

            bool allowRandomRecovery = useRandomMovements || stuck || stalled;
            int pathBudget = Math.Max(maxPathfindingPositions, 700);
            m_pathfinding.SetDestination(destination, speed, range, pathBudget,
                allowRandomRecovery, ignoreHeightDifference, raycastDestination, doNotAvoidBody);
            m_managedDestination = destination;
            m_hasManagedDestination = true;
            m_destinationRetryCooldown = 3.0;
            m_managedNoProgressTime = 0.0;
            m_managedProgressTarget = destination;
            m_managedProgressPosition = m_body.Position;
            m_hasManagedProgressSample = true;
        }

        private void StopManagedPath()
        {
            if (m_pathfinding != null && m_pathfinding.Destination.HasValue)
                m_pathfinding.Stop();
            m_hasManagedDestination = false;
            m_destinationRetryCooldown = 0.0;
            m_managedNoProgressTime = 0.0;
            m_hasManagedProgressSample = false;
        }

        private bool TryGetWorkApproach(int x, int y, int z, out Vector3 approach)
        {
            approach = Vector3.Zero;
            float bestDistance = float.MaxValue;
            bool found = false;
            Point3[] sides = { new Point3(1, 0, 0), new Point3(-1, 0, 0), new Point3(0, 0, 1), new Point3(0, 0, -1) };

            for (int i = 0; i < sides.Length; i++)
            {
                int sx = x + sides[i].X;
                int sz = z + sides[i].Z;
                // Tree trunks and ores can be several blocks above the walkable floor.
                // Search the adjacent column downward instead of requiring a floor at target Y.
                for (int standY = Math.Max(1, y - 4); standY <= Math.Min(250, y + 1); standY++)
                {
                    int feet = m_terrain.Terrain.GetCellValue(sx, standY, sz);
                    int head = m_terrain.Terrain.GetCellValue(sx, standY + 1, sz);
                    int floor = m_terrain.Terrain.GetCellValue(sx, standY - 1, sz);
                    int floorContent = Terrain.ExtractContents(floor);
                    if (!IsAir(feet) || !IsAir(head) || floorContent <= 0 || floorContent >= BlocksManager.Blocks.Length
                        || !BlocksManager.Blocks[floorContent].IsCollidable_(floor))
                        continue;

                    Vector3 candidate = new Vector3(sx + 0.5f, standY, sz + 0.5f);
                    float distance = Vector3.DistanceSquared(m_body.Position, candidate);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        approach = candidate;
                        found = true;
                    }
                }
            }
            return found;
        }

        private void TickFarmer()
        {
            if (m_inventory == null || m_terrain == null || m_pathfinding == null)
                return;
            int rakeSlot = FindItemSlot(BlocksManager.GetBlockIndex<IronRakeBlock>(), null);
            int seedsSlot = FindItemSlot(BlocksManager.GetBlockIndex<SeedsBlock>(), 5);
            if (rakeSlot < 0 || seedsSlot < 0)
            {
                TickPatrol(2.5f, 4.0f);
                return;
            }
            if (!TryFindFarmTask(out Point3 target, out int task))
            {
                TickPatrol(2.5f, 4.0f);
                return;
            }

            Vector3 targetPosition;
            int approachY = task == 4 ? target.Y : target.Y + 1;
            if (!TryGetWorkApproach(target.X, approachY, target.Z, out targetPosition))
            {
                StopManagedPath();
                return;
            }
            if (Vector3.Distance(m_body.Position, targetPosition) > 1.45f)
            {
                SetManagedDestination(targetPosition, WorkMoveSpeed, 1.2f, 220, false, false, false, null);
                return;
            }

            StopManagedPath();
            SelectInventorySlot(task == 3 ? seedsSlot : rakeSlot);
            m_miner?.Poke(true);
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
            if (!m_hasWorkTarget || !m_hasWorkApproach ||
                IsAir(m_terrain.Terrain.GetCellValue(m_workTarget.X, m_workTarget.Y, m_workTarget.Z)))
            {
                m_hasWorkTarget = false;
                m_hasWorkApproach = false;
                if (m_nextWorkSearchTime > 0.0)
                    m_nextWorkSearchTime -= 0.35;
                if (m_nextWorkSearchTime <= 0.0)
                {
                    m_nextWorkSearchTime = 2.0;
                    m_hasWorkTarget = TryFindWorkBlock(false, out m_workTarget, out m_workApproachPosition);
                    m_hasWorkApproach = m_hasWorkTarget;
                }
            }

            if (!m_hasWorkTarget)
            {
                TickPatrol(3f, 5f);
                return;
            }

            if (Vector3.Distance(m_body.Position, m_workApproachPosition) > 1.25f)
                SetManagedDestination(m_workApproachPosition, WorkMoveSpeed, 0.85f, 220, false, false, false, null);
            else
            {
                StopManagedPath();
                HarvestBlock(m_workTarget.X, m_workTarget.Y, m_workTarget.Z);
                m_hasWorkTarget = false;
                m_hasWorkApproach = false;
                m_nextWorkSearchTime = 0.0;
            }
        }

        private void TickMiner()
        {
            if (m_terrain == null || m_pathfinding == null)
                return;
            if (!m_hasWorkTarget || !m_hasWorkApproach ||
                IsAir(m_terrain.Terrain.GetCellValue(m_workTarget.X, m_workTarget.Y, m_workTarget.Z)))
            {
                m_hasWorkTarget = false;
                m_hasWorkApproach = false;
                if (m_nextWorkSearchTime > 0.0)
                    m_nextWorkSearchTime -= 0.35;
                if (m_nextWorkSearchTime <= 0.0)
                {
                    m_nextWorkSearchTime = 2.5;
                    m_hasWorkTarget = TryFindWorkBlock(true, out m_workTarget, out m_workApproachPosition);
                    m_hasWorkApproach = m_hasWorkTarget;
                }
            }

            if (!m_hasWorkTarget)
            {
                TickPatrol(2.5f, 4.5f);
                return;
            }

            if (Vector3.Distance(m_body.Position, m_workApproachPosition) > 1.25f)
                SetManagedDestination(m_workApproachPosition, WorkMoveSpeed, 0.85f, 240, false, false, false, null);
            else
            {
                StopManagedPath();
                HarvestBlock(m_workTarget.X, m_workTarget.Y, m_workTarget.Z);
                m_hasWorkTarget = false;
                m_hasWorkApproach = false;
                m_nextWorkSearchTime = 0.0;
            }
        }

        private bool TryFindWorkBlock(bool ore, out Point3 target, out Vector3 approach)
        {
            target = new Point3(0, 0, 0);
            approach = Vector3.Zero;
            int cx = Terrain.ToCell(m_body.Position.X);
            int cy = Terrain.ToCell(m_body.Position.Y);
            int cz = Terrain.ToCell(m_body.Position.Z);
            float bestDistance = float.MaxValue;
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
                if (!matches || !TryGetWorkApproach(x, y, z, out Vector3 candidate))
                    continue;
                float distance = Vector3.DistanceSquared(m_body.Position, candidate);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    target = new Point3(x, y, z);
                    approach = candidate;
                }
            }
            return bestDistance != float.MaxValue;
        }

        private void HarvestBlock(int x, int y, int z)
        {
            int value = m_terrain.Terrain.GetCellValue(x, y, z);
            int content = Terrain.ExtractContents(value);
            if (content == 0 || content >= BlocksManager.Blocks.Length)
                return;

            int toolValue = SelectBestToolForBlock(value);
            int toolLevel = toolValue == 0 ? 0 : BlocksManager.Blocks[Terrain.ExtractContents(toolValue)].GetToolLevel(toolValue);
            m_miner?.Poke(true);

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
            SetManagedDestination(destination, WorkMoveSpeed, 1.1f, 180, false, false, false, null);
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
                    m_nextWorkSearchTime = 4.0;
                    m_hasWorkTarget = TryFindNamedBlock("Furnace", out m_workTarget);
                }
            }
            if (!m_hasWorkTarget)
            {
                TickPatrol(2f, 4f);
                return;
            }
            Vector3 target = new Vector3(m_workTarget.X + 1.5f, m_workTarget.Y, m_workTarget.Z + 0.5f);
            if (Vector3.Distance(m_body.Position, target) > 1.45f)
                SetManagedDestination(target, WorkMoveSpeed, 1.0f, 180, false, false, false, null);
            else
            {
                StopManagedPath();
                ComponentFurnace furnace = FindFurnaceAt(m_workTarget);
                if (furnace != null)
                    TendFurnace(furnace);
            }
        }

        private ComponentFurnace FindFurnaceAt(Point3 coordinates)
        {
            foreach (Entity entity in Project.Entities)
            {
                ComponentFurnace furnace = entity.FindComponent<ComponentFurnace>();
                ComponentBlockEntity blockEntity = entity.FindComponent<ComponentBlockEntity>();
                if (furnace != null && blockEntity != null && blockEntity.Coordinates.X == coordinates.X
                    && blockEntity.Coordinates.Y == coordinates.Y && blockEntity.Coordinates.Z == coordinates.Z)
                    return furnace;
            }
            return null;
        }

        private void TendFurnace(ComponentFurnace furnace)
        {
            if (m_inventory == null || furnace == null)
                return;

            int[] outputSlots = { furnace.ResultSlotIndex, furnace.RemainsSlotIndex };
            foreach (int slot in outputSlots)
            {
                int count = furnace.GetSlotCount(slot);
                if (count <= 0)
                    continue;
                int value = furnace.GetSlotValue(slot);
                int remaining = ComponentInventoryBase.AcquireItems(m_inventory, value, count);
                int collected = count - remaining;
                if (collected > 0)
                    furnace.RemoveSlotItems(slot, collected);
            }

            if (furnace.GetSlotCount(furnace.FuelSlotIndex) == 0)
            {
                for (int i = 0; i < m_inventory.VisibleSlotsCount; i++)
                {
                    int value = m_inventory.GetSlotValue(i);
                    if (m_inventory.GetSlotCount(i) <= 0 || value == 0)
                        continue;
                    Block block = BlocksManager.Blocks[Terrain.ExtractContents(value)];
                    if (block.GetFuelHeatLevel(value) <= 0f || furnace.GetSlotCapacity(furnace.FuelSlotIndex, value) <= 0)
                        continue;
                    furnace.AddSlotItems(furnace.FuelSlotIndex, value, 1);
                    m_inventory.RemoveSlotItems(i, 1);
                    break;
                }
            }

            bool hasInput = false;
            for (int i = 0; i < furnace.m_furnaceSize; i++)
                if (furnace.GetSlotCount(i) > 0) { hasInput = true; break; }
            if (!hasInput)
            {
                for (int i = 0; i < m_inventory.VisibleSlotsCount; i++)
                {
                    int value = m_inventory.GetSlotValue(i);
                    if (m_inventory.GetSlotCount(i) <= 0 || value == 0
                        || BlocksManager.Blocks[Terrain.ExtractContents(value)].GetFuelHeatLevel(value) > 0f)
                        continue;
                    bool inserted = false;
                    for (int inputSlot = 0; inputSlot < furnace.m_furnaceSize; inputSlot++)
                    {
                        if (furnace.GetSlotCount(inputSlot) != 0 || furnace.GetSlotCapacity(inputSlot, value) <= 0)
                            continue;
                        furnace.AddSlotItems(inputSlot, value, 1);
                        furnace.UpdateSmeltingRecipe();
                        if (furnace.m_smeltingRecipe != null)
                        {
                            m_inventory.RemoveSlotItems(i, 1);
                            inserted = true;
                        }
                        else
                        {
                            furnace.RemoveSlotItems(inputSlot, 1);
                            furnace.UpdateSmeltingRecipe();
                        }
                        break;
                    }
                    if (inserted)
                        break;
                }
            }
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
                TickPatrol(3f, 5f);
                return;
            }
            ComponentLivingTownsVillager nearest = null;
            float best = 12f;
            foreach (ComponentCreature creature in m_creatureSpawn.Creatures)
            {
                if (creature.Entity == Entity)
                    continue;
                ComponentLivingTownsVillager other = creature.Entity.FindComponent<ComponentLivingTownsVillager>();
                if (other == null || other.m_body == null || other.m_inventory == null)
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
                TickPatrol(3f, 5f);
                return;
            }
            if (best > 2.6f)
            {
                Vector3 direction = m_body.Position - nearest.m_body.Position;
                direction.Y = 0f;
                if (direction.LengthSquared() < 0.01f)
                    direction = Vector3.UnitX;
                direction = Vector3.Normalize(direction);
                SetManagedDestination(nearest.m_body.Position + direction * 1.8f, WorkMoveSpeed, 1.0f, 180, false, false, false, null);
                return;
            }

            StopManagedPath();
            if (m_nextTradeTime <= 0.0)
            {
                bool gave = TransferTradeItem(m_inventory, nearest.m_inventory);
                bool received = TransferTradeItem(nearest.m_inventory, m_inventory);
                m_nextTradeTime = 18.0;
                if (gave || received)
                {
                    SetTalkingAnimation(true);
                    m_isTalking = true;
                    m_chatTimeRemaining = 2.5;
                    nearest.SetTalkingAnimation(true);
                    nearest.m_isTalking = true;
                    nearest.m_chatTimeRemaining = 2.5;
                    nearest.m_chatPartner = this;
                    m_chatPartner = nearest;
                }
            }
            else
                m_nextTradeTime -= 0.35;
        }

        private static bool TransferTradeItem(ComponentInventory from, ComponentInventory to)
        {
            if (from == null || to == null)
                return false;
            for (int i = 0; i < from.VisibleSlotsCount; i++)
            {
                int value = from.GetSlotValue(i);
                if (from.GetSlotCount(i) < 2 || value == 0)
                    continue;
                string name = BlocksManager.Blocks[Terrain.ExtractContents(value)].GetType().Name;
                if (name.IndexOf("Axe", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Pickaxe", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Rake", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Shovel", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Hammer", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Spear", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Machete", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Bow", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                int targetSlot = ComponentInventoryBase.FindAcquireSlotForItem(to, value);
                if (targetSlot < 0)
                    return false;
                to.AddSlotItems(targetSlot, value, 1);
                from.RemoveSlotItems(i, 1);
                return true;
            }
            return false;
        }

        private int FindItemSlot(int content, int? data)
        {
            if (content <= 0 || m_inventory == null)
                return -1;
            for (int i = 0; i < m_inventory.VisibleSlotsCount; i++)
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
