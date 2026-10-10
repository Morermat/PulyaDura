using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Расчёт траектории с отскоками от стен. Один и тот же код используют
    /// линия прицела, луч рельсы и ИИ «снайпера», поэтому превью всегда совпадает с реальностью.
    /// </summary>
    public static class Ricochet
    {
        /// <summary>Высота, на которой летят снаряды.</summary>
        public const float ShotHeight = 0.55f;

        /// <summary>
        /// Заполняет points: старт, точки отскоков, конец пути. Возвращает пройденную длину.
        /// hitColliders (если передан) получает коллайдер для каждой точки отскока/упора.
        /// </summary>
        public static float TracePath(Vector3 origin, Vector3 dir, float distance, int bounces, float radius,
            int mask, List<Vector3> points, List<Collider> hitColliders = null)
        {
            points.Clear();
            hitColliders?.Clear();
            points.Add(origin);
            dir = Prims.Flat(dir).normalized;
            float total = 0f;

            for (int i = 0; i <= bounces && distance > 0.001f; i++)
            {
                if (Physics.SphereCast(origin, radius, dir, out var hit, distance, mask, QueryTriggerInteraction.Ignore) && hit.distance > 0f)
                {
                    Vector3 center = origin + dir * hit.distance;
                    points.Add(center);
                    hitColliders?.Add(hit.collider);
                    total += hit.distance;
                    distance -= hit.distance;

                    Vector3 normal = Prims.Flat(hit.normal).normalized;
                    if (normal.sqrMagnitude < 0.01f) normal = -dir;
                    dir = Prims.Flat(Vector3.Reflect(dir, normal)).normalized;
                    origin = center + normal * 0.02f;
                }
                else
                {
                    points.Add(origin + dir * distance);
                    total += distance;
                    return total;
                }
            }
            return total;
        }

        /// <summary>Расстояние от точки до отрезка в плоскости XZ.</summary>
        public static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b, out float along)
        {
            p.y = a.y = b.y = 0f;
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            along = t * Mathf.Sqrt(len2);
            return Vector3.Distance(p, a + ab * t);
        }

        /// <summary>Нет ли стены на прямой между точками.</summary>
        public static bool ClearLine(Vector3 from, Vector3 to, float radius = 0.12f)
        {
            from.y = to.y = ShotHeight;
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.01f) return true;
            return !Physics.SphereCast(from, radius, d / len, out _, len, Layers.WallMask, QueryTriggerInteraction.Ignore);
        }
    }
}
