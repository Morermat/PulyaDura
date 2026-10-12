using System;
using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Кольцо волны на полу. Каждая точка кольца останавливается у своей стены,
    /// поэтому кольцо «обтекает» геометрию и само по себе подсказывает форму комнаты.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class EchoWave : MonoBehaviour
    {
        LineRenderer line;
        Action<EchoWave> release;
        float[] dists = new float[0];   // свой буфер: волна живёт дольше, чем вызов Emit
        int count;
        Vector3[] dirs;
        Vector3[] points;

        // Направления лучей и буфер точек зависят только от числа лучей — общие для всех волн, без выделений памяти
        static readonly Dictionary<int, (Vector3[] dirs, Vector3[] points)> shapes = new();
        Vector3 origin;
        float speed, life, t;
        Color color;

        public void Init(Material fx, Action<EchoWave> onRelease)
        {
            release = onRelease;
            line = GetComponent<LineRenderer>();
            line.sharedMaterial = fx;
            line.loop = true;
            line.useWorldSpace = true;
            line.widthMultiplier = 0.07f;
            line.numCapVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.TransformZ;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f); // лента лежит на полу
        }

        /// <param name="distances">До какой точки дойдёт каждый луч; копируется, буфер вызывающего можно переиспользовать</param>
        /// <param name="n">Сколько лучей из буфера использовать</param>
        public void Play(Vector3 from, float[] distances, int n, float radius, float waveSpeed, Color c)
        {
            gameObject.SetActive(true);
            origin = new Vector3(from.x, 0.04f, from.z);
            count = n;
            if (dists.Length < n) dists = new float[n];
            Array.Copy(distances, dists, n);
            if (!shapes.TryGetValue(n, out var shape))
            {
                shape = (new Vector3[n], new Vector3[n]);
                for (int i = 0; i < n; i++)
                {
                    float a = (float)i / n * Mathf.PI * 2f;
                    shape.dirs[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                }
                shapes[n] = shape;
            }
            dirs = shape.dirs;
            points = shape.points;
            speed = waveSpeed;
            life = radius / waveSpeed + 0.2f;
            t = 0f;
            color = c;
            line.positionCount = n;
            Tick();
        }

        void Update()
        {
            t += Time.deltaTime;
            if (t >= life)
            {
                gameObject.SetActive(false);
                release(this);
                return;
            }
            Tick();
        }

        void Tick()
        {
            float r = t * speed;
            for (int i = 0; i < count; i++)
                points[i] = origin + dirs[i] * Mathf.Min(r, dists[i]);
            line.SetPositions(points); // линия копирует точки сразу, поэтому буфер общий

            float k = 1f - t / life;
            var c = color / Mathf.Max(0.01f, color.maxColorComponent);
            c.a = k * k;
            line.startColor = line.endColor = c;
        }
    }
}
