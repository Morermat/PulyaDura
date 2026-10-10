using System;
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
        float[] dists;
        Vector3[] dirs;
        Vector3[] points;
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

        public void Play(Vector3 from, float[] distances, float radius, float waveSpeed, Color c)
        {
            gameObject.SetActive(true);
            origin = new Vector3(from.x, 0.04f, from.z);
            dists = distances;
            int n = distances.Length;
            if (dirs == null || dirs.Length != n)
            {
                dirs = new Vector3[n];
                points = new Vector3[n];
            }
            for (int i = 0; i < n; i++)
            {
                float a = (float)i / n * Mathf.PI * 2f;
                dirs[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
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
            for (int i = 0; i < dirs.Length; i++)
                points[i] = origin + dirs[i] * Mathf.Min(r, dists[i]);
            line.SetPositions(points);

            float k = 1f - t / life;
            var c = color / Mathf.Max(0.01f, color.maxColorComponent);
            c.a = k * k;
            line.startColor = line.endColor = c;
        }
    }
}
