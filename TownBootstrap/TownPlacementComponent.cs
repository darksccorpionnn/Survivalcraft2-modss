using System;
using System.Reflection;
using System.Collections.Generic;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game
{
    /// <summary>
    /// Procedural town prototype for Survivalcraft API 1.9.5.1.
    /// It searches for a flat, clear site and only places blocks into air.
    /// </summary>
    public sealed class TownPlacementComponent : Component, IUpdateable
    {
        private static Project s_activeProject;
        private static readonly HashSet<string> s_processedCells = new HashSet<string>();
        private ComponentPlayer m_componentPlayer;
        private Palette m_palette;
        private float m_retryTimer;
        private int m_attemptCount;
        private bool m_runtimeLogged;


        private sealed class Palette
        {
            public int Stone;
            public int Cobble;
            public int Planks;
            public int Log;
            public int Glass;
            public int Door;
            public int Chest;
            public int Torch;
            public int Carpet;
            public int Stairs;
            public int Water;
            public int Soil;
            public int Fence;
            public int Workbench;
        }

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap)
        {
            base.Load(valuesDictionary, idToEntityMap);
            m_componentPlayer = Entity.FindComponent<ComponentPlayer>(true);
            Log.Information("[TownBootstrap] Town generator loaded.");
        }

        public void Update(float dt)
        {
            Project project = GameManager.Project;
            if (project == null)
                return;

            if (s_activeProject != project)
            {
                s_activeProject = project;
                s_processedCells.Clear();
                Log.Information("[TownBootstrap] New world session detected; 256-block town grid enabled.");
            }

            if (m_componentPlayer == null)
                m_componentPlayer = Entity.FindComponent<ComponentPlayer>(true);
            if (m_componentPlayer == null)
                return;

            m_retryTimer += dt;
            if (m_retryTimer < 2f)
                return;
            m_retryTimer = 0f;

            SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>();
            if (terrain == null)
                return;

            try
            {
                Vector3 playerPosition = m_componentPlayer.ComponentBody.Position;
                int playerX = (int)Math.Floor(playerPosition.X);
                int playerY = (int)Math.Floor(playerPosition.Y);
                int playerZ = (int)Math.Floor(playerPosition.Z);

                // One town per 256x256 world-grid cell. Towns are centred on each cell,
                // so neighbouring town centres are exactly 256 blocks apart.
                // Grid nodes are multiples of 256. This puts the first town at/near
                // the usual world origin instead of leaving the spawn stranded 181 blocks
                // from the centre of cell (0,0).
                int cellX = (int)Math.Floor((playerX + 128.0) / 256.0);
                int cellZ = (int)Math.Floor((playerZ + 128.0) / 256.0);
                string cellKey = cellX + ":" + cellZ;
                if (s_processedCells.Contains(cellKey))
                    return;

                int anchorX = cellX * 256;
                int anchorZ = cellZ * 256;
                int dx = playerX - anchorX;
                int dz = playerZ - anchorZ;

                // Generate only when near the selected grid node, where its terrain is loaded.
                if (dx * dx + dz * dz > 112 * 112)
                    return;

                if (!m_runtimeLogged)
                {
                    Log.Information("[TownBootstrap] Runtime active. Player=" + playerX + "," + playerY + "," + playerZ +
                        "; grid cell=" + cellKey + "; town anchor=" + anchorX + "," + anchorZ + ".");
                    m_runtimeLogged = true;
                }

                if (HasTownMarker(terrain, anchorX, playerY, anchorZ))
                {
                    Log.Information("[TownBootstrap] Town already exists in grid cell " + cellKey + ".");
                    s_processedCells.Add(cellKey);
                    return;
                }

                if (m_palette == null)
                    m_palette = ResolvePalette();

                if (GenerateTown(terrain, anchorX, playerY, anchorZ, m_palette))
                {
                    s_processedCells.Add(cellKey);
                    return;
                }

                m_attemptCount++;
                if (m_attemptCount == 1 || m_attemptCount % 5 == 0)
                    Log.Information("[TownBootstrap] Cell " + cellKey + " has no safe site yet; retrying.");
            }
            catch (Exception ex)
            {
                Log.Information("[TownBootstrap] Town generation attempt failed and will retry: " + ex);
            }
        }

        private static bool GenerateTown(SubsystemTerrain terrain, int anchorX, int playerY, int anchorZ, Palette palette)
        {
            int playerX = anchorX;
            int playerZ = anchorZ;

            if (palette == null || palette.Stone <= 0 || palette.Planks <= 0 || palette.Log <= 0 || palette.Cobble <= 0)
            {
                Log.Information("[TownBootstrap] Required stone/wood blocks could not be resolved. No terrain was changed.");
                return false;
            }

            int townX, townZ, baseY;
            if (!TryFindVillageSite(terrain, playerX, playerY, playerZ, out townX, out townZ, out baseY))
            {
                Log.Information("[TownBootstrap] No suitable site was found in this pass. No terrain was changed.");
                return false;
            }

            int placed = 0;

            // A compact 41x41 village: six two-storey homes around a central square.
            BuildRoads(terrain, palette, townX, townZ, baseY, ref placed);

            BuildHouse(terrain, palette, townX - 14, townZ - 14, baseY, 1, 0, ref placed); // Northwest cottage
            BuildHouse(terrain, palette, townX + 14, townZ - 14, baseY, 0, 0, ref placed); // Northeast cottage
            BuildHouse(terrain, palette, townX - 14, townZ + 14, baseY, 0, 1, ref placed); // Southwest cottage
            BuildHouse(terrain, palette, townX + 14, townZ + 14, baseY, 1, 1, ref placed); // Southeast cottage
            BuildHouse(terrain, palette, townX - 14, townZ, baseY, 2, 2, ref placed);      // Blacksmith
            BuildHouse(terrain, palette, townX + 14, townZ, baseY, 3, 3, ref placed);      // Inn / larger home

            BuildSmallGarden(terrain, palette, townX, townZ, baseY, ref placed);
            BuildCentralSquare(terrain, palette, townX, townZ, baseY, ref placed);
            BuildTownLights(terrain, palette, townX, townZ, baseY, ref placed);

            Log.Information("[TownBootstrap] Village created at X=" + townX + ", Y=" + baseY + ", Z=" + townZ +
                ". Blocks placed: " + placed + ". Six two-storey houses, central well, market and paths.");
            return placed > 0;
        }

        private static Palette ResolvePalette()
        {
            Palette p = new Palette();
            p.Stone = ResolveBlockIndex("granite", "GraniteBlock", "StoneBlock");
            p.Cobble = ResolveBlockIndex("cobblestone", "CobblestoneBlock", "RoughStoneBlock");
            p.Planks = ResolveBlockIndex("planks", "PlanksBlock", "WoodPlanksBlock");
            p.Log = ResolveBlockIndex("oak log", "OakWoodBlock", "WoodBlock", "Log1Block", "OakLogBlock");
            p.Glass = ResolveBlockIndex("glass", "GlassBlock");
            p.Door = ResolveBlockIndex("wooden door", "WoodenDoorBlock", "WoodDoorBlock");
            p.Chest = ResolveBlockIndex("chest", "ChestBlock");
            p.Torch = ResolveBlockIndex("torch", "TorchBlock");
            p.Carpet = ResolveBlockIndex("carpet", "CarpetBlock");
            p.Stairs = ResolveBlockIndex("wooden stairs", "WoodenStairsBlock", "WoodStairsBlock");
            p.Water = ResolveBlockIndex("water", "WaterBlock");
            p.Soil = ResolveBlockIndex("soil", "SoilBlock", "FarmlandBlock");
            p.Fence = ResolveBlockIndex("wooden fence", "WoodenFenceBlock", "FenceBlock");
            p.Workbench = ResolveBlockIndex("workbench", "CraftingTableBlock", "WorkbenchBlock");

            // Vanilla Survivalcraft 2 content IDs. Keep these fallbacks because some
            // API builds rename concrete block classes, causing reflection to return 0.
            if (p.Stone <= 0) p.Stone = 1;     // Granite
            if (p.Cobble <= 0) p.Cobble = 6;   // Cobblestone
            if (p.Planks <= 0) p.Planks = 7;   // Planks
            if (p.Log <= 0) p.Log = 8;         // Oak log
            if (p.Glass <= 0) p.Glass = 15;    // Glass
            if (p.Water <= 0) p.Water = 12;    // Water
            if (p.Torch <= 0) p.Torch = 29;    // Torch
            if (p.Workbench <= 0) p.Workbench = 24; // Workbench

            Log.Information("[TownBootstrap] Palette: stone=" + p.Stone + ", cobble=" + p.Cobble +
                ", planks=" + p.Planks + ", log=" + p.Log + ", glass=" + p.Glass +
                ", torch=" + p.Torch + ", chest=" + p.Chest + ", door=" + p.Door + ".");
            return p;
        }

        /// <summary>
        /// Uses the API's own block registry rather than guessing numeric content IDs.
        /// Reflection keeps this mod compatible with API builds where concrete block types differ.
        /// </summary>
        private static int ResolveBlockIndex(string label, params string[] typeNames)
        {
            MethodInfo getIndex = null;
            MethodInfo[] methods = typeof(BlocksManager).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (candidate.Name == "GetBlockIndex" && candidate.IsGenericMethodDefinition &&
                    candidate.GetGenericArguments().Length == 1 && candidate.GetParameters().Length <= 1)
                {
                    getIndex = candidate;
                    break;
                }
            }

            if (getIndex == null)
            {
                Log.Information("[TownBootstrap] BlocksManager.GetBlockIndex was not found for " + label + ".");
                return 0;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int a = 0; a < assemblies.Length; a++)
            {
                Assembly assembly = assemblies[a];
                for (int n = 0; n < typeNames.Length; n++)
                {
                    Type blockType = assembly.GetType("Game." + typeNames[n], false) ??
                                     assembly.GetType(typeNames[n], false);
                    if (blockType == null)
                        continue;

                    try
                    {
                        MethodInfo closed = getIndex.MakeGenericMethod(blockType);
                        ParameterInfo[] parameters = closed.GetParameters();
                        object[] args = parameters.Length == 0 ? null : new object[] { true };
                        object result = closed.Invoke(null, args);
                        int index = Convert.ToInt32(result);
                        if (index > 0)
                        {
                            Log.Information("[TownBootstrap] Resolved " + label + " as " + blockType.FullName + " (content " + index + ").");
                            return index;
                        }
                    }
                    catch (Exception)
                    {
                        // Try the next compatible class name.
                    }
                }
            }

            Log.Information("[TownBootstrap] Optional block not resolved: " + label + ".");
            return 0;
        }

        private static bool TryFindVillageSite(
            SubsystemTerrain subsystemTerrain, int playerX, int playerY, int playerZ,
            out int centerX, out int centerZ, out int baseY)
        {
            // Candidate sites stay near the 256-block grid-cell centre.
            int[,] offsets = new int[,]
            {
                { 0, 0 }, { 16, 0 }, { -16, 0 }, { 0, 16 }, { 0, -16 },
                { 24, 24 }, { -24, 24 }, { 24, -24 }, { -24, -24 },
                { 32, 0 }, { -32, 0 }, { 0, 32 }, { 0, -32 },
                { 32, 16 }, { -32, 16 }, { 32, -16 }, { -32, -16 }
            };

            for (int i = 0; i < offsets.GetLength(0); i++)
            {
                int x = playerX + offsets[i, 0];
                int z = playerZ + offsets[i, 1];
                int candidateY;

                if (IsSafeFlatSite(subsystemTerrain, x, z, playerY, out candidateY))
                {
                    centerX = x;
                    centerZ = z;
                    baseY = candidateY;
                    return true;
                }
            }

            // Last-resort natural-ground search: don't reject an otherwise usable
            // location just because a tree or a small obstruction intersects a house pad.
            // Place() still writes only into air, so existing terrain/buildings are preserved.
            int[,] groundChecks = new int[,]
            {
                { 0, 0 }, { -14, -14 }, { 14, -14 }, { -14, 14 }, { 14, 14 },
                { -14, 0 }, { 14, 0 }
            };
            for (int i = 0; i < offsets.GetLength(0); i++)
            {
                int x = playerX + offsets[i, 0];
                int z = playerZ + offsets[i, 1];
                int minGround = 255;
                int maxGround = 0;
                int found = 0;
                for (int k = 0; k < groundChecks.GetLength(0); k++)
                {
                    int surface;
                    if (!TryFindSurfaceY(subsystemTerrain,
                        x + groundChecks[k, 0], z + groundChecks[k, 1], playerY, out surface))
                        continue;
                    found++;
                    if (surface < minGround) minGround = surface;
                    if (surface > maxGround) maxGround = surface;
                }
                if (found >= 5 && maxGround - minGround <= 24 && maxGround < 238)
                {
                    centerX = x;
                    centerZ = z;
                    baseY = maxGround;
                    Log.Information("[TownBootstrap] Using relaxed terrain site at X=" + x +
                        ", Z=" + z + ", surface range=" + minGround + "-" + maxGround + ".");
                    return true;
                }
            }

            centerX = centerZ = baseY = 0;
            return false;
        }

        private static bool IsSafeFlatSite(SubsystemTerrain terrain, int centerX, int centerZ, int scanY, out int baseY)
        {
            int minSurface = 255;
            int maxSurface = 0;

            // Check the entire footprint in a two-block grid for height consistency.
            for (int x = -20; x <= 20; x += 4)
            {
                for (int z = -20; z <= 20; z += 4)
                {
                    int surface;
                    if (!TryFindSurfaceY(terrain, centerX + x, centerZ + z, scanY, out surface))
                    {
                        baseY = 0;
                        return false;
                    }
                    if (surface < minSurface) minSurface = surface;
                    if (surface > maxSurface) maxSurface = surface;
                }
            }

            // Natural terrain rarely stays perfectly level over 41x41 blocks.
            // Allow a gentle slope; structures are elevated to the highest sampled ground.
            if (maxSurface - minSurface > 18 || maxSurface >= 238)
            {
                baseY = 0;
                return false;
            }

            // Check the actual house footprints and square, not every tree in the whole 41x41 area.
            // A blocked site is skipped; the placement routine itself still writes only into air.
            int[,] footprints = new int[,]
            {
                { -14, -14 }, { 14, -14 }, { -14, 14 }, { 14, 14 },
                { -14, 0 }, { 14, 0 }, { 0, 0 }
            };
            for (int i = 0; i < footprints.GetLength(0); i++)
            {
                int half = i == 6 ? 5 : 4;
                for (int x = -half; x <= half; x += 2)
                {
                    for (int z = -half; z <= half; z += 2)
                    {
                        for (int y = maxSurface + 1; y <= maxSurface + 13; y++)
                        {
                            int value = terrain.Terrain.GetCellValue(
                                centerX + footprints[i, 0] + x, y,
                                centerZ + footprints[i, 1] + z);
                            if (Terrain.ExtractContents(value) != 0)
                            {
                                baseY = 0;
                                return false;
                            }
                        }
                    }
                }
            }

            baseY = maxSurface;
            return true;
        }

        private static bool TryFindSurfaceY(
            SubsystemTerrain terrain, int x, int z, int scanY, out int surfaceY)
        {
            int minY = Math.Max(1, scanY - 64);
            // Search above and below the player: grid nodes can be on a different elevation.
            int maxY = Math.Min(254, scanY + 64);

            for (int y = maxY; y >= minY; y--)
            {
                int value = terrain.Terrain.GetCellValue(x, y, z);
                if (Terrain.ExtractContents(value) != 0)
                {
                    surfaceY = y;
                    return true;
                }
            }

            surfaceY = 0;
            return false;
        }

        private static bool HasTownMarker(SubsystemTerrain terrain, int anchorX, int playerY, int anchorZ)
        {
            // Search the central 112x112 area of this grid cell for the well's granite marker.
            int minY = Math.Max(1, playerY - 48);
            int maxY = Math.Min(248, playerY + 24);
            for (int x = anchorX - 56; x <= anchorX + 56; x++)
            {
                for (int z = anchorZ - 56; z <= anchorZ + 56; z++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        int a = Terrain.ExtractContents(terrain.Terrain.GetCellValue(x, y, z));
                        if (a != 1) continue;
                        int b = Terrain.ExtractContents(terrain.Terrain.GetCellValue(x, y + 1, z));
                        int c = Terrain.ExtractContents(terrain.Terrain.GetCellValue(x, y + 2, z));
                        int d = Terrain.ExtractContents(terrain.Terrain.GetCellValue(x, y + 3, z));
                        if (b == 1 && c == 1 && d == 1)
                            return true;
                    }
                }
            }
            return false;
        }

        private static bool Place(SubsystemTerrain terrain, int x, int y, int z, int contents, ref int placed)
        {
            if (contents <= 0 || y < 1 || y >= 255)
                return false;
            if (Terrain.ExtractContents(terrain.Terrain.GetCellValue(x, y, z)) != 0)
                return false;

            terrain.ChangeCell(x, y, z, Terrain.MakeBlockValue(contents));
            placed++;
            return true;
        }

        private static void FillSupport(SubsystemTerrain terrain, Palette p, int x, int z, int baseY, ref int placed)
        {
            int surface;
            if (!TryFindSurfaceY(terrain, x, z, baseY + 2, out surface))
                return;

            for (int y = surface + 1; y <= baseY + 1; y++)
                Place(terrain, x, y, z, p.Cobble, ref placed);
        }

        private static void BuildRoads(SubsystemTerrain terrain, Palette p, int cx, int cz, int baseY, ref int placed)
        {
            // Four diagonal approaches lead from the corner homes into the square.
            for (int z = -10; z <= -7; z++)
                for (int dx = -1; dx <= 1; dx++)
                    Place(terrain, cx - 14 + dx, baseY + 1, cz + z, p.Cobble, ref placed);
            for (int x = -14; x <= -7; x++)
                for (int dz = -1; dz <= 1; dz++)
                    Place(terrain, cx + x, baseY + 1, cz - 7 + dz, p.Cobble, ref placed);
            for (int z = -7; z <= -5; z++)
                for (int dx = -1; dx <= 1; dx++)
                    Place(terrain, cx - 7 + dx, baseY + 1, cz + z, p.Cobble, ref placed);

            for (int z = -10; z <= -7; z++)
                for (int dx = -1; dx <= 1; dx++)
                    Place(terrain, cx + 14 + dx, baseY + 1, cz + z, p.Cobble, ref placed);
            for (int x = 7; x <= 14; x++)
                for (int dz = -1; dz <= 1; dz++)
                    Place(terrain, cx + x, baseY + 1, cz - 7 + dz, p.Cobble, ref placed);
            for (int z = -7; z <= -5; z++)
                for (int dx = -1; dx <= 1; dx++)
                    Place(terrain, cx + 7 + dx, baseY + 1, cz + z, p.Cobble, ref placed);

            for (int z = 7; z <= 10; z++)
                for (int dx = -1; dx <= 1; dx++)
                    Place(terrain, cx - 14 + dx, baseY + 1, cz + z, p.Cobble, ref placed);
            for (int x = -14; x <= -7; x++)
                for (int dz = -1; dz <= 1; dz++)
                    Place(terrain, cx + x, baseY + 1, cz + 7 + dz, p.Cobble, ref placed);
            for (int z = 5; z <= 7; z++)
                for (int dx = -1; dx <= 1; dx++)
                    Place(terrain, cx - 7 + dx, baseY + 1, cz + z, p.Cobble, ref placed);

            for (int z = 7; z <= 10; z++)
                for (int dx = -1; dx <= 1; dx++)
                    Place(terrain, cx + 14 + dx, baseY + 1, cz + z, p.Cobble, ref placed);
            for (int x = 7; x <= 14; x++)
                for (int dz = -1; dz <= 1; dz++)
                    Place(terrain, cx + x, baseY + 1, cz + 7 + dz, p.Cobble, ref placed);
            for (int z = 5; z <= 7; z++)
                for (int dx = -1; dx <= 1; dx++)
                    Place(terrain, cx + 7 + dx, baseY + 1, cz + z, p.Cobble, ref placed);

            // Short east/west paths connect the two side homes without running through them.
            for (int x = -10; x <= -6; x++)
                for (int dz = -1; dz <= 1; dz++)
                    Place(terrain, cx + x, baseY + 1, cz + dz, p.Cobble, ref placed);
            for (int x = 6; x <= 10; x++)
                for (int dz = -1; dz <= 1; dz++)
                    Place(terrain, cx + x, baseY + 1, cz + dz, p.Cobble, ref placed);
        }

        // frontSide: 0 = south (+Z), 1 = north (-Z), 2 = east (+X), 3 = west (-X)
        private static void BuildHouse(
            SubsystemTerrain terrain, Palette p, int cx, int cz, int baseY,
            int style, int frontSide, ref int placed)
        {
            // Raised stone plinth and wooden ground floor.
            for (int dx = -4; dx <= 4; dx++)
            {
                for (int dz = -4; dz <= 4; dz++)
                {
                    FillSupport(terrain, p, cx + dx, cz + dz, baseY, ref placed);
                    int floor = (Math.Abs(dx) == 4 || Math.Abs(dz) == 4) ? p.Cobble : p.Planks;
                    Place(terrain, cx + dx, baseY + 1, cz + dz, floor, ref placed);
                }
            }

            // First-storey walls: stone lower course, warm timber above.
            for (int y = baseY + 2; y <= baseY + 4; y++)
            {
                for (int side = -4; side <= 4; side++)
                {
                    PlaceWallCell(terrain, p, cx, cz, baseY, style, frontSide, side, y, 0, ref placed);
                    PlaceWallCell(terrain, p, cx, cz, baseY, style, frontSide, side, y, 1, ref placed);
                }
            }

            // Corner timber posts emphasize the half-timbered frame.
            for (int y = baseY + 2; y <= baseY + 8; y++)
            {
                Place(terrain, cx - 4, y, cz - 4, p.Log, ref placed);
                Place(terrain, cx + 4, y, cz - 4, p.Log, ref placed);
                Place(terrain, cx - 4, y, cz + 4, p.Log, ref placed);
                Place(terrain, cx + 4, y, cz + 4, p.Log, ref placed);
            }

            // Second-storey floor, with an opening for the internal staircase.
            for (int dx = -3; dx <= 3; dx++)
            {
                for (int dz = -3; dz <= 3; dz++)
                {
                    if (dx == -1 && dz == 2) continue;
                    Place(terrain, cx + dx, baseY + 5, cz + dz, p.Planks, ref placed);
                }
            }

            // Second-storey walls.
            for (int y = baseY + 6; y <= baseY + 8; y++)
            {
                for (int side = -4; side <= 4; side++)
                {
                    PlaceWallCell(terrain, p, cx, cz, baseY, style, frontSide, side, y, 2, ref placed);
                    PlaceWallCell(terrain, p, cx, cz, baseY, style, frontSide, side, y, 3, ref placed);
                }
            }

            // Gable ends beneath the pitched timber roof.
            for (int h = 0; h <= 3; h++)
            {
                for (int dx = -h; dx <= h; dx++)
                {
                    Place(terrain, cx + dx, baseY + 9 + h, cz - 4, p.Planks, ref placed);
                    Place(terrain, cx + dx, baseY + 9 + h, cz + 4, p.Planks, ref placed);
                }
            }

            // Pitched roof: the two slopes meet in a long ridge.
            for (int dx = -4; dx <= 4; dx++)
            {
                int roofY = baseY + 9 + (4 - Math.Abs(dx));
                for (int dz = -5; dz <= 5; dz++)
                    Place(terrain, cx + dx, roofY, cz + dz, p.Planks, ref placed);
            }

            // Front door, two blocks high. A missing door class leaves a clean open doorway.
            int doorX = cx;
            int doorZ = cz;
            if (frontSide == 0) doorZ = cz + 4;
            else if (frontSide == 1) doorZ = cz - 4;
            else if (frontSide == 2) doorX = cx + 4;
            else doorX = cx - 4;

            if (p.Door > 0)
            {
                Place(terrain, doorX, baseY + 2, doorZ, p.Door, ref placed);
                Place(terrain, doorX, baseY + 3, doorZ, p.Door, ref placed);
            }

            // Glass windows on all facades; gaps are left clear if glass is unavailable.
            if (p.Glass > 0)
            {
                PlaceWindow(terrain, p, cx - 2, baseY + 3, cz - 4, ref placed);
                PlaceWindow(terrain, p, cx + 2, baseY + 3, cz - 4, ref placed);
                PlaceWindow(terrain, p, cx - 2, baseY + 3, cz + 4, ref placed);
                PlaceWindow(terrain, p, cx + 2, baseY + 3, cz + 4, ref placed);
                PlaceWindow(terrain, p, cx - 4, baseY + 3, cz - 2, ref placed);
                PlaceWindow(terrain, p, cx - 4, baseY + 3, cz + 2, ref placed);
                PlaceWindow(terrain, p, cx + 4, baseY + 3, cz - 2, ref placed);
                PlaceWindow(terrain, p, cx + 4, baseY + 3, cz + 2, ref placed);

                PlaceWindow(terrain, p, cx - 2, baseY + 7, cz - 4, ref placed);
                PlaceWindow(terrain, p, cx + 2, baseY + 7, cz - 4, ref placed);
                PlaceWindow(terrain, p, cx - 2, baseY + 7, cz + 4, ref placed);
                PlaceWindow(terrain, p, cx + 2, baseY + 7, cz + 4, ref placed);
            }

            // Warm indoor carpets on both floors.
            if (p.Carpet > 0)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        Place(terrain, cx + dx, baseY + 2, cz + dz, p.Carpet, ref placed);
                        Place(terrain, cx + dx, baseY + 6, cz + dz, p.Carpet, ref placed);
                    }
                }
            }

            // Interior storage and a small work corner on the ground floor.
            Place(terrain, cx - 2, baseY + 2, cz - 2, p.Chest, ref placed);
            Place(terrain, cx + 2, baseY + 2, cz - 2, p.Workbench, ref placed);
            Place(terrain, cx - 2, baseY + 6, cz - 2, p.Chest, ref placed);

            // Floor-mounted torches keep the rooms lit without replacing wall blocks.
            Place(terrain, cx - 3, baseY + 2, cz + 2, p.Torch, ref placed);
            Place(terrain, cx + 3, baseY + 2, cz + 2, p.Torch, ref placed);
            Place(terrain, cx - 3, baseY + 6, cz + 2, p.Torch, ref placed);
            Place(terrain, cx + 3, baseY + 6, cz + 2, p.Torch, ref placed);

            // A compact internal stair run. Wooden stair blocks are preferred; planks are a safe fallback.
            int stairBlock = p.Stairs > 0 ? p.Stairs : p.Planks;
            for (int step = 0; step < 4; step++)
                Place(terrain, cx + 2 - step, baseY + 2 + step, cz + 2, stairBlock, ref placed);

            // The blacksmith receives a stone chimney and extra stonework.
            if (style == 2)
            {
                for (int y = baseY + 9; y <= baseY + 14; y++)
                {
                    Place(terrain, cx + 3, y, cz - 3, p.Cobble, ref placed);
                    Place(terrain, cx + 3, y, cz - 2, p.Cobble, ref placed);
                }
                Place(terrain, cx + 2, baseY + 2, cz - 3, p.Stone, ref placed);
            }

            // A modest inn sign-like timber frame and extra upper-floor detail.
            if (style == 3)
            {
                Place(terrain, cx - 2, baseY + 9, cz + 5, p.Log, ref placed);
                Place(terrain, cx + 2, baseY + 9, cz + 5, p.Log, ref placed);
                Place(terrain, cx, baseY + 10, cz + 5, p.Planks, ref placed);
            }
        }

        private static void PlaceWallCell(
            SubsystemTerrain terrain, Palette p, int cx, int cz, int baseY,
            int style, int frontSide, int side, int y, int wallGroup, ref int placed)
        {
            // wallGroup identifies the north/south or east/west wall pair.
            int x, z;
            if (wallGroup == 0) { x = cx + side; z = cz - 4; }
            else if (wallGroup == 1) { x = cx + side; z = cz + 4; }
            else if (wallGroup == 2) { x = cx - 4; z = cz + side; }
            else { x = cx + 4; z = cz + side; }

            bool isFrontWall = (frontSide == 0 && wallGroup == 1) ||
                               (frontSide == 1 && wallGroup == 0) ||
                               (frontSide == 2 && wallGroup == 3) ||
                               (frontSide == 3 && wallGroup == 2);

            bool doorOpening = isFrontWall && side == 0 && y <= baseY + 3;
            if (doorOpening)
                return;

            // Openings for ground-floor windows.
            bool windowPosition = (y == baseY + 3 || y == baseY + 7) && (Math.Abs(side) == 2);
            bool windowOnSide = wallGroup >= 2 && (y == baseY + 3 || y == baseY + 7) && (Math.Abs(side) == 2);
            if (windowPosition || windowOnSide)
            {
                if (p.Glass > 0)
                    Place(terrain, x, y, z, p.Glass, ref placed);
                return;
            }

            int wallContents = (y == baseY + 2) ? p.Cobble : p.Planks;
            if (style == 2 && y <= baseY + 3)
                wallContents = p.Cobble;
            Place(terrain, x, y, z, wallContents, ref placed);
        }

        private static void PlaceWindow(SubsystemTerrain terrain, Palette p, int x, int y, int z, ref int placed)
        {
            if (p.Glass > 0)
            {
                // Only air can be replaced; the window helper never cuts through a built wall.
                if (Terrain.ExtractContents(terrain.Terrain.GetCellValue(x, y, z)) != 0)
                    return;
                Place(terrain, x, y, z, p.Glass, ref placed);
            }
        }

        private static void BuildCentralSquare(
            SubsystemTerrain terrain, Palette p, int cx, int cz, int baseY, ref int placed)
        {
            // 11x11 paved square.
            for (int dx = -5; dx <= 5; dx++)
            {
                for (int dz = -5; dz <= 5; dz++)
                    Place(terrain, cx + dx, baseY + 1, cz + dz, p.Cobble, ref placed);
            }

            // Stone well with a four-block centre column that also acts as a one-town marker.
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = -2; dz <= 2; dz++)
                {
                    if (Math.Abs(dx) == 2 || Math.Abs(dz) == 2)
                    {
                        Place(terrain, cx + dx, baseY + 2, cz + dz, p.Cobble, ref placed);
                        Place(terrain, cx + dx, baseY + 3, cz + dz, p.Cobble, ref placed);
                    }
                }
            }

            for (int y = baseY + 2; y <= baseY + 5; y++)
                Place(terrain, cx, y, cz, p.Stone, ref placed);

            if (p.Water > 0)
            {
                Place(terrain, cx + 1, baseY + 2, cz, p.Water, ref placed);
                Place(terrain, cx - 1, baseY + 2, cz, p.Water, ref placed);
                Place(terrain, cx, baseY + 2, cz + 1, p.Water, ref placed);
                Place(terrain, cx, baseY + 2, cz - 1, p.Water, ref placed);
            }

            // Market stall: timber posts, counter and a broad wooden canopy.
            for (int dx = 2; dx <= 4; dx += 2)
            {
                for (int dz = -4; dz <= -2; dz += 2)
                {
                    Place(terrain, cx + dx, baseY + 2, cz + dz, p.Log, ref placed);
                    Place(terrain, cx + dx, baseY + 3, cz + dz, p.Log, ref placed);
                }
            }
            for (int dx = 2; dx <= 4; dx++)
            {
                Place(terrain, cx + dx, baseY + 2, cz - 3, p.Planks, ref placed);
                Place(terrain, cx + dx, baseY + 4, cz - 4, p.Planks, ref placed);
                Place(terrain, cx + dx, baseY + 4, cz - 3, p.Planks, ref placed);
                Place(terrain, cx + dx, baseY + 4, cz - 2, p.Planks, ref placed);
            }
            Place(terrain, cx + 3, baseY + 2, cz - 2, p.Chest, ref placed);
            Place(terrain, cx + 3, baseY + 2, cz - 4, p.Chest, ref placed);

            // Short benches around the well.
            Place(terrain, cx - 4, baseY + 2, cz, p.Planks, ref placed);
            Place(terrain, cx + 4, baseY + 2, cz, p.Planks, ref placed);
            Place(terrain, cx, baseY + 2, cz - 4, p.Planks, ref placed);
            Place(terrain, cx, baseY + 2, cz + 4, p.Planks, ref placed);
        }

        private static void BuildTownLights(
            SubsystemTerrain terrain, Palette p, int cx, int cz, int baseY, ref int placed)
        {
            int[,] positions = new int[,] { { -5, -5 }, { 5, -5 }, { -5, 5 }, { 5, 5 } };
            for (int i = 0; i < positions.GetLength(0); i++)
            {
                int x = cx + positions[i, 0];
                int z = cz + positions[i, 1];
                Place(terrain, x, baseY + 2, z, p.Log, ref placed);
                Place(terrain, x, baseY + 3, z, p.Log, ref placed);
                Place(terrain, x, baseY + 4, z, p.Log, ref placed);
                if (p.Torch > 0)
                    Place(terrain, x, baseY + 5, z, p.Torch, ref placed);
            }
        }

        private static void BuildSmallGarden(
            SubsystemTerrain terrain, Palette p, int cx, int cz, int baseY, ref int placed)
        {
            // A small fenced planting bed on the south side of the square.
            for (int dx = -5; dx <= -3; dx++)
            {
                for (int dz = 2; dz <= 4; dz++)
                {
                    if (p.Soil > 0)
                        Place(terrain, cx + dx, baseY + 1, cz + dz, p.Soil, ref placed);
                    else
                        Place(terrain, cx + dx, baseY + 1, cz + dz, p.Cobble, ref placed);
                }
            }

            if (p.Fence > 0)
            {
                for (int dx = -5; dx <= -3; dx++)
                {
                    Place(terrain, cx + dx, baseY + 2, cz + 1, p.Fence, ref placed);
                    Place(terrain, cx + dx, baseY + 2, cz + 5, p.Fence, ref placed);
                }
                for (int dz = 2; dz <= 4; dz++)
                {
                    Place(terrain, cx - 6, baseY + 2, cz + dz, p.Fence, ref placed);
                    Place(terrain, cx - 2, baseY + 2, cz + dz, p.Fence, ref placed);
                }
            }
        }
    }
}
