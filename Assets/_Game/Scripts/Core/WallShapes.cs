using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Скруглённые стены. Форма клетки «#» зависит от соседей:
    /// одиночная — круглая колонна, конец стены — полукруглый торец, внешний угол — четверть круга,
    /// остальные — обычный куб. Так комнаты выглядят мягче, а танк не цепляется за углы.
    /// Меш выпуклый: им же служит MeshCollider, рикошет и NavMesh работают как с кубом.
    /// </summary>
    public static class WallShapes
    {
        static readonly Dictionary<int, Mesh> cache = new();

        /// <summary>
        /// Радиусы скругления углов (ЮВ, СВ, СЗ, ЮЗ) в долях клетки: 0 — острый угол, 0.5 — полклетки, 1 — целая клетка.
        /// Возвращает null, если клетка остаётся кубом. Соседи: n — +Z, e — +X, s — −Z, w — −X.
        /// </summary>
        public static float[] Corners(bool n, bool e, bool s, bool w)
        {
            int count = (n ? 1 : 0) + (e ? 1 : 0) + (s ? 1 : 0) + (w ? 1 : 0);
            if (count == 0) return new[] { 0.5f, 0.5f, 0.5f, 0.5f };
            if (count == 1)
            {
                // Торец: скругляем два угла со стороны, противоположной соседу
                return new[]
                {
                    !s && !e && (n || w) ? 0.5f : 0f,
                    !n && !e && (s || w) ? 0.5f : 0f,
                    !n && !w && (s || e) ? 0.5f : 0f,
                    !s && !w && (n || e) ? 0.5f : 0f,
                };
            }
            if (count == 2 && n != s)
            {
                // Внешний угол: четверть круга радиусом в клетку
                if (s && w) return new[] { 0f, 1f, 0f, 0f };
                if (s && e) return new[] { 0f, 0f, 1f, 0f };
                if (n && e) return new[] { 0f, 0f, 0f, 1f };
                return new[] { 1f, 0f, 0f, 0f }; // n && w
            }
            return null;
        }

        public static Mesh Get(float[] corners, float cell, float height)
        {
            int key = 17;
            foreach (var r in corners) key = key * 31 + Mathf.RoundToInt(r * 2f);
            key = key * 31 + Mathf.RoundToInt(cell * 100f);
            key = key * 31 + Mathf.RoundToInt(height * 100f);
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            return cache[key] = Build(corners, cell, height);
        }

        struct RimPoint
        {
            public Vector2 p;
            public bool sharp;
        }

        static List<RimPoint> Rim(float[] corners, float cell)
        {
            float h = cell * 0.5f;
            // Углы против часовой стрелки при взгляде сверху: ЮВ, СВ, СЗ, ЮЗ
            Vector2[] corner = { new(h, -h), new(h, h), new(-h, h), new(-h, -h) };
            float[] startDeg = { -90f, 0f, 90f, 180f };
            var rim = new List<RimPoint>();
            for (int i = 0; i < 4; i++)
            {
                float r = corners[i] * cell;
                if (r <= 0f)
                {
                    Add(rim, corner[i], true);
                    continue;
                }
                Vector2 sign = new(Mathf.Sign(corner[i].x), Mathf.Sign(corner[i].y));
                Vector2 center = corner[i] - sign * r;
                int steps = corners[i] >= 1f ? 20 : 12;
                for (int k = 0; k <= steps; k++)
                {
                    float a = (startDeg[i] + 90f * k / steps) * Mathf.Deg2Rad;
                    Add(rim, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, false);
                }
            }
            if (rim.Count > 1 && (rim[0].p - rim[^1].p).sqrMagnitude < 1e-6f)
            {
                var first = rim[0];
                first.sharp |= rim[^1].sharp;
                rim[0] = first;
                rim.RemoveAt(rim.Count - 1);
            }
            return rim;
        }

        static void Add(List<RimPoint> rim, Vector2 p, bool sharp)
        {
            if (rim.Count > 0 && (rim[^1].p - p).sqrMagnitude < 1e-6f)
            {
                var last = rim[^1];
                last.sharp |= sharp;
                rim[^1] = last;
                return;
            }
            rim.Add(new RimPoint { p = p, sharp = sharp });
        }

        static Mesh Build(float[] corners, float cell, float height)
        {
            var rim = Rim(corners, cell);
            int n = rim.Count;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Сторона между двумя острыми углами всегда примыкает к соседней стене: её не видно,
            // и на ней не должно быть ни светящейся линии, ни каймы — иначе «таблетка» из двух клеток получает шов.
            var inner = new bool[n];
            for (int i = 0; i < n; i++) inner[i] = rim[i].sharp && rim[(i + 1) % n].sharp;
            int firstInner = System.Array.IndexOf(inner, true);

            // Боковые грани. Рамка текстуры светится на краях UV: по V — верх и низ стены,
            // по U — вертикальные линии. Видимые стороны здесь только скруглённые, поэтому U идёт от 0.1 до 0.9:
            // вертикальных швов на дуге нет. Обход начинаем сразу после скрытой стороны (или с 0 у колонны).
            int start = firstInner < 0 ? 0 : (firstInner + 1) % n;
            var runs = new List<List<int>>();
            var current = new List<int>();
            for (int k = 0; k < n; k++)
            {
                int i = (start + k) % n;
                if (inner[i])
                {
                    if (current.Count > 0) runs.Add(current);
                    current = new List<int>();
                    continue;
                }
                current.Add(i);
            }
            if (current.Count > 0) runs.Add(current);

            foreach (var run in runs)
            {
                float length = 0f;
                foreach (int i in run) length += (rim[(i + 1) % n].p - rim[i].p).magnitude;
                float acc = 0f;
                for (int r = 0; r < run.Count; r++)
                {
                    int i = run[r], j = (i + 1) % n;
                    Vector2 a = rim[i].p, b = rim[j].p;
                    float seg = (b - a).magnitude;
                    float u0 = 0.1f + 0.8f * acc / length, u1 = 0.1f + 0.8f * (acc + seg) / length;
                    acc += seg;
                    Vector2 own = Outward(b - a);
                    Vector3 na = r == 0 && firstInner >= 0 ? new Vector3(own.x, 0f, own.y) : SideNormal(rim, i, true);
                    Vector3 nb = r == run.Count - 1 && firstInner >= 0 ? new Vector3(own.x, 0f, own.y) : SideNormal(rim, j, false);
                    if (rim[i].sharp) na = new Vector3(own.x, 0f, own.y);
                    if (rim[j].sharp) nb = new Vector3(own.x, 0f, own.y);
                    int v = verts.Count;
                    verts.Add(new Vector3(a.x, 0f, a.y)); normals.Add(na); uvs.Add(new Vector2(u0, 0f));
                    verts.Add(new Vector3(b.x, 0f, b.y)); normals.Add(nb); uvs.Add(new Vector2(u1, 0f));
                    verts.Add(new Vector3(b.x, height, b.y)); normals.Add(nb); uvs.Add(new Vector2(u1, 1f));
                    verts.Add(new Vector3(a.x, height, a.y)); normals.Add(na); uvs.Add(new Vector2(u0, 1f));
                    // Обход против часовой сверху → наружу смотрит грань с порядком (v, v+2, v+1)
                    tris.AddRange(new[] { v, v + 2, v + 1, v, v + 3, v + 2 });
                }
            }

            // Крышка: светящаяся кайма постоянной ширины вдоль видимого контура + тёмная середина.
            // Кайма — полоска, у которой U идёт от 0 (внешний край, яркая рамка текстуры) до EdgeU (внутрь).
            // Так линия одинаковой толщины и на прямых, и на дугах — как у кубических стен.
            const float strip = 0.18f;   // ширина каймы: с рамкой текстуры 3/64 даёт ту же толщину линии, что у куба 1.5 м
            const float edgeU = 0.12f;
            var innerPts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = rim[(i - 1 + n) % n].p, cur = rim[i].p, next = rim[(i + 1) % n].p;
                Vector2 n1 = Outward(cur - prev), n2 = Outward(next - cur);
                // На стыке со скрытой стороной кайма идёт по нормали видимой стороны — без зазубрины
                if (inner[(i - 1 + n) % n]) n1 = n2;
                else if (inner[i]) n2 = n1;
                Vector2 avg = (n1 + n2).normalized;
                float miter = Mathf.Max(0.35f, Vector2.Dot(avg, n1));
                innerPts[i] = cur - avg * (strip / miter);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float uo = inner[i] ? 0.5f : 0f, ui = inner[i] ? 0.5f : edgeU;
                int v = verts.Count;
                verts.Add(new Vector3(rim[i].p.x, height, rim[i].p.y)); normals.Add(Vector3.up); uvs.Add(new Vector2(uo, 0.5f));
                verts.Add(new Vector3(innerPts[i].x, height, innerPts[i].y)); normals.Add(Vector3.up); uvs.Add(new Vector2(ui, 0.5f));
                verts.Add(new Vector3(rim[j].p.x, height, rim[j].p.y)); normals.Add(Vector3.up); uvs.Add(new Vector2(uo, 0.5f));
                verts.Add(new Vector3(innerPts[j].x, height, innerPts[j].y)); normals.Add(Vector3.up); uvs.Add(new Vector2(ui, 0.5f));
                tris.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
            }
            // Середина крышки — веер из центра, тёмная (UV в центре текстуры, где рамки нет)
            Vector2 centroid = Vector2.zero;
            foreach (var r in rim) centroid += r.p;
            centroid /= n;
            int c0 = verts.Count;
            verts.Add(new Vector3(centroid.x, height, centroid.y)); normals.Add(Vector3.up); uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < n; i++)
            {
                verts.Add(new Vector3(innerPts[i].x, height, innerPts[i].y));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(0.5f, 0.5f));
            }
            for (int i = 0; i < n; i++) tris.AddRange(new[] { c0, c0 + 1 + (i + 1) % n, c0 + 1 + i });

            var mesh = new Mesh { name = "RoundWall" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Нормаль боковой грани в вершине края: на острых углах — нормаль своего отрезка
        /// (следующего, если вершина в начале отрезка), иначе — средняя.</summary>
        static Vector3 SideNormal(List<RimPoint> rim, int i, bool segmentStart)
        {
            int n = rim.Count;
            Vector2 prev = rim[(i - 1 + n) % n].p, cur = rim[i].p, next = rim[(i + 1) % n].p;
            Vector2 nPrev = Outward(cur - prev), nNext = Outward(next - cur);
            Vector2 nn = rim[i].sharp ? (segmentStart ? nNext : nPrev) : (nPrev + nNext).normalized;
            return new Vector3(nn.x, 0f, nn.y);
        }

        /// <summary>Наружная нормаль отрезка контура, обходимого против часовой стрелки.</summary>
        static Vector2 Outward(Vector2 dir) => new Vector2(dir.y, -dir.x).normalized;
    }
}
