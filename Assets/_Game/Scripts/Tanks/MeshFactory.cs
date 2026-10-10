using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Генератор простых угловатых мешей для танков: «лофт» между двумя выпуклыми контурами.
    /// Так получаются наклонные бронелисты, сужающиеся башни и гусеницы со скошенными концами.
    /// Грани плоские (flat shading), UV по доминирующей оси — текстура траков бежит вдоль Z.
    /// </summary>
    public static class MeshFactory
    {
        static readonly Dictionary<string, Mesh> cache = new();

        /// <summary>Боковой профиль (z, y), вытянутый по ширине вдоль X.</summary>
        public static Mesh Extrude(string key, Vector2[] profileZY, float width)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            int n = profileZY.Length;
            var a = new Vector3[n];
            var b = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                a[i] = new Vector3(-width * 0.5f, profileZY[i].y, profileZY[i].x);
                b[i] = new Vector3(width * 0.5f, profileZY[i].y, profileZY[i].x);
            }
            return cache[key] = Loft(key, a, b);
        }

        /// <summary>Контур сверху (x, z), поднятый на высоту h. Верх уменьшен в topScale раз и сдвинут на topShiftZ.</summary>
        public static Mesh Taper(string key, Vector2[] polyXZ, float height, float topScale, float topShiftZ = 0f)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            int n = polyXZ.Length;
            var a = new Vector3[n];
            var b = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                a[i] = new Vector3(polyXZ[i].x, 0f, polyXZ[i].y);
                b[i] = new Vector3(polyXZ[i].x * topScale, height, polyXZ[i].y * topScale + topShiftZ);
            }
            return cache[key] = Loft(key, a, b);
        }

        /// <summary>Правильный многоугольник (вид сверху), вытянутый по Z коэффициентом stretch.</summary>
        public static Vector2[] Polygon(int sides, float radiusX, float radiusZ, float rotationDeg = 0f)
        {
            var p = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (rotationDeg + 360f * i / sides) * Mathf.Deg2Rad;
                p[i] = new Vector2(Mathf.Cos(a) * radiusX, Mathf.Sin(a) * radiusZ);
            }
            return p;
        }

        /// <summary>Прямоугольник со срезанными углами (вид сверху).</summary>
        public static Vector2[] Chamfered(float w, float l, float c)
        {
            float x = w * 0.5f, z = l * 0.5f;
            return new[]
            {
                new Vector2(-x + c, -z), new Vector2(x - c, -z), new Vector2(x, -z + c), new Vector2(x, z - c),
                new Vector2(x - c, z), new Vector2(-x + c, z), new Vector2(-x, z - c), new Vector2(-x, -z + c),
            };
        }

        static Mesh Loft(string name, Vector3[] a, Vector3[] b)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            Vector3 center = Vector3.zero;
            foreach (var v in a) center += v;
            foreach (var v in b) center += v;
            center /= a.Length + b.Length;

            int n = a.Length;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Face(new[] { a[i], a[j], b[j], b[i] }, center, verts, normals, uvs, tris);
            }
            Face(a, center, verts, normals, uvs, tris);
            Face(b, center, verts, normals, uvs, tris);

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Выпуклая грань: веер треугольников, порядок обхода — наружу от центра меша.</summary>
        static void Face(Vector3[] poly, Vector3 meshCenter, List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
        {
            Vector3 fc = Vector3.zero;
            foreach (var v in poly) fc += v;
            fc /= poly.Length;

            Vector3 normal = Vector3.zero;
            for (int i = 1; i < poly.Length - 1 && normal.sqrMagnitude < 1e-10f; i++)
                normal = Vector3.Cross(poly[i] - poly[0], poly[i + 1] - poly[0]);
            if (normal.sqrMagnitude < 1e-10f) return; // вырожденная грань
            bool flip = Vector3.Dot(normal, fc - meshCenter) < 0f;
            normal = (flip ? -normal : normal).normalized;

            int start = verts.Count;
            Vector3 an = new(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
            foreach (var v in poly)
            {
                verts.Add(v);
                normals.Add(normal);
                // u всегда вдоль Z (там бегут траки), v — по второй оси грани
                uvs.Add(an.z >= an.x && an.z >= an.y ? new Vector2(v.x, v.y) : an.x >= an.y ? new Vector2(v.z, v.y) : new Vector2(v.z, v.x));
            }
            for (int i = 1; i < poly.Length - 1; i++)
            {
                if (flip) { tris.Add(start); tris.Add(start + i + 1); tris.Add(start + i); }
                else { tris.Add(start); tris.Add(start + i); tris.Add(start + i + 1); }
            }
        }
    }
}
