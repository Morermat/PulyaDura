using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace PulyaDura
{
    public class RoomData
    {
        public string title;
        public string hint;
        public string[] rows;
        public int width;
        public int height;
        /// <summary>Акустика комнаты: пресет реверберации из третьего поля заголовка (Room, Cave, Hallway…).</summary>
        public AudioReverbPreset reverb = AudioReverbPreset.Room;
    }

    /// <summary>
    /// Комнаты хранятся в текстовом файле (Assets/_Game/Data/rooms.txt), чтобы их мог править
    /// любой участник команды без конфликтов в сцене. Формат описан в начале файла.
    /// </summary>
    public static class RoomParser
    {
        public static List<RoomData> Parse(string text)
        {
            var result = new List<RoomData>();
            var rows = new List<string>();
            RoomData current = null;

            void Flush()
            {
                if (current != null && rows.Count > 0)
                {
                    current.rows = rows.ToArray();
                    current.height = rows.Count;
                    foreach (var r in rows) current.width = Mathf.Max(current.width, r.Length);
                    result.Add(current);
                }
                rows.Clear();
                current = null;
            }

            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("//")) continue;
                if (line.StartsWith("="))
                {
                    Flush();
                    var parts = line.Substring(1).Split('|');
                    current = new RoomData
                    {
                        title = parts[0].Trim(),
                        hint = parts.Length > 1 ? parts[1].Trim() : ""
                    };
                    if (parts.Length > 2 && System.Enum.TryParse(parts[2].Trim(), true, out AudioReverbPreset preset))
                        current.reverb = preset;
                }
                else if (line.Length > 0 && current != null)
                {
                    rows.Add(line);
                }
            }
            Flush();
            return result;
        }
    }

    /// <summary>Строит комнату из текстовой карты: пол, стены, ящики, точки появления, NavMesh.</summary>
    public class LevelBuilder
    {
        readonly GameConfig cfg;
        GameObject root;

        public Transform Root => root != null ? root.transform : null;
        public Vector3 PlayerSpawn { get; private set; }
        public readonly List<(EnemyConfig config, Vector3 position)> EnemySpawns = new();
        public Bounds Bounds { get; private set; }

        public LevelBuilder(GameConfig config) => cfg = config;

        public void Clear()
        {
            if (root == null) return;
            root.SetActive(false); // сразу убирает коллайдеры из физики, не дожидаясь конца кадра
            Object.Destroy(root);
            root = null;
        }

        public void Build(RoomData room)
        {
            Clear();
            EnemySpawns.Clear();
            root = new GameObject("Level");

            float c = cfg.cellSize;
            int w = room.width, h = room.height;
            Vector3 origin = new(-(w - 1) * c * 0.5f, 0f, (h - 1) * c * 0.5f);

            var floor = Prims.Part("Floor", PrimitiveType.Cube, root.transform, new Vector3(0f, -0.5f, 0f), new Vector3(w * c, 1f, h * c), Mats.Floor);
            floor.gameObject.AddComponent<BoxCollider>();
            Mats.Floor.SetTextureScale(Mats.BaseMap, new Vector2(w, h));

            var crates = new List<Vector3>();
            var supplies = new List<Vector3>();
            var barrels = new List<Vector3>();
            var traps = new List<Vector3>();
            var portals = new Dictionary<char, List<Vector3>>();
            bool IsWall(int x, int y) => x < 0 || y < 0 || x >= w || y >= h || (x < room.rows[y].Length && room.rows[y][x] == '#');
            bool IsFence(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && x < room.rows[y].Length && room.rows[y][x] == '=';
            var freeStanding = FreeStandingWalls(room, w, h);
            for (int y = 0; y < h; y++)
            {
                string row = room.rows[y];
                for (int x = 0; x < w; x++)
                {
                    char ch = x < row.Length ? row[x] : '.';
                    Vector3 pos = origin + new Vector3(x * c, 0f, -y * c);
                    switch (ch)
                    {
                        case '#':
                            // Скругляем только отдельно стоящие препятствия: колонны, блоки 2×2 и концы стенок.
                            // Всё, что примыкает к внешней стене, остаётся прямым — так скруглений немного и они заметны
                            float[] corners = null;
                            if (freeStanding[x, y] != 0)
                            {
                                int neighbours = (IsWall(x, y - 1) ? 1 : 0) + (IsWall(x + 1, y) ? 1 : 0) + (IsWall(x, y + 1) ? 1 : 0) + (IsWall(x - 1, y) ? 1 : 0);
                                if (neighbours <= 1 || freeStanding[x, y] == 4)
                                    corners = WallShapes.Corners(IsWall(x, y - 1), IsWall(x + 1, y), IsWall(x, y + 1), IsWall(x - 1, y));
                            }
                            CreateWall(pos, corners);
                            break;
                        case '=':
                            bool alongX = IsWall(x - 1, y) || IsWall(x + 1, y) || IsFence(x - 1, y) || IsFence(x + 1, y)
                                || !(IsWall(x, y - 1) || IsWall(x, y + 1) || IsFence(x, y - 1) || IsFence(x, y + 1));
                            CreateFence(pos, alongX);
                            break;
                        case 'c': crates.Add(pos); break;
                        case 'y': supplies.Add(pos); break;
                        case 'x': barrels.Add(pos); break;
                        case 'n': traps.Add(pos); break;
                        case >= '1' and <= '4':
                            if (!portals.TryGetValue(ch, out var list)) portals[ch] = list = new List<Vector3>();
                            list.Add(pos);
                            break;
                        case 'P': PlayerSpawn = pos; break;
                        case '.': case ' ': break;
                        default:
                            var enemy = cfg.Enemy(ch);
                            if (enemy != null) EnemySpawns.Add((enemy, pos));
                            else Debug.LogWarning($"[Комната «{room.title}»] неизвестный символ '{ch}' в ({x},{y})");
                            break;
                    }
                }
            }

            Bounds = new Bounds(new Vector3(0f, cfg.wallHeight * 0.5f, 0f), new Vector3(w * c, cfg.wallHeight, h * c));

            // NavMesh печём по полу и стенам. Ящики ставим после, они вырезают себе дыру как препятствия.
            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = (1 << 0) | Layers.WallMask | Layers.FenceMask;
            surface.BuildNavMesh();

            foreach (var p in crates) CreateCrate(p, Mats.Crate, false);
            foreach (var p in supplies) CreateCrate(p, SupplyMaterial, true);
            foreach (var p in barrels) CreateBarrel(p);
            foreach (var p in traps) NoiseTrap.Create(p, root.transform);
            foreach (var (key, points) in portals)
            {
                if (points.Count != 2)
                {
                    Debug.LogWarning($"[Комната «{room.title}»] телепорт '{key}' должен встречаться ровно 2 раза, а не {points.Count}");
                    continue;
                }
                var a = Teleporter.Create(points[0], key - '1', root.transform);
                var b = Teleporter.Create(points[1], key - '1', root.transform);
                a.Partner = b;
                b.Partner = a;
            }

            // Акустика: зона реверберации на всю комнату
            var reverb = root.AddComponent<AudioReverbZone>();
            reverb.reverbPreset = room.reverb;
            reverb.minDistance = Mathf.Max(w, h) * c * 0.6f;
            reverb.maxDistance = reverb.minDistance + 8f;
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Для каждой клетки стены: 0 — группа касается внешней стены, иначе размер отдельно стоящей группы
        /// (4 и форма 2×2 — большая круглая колонна).
        /// </summary>
        static int[,] FreeStandingWalls(RoomData room, int w, int h)
        {
            var result = new int[w, h];
            var seen = new bool[w, h];
            var group = new List<Vector2Int>();
            var stack = new Stack<Vector2Int>();
            bool Wall(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && x < room.rows[y].Length && room.rows[y][x] == '#';
            for (int sx = 0; sx < w; sx++)
                for (int sy = 0; sy < h; sy++)
                {
                    if (!Wall(sx, sy) || seen[sx, sy]) continue;
                    group.Clear();
                    stack.Push(new Vector2Int(sx, sy));
                    seen[sx, sy] = true;
                    bool border = false;
                    int minX = sx, maxX = sx, minY = sy, maxY = sy;
                    while (stack.Count > 0)
                    {
                        var c = stack.Pop();
                        group.Add(c);
                        if (c.x == 0 || c.y == 0 || c.x == w - 1 || c.y == h - 1) border = true;
                        minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
                        minY = Mathf.Min(minY, c.y); maxY = Mathf.Max(maxY, c.y);
                        foreach (var d in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                        {
                            var n = c + d;
                            if (Wall(n.x, n.y) && !seen[n.x, n.y]) { seen[n.x, n.y] = true; stack.Push(n); }
                        }
                    }
                    bool block2 = group.Count == 4 && maxX - minX == 1 && maxY - minY == 1;
                    foreach (var c in group) result[c.x, c.y] = border ? 0 : block2 ? 4 : group.Count == 4 ? 5 : group.Count;
                }
            return result;
        }

        /// <summary>
        /// Решётка: низкий цоколь, стойки и две светящиеся перекладины. Танк сквозь неё не проедет,
        /// а пули, эхо и взгляд врагов проходят. Видна всегда — тусклым белым.
        /// </summary>
        void CreateFence(Vector3 pos, bool alongX)
        {
            float c = cfg.cellSize;
            var go = new GameObject("Fence");
            go.transform.SetParent(root.transform, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, alongX ? 0f : 90f, 0f));
            go.layer = Layers.Fence;
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(c, cfg.wallHeight, c * 0.55f);
            col.center = new Vector3(0f, cfg.wallHeight * 0.5f, 0f);

            var dark = Mats.Lit(new Color(0.07f, 0.08f, 0.1f), 0.4f);
            Prims.Part("Base", PrimitiveType.Cube, go.transform, new Vector3(0f, 0.06f, 0f), new Vector3(c, 0.12f, 0.34f), dark);
            for (int i = -1; i <= 1; i++)
                Prims.Part("Post", PrimitiveType.Cube, go.transform, new Vector3(i * (c * 0.5f - 0.06f), 0.5f, 0f), new Vector3(0.1f, 0.9f, 0.1f), dark);
            var glow = Mats.Unlit(new Color(0.9f, 1f, 1.15f) * 0.55f);
            foreach (float y in new[] { 0.42f, 0.86f })
            {
                var bar = Prims.Part("Bar", PrimitiveType.Cube, go.transform, new Vector3(0f, y, 0f), new Vector3(c, 0.04f, 0.04f), glow);
                bar.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <param name="corners">Скругления углов из WallShapes.Corners; null — обычный куб</param>
        void CreateWall(Vector3 pos, float[] corners)
        {
            float c = cfg.cellSize;
            if (corners != null)
            {
                var go = new GameObject("Wall");
                go.transform.SetParent(root.transform, false);
                go.transform.position = pos;
                go.layer = Layers.Wall;
                var mesh = WallShapes.Get(corners, c, cfg.wallHeight);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = Mats.Wall;
                var col = go.AddComponent<MeshCollider>();
                col.sharedMesh = mesh;
                col.convex = true;
                go.AddComponent<WallBlock>();
                return;
            }
            var r = Prims.Part("Wall", PrimitiveType.Cube, root.transform, pos + Vector3.up * cfg.wallHeight * 0.5f, new Vector3(c, cfg.wallHeight, c), Mats.Wall);
            r.gameObject.layer = Layers.Wall;
            r.gameObject.AddComponent<BoxCollider>();
            r.gameObject.AddComponent<WallBlock>();
        }

        static Material supplyMaterial;
        static Material SupplyMaterial
        {
            get
            {
                if (supplyMaterial != null) return supplyMaterial;
                supplyMaterial = new Material(Mats.Crate) { name = "Supply" };
                supplyMaterial.SetColor(Mats.BaseColor, new Color(0.32f, 0.36f, 0.44f));
                return supplyMaterial;
            }
        }

        void CreateCrate(Vector3 pos, Material mat, bool supply)
        {
            float s = cfg.cellSize * (supply ? 0.6f : 0.78f);
            var r = Prims.Part(supply ? "Supply" : "Crate", PrimitiveType.Cube, root.transform, pos + Vector3.up * (supply ? 0.35f : 0.45f), new Vector3(s, supply ? 0.7f : 0.9f, s), mat,
                Quaternion.Euler(0f, Random.Range(-12f, 12f), 0f));
            r.gameObject.layer = Layers.Wall;
            r.gameObject.AddComponent<BoxCollider>();
            var wall = r.gameObject.AddComponent<WallBlock>();
            if (supply)
            {
                // Белый ящик чуть светится в темноте: его можно найти без эха
                wall.baseColor = new Color(2.2f, 2.4f, 2.8f);
                wall.baseGlow = 0.22f;
                r.gameObject.AddComponent<SupplyBox>();
            }
            else r.gameObject.AddComponent<Crate>();
            var obstacle = r.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = Vector3.one;
            obstacle.carving = true;
        }

        /// <summary>Красная бочка: тёмная, с тлеющими красными обручами — видна вблизи и в эхе.</summary>
        void CreateBarrel(Vector3 pos)
        {
            var r = Prims.Part("Barrel", PrimitiveType.Cylinder, root.transform, pos + Vector3.up * 0.48f, new Vector3(0.75f, 0.48f, 0.75f), Mats.Lit(new Color(0.35f, 0.05f, 0.04f), 0.5f));
            r.gameObject.layer = Layers.Wall;
            var col = r.gameObject.AddComponent<CapsuleCollider>();
            col.radius = 0.5f;
            col.height = 2f;
            var wall = r.gameObject.AddComponent<WallBlock>();
            wall.baseColor = new Color(2.6f, 0.3f, 0.15f);
            wall.baseGlow = 0.12f;
            r.gameObject.AddComponent<Barrel>();
            for (int i = -1; i <= 1; i += 2)
            {
                var hoop = Prims.Part("Hoop", PrimitiveType.Cylinder, r.transform, new Vector3(0f, i * 0.5f, 0f), new Vector3(1.04f, 0.04f, 1.04f), Mats.Unlit(new Color(2.4f, 0.25f, 0.1f)));
                hoop.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var obstacle = r.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.radius = 0.5f;
            obstacle.height = 2f;
            obstacle.carving = true;
        }
    }
}
