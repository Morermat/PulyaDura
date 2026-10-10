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

            foreach (var p in crates) CreateCrate(p);
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

        void CreateCrate(Vector3 pos)
        {
            float s = cfg.cellSize * 0.78f;
            var r = Prims.Part("Crate", PrimitiveType.Cube, root.transform, pos + Vector3.up * 0.45f, new Vector3(s, 0.9f, s), Mats.Crate,
                Quaternion.Euler(0f, Random.Range(-12f, 12f), 0f));
            r.gameObject.layer = Layers.Wall;
            r.gameObject.AddComponent<BoxCollider>();
            r.gameObject.AddComponent<WallBlock>();
            r.gameObject.AddComponent<Crate>();
            var obstacle = r.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = Vector3.one;
            obstacle.carving = true;
        }
    }
}
