using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Эхо: от точки отскока во все стороны летят искры. Каждая искра летит до первой стены,
    /// и в момент, когда волна до неё доходит, стена вспыхивает контуром. Враг в волне подсвечивается красным.
    /// </summary>
    public class EchoSystem : MonoBehaviour
    {
        public static EchoSystem I { get; private set; }

        struct Pending
        {
            public float time;
            public Collider collider;
            public Vector3 point;
            public Vector3 normal;
            public Color color;
            public float strength;
            public bool remember;
        }

        readonly List<Pending> pending = new();
        readonly Stack<EchoWave> wavePool = new();
        ParticleSystem sparks;
        ParticleSystem pings;
        GameConfig cfg;

        void Awake() => I = this;

        public void Init(GameConfig config)
        {
            cfg = config;
            sparks = Fx.CreateParticles("EchoSparks", transform, Mats.Fx(Color.white * 2.2f), 4000, 0.09f);
            gameObject.AddComponent<EchoGhosts>().Init(cfg);
            pings = Fx.CreateParticles("EchoPings", transform, Mats.Fx(Color.white * 3f), 2000, 0.25f);
        }

        public void Clear()
        {
            pending.Clear();
            EchoGhosts.I.Clear();
            sparks.Clear();
            pings.Clear();
        }

        /// <summary>Волна эха из точки. strength 0..1 — насколько ярко вспыхнут стены.
        /// ring = false — только подсветка стен, без кольца и искр (дёшево: для луча рельсы).</summary>
        public void Emit(Vector3 origin, float radius, Color color, float strength = 1f, bool ring = true)
        {
            origin.y = Ricochet.ShotHeight;
            int rays = ring ? Mathf.Clamp(Mathf.RoundToInt(radius * 9f), 24, 72) : 20;
            float speed = cfg.waveSpeed;
            var dists = new float[rays];

            for (int i = 0; i < rays; i++)
            {
                float a = (i + Random.value * 0.4f) / rays * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float d = radius;
                if (Physics.Raycast(origin, dir, out var hit, radius, Layers.SolidMask, QueryTriggerInteraction.Ignore))
                {
                    d = hit.distance;
                    pending.Add(new Pending
                    {
                        time = Time.time + d / speed,
                        collider = hit.collider,
                        point = hit.point,
                        normal = hit.normal,
                        color = color,
                        strength = strength * Mathf.Lerp(1f, 0.55f, d / radius),
                        remember = true
                    });
                }
                dists[i] = d;

                if (ring && i % 2 == 0)
                {
                    var ep = new ParticleSystem.EmitParams
                    {
                        position = origin + dir * 0.1f,
                        velocity = dir * speed,
                        startLifetime = Mathf.Max(0.05f, d / speed),
                        startSize = 0.07f,
                        startColor = Ldr(color)
                    };
                    sparks.Emit(ep, 1);
                }
            }

            FenceGlow.Ping(origin, radius, color, strength, speed);
            if (!ring) return;
            var wave = wavePool.Count > 0 ? wavePool.Pop() : CreateWave();
            wave.Play(origin, dists, radius, speed, color);
        }

        /// <summary>Кольцо без подсветки: показывает игроку, что он шумит.</summary>
        public void NoiseRing(Vector3 origin, float radius, Color color)
        {
            var wave = wavePool.Count > 0 ? wavePool.Pop() : CreateWave();
            var dists = new float[32];
            for (int i = 0; i < dists.Length; i++) dists[i] = radius;
            wave.Play(origin, dists, radius, radius * 2.5f, color);
        }

        /// <summary>Вспышка всей комнаты от точки (начало комнаты): показывает планировку на пару секунд.</summary>
        public void RevealAll(Vector3 from, float strength)
        {
            foreach (var wall in WallBlock.All)
            {
                float d = Vector3.Distance(Prims.Flat(wall.transform.position), Prims.Flat(from));
                pending.Add(new Pending { time = Time.time + d / cfg.startRevealSpeed, collider = wall.GetComponent<Collider>(), point = wall.transform.position, color = cfg.echoColor, strength = strength });
            }
            foreach (var tank in Tank.All)
            {
                if (tank.Team != Team.Enemy) continue;
                float d = Vector3.Distance(tank.transform.position, from);
                pending.Add(new Pending { time = Time.time + d / cfg.startRevealSpeed, collider = tank.Collider, point = tank.transform.position, color = cfg.enemyReveal, strength = strength });
            }
            var wave = wavePool.Count > 0 ? wavePool.Pop() : CreateWave();
            var dists = new float[64];
            for (int i = 0; i < dists.Length; i++) dists[i] = 40f;
            wave.Play(from + Vector3.up * Ricochet.ShotHeight, dists, 40f, cfg.startRevealSpeed, cfg.echoColor);
        }

        void Update()
        {
            float now = Time.time;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                if (p.time > now) continue;
                // порядок не важен: меняем с последним и удаляем хвост за O(1)
                pending[i] = pending[pending.Count - 1];
                pending.RemoveAt(pending.Count - 1);
                if (p.collider == null) continue;

                if (p.collider.TryGetComponent<WallBlock>(out var wall))
                {
                    wall.Show(p.strength, p.color, p.remember);
                    if (p.normal != Vector3.zero) Ping(p.point, p.normal, p.color);
                }
                else if (p.collider.TryGetComponent<Tank>(out var tank) && !tank.IsDead)
                {
                    if (tank.Visual.Hidden) EchoGhosts.I.Snapshot(tank, tank.Visual.Accent, p.strength);
                    else tank.Visual.Reveal(p.strength);
                }
            }
        }

        void Ping(Vector3 point, Vector3 normal, Color color)
        {
            var c = Ldr(color);
            pings.Emit(new ParticleSystem.EmitParams
            {
                position = point + normal * 0.03f,
                velocity = Vector3.zero,
                startLifetime = 0.45f,
                startSize = 0.28f,
                startColor = c
            }, 1);
            for (int k = 0; k < 2; k++)
            {
                Vector3 v = (normal + Random.insideUnitSphere * 0.8f).normalized * Random.Range(1f, 2.5f);
                sparks.Emit(new ParticleSystem.EmitParams { position = point, velocity = v, startLifetime = 0.3f, startSize = 0.05f, startColor = c }, 1);
            }
        }

        /// <summary>HDR-цвет в обычный: частицы хранят цвет в 8 битах, яркость даёт материал.</summary>
        public static Color32 Ldr(Color c)
        {
            float m = Mathf.Max(0.01f, c.maxColorComponent);
            return new Color(c.r / m, c.g / m, c.b / m, 1f);
        }

        EchoWave CreateWave()
        {
            var go = new GameObject("EchoWave");
            go.transform.SetParent(transform, false);
            var wave = go.AddComponent<EchoWave>();
            wave.Init(cfg.fxMaterial, w => wavePool.Push(w));
            return wave;
        }
    }
}
