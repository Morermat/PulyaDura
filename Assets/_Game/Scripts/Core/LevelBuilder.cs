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
            for (int y = 0; y < h; y++)
            {
                string row = room.rows[y];
                for (int x = 0; x < w; x++)
                {
                    char ch = x < row.Length ? row[x] : '.';
                    Vector3 pos = origin + new Vector3(x * c, 0f, -y * c);
                    switch (ch)
                    {
                        case '#': CreateWall(pos); break;
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
            surface.layerMask = (1 << 0) | Layers.WallMask;
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

        void CreateWall(Vector3 pos)
        {
            float c = cfg.cellSize;
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
                supplyMaterial.SetColor(Mats.BaseColor, new Color(0.45f, 0.36f, 0.05f));
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
                // Жёлтый ящик чуть светится в темноте: его можно найти без эха
                wall.baseColor = new Color(2.4f, 1.9f, 0.3f);
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
