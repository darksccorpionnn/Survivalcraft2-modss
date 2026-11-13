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
            public int SpruceWood;
            public int Planks;
            public int Cobblestone;
            public int FramedGlass;
            public int WoodenStairs;
            public int WoodenDoor;
            public int WickerLamp;
            public int Water;
            public HashSet<int> NonGroundContents = new HashSet<int>();
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

                // One town per 256x256 world-grid cell. Generate around the player's
                // actual position the first time that cell is visited. Using a fixed
                // cell centre can point at unloaded terrain or keep spawn outside range.
                int cellX = (int)Math.Floor((playerX + 128.0) / 256.0);
                int cellZ = (int)Math.Floor((playerZ + 128.0) / 256.0);
                string cellKey = cellX + ":" + cellZ;
                if (s_processedCells.Contains(cellKey))
                    return;

                if (!m_runtimeLogged)
                {
                    Log.Information("[TownBootstrap] Runtime active. Player=" + playerX + "," + playerY + "," + playerZ +
                        "; grid cell=" + cellKey + "; generator will search around the player's loaded area.");
                    m_runtimeLogged = true;
                }

                if (m_palette == null)
                    m_palette = ResolvePalette();

                if (!IsPaletteReady(m_palette))
                    return;

                if (HasTownMarker(terrain, playerX, playerY, playerZ, m_palette.SpruceWood))
                {
                    Log.Information("[TownBootstrap] Town already exists in grid cell " + cellKey + ".");
                    s_processedCells.Add(cellKey);
                    return;
                }

                if (GenerateTown(terrain, playerX, playerY, playerZ, m_palette))
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

        public static bool GenerateFromEgg(SubsystemTerrain terrain, int x, int y, int z)
        {
            if (terrain == null)
                return false;

            Palette palette = ResolvePalette();
            if (!IsPaletteReady(palette))
                return false;

            int centerX, centerZ, baseY;
            if (!TryFindVillageSite(terrain, x, y, z, palette, out centerX, out centerZ, out baseY))
            {
                Log.Information("[TownBootstrap] Egg used, but no suitable ground site was found nearby.");
                return false;
            }

            return GenerateTown(terrain, centerX, baseY, centerZ, palette);
        }

        private static bool IsPaletteReady(Palette p)
        {
            if (p == null) return false;
            bool ready = p.SpruceWood > 0 && p.Planks > 0 && p.Cobblestone > 0 &&
                         p.FramedGlass > 0 && p.WoodenStairs > 0 &&
                         p.WoodenDoor > 0 && p.WickerLamp > 0;
            if (!ready)
                Log.Information("[TownBootstrap] Required village blocks are missing. SpruceWood=" + p.SpruceWood +
                    ", Planks=" + p.Planks + ", Cobblestone=" + p.Cobblestone +
                    ", FramedGlass=" + p.FramedGlass + ", WoodenStairs=" + p.WoodenStairs +
                    ", WoodenDoor=" + p.WoodenDoor + ", WickerLamp=" + p.WickerLamp +
                    ". Generation stopped; no stone/bedrock fallback will be used.");
            return ready;
        }

        private static bool GenerateTown(SubsystemTerrain terrain, int anchorX, int playerY, int anchorZ, Palette palette)
        {
            int townX = anchorX;
            int townZ = anchorZ;

            if (!IsPaletteReady(palette))
                return false;

            int baseY;
            if (!TryFindVillageSite(terrain, townX, playerY, townZ, palette, out townX, out townZ, out baseY))
            {
                Log.Information("[TownBootstrap] No near-level natural-ground site found near the player. No blocks placed; retrying.");
                return false;
            }

            int placed = 0;
            BuildRoads(terrain, palette, townX, townZ, baseY, ref placed);

            BuildHouse(terrain, palette, townX - 14, townZ - 14, baseY, 0, ref placed);
            BuildHouse(terrain, palette, townX + 14, townZ - 14, baseY, 1, ref placed);
            BuildHouse(terrain, palette, townX - 14, townZ + 14, baseY, 1, ref placed);
            BuildHouse(terrain, palette, townX + 14, townZ + 14, baseY, 0, ref placed);
            BuildHouse(terrain, palette, townX - 14, townZ, baseY, 2, ref placed);
            BuildHouse(terrain, palette, townX + 14, townZ, baseY, 3, ref placed);

            BuildCentralSquare(terrain, palette, townX, townZ, baseY, ref placed);
            BuildTownLights(terrain, palette, townX, townZ, baseY, ref placed);

            Log.Information("[TownBootstrap] Village created at X=" + townX + ", Y=" + baseY + ", Z=" + townZ +
                ". Blocks placed: " + placed + ". Palette: spruce wood, planks, cobblestone, framed glass, wooden stairs, wooden door and wicker lamp.");
            return placed > 0;
        }

        private static Palette ResolvePalette()
        {
            Palette p = new Palette();
            p.SpruceWood = ResolveBlockIndex("spruce wood", "SpruceWoodBlock");
            p.Planks = ResolveBlockIndex("planks", "PlanksBlock");
            p.Cobblestone = ResolveBlockIndex("cobblestone", "CobblestoneBlock");
            p.FramedGlass = ResolveBlockIndex("framed glass", "FramedGlassBlock");
            p.WoodenStairs = ResolveBlockIndex("wooden stairs", "WoodenStairsBlock");
            p.WoodenDoor = ResolveBlockIndex("wooden door", "WoodenDoorBlock");
            p.WickerLamp = ResolveBlockIndex("wicker lamp", "WickerLampBlock");
            p.Water = ResolveBlockIndex("water", "WaterBlock");

            // These contents are only excluded from terrain-height detection.
            // They are never used as building materials.
            AddNonGround(p, "SpruceWoodBlock", "OakWoodBlock", "BirchWoodBlock",
                "SpruceLeavesBlock", "OakLeavesBlock", "BirchLeavesBlock",
                "TallSpruceLeavesBlock", "LeavesBlock", "BedrockBlock",
                "TallGrassBlock", "GrassBlock", "FlowerBlock", "VillageEggBlock");

            Log.Information("[TownBootstrap] Exact palette: spruce wood=" + p.SpruceWood +
                ", planks=" + p.Planks + ", cobblestone=" + p.Cobblestone +
                ", framed glass=" + p.FramedGlass + ", wooden stairs=" + p.WoodenStairs +
                ", wooden door=" + p.WoodenDoor + ", wicker lamp=" + p.WickerLamp + ".");
            return p;
        }

        private static void AddNonGround(Palette p, params string[] typeNames)
        {
            for (int i = 0; i < typeNames.Length; i++)
            {
                int index = ResolveBlockIndex(typeNames[i], typeNames[i]);
                if (index > 0)
                    p.NonGroundContents.Add(index);
            }
        }

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
            SubsystemTerrain terrain, int playerX, int playerY, int playerZ, Palette p,
            out int centerX, out int centerZ, out int baseY)
        {
            int[,] offsets = new int[,]
            {
                { 0, 0 }, { 16, 0 }, { -16, 0 }, { 0, 16 }, { 0, -16 },
                { 24, 16 }, { -24, 16 }, { 24, -16 }, { -24, -16 },
                { 32, 0 }, { -32, 0 }, { 0, 32 }, { 0, -32 }
            };

            for (int i = 0; i < offsets.GetLength(0); i++)
            {
                int x = playerX + offsets[i, 0];
                int z = playerZ + offsets[i, 1];
                int candidateY;
                if (IsSafeFlatSite(terrain, x, z, playerY, p, out candidateY))
                {
                    centerX = x;
                    centerZ = z;
                    baseY = candidateY;
                    Log.Information("[TownBootstrap] Natural ground site selected at X=" + x +
                        ", Y=" + candidateY + ", Z=" + z + ".");
                    return true;
                }
            }

            centerX = centerZ = baseY = 0;
            return false;
        }

        private static bool IsSafeFlatSite(
            SubsystemTerrain terrain, int centerX, int centerZ, int playerY,
            Palette p, out int baseY)
        {
            int minSurface = 255;
            int maxSurface = 0;

            // Sample the full 41x41 village footprint. A site must be almost level;
            // never lift an entire village to match a distant tree or mountain top.
            for (int x = -20; x <= 20; x += 4)
            {
                for (int z = -20; z <= 20; z += 4)
                {
                    int surface;
                    if (!TryFindSurfaceY(terrain, centerX + x, centerZ + z, playerY, p, out surface))
                    {
                        baseY = 0;
                        return false;
                    }
                    if (surface < minSurface) minSurface = surface;
                    if (surface > maxSurface) maxSurface = surface;
                }
            }

            if (maxSurface - minSurface > 2 ||
                maxSurface >= 238 ||
                maxSurface > playerY + 12 ||
                minSurface < playerY - 48)
            {
                baseY = 0;
                return false;
            }

            // Reject trees, existing buildings and overhangs above all house footprints.
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
                        for (int y = maxSurface + 1; y <= maxSurface + 12; y++)
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
            SubsystemTerrain terrain, int x, int z, int playerY, Palette p, out int surfaceY)
        {
            int maxY = Math.Min(254, playerY + 24);
            int minY = Math.Max(1, playerY - 64);
            bool waterSeen = false;

            for (int y = maxY; y >= minY; y--)
            {
                int contents = Terrain.ExtractContents(terrain.Terrain.GetCellValue(x, y, z));
                if (contents == 0)
                    continue;

                if (p.Water > 0 && contents == p.Water)
                {
                    waterSeen = true;
                    continue;
                }

                // Ignore trees, leaves, grass, and bedrock. The first remaining solid
                // block is the actual ground surface rather than a canopy or world floor.
                if (p.NonGroundContents.Contains(contents))
                    continue;

                if (waterSeen)
                {
                    surfaceY = 0;
                    return false;
                }

                surfaceY = y;
                return true;
            }

            surfaceY = 0;
            return false;
        }

                private static bool HasTownMarker(
            SubsystemTerrain terrain, int searchX, int playerY, int searchZ, int markerContent)
        {
            int minY = Math.Max(1, playerY - 40);
            int maxY = Math.Min(248, playerY + 32);
            for (int x = searchX - 48; x <= searchX + 48; x++)
            {
                for (int z = searchZ - 48; z <= searchZ + 48; z++)
                {
                    for (int y = minY; y <= maxY - 3; y++)
                    {
                        int a = Terrain.ExtractContents(terrain.Terrain.GetCellValue(x, y, z));
                        if (a != markerContent) continue;
                        int b = Terrain.ExtractContents(terrain.Terrain.GetCellValue(x, y + 1, z));
                        int c = Terrain.ExtractContents(terrain.Terrain.GetCellValue(x, y + 2, z));
                        int d = Terrain.ExtractContents(terrain.Terrain.GetCellValue(x, y + 3, z));
                        if (b == markerContent && c == markerContent && d == markerContent)
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

        private static void FillSupport(
            SubsystemTerrain terrain, Palette p, int x, int z, int baseY, ref int placed)
        {
            int surface;
            if (!TryFindSurfaceY(terrain, x, z, baseY + 2, p, out surface))
                return;

            // Support only the gap beneath the village floor. The floor itself is
            // placed at baseY+1, so this loop must stop at baseY.
            for (int y = surface + 1; y <= baseY; y++)
                Place(terrain, x, y, z, p.Cobblestone, ref placed);
        }

        private static void PlaceGrounded(
            SubsystemTerrain terrain, Palette p, int x, int z, int baseY, int contents, ref int placed)
        {
            FillSupport(terrain, p, x, z, baseY, ref placed);
            Place(terrain, x, baseY + 1, z, contents, ref placed);
        }

        private static void BuildRoads(
            SubsystemTerrain terrain, Palette p, int cx, int cz, int baseY, ref int placed)
        {
            // A compact cobblestone cross connects the square to the six houses.
            for (int x = -10; x <= 10; x++)
                for (int dz = -1; dz <= 1; dz++)
                    PlaceGrounded(terrain, p, cx + x, cz + dz, baseY, p.Cobblestone, ref placed);

            for (int z = -10; z <= 10; z++)
                for (int dx = -1; dx <= 1; dx++)
                    PlaceGrounded(terrain, p, cx + dx, cz + z, baseY, p.Cobblestone, ref placed);

            for (int signX = -1; signX <= 1; signX += 2)
            {
                for (int signZ = -1; signZ <= 1; signZ += 2)
                {
                    for (int i = 7; i <= 10; i++)
                        for (int w = -1; w <= 1; w++)
                            PlaceGrounded(terrain, p, cx + signX * 14 + w, cz + signZ * i,
                                baseY, p.Cobblestone, ref placed);
                }
            }
        }

                private static void BuildHouse(
            SubsystemTerrain terrain, Palette p, int cx, int cz, int baseY,
            int frontSide, ref int placed)
        {
            // 9x9 cobblestone foundation with a spruce/plank house above it.
            for (int dx = -4; dx <= 4; dx++)
            {
                for (int dz = -4; dz <= 4; dz++)
                {
                    FillSupport(terrain, p, cx + dx, cz + dz, baseY, ref placed);
                    int floor = (Math.Abs(dx) == 4 || Math.Abs(dz) == 4)
                        ? p.Cobblestone : p.Planks;
                    Place(terrain, cx + dx, baseY + 1, cz + dz, floor, ref placed);
                }
            }

            // Ground floor walls.
            for (int y = baseY + 2; y <= baseY + 3; y++)
            {
                for (int side = -4; side <= 4; side++)
                {
                    PlaceWallCell(terrain, p, cx, cz, baseY, frontSide, side, y, 0, ref placed);
                    PlaceWallCell(terrain, p, cx, cz, baseY, frontSide, side, y, 1, ref placed);
                }
            }

            // Spruce corner beams.
            for (int y = baseY + 2; y <= baseY + 6; y++)
            {
                Place(terrain, cx - 4, y, cz - 4, p.SpruceWood, ref placed);
                Place(terrain, cx + 4, y, cz - 4, p.SpruceWood, ref placed);
                Place(terrain, cx - 4, y, cz + 4, p.SpruceWood, ref placed);
                Place(terrain, cx + 4, y, cz + 4, p.SpruceWood, ref placed);
            }

            // Upper floor.
            for (int dx = -3; dx <= 3; dx++)
            {
                for (int dz = -3; dz <= 3; dz++)
                    Place(terrain, cx + dx, baseY + 4, cz + dz, p.Planks, ref placed);
            }

            for (int y = baseY + 5; y <= baseY + 6; y++)
            {
                for (int side = -4; side <= 4; side++)
                {
                    PlaceWallCell(terrain, p, cx, cz, baseY, frontSide, side, y, 2, ref placed);
                    PlaceWallCell(terrain, p, cx, cz, baseY, frontSide, side, y, 3, ref placed);
                }
            }

            // Low stepped gables and a wooden roof.
            for (int h = 0; h <= 2; h++)
            {
                for (int dx = -h; dx <= h; dx++)
                {
                    Place(terrain, cx + dx, baseY + 7 + h, cz - 4, p.Planks, ref placed);
                    Place(terrain, cx + dx, baseY + 7 + h, cz + 4, p.Planks, ref placed);
                }
            }
            for (int dx = -4; dx <= 4; dx++)
            {
                int roofY = baseY + 7 + (2 - Math.Abs(dx) / 2);
                for (int dz = -5; dz <= 5; dz++)
                {
                    int roofBlock = (Math.Abs(dx) == 4) ? p.WoodenStairs : p.Planks;
                    Place(terrain, cx + dx, roofY, cz + dz, roofBlock, ref placed);
                }
            }

            // Two-block wooden door on the chosen front side.
            int doorX = cx;
            int doorZ = cz;
            if (frontSide == 0) doorZ = cz + 4;
            else if (frontSide == 1) doorZ = cz - 4;
            else if (frontSide == 2) doorX = cx + 4;
            else doorX = cx - 4;
            Place(terrain, doorX, baseY + 2, doorZ, p.WoodenDoor, ref placed);
            Place(terrain, doorX, baseY + 3, doorZ, p.WoodenDoor, ref placed);

            // Framed-glass windows, not ordinary glass.
            PlaceWindow(terrain, p, cx - 2, baseY + 3, cz - 4, ref placed);
            PlaceWindow(terrain, p, cx + 2, baseY + 3, cz - 4, ref placed);
            PlaceWindow(terrain, p, cx - 2, baseY + 3, cz + 4, ref placed);
            PlaceWindow(terrain, p, cx + 2, baseY + 3, cz + 4, ref placed);
            PlaceWindow(terrain, p, cx - 4, baseY + 3, cz - 2, ref placed);
            PlaceWindow(terrain, p, cx - 4, baseY + 3, cz + 2, ref placed);
            PlaceWindow(terrain, p, cx + 4, baseY + 3, cz - 2, ref placed);
            PlaceWindow(terrain, p, cx + 4, baseY + 3, cz + 2, ref placed);
            PlaceWindow(terrain, p, cx - 2, baseY + 6, cz - 4, ref placed);
            PlaceWindow(terrain, p, cx + 2, baseY + 6, cz - 4, ref placed);
            PlaceWindow(terrain, p, cx - 2, baseY + 6, cz + 4, ref placed);
            PlaceWindow(terrain, p, cx + 2, baseY + 6, cz + 4, ref placed);

            // Interior wooden stair run and warm wicker lighting.
            for (int step = 0; step < 3; step++)
                Place(terrain, cx + 2 - step, baseY + 2 + step, cz + 2, p.WoodenStairs, ref placed);
            Place(terrain, cx - 2, baseY + 3, cz - 2, p.WickerLamp, ref placed);
            Place(terrain, cx + 2, baseY + 6, cz + 2, p.WickerLamp, ref placed);

            // A wooden stair at the entrance gives a clear step up from the path.
            if (frontSide == 0) Place(terrain, cx, baseY + 1, cz + 5, p.WoodenStairs, ref placed);
            else if (frontSide == 1) Place(terrain, cx, baseY + 1, cz - 5, p.WoodenStairs, ref placed);
            else if (frontSide == 2) Place(terrain, cx + 5, baseY + 1, cz, p.WoodenStairs, ref placed);
            else Place(terrain, cx - 5, baseY + 1, cz, p.WoodenStairs, ref placed);
        }

                private static void PlaceWallCell(
            SubsystemTerrain terrain, Palette p, int cx, int cz, int baseY,
            int frontSide, int side, int y, int wallGroup, ref int placed)
        {
            int x, z;
            if (wallGroup == 0) { x = cx + side; z = cz - 4; }
            else if (wallGroup == 1) { x = cx + side; z = cz + 4; }
            else if (wallGroup == 2) { x = cx - 4; z = cz + side; }
            else { x = cx + 4; z = cz + side; }

            bool isFrontWall = (frontSide == 0 && wallGroup == 1) ||
                               (frontSide == 1 && wallGroup == 0) ||
                               (frontSide == 2 && wallGroup == 3) ||
                               (frontSide == 3 && wallGroup == 2);
            if (isFrontWall && side == 0 && y <= baseY + 3)
                return;

            bool windowPosition = (y == baseY + 3 || y == baseY + 6) && Math.Abs(side) == 2;
            if (windowPosition)
            {
                Place(terrain, x, y, z, p.FramedGlass, ref placed);
                return;
            }

            int wallContents = (y == baseY + 2) ? p.Cobblestone : p.Planks;
            Place(terrain, x, y, z, wallContents, ref placed);
        }

                private static void PlaceWindow(SubsystemTerrain terrain, Palette p, int x, int y, int z, ref int placed)
        {
            // The exact framed-glass block is used for every window.
            if (Terrain.ExtractContents(terrain.Terrain.GetCellValue(x, y, z)) == 0)
                Place(terrain, x, y, z, p.FramedGlass, ref placed);
        }

        private static void BuildCentralSquare(
            SubsystemTerrain terrain, Palette p, int cx, int cz, int baseY, ref int placed)
        {
            for (int dx = -5; dx <= 5; dx++)
            {
                for (int dz = -5; dz <= 5; dz++)
                    PlaceGrounded(terrain, p, cx + dx, cz + dz, baseY, p.Cobblestone, ref placed);
            }

            // Cobblestone well ring, with a spruce marker column in the centre.
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = -2; dz <= 2; dz++)
                {
                    if (Math.Abs(dx) == 2 || Math.Abs(dz) == 2)
                    {
                        Place(terrain, cx + dx, baseY + 2, cz + dz, p.Cobblestone, ref placed);
                        Place(terrain, cx + dx, baseY + 3, cz + dz, p.Cobblestone, ref placed);
                    }
                }
            }
            for (int y = baseY + 2; y <= baseY + 5; y++)
                Place(terrain, cx, y, cz, p.SpruceWood, ref placed);

            // Small market canopy: spruce supports and plank counter/roof.
            for (int dx = 2; dx <= 4; dx += 2)
            {
                for (int dz = -4; dz <= -2; dz += 2)
                {
                    Place(terrain, cx + dx, baseY + 2, cz + dz, p.SpruceWood, ref placed);
                    Place(terrain, cx + dx, baseY + 3, cz + dz, p.SpruceWood, ref placed);
                }
            }
            for (int dx = 2; dx <= 4; dx++)
            {
                Place(terrain, cx + dx, baseY + 2, cz - 3, p.Planks, ref placed);
                Place(terrain, cx + dx, baseY + 4, cz - 4, p.Planks, ref placed);
                Place(terrain, cx + dx, baseY + 4, cz - 3, p.Planks, ref placed);
                Place(terrain, cx + dx, baseY + 4, cz - 2, p.Planks, ref placed);
            }
        }

        private static void BuildTownLights(
            SubsystemTerrain terrain, Palette p, int cx, int cz, int baseY, ref int placed)
        {
            int[,] positions = new int[,] { { -5, -5 }, { 5, -5 }, { -5, 5 }, { 5, 5 } };
            for (int i = 0; i < positions.GetLength(0); i++)
            {
                int x = cx + positions[i, 0];
                int z = cz + positions[i, 1];
                PlaceGrounded(terrain, p, x, z, baseY, p.Cobblestone, ref placed);
                Place(terrain, x, baseY + 2, z, p.SpruceWood, ref placed);
                Place(terrain, x, baseY + 3, z, p.SpruceWood, ref placed);
                Place(terrain, x, baseY + 4, z, p.WickerLamp, ref placed);
            }
        }

            }
}
