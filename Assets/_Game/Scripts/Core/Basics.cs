using System;
using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    public enum Team { Player, Enemy }

    /// <summary>Слои физики. Имена слоям проставляет билдер (Пуля — дура → Пересобрать).</summary>
    public static class Layers
    {
        public const int Wall = 8;
        public const int Tank = 9;
        public const int Debris = 10;
        /// <summary>Решётка «=»: держит танки, но пропускает пули, эхо и взгляд.</summary>
        public const int Fence = 11;
        public const int FenceMask = 1 << Fence;
        public const int WallMask = 1 << Wall;
        public const int TankMask = 1 << Tank;
        public const int SolidMask = WallMask | TankMask;
    }

    public struct DamageInfo
    {
        public int amount;
        public Tank source;
        public Vector3 point;
        public Vector3 direction;
        public int bounces;
    }

    public interface IDamageable
    {
        void TakeDamage(DamageInfo info);
    }

    /// <summary>
    /// Шина событий. Геймплей сообщает, что случилось, а звук, эффекты, UI и ИИ подписываются сами.
    /// </summary>
    public static class GameEvents
    {
        /// <summary>Шум: позиция, радиус слышимости, чей звук.</summary>
        public static event Action<Vector3, float, Team> Noise;
        public static event Action<Tank, DamageInfo> TankDamaged;
        public static event Action<Tank, DamageInfo> TankKilled;
        public static event Action<Tank> ShotFired;

        public static void RaiseNoise(Vector3 pos, float radius, Team team) => Noise?.Invoke(pos, radius, team);
        public static void RaiseDamaged(Tank t, DamageInfo d) => TankDamaged?.Invoke(t, d);
        public static void RaiseKilled(Tank t, DamageInfo d) => TankKilled?.Invoke(t, d);
        public static void RaiseShot(Tank t) => ShotFired?.Invoke(t);

        // Сброс на случай, если в Enter Play Mode Settings отключена перезагрузка домена
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Noise = null; TankDamaged = null; TankKilled = null; ShotFired = null;
        }
    }

    public static class Prims
    {
        static readonly Dictionary<PrimitiveType, Mesh> meshes = new();

        public static Mesh Mesh(PrimitiveType type)
        {
            if (!meshes.TryGetValue(type, out var mesh) || mesh == null)
            {
                var temp = GameObject.CreatePrimitive(type);
                mesh = temp.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(temp);
                meshes[type] = mesh;
            }
            return mesh;
        }

        /// <summary>Видимая деталь без коллайдера.</summary>
        public static MeshRenderer Part(string name, PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, Quaternion? localRot = null)
        {
            var go = new GameObject(name);
            go.layer = parent != null ? parent.gameObject.layer : 0;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = Mesh(type);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return r;
        }

        public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }

    /// <summary>Материалы, созданные из трёх базовых (Lit, Unlit, Fx), с кэшем по цвету.</summary>
    public static class Mats
    {
        public static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        public static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        public static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        public static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        public static readonly int Smoothness = Shader.PropertyToID("_Smoothness");

        static GameConfig cfg;
        static readonly Dictionary<(int, Color), Material> cache = new();

        public static Material Wall { get; private set; }
        public static Material Crate { get; private set; }
        public static Material Floor { get; private set; }
        public static Material Tread { get; private set; }

        public static void Init(GameConfig config)
        {
            cfg = config;
            cache.Clear();
            var edges = EdgeTexture(64, 3);

            Wall = new Material(cfg.litMaterial) { name = "Wall" };
            Wall.SetColor(BaseColor, cfg.wallColor);
            Wall.SetTexture(EmissionMap, edges);
            Wall.SetColor(EmissionColor, Color.black);
            Wall.SetFloat(Smoothness, 0.15f);
            Wall.EnableKeyword("_EMISSION");

            Crate = new Material(Wall) { name = "Crate" };
            Crate.SetColor(BaseColor, cfg.crateColor);
            Crate.SetTexture(EmissionMap, EdgeTexture(64, 6));

            Tread = new Material(cfg.litMaterial) { name = "Tread" };
            Tread.SetColor(BaseColor, cfg.trackColor * 2.2f);
            Tread.SetTexture(BaseMap, TreadTexture());
            Tread.SetFloat(Smoothness, 0.5f);
            Tread.SetColor(EmissionColor, Color.black);
            Tread.EnableKeyword("_EMISSION");

            var grid = GridTexture(64);
            Floor = new Material(cfg.litMaterial) { name = "Floor" };
            Floor.SetColor(BaseColor, cfg.floorColor);
            Floor.SetTexture(BaseMap, grid);
            Floor.SetTexture(EmissionMap, grid);
            Floor.SetColor(EmissionColor, cfg.floorGridGlow);
            Floor.SetFloat(Smoothness, 0.25f);
            Floor.EnableKeyword("_EMISSION");
        }

        public static Material Lit(Color c, float smoothness = 0.35f)
        {
            if (cache.TryGetValue((0, c), out var m)) return m;
            m = new Material(cfg.litMaterial);
            m.SetColor(BaseColor, c);
            m.SetFloat(Smoothness, smoothness);
            m.SetColor(EmissionColor, Color.black);
            m.EnableKeyword("_EMISSION");
            return cache[(0, c)] = m;
        }

        public static Material Unlit(Color hdr)
        {
            if (cache.TryGetValue((1, hdr), out var m)) return m;
            m = new Material(cfg.unlitMaterial);
            m.SetColor(BaseColor, hdr);
            return cache[(1, hdr)] = m;
        }

        /// <summary>Аддитивный светящийся материал для частиц, линий и пузырей.</summary>
        public static Material Fx(Color hdr)
        {
            if (cache.TryGetValue((2, hdr), out var m)) return m;
            m = new Material(cfg.fxMaterial);
            m.SetColor(BaseColor, hdr);
            return cache[(2, hdr)] = m;
        }

        static Texture2D EdgeTexture(int size, int border)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 4, name = "Edges" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
                byte v = d < border ? (byte)255 : d < border + 2 ? (byte)70 : (byte)10;
                px[y * size + x] = new Color32(v, v, v, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Поперечные траки: светлые полосы с тёмными зазорами, повторяются вдоль U.</summary>
        static Texture2D TreadTexture()
        {
            const int w = 32, h = 8;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "Tread", filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte v = x % 16 < 3 ? (byte)40 : x % 16 < 5 ? (byte)120 : (byte)200;
                px[y * w + x] = new Color32(v, v, v, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        static Texture2D GridTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 8, name = "Grid" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool line = x < 2 || y < 2;
                byte v = line ? (byte)255 : (byte)150;
                px[y * size + x] = new Color32(v, v, v, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }
    }
}
