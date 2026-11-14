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
        private sealed class HomeDoorPassageReservation
        {
            public ComponentLivingTownsVillager Owner;
            public DateTime LastTouchedUtc;
        }

        private sealed class FailedWorkPosition
        {
            public Point3 Cell;
            public double RemainingSeconds;
        }
        private static readonly object s_homeDoorReservationSync = new object();
        private static readonly Dictionary<string, HomeDoorPassageReservation> s_homeDoorReservations =
            new Dictionary<string, HomeDoorPassageReservation>();
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
        private SubsystemSignBlockBehavior m_signBehavior;

        private LivingTownsJob m_job = LivingTownsJob.Unemployed;
        private string m_villagerName = string.Empty;
        private Point3 m_homeDoor;
        private bool m_hasHomeDoor;
        private Point3 m_bedCell;
        private bool m_hasBedCell;
        private Point3 m_namedSignCell;
        private bool m_hasNamedSignCell;
        private double m_nextNamedSignLookupTime;
        private double m_nextPersonalChestDepositTime;
        private bool m_personalChestMissingWarningShown;
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
        private readonly List<FailedWorkPosition> m_failedWorkPositions = new List<FailedWorkPosition>();
        private Point3 m_workTarget;
        private int m_workTask;
        private bool m_hasWorkTarget;
        private Vector3 m_workApproachPosition;
        private bool m_hasWorkApproach;
        private int m_workApproachRetryCount;
        private bool m_hasLumberTreeLock;
        private bool m_isReturningForStorage;
        private int m_lumberjackTreesSinceStorage;
        private double m_lumberjackWorkTimeSinceStorage;
        private int m_lumberTreeX;
        private int m_lumberTreeZ;
        private int m_lumberTreeBaseY;
        private Vector3 m_lumberTreeApproachPosition;
        private int m_lumberSearchRadius;
        private int m_lumberSearchOffset;
        private bool m_lumberScanFound;
        private Point3 m_lumberScanBestTarget;
        private Vector3 m_lumberScanBestApproach;
        private int m_lumberScanBestBaseY;
        private float m_lumberScanBestDistance = float.MaxValue;
        private bool m_wasDusk;
        private double m_nextTradeTime;
        private bool m_farmerKitGranted;
        private int m_farmSearchRadius;
        private int m_farmSearchDx;
        private int m_farmSearchDz;
        private Vector3 m_managedDestination;
        private bool m_hasManagedDestination;
        private double m_destinationRetryCooldown;
        private Vector3 m_managedProgressTarget;
        private float m_managedProgressDistance;
        private double m_managedNoProgressTime;
        private bool m_hasManagedProgressSample;
        private double m_workRouteNoProgressTime;
        private float m_workRouteBestDistance;
        private Vector3 m_workRouteTrackedTarget;
        private bool m_hasWorkRouteSample;
        private double m_homeReturnNoProgressTime;
        private float m_homeReturnBestDistance;
        private Vector3 m_homeReturnTrackedTarget;
        private bool m_hasHomeReturnSample;
        private bool m_wasNight;
        private bool m_hasFallbackRestPosition;
        private Vector3 m_fallbackRestPosition;
        private double m_nextFallbackRestSearchTime;
        private double m_nextObstacleJumpTime;
        private double m_nextObstacleDetourTime;
        private bool m_obstacleDetourActive;
        private Vector3 m_obstacleDetourTarget;
        private double m_bedAssignmentFailureTime;
        private bool m_sleepDoorRouteBypassed;
        private bool m_homeDoorRouteActive;
        private bool m_homeDoorRouteEntering;
        private bool m_waitingForHomeDoorPassage;
        private int m_homeDoorApproachVariant;
        private int m_homeDoorRouteCachedApproachVariant = -1;
        private Vector3 m_homeDoorRouteOutsideApproach;
        private Vector3 m_homeDoorRouteInsideApproach;
        private bool m_hasCachedHomeDoorInward;
        private Vector3 m_cachedHomeDoorInward;
        private int m_homeDoorRouteStage;
        private Vector3 m_homeDoorRouteFinalTarget;
        private bool m_isLeavingHome;

        private const float VillagerWalkSpeed = 2.2f;
        private const float WorkMoveSpeed = 2.2f;
        private const int FarmerWorkRadius = 60;
        private const int FarmCellsPerScan = 512;
        private const int LumberjackHomeRadius = 24;
        private const int LumberjackTreesPerStorageTrip = 1;
        private const int LumberjackDepositItemsPerStorageTrip = 8;
        private const double LumberjackMaxWorkSecondsPerTrip = 150.0;
        private const float LumberjackStorageReturnDistance = 18f;
        private const float LumberjackMaxApproachDistanceFromHome = 16.5f;
        private const int HomeReturnPathfindingPositions = 30000;
        private const int LumberjackWorkPathfindingPositions = 10000;
        private const int LumberjackTreeMaxHeight = 25;
        private const int LumberjackScanColumnsPerUpdate = 900;

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
            m_signBehavior = Project.FindSubsystem<SubsystemSignBlockBehavior>(true);

            // Villager role is determined by the model template, never by a menu assignment
            // or stale saved Job value. This keeps the Farmer model and male/female models isolated.
            m_job = GetJobForModelRoute(m_model?.ModelRoute);
            m_villagerName = valuesDictionary.GetValue<string>("VillagerName", string.Empty);
            m_hasHomeDoor = valuesDictionary.GetValue<bool>("HasHomeDoor", false);
            m_homeDoor = new Point3(
                valuesDictionary.GetValue<int>("HomeDoorX", 0),
                valuesDictionary.GetValue<int>("HomeDoorY", 0),
                valuesDictionary.GetValue<int>("HomeDoorZ", 0));
            m_bedCell = new Point3(
                valuesDictionary.GetValue<int>("BedX", 0),
                valuesDictionary.GetValue<int>("BedY", 0),
                valuesDictionary.GetValue<int>("BedZ", 0));
            // Old releases assigned beds by home door, which allowed roommates to share one spot.
            // Force a fresh lookup of this villager's own named wall sign.
            m_hasBedCell = false;
            m_hasNamedSignCell = false;
            m_personalChestMissingWarningShown = false;
            EnsureVillagerName();
            m_farmerKitGranted = valuesDictionary.GetValue<bool>("FarmerKitGranted", false);

            if (m_locomotion != null)
                m_locomotion.WalkSpeed = VillagerWalkSpeed;
            if (m_body != null)
                m_body.IsGravityEnabled = true;

            Log.Information("[LivingTowns] Villager controller loaded; speed=" + VillagerWalkSpeed + ", miner=" + (m_miner != null) + ", job=" + m_job);
        }

        private static LivingTownsJob GetJobForModelRoute(string modelRoute)
        {
            if (string.Equals(modelRoute, "Models/Farmer/Farmer", StringComparison.OrdinalIgnoreCase))
                return LivingTownsJob.Farmer;

            return LivingTownsJob.Unemployed;
        }

        public override void Save(ValuesDictionary valuesDictionary, EntityToIdMap entityToIdMap)
        {
            valuesDictionary.SetValue("Job", (int)m_job);
            valuesDictionary.SetValue("VillagerName", m_villagerName ?? string.Empty);
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
            ReleaseHomeDoorPassage();
            m_isReturningForStorage = false;
            m_lumberjackTreesSinceStorage = 0;
            m_lumberjackWorkTimeSinceStorage = 0.0;
            m_homeDoor = new Point3(x, y, z);
            m_hasHomeDoor = true;
            m_hasBedCell = false;
            m_hasNamedSignCell = false;
            m_personalChestMissingWarningShown = false;
            m_hasFallbackRestPosition = false;
            m_nextBedTryTime = 0.0;
            m_nextFallbackRestSearchTime = 0.0;
            m_bedAssignmentFailureTime = 0.0;
            m_sleepDoorRouteBypassed = false;
            m_homeDoorApproachVariant = 0;
            m_homeDoorRouteCachedApproachVariant = -1;
            m_hasCachedHomeDoorInward = false;
        }

        public void Update(float dt)
        {
            if (m_body == null)
                return;

            // Grant the farmer kit after every component has completed loading.
            if (m_job == LivingTownsJob.Farmer && !m_farmerKitGranted)
                EnsureFarmerKit();

            m_nextPersonalChestDepositTime -= dt;
            if (m_nextPersonalChestDepositTime < 0.0)
                m_nextPersonalChestDepositTime = 0.0;
            m_nextNamedSignLookupTime -= dt;
            if (m_nextNamedSignLookupTime < 0.0)
                m_nextNamedSignLookupTime = 0.0;

            m_destinationRetryCooldown -= dt;
            if (m_destinationRetryCooldown < 0.0)
                m_destinationRetryCooldown = 0.0;
            m_nextObstacleDetourTime -= dt;
            if (m_nextObstacleDetourTime < 0.0)
                m_nextObstacleDetourTime = 0.0;
            UpdateManagedPathProgress(dt);
            UpdateWorkRouteProgress(dt);
            TryJumpAtBlockingStep(dt);
            UpdateFailedWorkPositions(dt);

            m_nextDoorCheckTime -= dt;
            if (m_nextDoorCheckTime <= 0.0)
            {
                m_nextDoorCheckTime = 0.10;
                TickAutomaticDoors();
            }

            bool night = IsNight();
            bool dusk = IsDusk();
            // Store gathered items even if the villager has not reached the exact bed cell.
            if ((night || dusk) && IsNearHomeForPersonalStorage())
                DepositCollectedItemsIntoPersonalChest();
            if (night)
            {
                if (!m_wasNight)
                {
                    m_wasNight = true;
                    m_wasDusk = true;
                    EndConversation(false);
                    m_hasWorkTarget = false;
                    m_hasWorkApproach = false;
                    m_nextWorkSearchTime = 0.0;
                    StopManagedPath();
                    ReleaseHomeDoorPassage();
                    m_homeDoorRouteActive = false;
                    m_isLeavingHome = false;
                    m_sleepDoorRouteBypassed = false;
                    m_bedAssignmentFailureTime = 0.0;
                    m_hasFallbackRestPosition = false;
                    m_nextFallbackRestSearchTime = 0.0;
                    m_hasLumberTreeLock = false;
                    m_isReturningForStorage = false;
                    m_lumberjackTreesSinceStorage = 0;
                    m_lumberjackWorkTimeSinceStorage = 0.0;
                    ResetLumberSearch();
                    ResetHomeReturnProgress();
                    Log.Information("[LivingTowns] Night routine started; work movement stopped for villager.");
                }
                TickSleepRoutine(dt);
                return;
            }

            // Dusk starts at DuskStart: villagers stop working and go indoors,
            // but remain awake until NightStart begins.
            if (dusk)
            {
                if (!m_wasDusk)
                {
                    m_wasDusk = true;
                    EndConversation(false);
                    m_hasWorkTarget = false;
                    m_hasWorkApproach = false;
                    m_workTask = 0;
                    m_nextWorkSearchTime = 0.0;
                    m_hasLumberTreeLock = false;
                    m_isReturningForStorage = false;
                    ResetLumberSearch();
                    StopManagedPath();
                    ReleaseHomeDoorPassage();
                    m_homeDoorRouteActive = false;
                    m_isLeavingHome = false;
                    m_bedAssignmentFailureTime = 0.0;
                    m_hasFallbackRestPosition = false;
                    m_nextFallbackRestSearchTime = 0.0;
                    m_sleepDoorRouteBypassed = false;
                    ResetHomeReturnProgress();
                    Log.Information("[LivingTowns] Dusk started; villager is returning home while remaining awake.");
                }
                TickHomeReturnRoutine(dt);
                return;
            }

            if (m_wasNight)
            {
                m_wasNight = false;
                m_wasDusk = false;
                StopManagedPath();
                ReleaseHomeDoorPassage();
                m_homeDoorRouteActive = false;
                m_isLeavingHome = TryBeginHomeExit();
                Log.Information("[LivingTowns] Dawn routine started; villager will leave through its home door.");
            }
            else if (m_wasDusk)
            {
                // Handles a world-time mode change from Sunset back to Day.
                m_wasDusk = false;
                StopManagedPath();
                ReleaseHomeDoorPassage();
                m_homeDoorRouteActive = false;
                m_isLeavingHome = TryBeginHomeExit();
            }
            WakeFromBedIfNeeded();
            if (m_isLeavingHome && TickHomeExitRoutine())
                return;
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
                "Eldeki eşyayı NPC'ye ver"
            };

            DialogsManager.ShowDialog(null, new ListSelectionDialog(
                (string.IsNullOrEmpty(m_villagerName) ? "Köylü" : m_villagerName) + " | Etkileşim",
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
                if (string.Equals((choice ?? string.Empty).Trim(), "Eldeki eşyayı NPC'ye ver", StringComparison.Ordinal))
                    GiveHeldItems(player);
            }
            catch (Exception ex)
            {
                Log.Error("[LivingTowns] Villager item-transfer menu action failed safely: " + ex);
                player?.ComponentGui?.DisplaySmallMessage("Eşya aktarılırken hata oluştu; ayrıntı günlüğe yazıldı.", Color.White, true, false);
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

        private bool EnsureAssignedBed(float dt)
        {
            if (!m_hasHomeDoor)
            {
                m_homeDoor = new Point3(Terrain.ToCell(m_body.Position.X), Terrain.ToCell(m_body.Position.Y), Terrain.ToCell(m_body.Position.Z));
                m_hasHomeDoor = true;
            }

            // A successfully resolved named sleep cell is a persistent assignment.
            // The old 8-second timer expired while slower villagers were still walking
            // through the house. That cleared m_bedCell, stopped their active route and
            // made them restart the door approach every few seconds. Faster villagers
            // could appear to work while the male/farmer got stuck near the doorway.
            if (m_hasBedCell)
            {
                if (IsValidStandingCell(m_bedCell.X, m_bedCell.Y, m_bedCell.Z)
                    && !IsSleepCellClaimed(m_bedCell))
                {
                    // On older saves the bed cell may exist without the in-memory
                    // sign locator. Resolve it lazily for the personal chest link.
                    if (!m_hasNamedSignCell && m_nextNamedSignLookupTime <= 0.0)
                    {
                        m_nextNamedSignLookupTime = 10.0;
                        if (TryFindNamedSignSleepPosition(out Point3 savedSignCell, out Point3 ignoredFloor))
                        {
                            m_namedSignCell = savedSignCell;
                            m_hasNamedSignCell = true;
                        }
                    }
                    return true;
                }

                // Only discard an assignment when its floor/clearance is no longer valid
                // or another villager owns the exact same cell.
                m_hasBedCell = false;
                m_nextBedTryTime = 0.0;
            }

            // Retry only failed/missing assignments. Do not time-expire a valid bed.
            m_nextBedTryTime -= dt;
            if (m_nextBedTryTime > 0.0)
                return false;

            m_nextBedTryTime = 4.0;
            if (!TryFindNamedSignSleepPosition(out Point3 signCell, out Point3 floorCell))
            {
                Log.Warning("[LivingTowns] No wall sign found for " + m_villagerName + "; villager will not choose a random sleep position.");
                return false;
            }

            m_bedCell = floorCell;
            m_namedSignCell = signCell;
            m_hasNamedSignCell = true;
            m_personalChestMissingWarningShown = false;
            // GetBedSleepPosition subtracts 1.28 from the frame origin. Offset it
            // so the resulting body position is exactly centered above the floor cell.
            m_bedFramePosition = new Vector3(floorCell.X + 0.5f, floorCell.Y + 2.28f, floorCell.Z + 0.5f);
            m_hasBedCell = true;
            m_nextBedTryTime = 0.0;
            Log.Information("[LivingTowns] " + m_villagerName + " assigned to wall sign at "
                + signCell.X + "," + signCell.Y + "," + signCell.Z
                + " and sleep cell " + floorCell.X + "," + floorCell.Y + "," + floorCell.Z + ".");
            return true;
        }

        private void EnsureVillagerName()
        {
            if (!string.IsNullOrWhiteSpace(m_villagerName))
            {
                m_villagerName = m_villagerName.Trim();
                return;
            }

            for (int number = 1; number <= 9999; number++)
            {
                string candidate = "Köylü-" + number.ToString("000");
                bool alreadyUsed = false;
                foreach (Entity entity in Project.Entities)
                {
                    if (entity == null || entity == Entity)
                        continue;
                    ComponentLivingTownsVillager other = entity.FindComponent<ComponentLivingTownsVillager>();
                    if (other != null && string.Equals(other.m_villagerName, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        alreadyUsed = true;
                        break;
                    }
                }
                if (!alreadyUsed)
                {
                    m_villagerName = candidate;
                    return;
                }
            }

            m_villagerName = "Köylü-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        private bool TryFindNamedSignSleepPosition(out Point3 selectedSign, out Point3 floorCell)
        {
            selectedSign = new Point3(0, 0, 0);
            floorCell = new Point3(0, 0, 0);
            if (m_terrain == null || m_signBehavior == null || !m_hasHomeDoor || string.IsNullOrWhiteSpace(m_villagerName))
                return false;

            bool found = false;
            float bestScore = float.MaxValue;
            int minX = m_homeDoor.X - 16;
            int maxX = m_homeDoor.X + 16;
            int minZ = m_homeDoor.Z - 16;
            int maxZ = m_homeDoor.Z + 16;
            int minY = Math.Max(1, m_homeDoor.Y - 4);
            int maxY = Math.Min(250, m_homeDoor.Y + 14);

            for (int x = minX; x <= maxX; x++)
            for (int z = minZ; z <= maxZ; z++)
            for (int y = minY; y <= maxY; y++)
            {
                int signValue = m_terrain.Terrain.GetCellValue(x, y, z);
                int signContent = Terrain.ExtractContents(signValue);
                if (signContent <= 0 || signContent >= BlocksManager.Blocks.Length)
                    continue;

                // Wall-mounted signs only. Standing/hanging signs do not define a bed-side wall position.
                if (!(BlocksManager.Blocks[signContent] is AttachedSignBlock))
                    continue;

                SignData signData = m_signBehavior.GetSignData(new Point3(x, y, z));
                if (signData == null || signData.Lines == null)
                    continue;

                bool nameMatches = false;
                for (int line = 0; line < signData.Lines.Length; line++)
                {
                    if (string.Equals((signData.Lines[line] ?? string.Empty).Trim(), m_villagerName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        nameMatches = true;
                        break;
                    }
                }
                if (!nameMatches)
                    continue;

                Point3 outward = CellFace.FaceToPoint3(AttachedSignBlock.GetFace(Terrain.ExtractData(signValue)));
                int preferredX = x + outward.X;
                int preferredZ = z + outward.Z;

                // The sign is on the wall; choose a safe, roofed standing cell on
                // the room side immediately in front of it, never the doorway.
                for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                for (int candidateY = Math.Max(1, y - 6); candidateY <= y - 1; candidateY++)
                {
                    int cx = preferredX + dx;
                    int cz = preferredZ + dz;
                    if (!IsValidStandingCell(cx, candidateY, cz))
                        continue;
                    if (!IsIndoorBedSpot(cx, candidateY, cz, out _))
                        continue;

                    Vector3 candidate = new Vector3(cx + 0.5f, candidateY + 1f, cz + 0.5f);
                    Vector3 doorCenter = new Vector3(m_homeDoor.X + 0.5f, m_homeDoor.Y + 0.5f, m_homeDoor.Z + 0.5f);
                    if (Vector3.DistanceSquared(candidate, doorCenter) < 2.25f)
                        continue;

                    Point3 candidateCell = new Point3(cx, candidateY, cz);
                    if (IsSleepCellClaimed(candidateCell))
                        continue;

                    float score = (dx * dx + dz * dz) * 2.0f
                        + Math.Abs(candidateY - (y - 2)) * 0.4f
                        + Vector3.DistanceSquared(candidate, new Vector3(preferredX + 0.5f, candidateY + 1f, preferredZ + 0.5f));
                    if (!found || score < bestScore)
                    {
                        found = true;
                        bestScore = score;
                        selectedSign = new Point3(x, y, z);
                        floorCell = candidateCell;
                    }
                }
            }
            return found;
        }

        private bool IsSleepCellClaimed(Point3 candidate)
        {
            foreach (Entity entity in Project.Entities)
            {
                if (entity == null || entity == Entity)
                    continue;
                ComponentLivingTownsVillager other = entity.FindComponent<ComponentLivingTownsVillager>();
                if (other == null || !other.m_hasBedCell)
                    continue;

                // Only the exact same standing cell is a conflict. Adjacent named
                // sleep cells are valid: the old 3x3x3 check incorrectly blocked
                // separate beds placed next to each other in the same small house.
                int dx = other.m_bedCell.X - candidate.X;
                int dy = other.m_bedCell.Y - candidate.Y;
                int dz = other.m_bedCell.Z - candidate.Z;
                if (dx * dx + dy * dy + dz * dz < 1)
                    return true;
            }
            return false;
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

        private bool IsDusk()
        {
            if (m_timeOfDay == null || !m_timeOfDay.TimeOfDayEnabled)
                return false;
            return IntervalUtils.IsBetween(m_timeOfDay.TimeOfDay, m_timeOfDay.DuskStart, m_timeOfDay.NightStart);
        }

        private void TickHomeReturnRoutine(float dt)
        {
            TickRestRoutine(dt, false);
        }

        private void TickSleepRoutine(float dt)
        {
            TickRestRoutine(dt, true);
        }

        private void TickRestRoutine(float dt, bool sleepOnArrival)
        {
            if (!EnsureAssignedBed(dt))
            {
                m_bedAssignmentFailureTime += dt;
                m_nextFallbackRestSearchTime -= dt;

                // A missing/misread wall sign is not a reason to stand still.
                // Immediately look for a room position, then widen to safe ground
                // cells around the registered home door.
                if (!m_hasFallbackRestPosition && m_nextFallbackRestSearchTime <= 0.0)
                {
                    m_nextFallbackRestSearchTime = 2.0;
                    if (TryFindHomeRestPosition(out Vector3 fallbackPosition)
                        || TryFindSafeHomeRestPosition(out fallbackPosition))
                    {
                        m_fallbackRestPosition = fallbackPosition;
                        m_hasFallbackRestPosition = true;
                        ResetHomeReturnProgress();
                        Log.Warning("[LivingTowns] " + m_villagerName
                            + " could not resolve its named sign; returning to a safe fallback position at "
                            + fallbackPosition.X.ToString("0.0") + ","
                            + fallbackPosition.Y.ToString("0.0") + ","
                            + fallbackPosition.Z.ToString("0.0") + ".");
                    }
                }

                if (m_hasFallbackRestPosition)
                {
                    RestAtPosition(m_fallbackRestPosition, sleepOnArrival, dt);
                    return;
                }

                // Keep retrying rather than repeatedly stopping movement. If the
                // terrain around the door is temporarily unavailable, the next
                // two-second scan can recover once its chunk becomes ready.
                SetSleepingAnimation(false);
                // A missing sign/bed assignment must not freeze the villager at dusk.
                // Keep it moving to the registered home entrance and retry the bed scan.
                if (m_hasHomeDoor && TryFindDoorSearchApproach(out Vector3 doorSearchTarget))
                {
                    SetManagedDestination(doorSearchTarget, 2.2f, 0.65f,
                        HomeReturnPathfindingPositions, false, false, false, null, false);
                }
                return;
            }

            m_bedAssignmentFailureTime = 0.0;
            m_hasFallbackRestPosition = false;
            m_nextFallbackRestSearchTime = 0.0;
            RestAtPosition(GetBedSleepPosition(), sleepOnArrival, dt);
        }

        private void RestAtPosition(Vector3 target, bool sleepOnArrival, float dt)
        {
            float dx = m_body.Position.X - target.X;
            float dz = m_body.Position.Z - target.Z;
            // Different villager models can have slightly different body origins.
            // Judge bed arrival in the horizontal plane while keeping a vertical safety bound.
            bool reached = dx * dx + dz * dz <= 0.90f * 0.90f
                && Math.Abs(m_body.Position.Y - target.Y) <= 1.75f;
            if (reached)
            {
                ReleaseHomeDoorPassage();
                m_homeDoorRouteActive = false;
                ResetHomeReturnProgress();
                StopManagedPath();
                CloseDoorsForSleep();
                DepositCollectedItemsIntoPersonalChest();

                if (sleepOnArrival)
                {
                    if (!m_sleepPhysicsSaved)
                    {
                        m_wasGravityEnabled = m_body.IsGravityEnabled;
                        m_sleepPhysicsSaved = true;
                    }
                    m_body.IsGravityEnabled = false;
                    m_body.Velocity = Vector3.Zero;
                    m_body.Position = target;
                    SetSleepingAnimation(true);
                }
                else
                {
                    // At dusk the villager waits indoors by its assigned bed,
                    // with gravity enabled and no sleeping animation. It only
                    // lies down after the clock reaches NightStart.
                    if (m_sleepPhysicsSaved)
                    {
                        m_body.IsGravityEnabled = m_wasGravityEnabled;
                        m_sleepPhysicsSaved = false;
                    }
                    m_body.Velocity = Vector3.Zero;
                    SetSleepingAnimation(false);

                    if (m_isReturningForStorage)
                    {
                        m_isReturningForStorage = false;
                        m_lumberjackTreesSinceStorage = 0;
                        m_lumberjackWorkTimeSinceStorage = 0.0;
                        m_nextPersonalChestDepositTime = 0.0;
                        m_isLeavingHome = TryBeginHomeExit();
                        Log.Information("[LivingTowns] " + m_villagerName
                            + " stored its collected items and is resuming the lumberjack shift.");
                    }
                }
            }
            else
            {
                SetSleepingAnimation(false);
                Vector3 routeTarget = target;
                // Measure progress against the active waypoint (outside door, threshold,
                // inside door or final bed), not always against the bed. A doorway route
                // can initially move sideways or away from the bed and must not be
                // misclassified as stuck while it is following a valid waypoint.
                if (!m_sleepDoorRouteBypassed)
                    TryGetHomeDoorRouteDestination(target, true, out routeTarget);
                UpdateHomeReturnProgress(routeTarget, dt);

                // Never bypass the doorway when the villager is stuck outside:
                // doing so sends it directly to the bed through the house wall.
                // Retry the current waypoint and vary the outside approach instead.
                bool homePathSearchPending = m_pathfinding?.m_pathfindingResult?.IsInProgress == true;
                double homeRetryThreshold = homePathSearchPending ? 30.0 : 14.0;
                if (m_homeReturnNoProgressTime >= homeRetryThreshold)
                {
                    if (m_homeDoorRouteActive)
                    {
                        if (m_homeDoorRouteStage == 0)
                        {
                            m_homeDoorApproachVariant = (m_homeDoorApproachVariant + 1) % 3;
                            Log.Warning("[LivingTowns] " + m_villagerName
                                + " could not reach the home-door approach; trying approach distance variant "
                                + m_homeDoorApproachVariant + ".");
                        }
                        else
                        {
                            // The villager is already at the threshold. Re-open the door
                            // and retry the same crossing stage rather than routing through a wall.
                            OpenHomeDoorForRoute();
                            Log.Warning("[LivingTowns] " + m_villagerName
                                + " is retrying home-door crossing stage " + m_homeDoorRouteStage + ".");
                        }
                    }
                    else
                    {
                        Log.Warning("[LivingTowns] " + m_villagerName
                            + " is retrying its home destination after a navigation stall.");
                    }

                    StopManagedPath();
                    ResetHomeReturnProgress();
                }

                SetManagedDestination(routeTarget, 2.2f, 0.65f, HomeReturnPathfindingPositions,
                    false, false, false, null, false);
            }
        }

        private ComponentChest FindPersonalChest()
        {
            if (m_terrain == null || !m_hasHomeDoor)
                return null;

            // Prefer this villager's own name sign. If the sign is temporarily
            // unavailable (for example after loading a chunk), still search near
            // its assigned home door instead of giving up on storage.
            if (!m_hasNamedSignCell && m_nextNamedSignLookupTime <= 0.0)
            {
                m_nextNamedSignLookupTime = 10.0;
                if (TryFindNamedSignSleepPosition(out Point3 signCell, out Point3 ignoredFloor))
                {
                    m_namedSignCell = signCell;
                    m_hasNamedSignCell = true;
                }
            }

            Point3 anchor = m_hasNamedSignCell ? m_namedSignCell : m_homeDoor;
            int horizontalRange = m_hasNamedSignCell ? 10 : 8;
            int verticalRange = m_hasNamedSignCell ? 6 : 5;
            int maxDistanceSquared = horizontalRange * horizontalRange;

            ComponentChest bestChest = null;
            float bestScore = float.MaxValue;

            // Sign orientation varies with house construction. Search around both
            // sides of the sign and retain the nearest real chest block entity.
            foreach (Entity entity in Project.Entities)
            {
                if (entity == null)
                    continue;

                ComponentChest chest = entity.FindComponent<ComponentChest>();
                ComponentBlockEntity blockEntity = entity.FindComponent<ComponentBlockEntity>();
                if (chest == null || blockEntity == null)
                    continue;

                Point3 cell = blockEntity.Coordinates;
                int dx = cell.X - anchor.X;
                int dy = cell.Y - anchor.Y;
                int dz = cell.Z - anchor.Z;
                if (Math.Abs(dx) > horizontalRange || Math.Abs(dz) > horizontalRange
                    || Math.Abs(dy) > verticalRange || dx * dx + dz * dz > maxDistanceSquared)
                    continue;

                int value = m_terrain.Terrain.GetCellValue(cell.X, cell.Y, cell.Z);
                int content = Terrain.ExtractContents(value);
                if (content <= 0 || content >= BlocksManager.Blocks.Length
                    || !(BlocksManager.Blocks[content] is ChestBlock))
                    continue;

                float score = dx * dx + dz * dz + dy * dy * 1.25f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestChest = chest;
                }
            }

            return bestChest;
        }

        private bool IsPersonalChestProtectedItem(int value)
        {
            int content = Terrain.ExtractContents(value);
            if (content <= 0 || content >= BlocksManager.Blocks.Length)
                return true;

            Block block = BlocksManager.Blocks[content];
            string typeName = block.GetType().Name;
            string category = block.GetCategory(value) ?? string.Empty;

            // Keep work tools and farmer supplies in the villager's inventory.
            if (category.IndexOf("Tool", StringComparison.OrdinalIgnoreCase) >= 0
                || category.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Seed", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Axe", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Pickaxe", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Rake", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Shovel", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Hammer", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Spear", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Bow", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Machete", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Firestarter", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private bool IsNearHomeForPersonalStorage()
        {
            if (!m_hasHomeDoor || m_body == null)
                return false;
            float dx = m_body.Position.X - (m_homeDoor.X + 0.5f);
            float dz = m_body.Position.Z - (m_homeDoor.Z + 0.5f);
            return dx * dx + dz * dz <= 20f * 20f
                && Math.Abs(m_body.Position.Y - (m_homeDoor.Y + 0.5f)) <= 18f;
        }

        private void DepositCollectedItemsIntoPersonalChest()
        {
            if (m_inventory == null || m_nextPersonalChestDepositTime > 0.0)
                return;

            m_nextPersonalChestDepositTime = 4.0;
            try
            {
                ComponentChest chest = FindPersonalChest();
                if (chest == null)
                {
                    if (!m_personalChestMissingWarningShown)
                    {
                        Log.Warning("[LivingTowns] No personal chest found near the home/sign for "
                            + m_villagerName + "; collected items remain in the villager inventory.");
                        m_personalChestMissingWarningShown = true;
                    }
                    return;
                }

                m_personalChestMissingWarningShown = false;
                int movedItems = 0;
                for (int i = 0; i < m_inventory.VisibleSlotsCount; i++)
                {
                    int value = m_inventory.GetSlotValue(i);
                    int count = m_inventory.GetSlotCount(i);
                    if (value == 0 || count <= 0 || IsPersonalChestProtectedItem(value))
                        continue;

                    // AcquireItems handles matching stacks and empty slots. Remove
                    // from the villager only after the chest accepted those items.
                    int remainder = ComponentInventoryBase.AcquireItems(chest, value, count);
                    int inserted = count - remainder;
                    if (inserted > 0)
                        movedItems += m_inventory.RemoveSlotItems(i, inserted);
                }

                if (movedItems > 0)
                    Log.Information("[LivingTowns] " + m_villagerName
                        + " deposited " + movedItems + " collected items into its personal chest.");
                else
                    Log.Information("[LivingTowns] " + m_villagerName
                        + " checked its personal chest; no transferable items or free chest slots were available.");
            }
            catch (Exception ex)
            {
                Log.Error("[LivingTowns] Personal chest deposit failed for "
                    + m_villagerName + ": " + ex);
            }
        }

        private int CountDepositableItems()
        {
            if (m_inventory == null)
                return 0;

            int total = 0;
            for (int i = 0; i < m_inventory.VisibleSlotsCount; i++)
            {
                int value = m_inventory.GetSlotValue(i);
                int count = m_inventory.GetSlotCount(i);
                if (value != 0 && count > 0 && !IsPersonalChestProtectedItem(value))
                    total += count;
            }
            return total;
        }

        private bool TryInferHomeDoorInsideDirection(Vector3 doorCenter, Vector3 axis, out Vector3 inward)
        {
            inward = Vector3.Zero;
            if (m_terrain == null)
                return false;
            Vector3 tangent = new Vector3(-axis.Z, 0f, axis.X);
            bool found = false;
            float bestScore = float.NegativeInfinity;
            Vector3 bestDirection = Vector3.Zero;
            for (int sign = -1; sign <= 1; sign += 2)
            for (int distance = 1; distance <= 8; distance++)
            for (int lateral = -3; lateral <= 3; lateral++)
            {
                int x = Terrain.ToCell(doorCenter.X + axis.X * sign * distance + tangent.X * lateral);
                int z = Terrain.ToCell(doorCenter.Z + axis.Z * sign * distance + tangent.Z * lateral);
                for (int floorY = Math.Max(1, m_homeDoor.Y - 2); floorY <= Math.Min(250, m_homeDoor.Y); floorY++)
                {
                    if (!IsValidStandingCell(x, floorY, z)
                        || !IsIndoorBedSpot(x, floorY, z, out int walls) || walls < 2)
                        continue;
                    float verticalPenalty = Math.Abs((floorY + 1f) - doorCenter.Y) * 2f;
                    float score = walls * 10f - distance * 0.8f - Math.Abs(lateral) * 0.6f - verticalPenalty;
                    if (!found || score > bestScore)
                    {
                        found = true;
                        bestScore = score;
                        bestDirection = axis * sign;
                    }
                }
            }
            if (found) inward = bestDirection;
            return found;
        }

        private Vector3 FindHomeDoorApproachPoint(Vector3 doorCenter, Vector3 inward, bool inside)
        {
            Vector3 direction = inside ? inward : -inward;
            float idealDistance = m_homeDoorApproachVariant == 0 ? 1.10f
                : m_homeDoorApproachVariant == 1 ? 2.10f : 3.10f;
            Vector3 ideal = doorCenter + direction * idealDistance;
            Vector3 tangent = new Vector3(-direction.Z, 0f, direction.X);
            Vector3 best = ideal;
            float bestScore = float.MaxValue;
            bool found = false;
            for (int along = 1; along <= 4; along++)
            for (int lateral = -2; lateral <= 2; lateral++)
            {
                int x = Terrain.ToCell(doorCenter.X + direction.X * along + tangent.X * lateral);
                int z = Terrain.ToCell(doorCenter.Z + direction.Z * along + tangent.Z * lateral);
                for (int floorY = Math.Max(1, m_homeDoor.Y - 2); floorY <= Math.Min(250, m_homeDoor.Y + 1); floorY++)
                {
                    if (!IsValidStandingCell(x, floorY, z))
                        continue;
                    Vector3 candidate = new Vector3(x + 0.5f, floorY + 1f, z + 0.5f);
                    float side = Vector3.Dot(candidate - doorCenter, direction);
                    if (side < 0.55f || Math.Abs(candidate.Y - doorCenter.Y) > 1.75f)
                        continue;
                    float score = Vector3.DistanceSquared(candidate, ideal)
                        + Math.Abs(candidate.Y - doorCenter.Y) * 2f + Math.Abs(lateral) * 0.15f;
                    if (!found || score < bestScore)
                    {
                        found = true;
                        bestScore = score;
                        best = candidate;
                    }
                }
            }
            return found ? best : ideal;
        }

        private bool TryFindDoorSearchApproach(out Vector3 approach)
        {
            approach = Vector3.Zero;
            if (m_terrain == null || m_body == null || !m_hasHomeDoor)
                return false;

            bool found = false;
            float bestScore = float.MaxValue;
            // Search reachable standing cells around the registered entrance.
            // This avoids asking the pathfinder to stand inside a wall or door block.
            for (int radius = 2; radius <= 8; radius++)
            for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius)
                    continue;
                int x = m_homeDoor.X + dx;
                int z = m_homeDoor.Z + dz;
                for (int floorY = Math.Max(1, m_homeDoor.Y - 2); floorY <= Math.Min(250, m_homeDoor.Y + 1); floorY++)
                {
                    if (!IsValidStandingCell(x, floorY, z))
                        continue;
                    Vector3 candidate = new Vector3(x + 0.5f, floorY + 1f, z + 0.5f);
                    float score = Vector3.DistanceSquared(m_body.Position, candidate)
                        + Vector3.DistanceSquared(candidate, new Vector3(m_homeDoor.X + 0.5f, m_homeDoor.Y + 1f, m_homeDoor.Z + 0.5f)) * 0.20f;
                    if (!found || score < bestScore)
                    {
                        found = true;
                        bestScore = score;
                        approach = candidate;
                    }
                }
            }
            return found;
        }

        private bool TryGetHomeDoorGeometry(out Vector3 doorCenter, out Vector3 inward)
        {
            doorCenter = Vector3.Zero;
            inward = Vector3.Zero;
            if (m_terrain == null || !m_hasHomeDoor || (!m_hasBedCell && !m_hasFallbackRestPosition))
                return false;

            // The assigned home coordinate can occasionally point at the upper door
            // half (or a neighboring cell after a world edit). Resolve the actual
            // bottom half before deriving its rotation and threshold.
            Point3 resolvedDoor = m_homeDoor;
            bool doorFound = false;
            int bestDoorOffset = int.MaxValue;
            // Home-door assignments may be several blocks off after house edits
            // or old saves. Search a broad 15x7x15 volume around the registered point.
            // Keep the nearest real bottom-half door so a neighboring house is less
            // likely to be selected.
            for (int dx = -7; dx <= 7; dx++)
            for (int dy = -3; dy <= 3; dy++)
            for (int dz = -7; dz <= 7; dz++)
            {
                int x = m_homeDoor.X + dx;
                int y = m_homeDoor.Y + dy;
                int z = m_homeDoor.Z + dz;
                int candidateValue = m_terrain.Terrain.GetCellValue(x, y, z);
                int candidateContent = Terrain.ExtractContents(candidateValue);
                if (candidateContent <= 0 || candidateContent >= BlocksManager.Blocks.Length
                    || !(BlocksManager.Blocks[candidateContent] is DoorBlock)
                    || !DoorBlock.IsBottomPart(m_terrain.Terrain, x, y, z))
                    continue;

                int offset = Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz);
                if (offset < bestDoorOffset)
                {
                    bestDoorOffset = offset;
                    resolvedDoor = new Point3(x, y, z);
                    doorFound = true;
                }
            }
            if (!doorFound)
                return false;

            if (resolvedDoor.X != m_homeDoor.X || resolvedDoor.Y != m_homeDoor.Y || resolvedDoor.Z != m_homeDoor.Z)
            {
                m_homeDoor = resolvedDoor;
                m_hasCachedHomeDoorInward = false;
            }

            int value = m_terrain.Terrain.GetCellValue(m_homeDoor.X, m_homeDoor.Y, m_homeDoor.Z);
            int rotation = DoorBlock.GetRotation(Terrain.ExtractData(value));
            Vector3 axis = (rotation & 1) == 0 ? Vector3.UnitZ : Vector3.UnitX;
            doorCenter = new Vector3(m_homeDoor.X + 0.5f, m_homeDoor.Y, m_homeDoor.Z + 0.5f);
            if (m_hasCachedHomeDoorInward)
            {
                inward = m_cachedHomeDoorInward;
                return true;
            }

            Vector3 bedPosition = m_hasBedCell
                ? new Vector3(m_bedCell.X + 0.5f, m_bedCell.Y + 1f, m_bedCell.Z + 0.5f)
                : m_fallbackRestPosition;
            float side = Vector3.Dot(bedPosition - doorCenter, axis);
            if (Math.Abs(side) >= 0.45f)
            {
                inward = side > 0f ? axis : -axis;
                m_cachedHomeDoorInward = inward;
                m_hasCachedHomeDoorInward = true;
                return true;
            }

            if (TryInferHomeDoorInsideDirection(doorCenter, axis, out inward))
            {
                m_cachedHomeDoorInward = inward;
                m_hasCachedHomeDoorInward = true;
                return true;
            }
            return false;
        }

        private string GetHomeDoorReservationKey()
        {
            return m_homeDoor.X + ":" + m_homeDoor.Y + ":" + m_homeDoor.Z;
        }

        private bool TryAcquireHomeDoorPassage()
        {
            string key = GetHomeDoorReservationKey();
            DateTime now = DateTime.UtcNow;
            lock (s_homeDoorReservationSync)
            {
                if (s_homeDoorReservations.TryGetValue(key, out HomeDoorPassageReservation reservation))
                {
                    // Recover a reservation left behind if an entity was removed while crossing.
                    if (reservation.Owner == null || (now - reservation.LastTouchedUtc).TotalSeconds > 30.0)
                        s_homeDoorReservations.Remove(key);
                    else if (object.ReferenceEquals(reservation.Owner, this))
                    {
                        reservation.LastTouchedUtc = now;
                        return true;
                    }
                    else
                        return false;
                }

                s_homeDoorReservations[key] = new HomeDoorPassageReservation
                {
                    Owner = this,
                    LastTouchedUtc = now
                };
                return true;
            }
        }

        private void ReleaseHomeDoorPassage()
        {
            m_waitingForHomeDoorPassage = false;
            if (!m_hasHomeDoor)
                return;

            string key = GetHomeDoorReservationKey();
            lock (s_homeDoorReservationSync)
            {
                if (s_homeDoorReservations.TryGetValue(key, out HomeDoorPassageReservation reservation)
                    && object.ReferenceEquals(reservation.Owner, this))
                    s_homeDoorReservations.Remove(key);
            }
        }

        private bool TryGetHomeDoorRouteDestination(Vector3 finalTarget, bool entering, out Vector3 routeTarget)
        {
            routeTarget = finalTarget;
            if (m_body == null)
            {
                ReleaseHomeDoorPassage();
                m_homeDoorRouteActive = false;
                return false;
            }
            if (!TryGetHomeDoorGeometry(out Vector3 doorCenter, out Vector3 inward))
            {
                // Fail closed: never send a villager straight to an indoor bed when
                // its registered doorway cannot be resolved. That direct path is
                // exactly what causes wall-walking when door coordinates are stale.
                ReleaseHomeDoorPassage();
                m_homeDoorRouteActive = true;
                m_homeDoorRouteEntering = entering;
                m_homeDoorRouteFinalTarget = finalTarget;
                // Never use the villager's current position as its destination:
                // that silently converts a failed door lookup into a permanent freeze.
                // Keep moving toward the registered doorway while the wider scan retries.
                m_waitingForHomeDoorPassage = false;
                // Never path to the registered door cell itself when its geometry
                // cannot be resolved: that cell may now be a wall. Choose a verified
                // two-block-high standing cell beside the entrance instead.
                if (TryFindDoorSearchApproach(out Vector3 searchApproach))
                    routeTarget = searchApproach;
                else
                    routeTarget = new Vector3(m_body.Position.X, m_body.Position.Y, m_body.Position.Z);
                return true;
            }

            float targetSide = Vector3.Dot(finalTarget - doorCenter, inward);
            float currentSide = Vector3.Dot(m_body.Position - doorCenter, inward);
            bool sameRoute = m_homeDoorRouteActive
                && m_homeDoorRouteEntering == entering
                && Vector3.DistanceSquared(m_homeDoorRouteFinalTarget, finalTarget) <= 0.25f;

            if (!sameRoute)
            {
                if (m_homeDoorRouteActive)
                    ReleaseHomeDoorPassage();
                m_homeDoorRouteActive = false;
                // Start a doorway route only while clearly on the opposite
                // side. Once the villager has crossed the threshold, do not re-enter
                // the route state on the next frame.
                bool shouldRoute = entering
                    ? targetSide > 0.45f && currentSide < 0.45f
                    : targetSide < -0.45f && currentSide > -0.45f;
                if (!shouldRoute)
                {
                    ReleaseHomeDoorPassage();
                    return false;
                }

                m_homeDoorRouteActive = true;
                m_homeDoorRouteEntering = entering;
                m_homeDoorRouteStage = 0;
                m_homeDoorApproachVariant = 0;
                m_homeDoorRouteCachedApproachVariant = -1;
                m_homeDoorRouteFinalTarget = finalTarget;

                // Open a normal wooden door as soon as the return route is established.
                // Waiting until the pathfinder reaches the threshold can deadlock when
                // its first closed-door path search returns no route.
                if (entering)
                    OpenHomeDoorForRoute();

                Log.Information("[LivingTowns] " + m_villagerName
                    + " started " + (entering ? "entry" : "exit")
                    + " route through home door at " + m_homeDoor.X + "," + m_homeDoor.Y + "," + m_homeDoor.Z
                    + " (rotation " + DoorBlock.GetRotation(Terrain.ExtractData(
                        m_terrain.Terrain.GetCellValue(m_homeDoor.X, m_homeDoor.Y, m_homeDoor.Z))) + ").");
            }

            // ComponentBody.Position is at standing height, one block above the door's bottom cell.
            // Targeting the raw door-cell Y made larger models stop below the threshold.
            Vector3 doorwayPoint = doorCenter + Vector3.UnitY;
            Vector3 outsidePoint = doorwayPoint - inward * 1.10f;
            Vector3 insidePoint = doorwayPoint + inward * 1.10f;
            if (m_homeDoorRouteCachedApproachVariant != m_homeDoorApproachVariant)
            {
                m_homeDoorRouteOutsideApproach = FindHomeDoorApproachPoint(doorCenter, inward, false);
                m_homeDoorRouteInsideApproach = FindHomeDoorApproachPoint(doorCenter, inward, true);
                m_homeDoorRouteCachedApproachVariant = m_homeDoorApproachVariant;
            }
            Vector3 outsideApproach = m_homeDoorRouteOutsideApproach;
            Vector3 insideApproach = m_homeDoorRouteInsideApproach;

            // Only one villager may occupy the doorway route at a time. A second
            // villager waits safely away from the threshold until the passage is free.
            if (!TryAcquireHomeDoorPassage())
            {
                routeTarget = entering
                    ? outsidePoint - inward * 1.10f
                    : insidePoint + inward * 1.10f;
                float waitDx = m_body.Position.X - routeTarget.X;
                float waitDz = m_body.Position.Z - routeTarget.Z;
                m_waitingForHomeDoorPassage = waitDx * waitDx + waitDz * waitDz <= 0.80f * 0.80f
                    && Math.Abs(m_body.Position.Y - routeTarget.Y) <= 1.75f;
                return true;
            }
            m_waitingForHomeDoorPassage = false;

            // Use horizontal distance and threshold-plane crossing for doorway
            // waypoints. Full 3D distance to the door center is unreliable for
            // different villager body origins and can leave larger models oscillating
            // at the doorway forever.
            float outsideDx = m_body.Position.X - outsideApproach.X;
            float outsideDz = m_body.Position.Z - outsideApproach.Z;
            float insideDx = m_body.Position.X - insideApproach.X;
            float insideDz = m_body.Position.Z - insideApproach.Z;
            float outsideDistanceSquared = outsideDx * outsideDx + outsideDz * outsideDz;
            float insideDistanceSquared = insideDx * insideDx + insideDz * insideDz;
            float outsideDoorDistanceSquared =
                (m_body.Position.X - outsidePoint.X) * (m_body.Position.X - outsidePoint.X)
                + (m_body.Position.Z - outsidePoint.Z) * (m_body.Position.Z - outsidePoint.Z);
            float insideDoorDistanceSquared =
                (m_body.Position.X - insidePoint.X) * (m_body.Position.X - insidePoint.X)
                + (m_body.Position.Z - insidePoint.Z) * (m_body.Position.Z - insidePoint.Z);

            if (entering)
            {
                if (m_homeDoorRouteStage == 0 && outsideDistanceSquared <= 0.72f * 0.72f)
                    m_homeDoorRouteStage = 1;
                if (m_homeDoorRouteStage == 1 && currentSide >= -0.10f)
                    m_homeDoorRouteStage = 2;
                if (m_homeDoorRouteStage == 2
                    && (insideDoorDistanceSquared <= 0.60f * 0.60f || currentSide >= 0.50f))
                    m_homeDoorRouteStage = 3;
            }
            else
            {
                if (m_homeDoorRouteStage == 0 && insideDistanceSquared <= 0.72f * 0.72f)
                    m_homeDoorRouteStage = 1;
                if (m_homeDoorRouteStage == 1 && currentSide <= 0.10f)
                    m_homeDoorRouteStage = 2;
                if (m_homeDoorRouteStage == 2
                    && (outsideDoorDistanceSquared <= 0.60f * 0.60f || currentSide <= -0.50f))
                    m_homeDoorRouteStage = 3;
            }

            if (m_homeDoorRouteStage >= 3)
            {
                ReleaseHomeDoorPassage();
                m_homeDoorRouteActive = false;
                m_homeDoorApproachVariant = 0;
                Log.Information("[LivingTowns] " + m_villagerName
                    + " crossed the home doorway successfully.");
                return false;
            }

            if (m_homeDoorRouteStage >= 1
                && Vector3.Distance(m_body.Position, doorCenter) <= 3.5f)
                OpenHomeDoorForRoute();

            routeTarget = m_homeDoorRouteStage == 0
                ? (entering ? outsideApproach : insideApproach)
                : m_homeDoorRouteStage == 1 ? doorwayPoint
                : (entering ? insidePoint : outsidePoint);
            return true;
        }

        private void OpenHomeDoorForRoute()
        {
            if (m_doorBehavior == null || m_terrain == null)
                return;

            int value = m_terrain.Terrain.GetCellValue(m_homeDoor.X, m_homeDoor.Y, m_homeDoor.Z);
            int content = Terrain.ExtractContents(value);
            int data = Terrain.ExtractData(value);
            if (content <= 0 || content >= BlocksManager.Blocks.Length
                || !(BlocksManager.Blocks[content] is DoorBlock)
                || !DoorBlock.IsBottomPart(m_terrain.Terrain, m_homeDoor.X, m_homeDoor.Y, m_homeDoor.Z)
                || DoorBlock.GetOpen(data)
                || m_doorBehavior.IsDoorElectricallyConnected(m_homeDoor.X, m_homeDoor.Y, m_homeDoor.Z))
                return;

            if (m_doorBehavior.OpenCloseDoor(m_homeDoor.X, m_homeDoor.Y, m_homeDoor.Z, true)
                && !ContainsDoor(m_autoOpenedDoors, m_homeDoor.X, m_homeDoor.Y, m_homeDoor.Z))
                m_autoOpenedDoors.Add(m_homeDoor);
        }

        private bool TryBeginHomeExit()
        {
            if (m_body == null)
                return false;
            if (!TryGetHomeDoorGeometry(out Vector3 doorCenter, out Vector3 inward))
            {
                // Keep dawn exit active until the assigned door can be resolved;
                // do not start a work/patrol route from inside the house.
                return m_hasHomeDoor;
            }

            float currentSide = Vector3.Dot(m_body.Position - doorCenter, inward);
            // A shallow inside position still needs to use the door. The old
            // 0.60 threshold could leave a villager at the inner door edge and
            // let its next job start without crossing the doorway.
            if (currentSide <= 0.10f)
                return false;

            m_homeDoorRouteActive = true;
            m_homeDoorRouteEntering = false;
            m_homeDoorRouteStage = 0;
            m_homeDoorApproachVariant = 0;
            m_homeDoorRouteCachedApproachVariant = -1;
            m_homeDoorRouteFinalTarget = doorCenter + Vector3.UnitY - inward * 1.10f;
            ResetHomeReturnProgress();
            return true;
        }

        private bool TickHomeExitRoutine()
        {
            if (!m_isLeavingHome)
                return false;

            if (!TryGetHomeDoorGeometry(out Vector3 doorCenter, out Vector3 inward))
            {
                // Retain the exit state and retry on the next update. Abandoning it
                // would let the villager begin its job inside the house or through a wall.
                ReleaseHomeDoorPassage();
                return m_hasHomeDoor;
            }

            Vector3 exitTarget = doorCenter + Vector3.UnitY - inward * 1.10f;
            if (Vector3.Distance(m_body.Position, exitTarget) <= 0.70f)
            {
                ReleaseHomeDoorPassage();
                m_isLeavingHome = false;
                m_homeDoorRouteActive = false;
                m_homeDoorApproachVariant = 0;
                StopManagedPath();
                ResetHomeReturnProgress();
                return false;
            }

            Vector3 routeTarget = exitTarget;
            TryGetHomeDoorRouteDestination(exitTarget, false, out routeTarget);
            if (!m_homeDoorRouteActive)
            {
                ReleaseHomeDoorPassage();
                m_isLeavingHome = false;
                StopManagedPath();
                ResetHomeReturnProgress();
                return false;
            }

            UpdateHomeReturnProgress(routeTarget, 0.35f);
            bool exitPathSearchPending = m_pathfinding?.m_pathfindingResult?.IsInProgress == true;
            double exitRetryThreshold = exitPathSearchPending ? 30.0 : 14.0;
            if (m_homeReturnNoProgressTime >= exitRetryThreshold)
            {
                if (m_homeDoorRouteStage == 0)
                {
                    m_homeDoorApproachVariant = (m_homeDoorApproachVariant + 1) % 3;
                    Log.Warning("[LivingTowns] " + m_villagerName
                        + " is retrying the home exit approach distance variant "
                        + m_homeDoorApproachVariant + ".");
                }
                else
                {
                    OpenHomeDoorForRoute();
                    Log.Warning("[LivingTowns] " + m_villagerName
                        + " is retrying home exit doorway stage " + m_homeDoorRouteStage + ".");
                }
                StopManagedPath();
                ResetHomeReturnProgress();
            }

            SetManagedDestination(routeTarget, 2.2f, 0.45f, HomeReturnPathfindingPositions,
                false, false, false, null, false);
            return true;
        }

        private bool TryFindSafeHomeRestPosition(out Vector3 restPosition)
        {
            restPosition = Vector3.Zero;
            if (m_terrain == null || !m_hasHomeDoor)
                return false;

            float bestScore = float.MaxValue;
            bool found = false;
            Vector3 bestPosition = Vector3.Zero;
            int minFloorY = Math.Max(1, m_homeDoor.Y - 5);
            int maxFloorY = Math.Min(250, m_homeDoor.Y + 2);

            // Search at the actual house elevation, not the terrain column's top.
            // Indoor cells receive a strong preference, with closeness to the door
            // breaking ties between equivalent positions.
            for (int radius = 0; radius <= 12; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (radius > 0 && Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius)
                        continue;

                    int x = m_homeDoor.X + dx;
                    int z = m_homeDoor.Z + dz;
                    for (int floorY = minFloorY; floorY <= maxFloorY; floorY++)
                    {
                        if (!IsValidStandingCell(x, floorY, z))
                            continue;

                        bool indoor = IsIndoorBedSpot(x, floorY, z, out int walls);
                        Vector3 candidate = new Vector3(x + 0.5f, floorY + 1f, z + 0.5f);
                        float horizontalDistanceSquared = dx * dx + dz * dz;
                        float score = horizontalDistanceSquared
                            + Math.Abs(floorY + 1f - m_homeDoor.Y) * 4f
                            - (indoor ? 100f + walls * 15f : 0f);
                        if (!found || score < bestScore)
                        {
                            found = true;
                            bestScore = score;
                            bestPosition = candidate;
                        }
                    }
                }
            }

            if (found)
            {
                restPosition = bestPosition;
                return true;
            }

            // Never reuse a remote current body position as a fake "home" target.
            // It is acceptable only when the villager is already beside its door.
            Vector3 doorPosition = new Vector3(m_homeDoor.X + 0.5f, m_homeDoor.Y, m_homeDoor.Z + 0.5f);
            if (Vector3.DistanceSquared(m_body.Position, doorPosition) <= 9f)
            {
                int bodyX = Terrain.ToCell(m_body.Position.X);
                int bodyY = Terrain.ToCell(m_body.Position.Y);
                int bodyZ = Terrain.ToCell(m_body.Position.Z);
                if (IsValidStandingCell(bodyX, bodyY - 1, bodyZ))
                {
                    restPosition = new Vector3(bodyX + 0.5f, bodyY, bodyZ + 0.5f);
                    return true;
                }
            }
            return false;
        }

        private bool TryFindHomeRestPosition(out Vector3 restPosition)
        {
            restPosition = Vector3.Zero;
            if (m_terrain == null || !m_hasHomeDoor)
                return false;

            bool foundIndoor = false;
            float bestScore = float.NegativeInfinity;
            Vector3 bestPosition = Vector3.Zero;

            // First prefer a real indoor standing cell close to this villager's door.
            // Search a wider vertical range because houses are often built above/below
            // the door's lower half, and rooms need not be directly beside the entrance.
            for (int radius = 1; radius <= 9; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius)
                        continue;

                    int x = m_homeDoor.X + dx;
                    int z = m_homeDoor.Z + dz;
                    for (int yOffset = -4; yOffset <= 3; yOffset++)
                    {
                        int floorY = m_homeDoor.Y - 1 + yOffset;
                        if (!IsValidStandingCell(x, floorY, z))
                            continue;

                        bool indoor = IsIndoorBedSpot(x, floorY, z, out int walls);
                        if (!indoor)
                            continue;

                        float distance = Vector3.Distance(m_body.Position,
                            new Vector3(x + 0.5f, floorY + 1f, z + 0.5f));
                        float score = 1000f + walls * 35f - radius * 8f
                            - Math.Abs(yOffset) * 12f - distance;
                        if (!foundIndoor || score > bestScore)
                        {
                            foundIndoor = true;
                            bestScore = score;
                            bestPosition = new Vector3(x + 0.5f, floorY + 1f, z + 0.5f);
                        }
                    }
                }
                // Once we have indoor candidates in this ring, farther rings cannot
                // beat the distance preference unless they have substantially more walls.
                if (foundIndoor && radius >= 4)
                    break;
            }

            if (foundIndoor)
            {
                restPosition = bestPosition;
                return true;
            }

            // Last-resort home return: if no roofed cell exists, at least route to a
            // safe, walkable cell immediately beside the registered home door.
            float nearest = float.MaxValue;
            for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            for (int yOffset = -2; yOffset <= 2; yOffset++)
            {
                if (dx == 0 && dz == 0)
                    continue;
                int x = m_homeDoor.X + dx;
                int z = m_homeDoor.Z + dz;
                int floorY = m_homeDoor.Y - 1 + yOffset;
                if (!IsValidStandingCell(x, floorY, z))
                    continue;
                float distance = Vector3.DistanceSquared(m_body.Position,
                    new Vector3(x + 0.5f, floorY + 1f, z + 0.5f));
                if (distance < nearest)
                {
                    nearest = distance;
                    bestPosition = new Vector3(x + 0.5f, floorY + 1f, z + 0.5f);
                }
            }

            if (nearest == float.MaxValue)
                return false;
            restPosition = bestPosition;
            return true;
        }

        private bool IsValidStandingCell(int x, int floorY, int z)
        {
            if (floorY < 1 || floorY > 251)
                return false;
            int floorValue = m_terrain.Terrain.GetCellValue(x, floorY, z);
            int feetValue = m_terrain.Terrain.GetCellValue(x, floorY + 1, z);
            int headValue = m_terrain.Terrain.GetCellValue(x, floorY + 2, z);
            int floorContent = Terrain.ExtractContents(floorValue);
            int feetContent = Terrain.ExtractContents(feetValue);
            int headContent = Terrain.ExtractContents(headValue);
            if (floorContent <= 0 || floorContent >= BlocksManager.Blocks.Length
                || !BlocksManager.Blocks[floorContent].IsCollidable_(floorValue))
                return false;
            if (feetContent >= BlocksManager.Blocks.Length || headContent >= BlocksManager.Blocks.Length)
                return false;
            return !BlocksManager.Blocks[feetContent].IsCollidable_(feetValue)
                && !BlocksManager.Blocks[headContent].IsCollidable_(headValue);
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

                    // Open only the door the villager is actually approaching.
                    // The old 7x7 scan opened side doors too early and repeatedly
                    // when the destination direction changed around nearby obstacles.
                    for (int dx = -2; dx <= 2; dx++)
                    for (int dz = -2; dz <= 2; dz++)
                    for (int dy = -1; dy <= 2; dy++)
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
                        if (distance < 0.25f || distance > 2.35f)
                            continue;

                        float forwardDistance = Vector3.Dot(toDoor, direction);
                        float lateralDistance = Math.Abs(toDoor.X * direction.Z - toDoor.Z * direction.X);
                        if (forwardDistance < 0.25f || forwardDistance > 2.2f || lateralDistance > 0.72f
                            || Math.Abs((y + 0.5f) - m_body.Position.Y) > 1.65f)
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

        private void CloseDoorsForSleep()
        {
            if (m_doorBehavior == null || m_terrain == null)
                return;

            // Close doors opened by this villager even if it is standing near the
            // doorway. The previous distance-only rule deliberately kept these open,
            // so a villager could go to sleep with its own house door still open.
            for (int i = m_autoOpenedDoors.Count - 1; i >= 0; i--)
            {
                Point3 p = m_autoOpenedDoors[i];
                if (IsAnotherVillagerInDoorway(p))
                    continue;
                CloseDoorIfOpen(p.X, p.Y, p.Z);
                m_autoOpenedDoors.RemoveAt(i);
            }

            // The home door may have been opened by another villager or already been
            // open when this villager arrived, so it might not be in the local list.
            if (!IsAnotherVillagerInDoorway(m_homeDoor))
                CloseDoorIfOpen(m_homeDoor.X, m_homeDoor.Y, m_homeDoor.Z);
        }

        private void CloseDoorIfOpen(int x, int y, int z)
        {
            if (m_doorBehavior == null || m_terrain == null)
                return;
            int value = m_terrain.Terrain.GetCellValue(x, y, z);
            int content = Terrain.ExtractContents(value);
            if (content <= 0 || content >= BlocksManager.Blocks.Length
                || !(BlocksManager.Blocks[content] is DoorBlock)
                || !DoorBlock.IsBottomPart(m_terrain.Terrain, x, y, z)
                || !DoorBlock.GetOpen(Terrain.ExtractData(value))
                || m_doorBehavior.IsDoorElectricallyConnected(x, y, z))
                return;

            // OpenCloseDoor always emits a sound, even if asked for the current state.
            // Guard it with GetOpen so a sleeping villager cannot spam close sounds.
            m_doorBehavior.OpenCloseDoor(x, y, z, false);
        }

        private void CloseDoorsFarBehind()
        {
            for (int i = m_autoOpenedDoors.Count - 1; i >= 0; i--)
            {
                Point3 p = m_autoOpenedDoors[i];
                Vector3 center = new Vector3(p.X + 0.5f, p.Y, p.Z + 0.5f);
                // Keep a door open while the villager is still passing through it.
                // A little hysteresis prevents open/close chatter near the threshold.
                if (Vector3.Distance(m_body.Position, center) <= 2.8f || IsAnotherVillagerNearDoor(p))
                    continue;

                CloseDoorIfOpen(p.X, p.Y, p.Z);
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
                if (body != null && Vector3.Distance(body.Position, center) < 3.5f)
                    return true;
            }
            return false;
        }

        private bool IsAnotherVillagerInDoorway(Point3 door)
        {
            if (m_creatureSpawn == null)
                return false;
            Vector3 center = new Vector3(door.X + 0.5f, door.Y, door.Z + 0.5f);
            foreach (ComponentCreature creature in m_creatureSpawn.Creatures)
            {
                if (creature.Entity == Entity || creature.Entity.FindComponent<ComponentLivingTownsVillager>() == null)
                    continue;
                ComponentBody body = creature.Entity.FindComponent<ComponentBody>();
                if (body != null && Vector3.Distance(body.Position, center) < 1.45f)
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

        private void TryJumpAtBlockingStep(float dt)
        {
            m_nextObstacleJumpTime -= dt;
            if (m_nextObstacleJumpTime > 0.0 || m_locomotion == null || m_terrain == null
                || m_pathfinding?.Destination.HasValue != true
                || !m_body.StandingOnValue.HasValue
                || m_body.ImmersionFactor > 0.5f
                || m_managedNoProgressTime < 0.55)
                return;

            Vector3 direction = m_pathfinding.Destination.Value - m_body.Position;
            direction.Y = 0f;
            if (direction.LengthSquared() < 0.04f)
                return;
            direction = Vector3.Normalize(direction);

            // Probe the next foot cell along the current route. When a single
            // collidable block is directly in front and two blocks of headroom
            // remain, issue a short jump instead of pushing into it indefinitely.
            int x = Terrain.ToCell(m_body.Position.X + direction.X * 0.78f);
            int z = Terrain.ToCell(m_body.Position.Z + direction.Z * 0.78f);
            int feetY = Terrain.ToCell(m_body.Position.Y + 0.1f);
            int obstacleValue = m_terrain.Terrain.GetCellValue(x, feetY, z);
            int obstacleContent = Terrain.ExtractContents(obstacleValue);
            if (obstacleContent <= 0 || obstacleContent >= BlocksManager.Blocks.Length)
                return;

            Block obstacle = BlocksManager.Blocks[obstacleContent];
            if (!obstacle.IsCollidable_(obstacleValue) || obstacle is DoorBlock)
                return;

            int headValue = m_terrain.Terrain.GetCellValue(x, feetY + 1, z);
            int ceilingValue = m_terrain.Terrain.GetCellValue(x, feetY + 2, z);
            int headContent = Terrain.ExtractContents(headValue);
            int ceilingContent = Terrain.ExtractContents(ceilingValue);
            if (headContent < 0 || headContent >= BlocksManager.Blocks.Length
                || ceilingContent < 0 || ceilingContent >= BlocksManager.Blocks.Length)
                return;
            if (BlocksManager.Blocks[headContent].IsCollidable_(headValue)
                || BlocksManager.Blocks[ceilingContent].IsCollidable_(ceilingValue))
                return;

            // JumpOrder is merged with the pilot's own order by ComponentPilot,
            // so this remains compatible with the game's normal locomotion update.
            m_locomotion.JumpOrder = 1f;
            m_nextObstacleJumpTime = 1.15;
        }

        private void UpdateWorkRouteProgress(float dt)
        {
            if (!m_hasWorkTarget || !m_hasWorkApproach || m_body == null)
            {
                ResetWorkRouteProgress();
                return;
            }

            float distance = Vector3.Distance(m_body.Position, m_workApproachPosition);
            if (!m_hasWorkRouteSample
                || Vector3.DistanceSquared(m_workRouteTrackedTarget, m_workApproachPosition) > 0.25f)
            {
                m_workRouteTrackedTarget = m_workApproachPosition;
                m_workRouteBestDistance = distance;
                m_workRouteNoProgressTime = 0.0;
                m_hasWorkRouteSample = true;
                return;
            }

            if (distance <= 1.55f)
            {
                m_workRouteBestDistance = distance;
                m_workRouteNoProgressTime = 0.0;
                return;
            }

            if (distance < m_workRouteBestDistance - 0.15f)
            {
                m_workRouteBestDistance = distance;
                m_workRouteNoProgressTime = 0.0;
            }
            else
            {
                // Sideways circling is not progress. This timer continues even when
                // ComponentPathfinding temporarily clears Destination.
                m_workRouteNoProgressTime += dt;
            }
        }

        private void ResetWorkRouteProgress()
        {
            m_workRouteNoProgressTime = 0.0;
            m_workRouteBestDistance = 0f;
            m_workRouteTrackedTarget = Vector3.Zero;
            m_hasWorkRouteSample = false;
        }

        private void UpdateHomeReturnProgress(Vector3 target, float dt)
        {
            if (m_body == null)
                return;

            float dx = m_body.Position.X - target.X;
            float dz = m_body.Position.Z - target.Z;
            float dy = Math.Abs(m_body.Position.Y - target.Y);
            float distance = Vector3.Distance(m_body.Position, target);
            bool reached = dx * dx + dz * dz <= 0.90f * 0.90f && dy <= 1.75f;

            if (m_waitingForHomeDoorPassage)
            {
                m_homeReturnTrackedTarget = target;
                m_homeReturnBestDistance = distance;
                m_homeReturnNoProgressTime = 0.0;
                m_hasHomeReturnSample = true;
                return;
            }

            if (!m_hasHomeReturnSample
                || Vector3.DistanceSquared(m_homeReturnTrackedTarget, target) > 0.25f)
            {
                m_homeReturnTrackedTarget = target;
                m_homeReturnBestDistance = distance;
                m_homeReturnNoProgressTime = 0.0;
                m_hasHomeReturnSample = true;
                return;
            }

            if (reached || distance < m_homeReturnBestDistance - 0.15f)
            {
                m_homeReturnBestDistance = distance;
                m_homeReturnNoProgressTime = 0.0;
            }
            else
            {
                m_homeReturnNoProgressTime += dt;
            }
        }

        private void ResetHomeReturnProgress()
        {
            m_homeReturnNoProgressTime = 0.0;
            m_homeReturnBestDistance = 0f;
            m_homeReturnTrackedTarget = Vector3.Zero;
            m_hasHomeReturnSample = false;
        }

        private void UpdateManagedPathProgress(float dt)
        {
            if (m_pathfinding?.Destination.HasValue != true || m_body == null)
            {
                m_managedNoProgressTime = 0.0;
                m_managedProgressDistance = 0f;
                m_hasManagedProgressSample = false;
                return;
            }

            Vector3 target = m_pathfinding.Destination.Value;
            float distanceToTarget = Vector3.Distance(m_body.Position, target);
            if (!m_hasManagedProgressSample
                || Vector3.DistanceSquared(target, m_managedProgressTarget) > 0.25f)
            {
                m_managedProgressTarget = target;
                m_managedProgressDistance = distanceToTarget;
                m_managedNoProgressTime = 0.0;
                m_hasManagedProgressSample = true;
                return;
            }

            // Track actual progress toward the requested destination, not raw movement.
            // A villager circling a tree can move every frame without getting any closer;
            // the old position-delta watchdog incorrectly treated that as progress.
            float arrivalDistance = Math.Max(0.9f, m_pathfinding.Range + 0.25f);
            if (distanceToTarget <= arrivalDistance)
            {
                m_managedProgressDistance = distanceToTarget;
                m_managedNoProgressTime = 0.0;
                return;
            }

            // Keep the best distance reached during this attempt. Small oscillations and
            // sideways movement do not reset the watchdog; meaningful approach does.
            if (distanceToTarget < m_managedProgressDistance - 0.15f)
            {
                m_managedProgressDistance = distanceToTarget;
                m_managedNoProgressTime = 0.0;
            }
            else
            {
                m_managedNoProgressTime += dt;
            }
        }

        private void SetManagedDestination(Vector3 destination, float speed, float range, int maxPathfindingPositions,
            bool useRandomMovements, bool ignoreHeightDifference, bool raycastDestination, ComponentBody doNotAvoidBody,
            bool allowRandomRecoveryOnFailure = true)
        {
            if (m_pathfinding == null)
                return;

            // When the direct route stops making progress, briefly route to a nearby
            // clear standing cell. The normal job/home routine will resume its real
            // destination as soon as this side-step waypoint is reached.
            if (m_obstacleDetourActive)
            {
                if (Vector3.DistanceSquared(m_body.Position, m_obstacleDetourTarget) <= 1.10f * 1.10f)
                {
                    m_obstacleDetourActive = false;
                    m_nextObstacleDetourTime = 0.35;
                }
                else
                    destination = m_obstacleDetourTarget;
            }
            else if (m_nextObstacleDetourTime <= 0.0 && m_managedNoProgressTime >= 0.20)
            {
                if (TryChooseObstacleDetour(destination, out Vector3 detour))
                {
                    m_obstacleDetourActive = true;
                    m_obstacleDetourTarget = detour;
                    m_nextObstacleDetourTime = 2.0;
                    destination = detour;
                    Log.Information("[LivingTowns] " + m_villagerName
                        + " is taking a clear side route around an obstacle.");
                }
                else
                    m_nextObstacleDetourTime = 1.5;
            }

            Vector3? current = m_pathfinding.Destination;
            bool sameRequestedTarget = m_hasManagedDestination
                && Vector3.DistanceSquared(m_managedDestination, destination) <= 0.0625f;
            bool sameCurrentTarget = current.HasValue
                && Vector3.DistanceSquared(current.Value, destination) <= 0.0625f;
            bool stuck = m_pathfinding.IsStuck;
            bool pathSearchPending = m_pathfinding.m_pathfindingResult?.IsInProgress == true;
            double stalledAfter = pathSearchPending ? 30.0
                : ((m_homeDoorRouteActive || m_isLeavingHome || m_isReturningForStorage)
                    ? 18.0 : (m_job == LivingTownsJob.Lumberjack ? 15.0 : 10.0));
            bool stalled = sameRequestedTarget && m_managedNoProgressTime >= stalledAfter;

            // Important: a stuck pathfinder keeps Destination set. Never treat that as
            // proof that the route is still working; explicitly restart the route search.
            if (sameCurrentTarget && !stuck && !stalled)
                return;
            if (!current.HasValue && sameRequestedTarget && m_destinationRetryCooldown > 0.0
                && !stuck && !stalled)
                return;

            if ((stuck || stalled) && !allowRandomRecoveryOnFailure && sameCurrentTarget)
            {
                // Retry the same safe waypoint after a short pause, never wander randomly
                // when a villager is navigating a house entrance.
                m_pathfinding.Stop();
                m_destinationRetryCooldown = 1.5;
                m_managedNoProgressTime = 0.0;
                return;
            }

            if (stuck || stalled)
                m_pathfinding.Stop();

            bool allowRandomRecovery = useRandomMovements
                || (allowRandomRecoveryOnFailure && (stuck || stalled));
            int pathBudget = Math.Max(maxPathfindingPositions, 700);
            m_pathfinding.SetDestination(destination, speed, range, pathBudget,
                allowRandomRecovery, ignoreHeightDifference, raycastDestination, doNotAvoidBody);
            m_managedDestination = destination;
            m_hasManagedDestination = true;
            m_destinationRetryCooldown = 3.0;
            m_managedNoProgressTime = 0.0;
            m_managedProgressTarget = destination;
            m_managedProgressDistance = Vector3.Distance(m_body.Position, destination);
            m_hasManagedProgressSample = true;
        }

        private bool TryChooseObstacleDetour(Vector3 goal, out Vector3 detour)
        {
            detour = Vector3.Zero;
            if (m_terrain == null || m_body == null || m_pathfinding == null)
                return false;

            Vector3 toGoal = goal - m_body.Position;
            toGoal.Y = 0f;
            if (toGoal.LengthSquared() < 1.0f)
                return false;
            toGoal = Vector3.Normalize(toGoal);

            bool found = false;
            float bestScore = float.MaxValue;
            int cx = Terrain.ToCell(m_body.Position.X);
            int cz = Terrain.ToCell(m_body.Position.Z);
            int currentFloor = Terrain.ToCell(m_body.Position.Y) - 1;

            // Sample complete rings around the villager. Side and diagonal cells let
            // it step around corners instead of repeatedly pushing into the same wall.
            for (int radius = 1; radius <= 3; radius++)
            for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius)
                    continue;
                int x = cx + dx;
                int z = cz + dz;
                for (int floorY = Math.Max(1, currentFloor - 1); floorY <= Math.Min(250, currentFloor + 1); floorY++)
                {
                    if (!IsValidStandingCell(x, floorY, z))
                        continue;

                    Vector3 candidate = new Vector3(x + 0.5f, floorY + 1f, z + 0.5f);
                    if (Vector3.DistanceSquared(candidate, m_body.Position) < 1.15f * 1.15f
                        || Vector3.DistanceSquared(candidate, goal) < 1.0f)
                        continue;

                    int openNeighbors = 0;
                    for (int nx = -1; nx <= 1; nx++)
                    for (int nz = -1; nz <= 1; nz++)
                    {
                        if (nx == 0 && nz == 0)
                            continue;
                        if (IsValidStandingCell(x + nx, floorY, z + nz))
                            openNeighbors++;
                    }

                    Vector3 candidateDirection = candidate - m_body.Position;
                    candidateDirection.Y = 0f;
                    float sideStep = Math.Abs(candidateDirection.X * toGoal.Z - candidateDirection.Z * toGoal.X);
                    float progressPenalty = Vector3.DistanceSquared(candidate, goal);
                    float travelPenalty = Vector3.DistanceSquared(candidate, m_body.Position) * 0.30f;
                    float verticalPenalty = Math.Abs(candidate.Y - m_body.Position.Y) * 2.0f;
                    float score = progressPenalty + travelPenalty + verticalPenalty
                        + (8 - openNeighbors) * 0.45f - sideStep * 0.20f;

                    if (!found || score < bestScore)
                    {
                        found = true;
                        bestScore = score;
                        detour = candidate;
                    }
                }
            }
            return found;
        }

        private void StopManagedPath()
        {
            if (m_pathfinding != null && m_pathfinding.Destination.HasValue)
                m_pathfinding.Stop();
            m_hasManagedDestination = false;
            m_destinationRetryCooldown = 0.0;
            m_managedNoProgressTime = 0.0;
            m_managedProgressDistance = 0f;
            m_hasManagedProgressSample = false;
            m_obstacleDetourActive = false;
            m_nextObstacleDetourTime = 0.0;
        }

        private bool TryGetWorkApproach(int x, int y, int z, out Vector3 approach)
        {
            approach = Vector3.Zero;
            float bestDistance = float.MaxValue;
            bool found = false;
            Point3[] sides =
            {
                new Point3(1, 0, 0), new Point3(-1, 0, 0),
                new Point3(0, 0, 1), new Point3(0, 0, -1),
                new Point3(1, 0, 1), new Point3(1, 0, -1),
                new Point3(-1, 0, 1), new Point3(-1, 0, -1)
            };

            for (int i = 0; i < sides.Length; i++)
            {
                int sx = x + sides[i].X;
                int sz = z + sides[i].Z;
                // Tree trunks and ores can be several blocks above the walkable floor.
                // Search the adjacent column downward instead of requiring a floor at target Y.
                for (int standY = Math.Max(1, y - 12); standY <= Math.Min(250, y + 1); standY++)
                {
                    int feet = m_terrain.Terrain.GetCellValue(sx, standY, sz);
                    int head = m_terrain.Terrain.GetCellValue(sx, standY + 1, sz);
                    int floor = m_terrain.Terrain.GetCellValue(sx, standY - 1, sz);
                    int floorContent = Terrain.ExtractContents(floor);
                    if (!IsAir(feet) || !IsAir(head) || floorContent <= 0 || floorContent >= BlocksManager.Blocks.Length
                        || !BlocksManager.Blocks[floorContent].IsCollidable_(floor))
                        continue;

                    if (IsFailedWorkPosition(sx, standY, sz))
                        continue;

                    Vector3 candidate = new Vector3(sx + 0.5f, standY, sz + 0.5f);
                    float verticalPenalty = Math.Abs(candidate.Y - m_body.Position.Y) * 5f;
                    float score = Vector3.DistanceSquared(m_body.Position, candidate) + verticalPenalty;
                    if (score < bestDistance)
                    {
                        bestDistance = score;
                        approach = candidate;
                        found = true;
                    }
                }
            }
            return found;
        }


        // Failed work cells are temporarily blacklisted. A pathfinder that cannot
        // reach one tree or plot must not retry the same coordinates forever.
        private void UpdateFailedWorkPositions(float dt)
        {
            for (int i = m_failedWorkPositions.Count - 1; i >= 0; i--)
            {
                m_failedWorkPositions[i].RemainingSeconds -= dt;
                if (m_failedWorkPositions[i].RemainingSeconds <= 0.0)
                    m_failedWorkPositions.RemoveAt(i);
            }
        }

        private bool IsFailedWorkPosition(int x, int y, int z)
        {
            for (int i = 0; i < m_failedWorkPositions.Count; i++)
            {
                Point3 cell = m_failedWorkPositions[i].Cell;
                if (cell.X == x && cell.Y == y && cell.Z == z)
                    return true;
            }
            return false;
        }

        private void RejectWorkPosition(Point3 cell, double seconds)
        {
            for (int i = 0; i < m_failedWorkPositions.Count; i++)
            {
                FailedWorkPosition failed = m_failedWorkPositions[i];
                if (failed.Cell.X == cell.X && failed.Cell.Y == cell.Y && failed.Cell.Z == cell.Z)
                {
                    failed.RemainingSeconds = Math.Max(failed.RemainingSeconds, seconds);
                    return;
                }
            }

            if (m_failedWorkPositions.Count >= 64)
                m_failedWorkPositions.RemoveAt(0);
            m_failedWorkPositions.Add(new FailedWorkPosition
            {
                Cell = cell,
                RemainingSeconds = seconds
            });
        }

        private bool WorkRouteHasFailed(float arrivalDistance)
        {
            if (m_pathfinding == null || !m_hasWorkApproach)
                return false;
            if (Vector3.Distance(m_body.Position, m_workApproachPosition) <= arrivalDistance)
                return false;

            // Destination can briefly be cleared during the pathfinder's Stuck ->
            // MovingDirect transition. Check IsStuck and the progress watchdog even
            // when Destination is null so a blocked villager cannot retry forever.
            bool pathSearchPending = m_pathfinding.m_pathfindingResult?.IsInProgress == true;
            double workRetryThreshold = pathSearchPending ? 30.0
                : (m_job == LivingTownsJob.Lumberjack ? 14.0 : 5.0);
            double managedRetryThreshold = pathSearchPending ? 30.0
                : (m_job == LivingTownsJob.Lumberjack ? 16.0 : 6.0);
            return (!pathSearchPending && m_pathfinding.IsStuck)
                || m_workRouteNoProgressTime >= workRetryThreshold
                || m_managedNoProgressTime >= managedRetryThreshold;
        }

        private bool TryRetryWorkApproach(string jobName)
        {
            int maximumApproaches = m_job == LivingTownsJob.Lumberjack ? 2 : 4;
            if (!m_hasWorkTarget || !m_hasWorkApproach || m_workApproachRetryCount >= maximumApproaches)
                return false;

            Point3 failedApproach = new Point3(
                Terrain.ToCell(m_workApproachPosition.X),
                Terrain.ToCell(m_workApproachPosition.Y),
                Terrain.ToCell(m_workApproachPosition.Z));
            RejectWorkPosition(failedApproach, 60.0);
            Vector3 nextApproach = Vector3.Zero;
            bool found = false;

            if (m_job == LivingTownsJob.Lumberjack)
            {
                if (m_hasLumberTreeLock)
                    found = TryGetLumberjackApproach(m_lumberTreeX, m_lumberTreeBaseY, m_lumberTreeZ, out nextApproach);
                else if (TryGetGroundedTrunkBaseY(m_workTarget.X, m_workTarget.Y, m_workTarget.Z, out int baseY))
                    found = TryGetLumberjackApproach(m_workTarget.X, baseY, m_workTarget.Z, out nextApproach);
            }
            else
            {
                int approachY = m_job == LivingTownsJob.Farmer
                    ? (m_workTask == 4 ? m_workTarget.Y : m_workTarget.Y + 1)
                    : m_workTarget.Y;
                found = TryGetWorkApproach(m_workTarget.X, approachY, m_workTarget.Z, out nextApproach);
            }

            if (!found || Vector3.DistanceSquared(nextApproach, m_workApproachPosition) < 0.25f)
                return false;

            StopManagedPath();
            m_workApproachPosition = nextApproach;
            if (m_job == LivingTownsJob.Lumberjack && m_hasLumberTreeLock)
                m_lumberTreeApproachPosition = nextApproach;
            m_hasWorkApproach = true;
            m_workApproachRetryCount++;
            Log.Warning("[LivingTowns] " + jobName + " for " + m_villagerName
                + " found an alternate approach (" + m_workApproachRetryCount + "/" + maximumApproaches
                + ") after an obstacle blocked the first route.");
            return true;
        }

        private void AbandonCurrentWorkTarget(string jobName)
        {
            if (m_hasWorkTarget)
                RejectWorkPosition(m_workTarget, 35.0);
            if (m_hasWorkApproach)
            {
                RejectWorkPosition(new Point3(
                    Terrain.ToCell(m_workApproachPosition.X),
                    Terrain.ToCell(m_workApproachPosition.Y),
                    Terrain.ToCell(m_workApproachPosition.Z)), 35.0);
            }

            if (m_job == LivingTownsJob.Lumberjack && m_hasLumberTreeLock)
            {
                // The old implementation kept the same tree lock and swapped in a
                // new waypoint. If every side was unreachable, this recreated the
                // same tree target forever and the villager circled beneath its canopy.
                RejectWorkPosition(new Point3(m_lumberTreeX, m_lumberTreeBaseY, m_lumberTreeZ), 180.0);
                m_hasLumberTreeLock = false;
            }

            Log.Warning("[LivingTowns] " + jobName + " abandoned an unreachable work target for "
                + m_villagerName + "; it will try another target instead of circling.");
            m_hasWorkTarget = false;
            m_hasWorkApproach = false;
            m_workApproachRetryCount = 0;
            m_workTask = 0;
            m_nextWorkSearchTime = 0.8;
            StopManagedPath();
        }

        private bool TryGetFarmTaskAt(Point3 cell, out int task)
        {
            task = 0;
            if (m_terrain == null || cell.Y < 1 || cell.Y > 253)
                return false;

            int value = m_terrain.Terrain.GetCellValue(cell.X, cell.Y, cell.Z);
            int content = Terrain.ExtractContents(value);
            int rye = BlocksManager.GetBlockIndex<RyeBlock>();

            if (content == rye && RyeBlock.GetSize(Terrain.ExtractData(value)) >= 7
                && Terrain.ExtractContents(m_terrain.Terrain.GetCellValue(cell.X, cell.Y - 1, cell.Z)) == 168)
            {
                task = 4;
                return true;
            }

            int above = Terrain.ExtractContents(m_terrain.Terrain.GetCellValue(cell.X, cell.Y + 1, cell.Z));
            if (content == 168 && above == 0)
            {
                task = 3;
                return true;
            }
            return false;
        }

        private void TickFarmer()
        {
            if (m_inventory == null || m_terrain == null || m_pathfinding == null)
                return;

            int rakeSlot = FindItemSlot(BlocksManager.GetBlockIndex<IronRakeBlock>(), null);
            int seedsSlot = FindItemSlot(BlocksManager.GetBlockIndex<SeedsBlock>(), 5);
            if (rakeSlot < 0 || seedsSlot < 0)
            {
                m_hasWorkTarget = false;
                m_hasWorkApproach = false;
                StopManagedPath();
                TickPatrol(2.5f, 4.0f);
                return;
            }

            // Keep one farm task until it is completed. The old implementation
            // selected a new cell every AI tick while still walking to the previous one.
            if (m_hasWorkTarget && !TryGetFarmTaskAt(m_workTarget, out m_workTask))
            {
                m_hasWorkTarget = false;
                m_hasWorkApproach = false;
                m_workTask = 0;
                StopManagedPath();
            }

            if (!m_hasWorkTarget)
            {
                if (m_nextWorkSearchTime > 0.0)
                {
                    m_nextWorkSearchTime -= 0.35;
                    TickPatrol(2.0f, 3.5f);
                    return;
                }

                if (!TryFindFarmTask(out Point3 target, out int task))
                {
                    TickPatrol(2.5f, 4.0f);
                    return;
                }

                m_workTarget = target;
                m_workTask = task;
                m_hasWorkTarget = true;
                m_hasWorkApproach = false;
                m_workApproachRetryCount = 0;
            }

            if (!m_hasWorkApproach)
            {
                int approachY = m_workTask == 4 ? m_workTarget.Y : m_workTarget.Y + 1;
                if (!TryGetWorkApproach(m_workTarget.X, approachY, m_workTarget.Z, out m_workApproachPosition))
                {
                    RejectWorkPosition(m_workTarget, 25.0);
                    m_hasWorkTarget = false;
                    m_workTask = 0;
                    m_nextWorkSearchTime = 0.8;
                    StopManagedPath();
                    return;
                }
                m_hasWorkApproach = true;
            }

            if (WorkRouteHasFailed(1.45f))
            {
                if (TryRetryWorkApproach("Farmer"))
                    return;
                AbandonCurrentWorkTarget("Farmer");
                return;
            }

            if (Vector3.Distance(m_body.Position, m_workApproachPosition) > 1.45f)
            {
                SetManagedDestination(m_workApproachPosition, WorkMoveSpeed, 1.25f, 2400,
                    false, false, false, null, true);
                return;
            }

            StopManagedPath();
            if (!TryGetFarmTaskAt(m_workTarget, out int currentTask))
            {
                m_hasWorkTarget = false;
                m_hasWorkApproach = false;
                m_workTask = 0;
                m_nextWorkSearchTime = 0.25;
                return;
            }

            SelectInventorySlot(currentTask == 3 ? seedsSlot : rakeSlot);
            m_miner?.Poke(true);
            if (currentTask == 3)
            {
                int rye = BlocksManager.GetBlockIndex<RyeBlock>();
                int cropData = RyeBlock.SetSize(RyeBlock.SetIsWild(0, false), 0);
                m_terrain.ChangeCell(m_workTarget.X, m_workTarget.Y + 1, m_workTarget.Z,
                    Terrain.MakeBlockValue(rye, 0, cropData));
                m_inventory.RemoveSlotItems(seedsSlot, 1);
            }
            else if (currentTask == 4)
                HarvestCrop(m_workTarget.X, m_workTarget.Y, m_workTarget.Z);

            m_hasWorkTarget = false;
            m_hasWorkApproach = false;
            m_workTask = 0;
            m_nextWorkSearchTime = 0.45;
        }

        private bool TryFindFarmTask(out Point3 target, out int task)
        {
            target = new Point3(0, 0, 0);
            task = 0;
            int centerX = m_hasHomeDoor ? m_homeDoor.X : Terrain.ToCell(m_body.Position.X);
            int centerZ = m_hasHomeDoor ? m_homeDoor.Z : Terrain.ToCell(m_body.Position.Z);

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
                if (y < 1 || y > 253 || IsFailedWorkPosition(x, y, z))
                    continue;

                Point3 candidate = new Point3(x, y, z);
                if (!TryGetFarmTaskAt(candidate, out task))
                    continue;

                target = candidate;
                return true;
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

        private bool ShouldLumberjackReturnHomeForStorage()
        {
            if (!m_hasHomeDoor || m_body == null)
                return false;

            float dx = m_body.Position.X - (m_homeDoor.X + 0.5f);
            float dz = m_body.Position.Z - (m_homeDoor.Z + 0.5f);
            bool tooFarFromHome = dx * dx + dz * dz
                >= LumberjackStorageReturnDistance * LumberjackStorageReturnDistance;

            return tooFarFromHome
                || m_lumberjackTreesSinceStorage >= LumberjackTreesPerStorageTrip
                || CountDepositableItems() >= LumberjackDepositItemsPerStorageTrip
                || m_lumberjackWorkTimeSinceStorage >= LumberjackMaxWorkSecondsPerTrip
                || (m_hasWorkTarget && m_workRouteNoProgressTime >= 8.0);
        }

        private void BeginLumberjackStorageReturn()
        {
            m_isReturningForStorage = true;
            m_hasWorkTarget = false;
            m_hasWorkApproach = false;
            m_hasLumberTreeLock = false;
            m_workApproachRetryCount = 0;
            m_workTask = 0;
            m_nextWorkSearchTime = 0.0;
            m_isLeavingHome = false;
            m_sleepDoorRouteBypassed = false;
            m_homeDoorApproachVariant = 0;
            m_nextPersonalChestDepositTime = 0.0;
            ResetLumberSearch();
            ResetWorkRouteProgress();
            ResetHomeReturnProgress();
            ReleaseHomeDoorPassage();
            m_homeDoorRouteActive = false;
            StopManagedPath();
            Log.Information("[LivingTowns] " + m_villagerName
                + " is returning home to store lumber and resume work.");
        }

        private void TickLumberjack()
        {
            if (m_terrain == null || m_pathfinding == null)
                return;

            if (m_isReturningForStorage)
            {
                TickRestRoutine(0.35f, false);
                return;
            }

            m_lumberjackWorkTimeSinceStorage += 0.35;

            // This deadline is checked before validating/approaching the current tree.
            // A target that keeps circling under foliage must never suppress the return.
            if (ShouldLumberjackReturnHomeForStorage())
            {
                BeginLumberjackStorageReturn();
                TickRestRoutine(0.35f, false);
                return;
            }

            if (m_hasWorkTarget)
            {
                int currentValue = m_terrain.Terrain.GetCellValue(m_workTarget.X, m_workTarget.Y, m_workTarget.Z);
                int currentContent = Terrain.ExtractContents(currentValue);
                Block currentBlock = currentContent > 0 && currentContent < BlocksManager.Blocks.Length
                    ? BlocksManager.Blocks[currentContent] : null;
                if (!(currentBlock is WoodBlock) && !(currentBlock is LeavesBlock))
                {
                    m_hasWorkTarget = false;
                    m_hasWorkApproach = false;
                    ResetWorkRouteProgress();
                    StopManagedPath();
                }
            }

            // Fail based on actual distance-to-waypoint progress, not only the
            // pathfinder's transient IsStuck / Destination values.
            if (m_hasWorkTarget && m_hasWorkApproach && WorkRouteHasFailed(1.55f))
            {
                if (TryRetryWorkApproach("Lumberjack"))
                {
                    ResetWorkRouteProgress();
                    return;
                }
                AbandonCurrentWorkTarget("Lumberjack");
                BeginLumberjackStorageReturn();
                TickRestRoutine(0.35f, false);
                ResetWorkRouteProgress();
                return;
            }

            if (!m_hasWorkTarget)
            {
                if (m_nextWorkSearchTime > 0.0)
                {
                    m_nextWorkSearchTime -= 0.35;
                    if (m_nextWorkSearchTime > 0.0)
                        return;
                }

                // TickJob runs every 0.35 seconds; keep the incremental tree scan moving.
                m_nextWorkSearchTime = 0.35;
                if (m_hasLumberTreeLock)
                {
                    m_hasWorkTarget = TryFindNextLockedTreePart(out m_workTarget, out m_workApproachPosition);
                    m_hasWorkApproach = m_hasWorkTarget;
                    if (!m_hasWorkTarget)
                    {
                        Log.Information("[LivingTowns] " + m_villagerName + " finished the locked tree at "
                            + m_lumberTreeX + "," + m_lumberTreeBaseY + "," + m_lumberTreeZ
                            + " including its leaves; searching for the next tree within 24 blocks of home.");
                        m_hasLumberTreeLock = false;
                        m_lumberjackTreesSinceStorage++;
                    }
                }

                if (!m_hasWorkTarget && ShouldLumberjackReturnHomeForStorage())
                {
                    BeginLumberjackStorageReturn();
                    TickRestRoutine(0.35f, false);
                    return;
                }

                if (!m_hasWorkTarget)
                {
                    m_hasWorkTarget = TryFindWorkBlock(false, out m_workTarget, out m_workApproachPosition);
                    m_hasWorkApproach = m_hasWorkTarget;
                    if (!m_hasWorkTarget && m_lumberSearchRadius == 0 && m_lumberSearchOffset == 0)
                        m_nextWorkSearchTime = 8.0;
                }
                m_workApproachRetryCount = 0;
                ResetWorkRouteProgress();
            }

            if (!m_hasWorkTarget || !m_hasWorkApproach)
            {
                TickPatrol(2.0f, 3.5f);
                return;
            }

            if (!IsAtLumberjackWorkRange())
            {
                // Do not allow random recovery movement around the trunk. Move to
                // one verified standing cell, then re-evaluate after a no-progress timeout.
                SetManagedDestination(m_workApproachPosition, WorkMoveSpeed, 1.45f,
                    LumberjackWorkPathfindingPositions, false, false, false, null, false);
            }
            else
            {
                StopManagedPath();
                HarvestBlock(m_workTarget.X, m_workTarget.Y, m_workTarget.Z);
                m_hasWorkTarget = false;
                m_hasWorkApproach = false;
                ResetWorkRouteProgress();
                m_nextWorkSearchTime = 0.35;
            }
        }

        private void TickMiner()
        {
            if (m_terrain == null || m_pathfinding == null)
                return;

            if (m_hasWorkTarget && m_hasWorkApproach && WorkRouteHasFailed(1.25f))
            {
                if (TryRetryWorkApproach("Miner"))
                    return;
                AbandonCurrentWorkTarget("Miner");
                return;
            }

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
                    m_workApproachRetryCount = 0;
                }
            }

            if (!m_hasWorkTarget)
            {
                TickPatrol(2.5f, 4.5f);
                return;
            }

            if (Vector3.Distance(m_body.Position, m_workApproachPosition) > 1.25f)
                SetManagedDestination(m_workApproachPosition, WorkMoveSpeed, 1.15f, 1800, false, false, false, null, true);
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
            if (!ore)
                return TryFindTreeTopWorkBlock(out target, out approach);

            target = new Point3(0, 0, 0);
            approach = Vector3.Zero;
            int cx = Terrain.ToCell(m_body.Position.X);
            int cy = Terrain.ToCell(m_body.Position.Y);
            int cz = Terrain.ToCell(m_body.Position.Z);
            float bestDistance = float.MaxValue;
            for (int x = cx - 12; x <= cx + 12; x++)
            for (int z = cz - 12; z <= cz + 12; z++)
            for (int y = Math.Max(1, cy - 20); y <= Math.Min(250, cy + 5); y++)
            {
                int value = m_terrain.Terrain.GetCellValue(x, y, z);
                int content = Terrain.ExtractContents(value);
                if (content == 0 || content >= BlocksManager.Blocks.Length)
                    continue;
                Block block = BlocksManager.Blocks[content];
                if (block.GetType().Name.IndexOf("Ore", StringComparison.OrdinalIgnoreCase) < 0
                    || !TryGetWorkApproach(x, y, z, out Vector3 candidate))
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

        private void ResetLumberSearch()
        {
            m_lumberSearchRadius = 0;
            m_lumberSearchOffset = 0;
            m_lumberScanFound = false;
            m_lumberScanBestDistance = float.MaxValue;
            m_lumberScanBestTarget = new Point3(0, 0, 0);
            m_lumberScanBestApproach = Vector3.Zero;
            m_lumberScanBestBaseY = 0;
        }

        // Scan outward in rings from the villager's home door. The scan is
        // incremental to avoid freezing mobile devices while covering a full
        // 100-block work radius. One chosen tree remains locked until its logs
        // and leaves inside the 25-block canopy envelope are cleared.
        private bool TryFindTreeTopWorkBlock(out Point3 target, out Vector3 approach)
        {
            target = new Point3(0, 0, 0);
            approach = Vector3.Zero;
            int centerX = m_hasHomeDoor ? m_homeDoor.X : Terrain.ToCell(m_body.Position.X);
            int centerZ = m_hasHomeDoor ? m_homeDoor.Z : Terrain.ToCell(m_body.Position.Z);
            int centerY = m_hasHomeDoor ? m_homeDoor.Y : Terrain.ToCell(m_body.Position.Y);
            int columnsLeft = LumberjackScanColumnsPerUpdate;

            while (m_lumberSearchRadius <= LumberjackHomeRadius && columnsLeft > 0)
            {
                int radius = m_lumberSearchRadius;
                int perimeter = radius == 0 ? 1 : 8 * radius;
                if (m_lumberSearchOffset >= perimeter)
                {
                    if (m_lumberScanFound)
                    {
                        target = m_lumberScanBestTarget;
                        approach = m_lumberScanBestApproach;
                        m_lumberTreeX = target.X;
                        m_lumberTreeZ = target.Z;
                        m_lumberTreeBaseY = m_lumberScanBestBaseY;
                        m_lumberTreeApproachPosition = approach;
                        m_hasLumberTreeLock = true;
                        Log.Information("[LivingTowns] " + m_villagerName + " locked a tree at "
                            + m_lumberTreeX + "," + m_lumberTreeBaseY + "," + m_lumberTreeZ
                            + " within " + LumberjackHomeRadius + " blocks of home.");
                        ResetLumberSearch();
                        return true;
                    }

                    m_lumberSearchRadius++;
                    m_lumberSearchOffset = 0;
                    m_lumberScanFound = false;
                    m_lumberScanBestDistance = float.MaxValue;
                    continue;
                }

                int dx;
                int dz;
                int index = m_lumberSearchOffset;
                if (radius == 0)
                {
                    dx = 0;
                    dz = 0;
                }
                else if (index < 2 * radius)
                {
                    dx = -radius + index;
                    dz = -radius;
                }
                else if (index < 4 * radius)
                {
                    dx = radius;
                    dz = -radius + (index - 2 * radius);
                }
                else if (index < 6 * radius)
                {
                    dx = radius - (index - 4 * radius);
                    dz = radius;
                }
                else
                {
                    dx = -radius;
                    dz = radius - (index - 6 * radius);
                }

                m_lumberSearchOffset++;
                columnsLeft--;
                if (dx * dx + dz * dz > LumberjackHomeRadius * LumberjackHomeRadius)
                    continue;

                int x = centerX + dx;
                int z = centerZ + dz;
                // Terrain height varies around a house. Cover nearby valleys and hills
                // while the grounded-trunk check limits each harvested tree to 25 blocks.
                int minY = Math.Max(1, centerY - 16);
                int maxY = Math.Min(250, centerY + 41);
                // Scan from the top down. This usually finds one top log per
                // column instead of repeatedly resolving the same trunk base.
                for (int y = maxY; y >= minY; y--)
                {
                    int value = m_terrain.Terrain.GetCellValue(x, y, z);
                    int content = Terrain.ExtractContents(value);
                    if (content <= 0 || content >= BlocksManager.Blocks.Length
                        || !(BlocksManager.Blocks[content] is WoodBlock)
                        || IsFailedWorkPosition(x, y, z))
                        continue;

                    if (!TryGetGroundedTrunkBaseY(x, y, z, out int baseY)
                        || y > baseY + LumberjackTreeMaxHeight)
                        continue;

                    int aboveValue = m_terrain.Terrain.GetCellValue(x, y + 1, z);
                    int aboveContent = Terrain.ExtractContents(aboveValue);
                    bool woodAbove = aboveContent > 0 && aboveContent < BlocksManager.Blocks.Length
                        && BlocksManager.Blocks[aboveContent] is WoodBlock;
                    if (woodAbove && y < baseY + LumberjackTreeMaxHeight)
                        continue;

                    if (IsFailedWorkPosition(x, baseY, z)
                        || !TryGetLumberjackApproach(x, baseY, z, out Vector3 candidateApproach))
                        continue;

                    if (m_hasHomeDoor)
                    {
                        float homeDx = candidateApproach.X - (m_homeDoor.X + 0.5f);
                        float homeDz = candidateApproach.Z - (m_homeDoor.Z + 0.5f);
                        if (homeDx * homeDx + homeDz * homeDz
                            > LumberjackMaxApproachDistanceFromHome * LumberjackMaxApproachDistanceFromHome)
                            continue;
                    }

                    float distance = Vector3.DistanceSquared(m_body.Position, candidateApproach)
                        + Math.Abs(y - baseY) * 0.20f;
                    if (!m_lumberScanFound || distance < m_lumberScanBestDistance)
                    {
                        m_lumberScanFound = true;
                        m_lumberScanBestDistance = distance;
                        m_lumberScanBestTarget = new Point3(x, y, z);
                        m_lumberScanBestApproach = candidateApproach;
                        m_lumberScanBestBaseY = baseY;
                    }
                    break;
                }
            }

            if (m_lumberSearchRadius > LumberjackHomeRadius)
            {
                ResetLumberSearch();
                Log.Information("[LivingTowns] " + m_villagerName
                    + " finished scanning the 24-block home radius; no reachable tree was found.");
            }
            return false;
        }

        private bool IsAtLumberjackWorkRange()
        {
            if (m_body == null || !m_hasWorkApproach)
                return false;

            // Match the pathfinder's arrival radius. A separate tighter horizontal
            // threshold made a villager keep circling after pathfinding considered
            // the destination reached.
            return Vector3.Distance(m_body.Position, m_workApproachPosition) <= 1.55f;
        }

        private bool TryFindNextLockedTreePart(out Point3 target, out Vector3 approach)
        {
            target = new Point3(0, 0, 0);
            approach = Vector3.Zero;
            if (!m_hasLumberTreeLock)
                return false;

            // Keep one approach position while clearing this tree; changing sides
            // after each block made the villager circle beneath the canopy.
            approach = m_lumberTreeApproachPosition;

            // Finish all trunk/branch wood first. After the logs are gone,
            // collect every nearby LeavesBlock belonging to this canopy.
            bool foundWood = false;
            int bestWoodY = int.MinValue;
            for (int x = m_lumberTreeX - 7; x <= m_lumberTreeX + 7; x++)
            for (int z = m_lumberTreeZ - 7; z <= m_lumberTreeZ + 7; z++)
            for (int y = m_lumberTreeBaseY; y <= Math.Min(250, m_lumberTreeBaseY + LumberjackTreeMaxHeight); y++)
            {
                int value = m_terrain.Terrain.GetCellValue(x, y, z);
                int content = Terrain.ExtractContents(value);
                if (content <= 0 || content >= BlocksManager.Blocks.Length
                    || !(BlocksManager.Blocks[content] is WoodBlock)
                    || IsFailedWorkPosition(x, y, z))
                    continue;

                if (!foundWood || y > bestWoodY)
                {
                    foundWood = true;
                    bestWoodY = y;
                    target = new Point3(x, y, z);
                }
            }
            if (foundWood)
                return true;

            bool foundLeaves = false;
            float bestLeafScore = float.MaxValue;
            for (int x = m_lumberTreeX - 7; x <= m_lumberTreeX + 7; x++)
            for (int z = m_lumberTreeZ - 7; z <= m_lumberTreeZ + 7; z++)
            for (int y = m_lumberTreeBaseY; y <= Math.Min(250, m_lumberTreeBaseY + LumberjackTreeMaxHeight); y++)
            {
                int value = m_terrain.Terrain.GetCellValue(x, y, z);
                int content = Terrain.ExtractContents(value);
                if (content <= 0 || content >= BlocksManager.Blocks.Length
                    || !(BlocksManager.Blocks[content] is LeavesBlock)
                    || IsFailedWorkPosition(x, y, z))
                    continue;

                float score = (x - m_lumberTreeX) * (x - m_lumberTreeX)
                    + (z - m_lumberTreeZ) * (z - m_lumberTreeZ)
                    + Math.Abs(y - m_lumberTreeBaseY) * 0.15f;
                if (!foundLeaves || score < bestLeafScore)
                {
                    foundLeaves = true;
                    bestLeafScore = score;
                    target = new Point3(x, y, z);
                }
            }
            return foundLeaves;
        }

        private bool TryGetGroundedTrunkBaseY(int x, int y, int z, out int baseY)
        {
            baseY = y;
            for (int depth = 0; depth < 32; depth++)
            {
                int belowValue = m_terrain.Terrain.GetCellValue(x, baseY - 1, z);
                int belowContent = Terrain.ExtractContents(belowValue);
                if (belowContent > 0 && belowContent < BlocksManager.Blocks.Length
                    && BlocksManager.Blocks[belowContent] is WoodBlock)
                {
                    baseY--;
                    continue;
                }

                return belowContent > 0 && belowContent < BlocksManager.Blocks.Length
                    && BlocksManager.Blocks[belowContent].IsCollidable_(belowValue);
            }
            return false;
        }

        private bool TryGetLumberjackApproach(int x, int baseY, int z, out Vector3 approach)
        {
            approach = Vector3.Zero;
            float bestScore = float.MaxValue;
            bool found = false;

            // Stay outside the usual canopy footprint. Approaching at 5-8 blocks
            // often places the villager beneath low branches, where its body keeps
            // colliding with leaves and steering in circles.
            for (int radius = 9; radius <= 11; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius)
                        continue;

                    int sx = x + dx;
                    int sz = z + dz;
                    for (int standY = Math.Max(1, baseY - 1); standY <= Math.Min(250, baseY + 1); standY++)
                    {
                        if (!IsValidStandingCell(sx, standY - 1, sz)
                            || IsFailedWorkPosition(sx, standY, sz))
                            continue;

                        int openNeighbors = 0;
                        for (int nx = -1; nx <= 1; nx++)
                        for (int nz = -1; nz <= 1; nz++)
                        {
                            if (nx == 0 && nz == 0)
                                continue;
                            if (IsValidStandingCell(sx + nx, standY - 1, sz + nz))
                                openNeighbors++;
                        }

                        Vector3 candidate = new Vector3(sx + 0.5f, standY, sz + 0.5f);
                        float score = Vector3.DistanceSquared(m_body.Position, candidate)
                            + Math.Abs(candidate.Y - m_body.Position.Y) * 4f
                            + (8 - openNeighbors) * 2f
                            + (11 - radius) * 0.75f;
                        if (!found || score < bestScore)
                        {
                            bestScore = score;
                            approach = candidate;
                            found = true;
                        }
                    }
                }
            }
            return found;
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
            if (m_pathfinding.Destination.HasValue
                && Vector3.Distance(m_body.Position, m_pathfinding.Destination.Value) > 1.25f
                && !m_pathfinding.IsStuck
                && m_managedNoProgressTime < 10.0)
                return;

            // Patrol has no persistent work target to preserve. If its current
            // destination is blocked or has stopped getting closer, choose a new
            // patrol point instead of waiting forever on the old destination.

            int cx = m_hasHomeDoor ? m_homeDoor.X : Terrain.ToCell(m_body.Position.X);
            int cz = m_hasHomeDoor ? m_homeDoor.Z : Terrain.ToCell(m_body.Position.Z);
            Vector3 destination = Vector3.Zero;
            float bestScore = float.NegativeInfinity;
            bool found = false;

            // Native WalkAroundBehavior samples many positions and scores them.
            // Do the same here, but reject holes, walls and cells without headroom.
            for (int attempt = 0; attempt < 40; attempt++)
            {
                float angle = (float)(s_random.NextDouble() * Math.PI * 2.0);
                float radius = minRadius + (float)s_random.NextDouble() * (maxRadius - minRadius);
                int x = cx + Terrain.ToCell(MathF.Cos(angle) * radius);
                int z = cz + Terrain.ToCell(MathF.Sin(angle) * radius);
                int y = m_terrain.Terrain.GetTopHeight(x, z);
                if (!IsValidStandingCell(x, y, z))
                    continue;

                Vector3 candidate = new Vector3(x + 0.5f, y + 1f, z + 0.5f);
                float score = 100f - Vector3.DistanceSquared(m_body.Position, candidate)
                    - Math.Abs(candidate.Y - m_body.Position.Y) * 4f;
                if (!found || score > bestScore)
                {
                    found = true;
                    bestScore = score;
                    destination = candidate;
                }
            }

            if (found)
                SetManagedDestination(destination, 2.2f, 1.1f, 900, true, false, false, null);
            else
                StopManagedPath();
            m_patrolPause = 0.25 + s_random.NextDouble() * 0.65;
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
