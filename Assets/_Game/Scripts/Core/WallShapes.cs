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
                int steps = corners[i] >= 1f ? 14 : 8;
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

            // Боковые грани. Светящаяся рамка текстуры рисуется по краям UV: вертикальные линии
            // нужны только на острых углах, поэтому U идёт от 0 до 1 между острыми вершинами,
            // а на круглой колонне (острых нет) — от 0.1 до 0.9, чтобы шов не светился.
            int start = rim.FindIndex(r => r.sharp);
            bool round = start < 0;
            if (round) start = 0;
            var order = new List<int>();
            for (int i = 0; i <= n; i++) order.Add((start + i) % n);

            var runs = new List<(int from, int to)>();
            int runStart = 0;
            for (int i = 1; i < order.Count; i++)
                if (i == order.Count - 1 || rim[order[i]].sharp)
                {
                    runs.Add((runStart, i));
                    runStart = i;
                }

            foreach (var (from, to) in runs)
            {
                float length = 0f;
                for (int i = from; i < to; i++) length += (rim[order[i + 1]].p - rim[order[i]].p).magnitude;
                float acc = 0f;
                for (int i = from; i < to; i++)
                {
                    Vector2 a = rim[order[i]].p, b = rim[order[i + 1]].p;
                    float seg = (b - a).magnitude;
                    float u0 = acc / length, u1 = (acc + seg) / length;
                    acc += seg;
                    if (round) { u0 = 0.1f + u0 * 0.8f; u1 = 0.1f + u1 * 0.8f; }
                    Vector3 na = SideNormal(rim, order[i], true);
                    Vector3 nb = SideNormal(rim, order[i + 1], false);
                    int v = verts.Count;
                    verts.Add(new Vector3(a.x, 0f, a.y)); normals.Add(na); uvs.Add(new Vector2(u0, 0f));
                    verts.Add(new Vector3(b.x, 0f, b.y)); normals.Add(nb); uvs.Add(new Vector2(u1, 0f));
                    verts.Add(new Vector3(b.x, height, b.y)); normals.Add(nb); uvs.Add(new Vector2(u1, 1f));
                    verts.Add(new Vector3(a.x, height, a.y)); normals.Add(na); uvs.Add(new Vector2(u0, 1f));
                    // Обход против часовой сверху → наружу смотрит грань с порядком (v, v+2, v+1)
                    tris.AddRange(new[] { v, v + 2, v + 1, v, v + 3, v + 2 });
                }
            }

            // Крышка: веер из центра, каждая точка края попадает на край UV-квадрата — рамка обводит контур
            Vector2 centroid = Vector2.zero;
            foreach (var r in rim) centroid += r.p;
            centroid /= n;
            int c0 = verts.Count;
            verts.Add(new Vector3(centroid.x, height, centroid.y)); normals.Add(Vector3.up); uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < n; i++)
            {
                Vector2 d = rim[i].p - centroid;
                float m = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y));
                verts.Add(new Vector3(rim[i].p.x, height, rim[i].p.y));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(0.5f, 0.5f) + (m > 1e-5f ? d / m * 0.5f : Vector2.zero));
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
