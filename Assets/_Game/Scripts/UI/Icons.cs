using System;
using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    public enum IconId
    {
        Tank, Armor, Dash, Mirror, Wall, Cannon, Plasma, Shotgun, Rail,
        Sound, Quiet, Clock, Target, Ricochet, Chevron,
    }

    /// <summary>
    /// Иконки и панели рисуются кодом в текстуры (белым по прозрачному, цвет задаёт Image.color).
    /// Фигуры описаны простыми примитивами в координатах [-1..1], сглаживание — суперсэмплингом 4×4.
    /// </summary>
    public static class Icons
    {
        const int Size = 96;
        static readonly Dictionary<IconId, Sprite> icons = new();
        static Sprite panel, frame;

        // ---------- Панели со срезанными углами (9-slice) ----------

        /// <summary>Заливка панели со срезанными углами.</summary>
        public static Sprite Panel => panel != null ? panel : panel = Chamfer(false);

        /// <summary>Рамка той же формы.</summary>
        public static Sprite Frame => frame != null ? frame : frame = Chamfer(true);

        static Sprite Chamfer(bool outline)
        {
            const int s = 48, cut = 12, border = 16;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float outer = Coverage(x, y, s, cut, 0f);
                float a = outline ? Mathf.Clamp01(outer - Coverage(x, y, s, cut, 2f)) : outer;
                px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        /// <summary>Доля пикселя внутри восьмиугольника (квадрат со срезанными углами), сжатого на inset.</summary>
        static float Coverage(int px, int py, int s, float cut, float inset)
        {
            int inside = 0;
            for (int sy = 0; sy < 4; sy++)
            for (int sx = 0; sx < 4; sx++)
            {
                float x = px + (sx + 0.5f) / 4f, y = py + (sy + 0.5f) / 4f;
                float lo = inset, hi = s - inset;
                float c = cut + inset * 0.41f;
                bool ok = x >= lo && x <= hi && y >= lo && y <= hi
                          && (x - lo) + (y - lo) >= c && (hi - x) + (y - lo) >= c
                          && (x - lo) + (hi - y) >= c && (hi - x) + (hi - y) >= c;
                if (ok) inside++;
            }
            return inside / 16f;
        }

        // ---------- Иконки ----------

        public static Sprite Get(IconId id)
        {
            if (icons.TryGetValue(id, out var sp) && sp != null) return sp;
            var shape = Shape(id);
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Icon_" + id };
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int inside = 0;
                for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                {
                    float u = (x + (sx + 0.5f) / 4f) / Size * 2f - 1f;
                    float v = (y + (sy + 0.5f) / 4f) / Size * 2f - 1f;
                    if (shape(new Vector2(u, v))) inside++;
                }
                px[y * Size + x] = new Color32(255, 255, 255, (byte)(inside * 255 / 16));
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return icons[id] = Sprite.Create(tex, new UnityEngine.Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
        }

        static Func<Vector2, bool> Shape(IconId id) => id switch
        {
            IconId.Tank => p =>
                (Box(p, new(0f, -0.08f), new(0.56f, 0.72f)) && !Seg(p, new(-0.3f, -0.8f), new(-0.3f, 0.64f), 0.05f) && !Seg(p, new(0.3f, -0.8f), new(0.3f, 0.64f), 0.05f) && !Ring(p, new(0f, -0.05f), 0.36f, 0.05f))
                || Seg(p, new(0f, 0f), new(0f, 0.95f), 0.09f),
            IconId.Armor => p => Poly(p, new(0f, 0.85f), new(0.72f, 0.58f), new(0.62f, -0.18f), new(0f, -0.88f), new(-0.62f, -0.18f), new(-0.72f, 0.58f))
                                 && !Poly(p, new(0f, 0.6f), new(0.48f, 0.42f), new(0.4f, -0.12f), new(0f, -0.6f), new(-0.4f, -0.12f), new(-0.48f, 0.42f))
                                 || Seg(p, new(0f, 0.5f), new(0f, -0.5f), 0.08f),
            IconId.Dash => p => Chevron(p, -0.45f) || Chevron(p, 0.15f) || Seg(p, new(-0.95f, 0f), new(-0.6f, 0f), 0.06f),
            IconId.Mirror => p => (Poly(p, Hex(0.85f)) && !Poly(p, Hex(0.68f)))
                                  || Seg(p, new(-0.45f, 0.45f), new(0f, -0.2f), 0.08f) || Seg(p, new(0f, -0.2f), new(0.45f, 0.45f), 0.08f),
            IconId.Wall => p => Box(p, new(-0.42f, 0.5f), new(0.36f, 0.18f)) || Box(p, new(0.42f, 0.5f), new(0.36f, 0.18f))
                                || Box(p, new(0f, 0f), new(0.36f, 0.18f)) || Box(p, new(-0.78f, 0f), new(0.1f, 0.18f)) || Box(p, new(0.78f, 0f), new(0.1f, 0.18f))
                                || Box(p, new(-0.42f, -0.5f), new(0.36f, 0.18f)) || Box(p, new(0.42f, -0.5f), new(0.36f, 0.18f)),
            IconId.Cannon => p => Circle(p, new(-0.35f, 0f), 0.42f) || Seg(p, new(-0.2f, 0f), new(0.9f, 0f), 0.13f) || Box(p, new(0.82f, 0f), new(0.1f, 0.2f)),
            IconId.Plasma => p => Seg(p, new(-0.9f, 0.22f), new(0.1f, 0.22f), 0.09f) || Seg(p, new(-0.9f, -0.22f), new(0.1f, -0.22f), 0.09f)
                                  || Circle(p, new(0.45f, 0.22f), 0.16f) || Circle(p, new(0.8f, -0.22f), 0.13f),
            IconId.Shotgun => p => Circle(p, new(-0.7f, 0f), 0.22f) || Seg(p, new(-0.6f, 0f), new(0.9f, 0f), 0.07f)
                                   || Seg(p, new(-0.6f, 0f), new(0.8f, 0.55f), 0.07f) || Seg(p, new(-0.6f, 0f), new(0.8f, -0.55f), 0.07f),
            IconId.Rail => p => Seg(p, new(-0.9f, 0.28f), new(0.9f, 0.28f), 0.06f) || Seg(p, new(-0.9f, -0.28f), new(0.9f, -0.28f), 0.06f)
                                || Seg(p, new(-0.6f, 0f), new(-0.2f, 0.12f), 0.05f) || Seg(p, new(-0.2f, 0.12f), new(0.2f, -0.12f), 0.05f) || Seg(p, new(0.2f, -0.12f), new(0.6f, 0f), 0.05f),
            IconId.Sound => p => Speaker(p) || Arc(p, new(-0.1f, 0f), 0.45f, 0.08f, 50f) || Arc(p, new(-0.1f, 0f), 0.78f, 0.08f, 50f),
            IconId.Quiet => p => Speaker(p) || Seg(p, new(0.25f, 0.3f), new(0.85f, -0.3f), 0.07f) || Seg(p, new(0.25f, -0.3f), new(0.85f, 0.3f), 0.07f),
            IconId.Clock => p => Ring(p, Vector2.zero, 0.8f, 0.1f) || Seg(p, Vector2.zero, new(0f, 0.55f), 0.08f) || Seg(p, Vector2.zero, new(0.38f, 0f), 0.08f),
            IconId.Target => p => Ring(p, Vector2.zero, 0.62f, 0.1f) || Circle(p, Vector2.zero, 0.16f)
                                  || Seg(p, new(0f, 0.95f), new(0f, 0.55f), 0.07f) || Seg(p, new(0f, -0.95f), new(0f, -0.55f), 0.07f)
                                  || Seg(p, new(0.95f, 0f), new(0.55f, 0f), 0.07f) || Seg(p, new(-0.95f, 0f), new(-0.55f, 0f), 0.07f),
            IconId.Ricochet => p => Seg(p, new(-0.8f, 0.7f), new(0f, -0.45f), 0.08f) || Seg(p, new(0f, -0.45f), new(0.7f, 0.55f), 0.08f)
                                    || Circle(p, new(0.75f, 0.62f), 0.15f) || Box(p, new(0f, -0.68f), new(0.85f, 0.06f)),
            IconId.Chevron => p => Poly(p, new(0.9f, 0f), new(-0.6f, 0.75f), new(-0.25f, 0f), new(-0.6f, -0.75f)),
            _ => p => false,
        };

        // ---------- Примитивы ----------

        static bool Box(Vector2 p, Vector2 c, Vector2 half) => Mathf.Abs(p.x - c.x) <= half.x && Mathf.Abs(p.y - c.y) <= half.y;
        static bool Circle(Vector2 p, Vector2 c, float r) => (p - c).sqrMagnitude <= r * r;
        static bool Ring(Vector2 p, Vector2 c, float r, float t) { float d = (p - c).magnitude; return d <= r + t * 0.5f && d >= r - t * 0.5f; }

        static bool Arc(Vector2 p, Vector2 c, float r, float t, float halfAngle)
        {
            Vector2 d = p - c;
            return Ring(p, c, r, t) && d.x > 0f && Mathf.Abs(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg) <= halfAngle;
        }

        static bool Seg(Vector2 p, Vector2 a, Vector2 b, float halfWidth)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).sqrMagnitude <= halfWidth * halfWidth;
        }

        /// <summary>Точка внутри выпуклого многоугольника (вершины в любом порядке обхода).</summary>
        static bool Poly(Vector2 p, params Vector2[] v)
        {
            int sign = 0;
            for (int i = 0; i < v.Length; i++)
            {
                Vector2 a = v[i], b = v[(i + 1) % v.Length];
                float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                int s = cross > 0f ? 1 : cross < 0f ? -1 : 0;
                if (s == 0) continue;
                if (sign == 0) sign = s;
                else if (s != sign) return false;
            }
            return true;
        }

        static Vector2[] Hex(float r)
        {
            var v = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                float a = (90f + i * 60f) * Mathf.Deg2Rad;
                v[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            return v;
        }

        static bool Chevron(Vector2 p, float x) => Seg(p, new(x - 0.25f, 0.6f), new(x + 0.3f, 0f), 0.11f) || Seg(p, new(x + 0.3f, 0f), new(x - 0.25f, -0.6f), 0.11f);

        static bool Speaker(Vector2 p) => Box(p, new(-0.62f, 0f), new(0.18f, 0.22f)) || Poly(p, new(-0.5f, 0.22f), new(-0.1f, 0.6f), new(-0.1f, -0.6f), new(-0.5f, -0.22f));
    }
}
