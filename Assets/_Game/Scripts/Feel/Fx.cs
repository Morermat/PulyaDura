using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>Частицы и вспышки света. Всё создаётся кодом и переиспользуется.</summary>
    public class Fx : MonoBehaviour
    {
        public static Fx I { get; private set; }

        ParticleSystem sparks;
        ParticleSystem embers;
        readonly List<FlashLight> flashes = new();

        class FlashLight
        {
            public Light light;
            public float t, life, intensity;
        }

        void Awake() => I = this;

        public void Init()
        {
            sparks = CreateParticles("Sparks", transform, Mats.Fx(Color.white * 2.5f), 3000, 0.06f, gravity: 0.6f);
            embers = CreateParticles("Embers", transform, Mats.Fx(Color.white * 1.6f), 1500, 0.18f, gravity: -0.15f);
            for (int i = 0; i < 10; i++)
            {
                var go = new GameObject("FlashLight");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.enabled = false;
                flashes.Add(new FlashLight { light = l });
            }
        }

        public static ParticleSystem CreateParticles(string name, Transform parent, Material mat, int max, float size, float gravity = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startLifetime = 0.5f;
            main.startSize = size;
            main.gravityModifier = gravity;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var sizeLife = ps.sizeOverLifetime;
            sizeLife.enabled = true;
            sizeLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            ps.Play();
            return ps;
        }

        public void Sparks(Vector3 pos, Vector3 normal, Color color, int count, float speed)
        {
            Color32 c = EchoSystem.Ldr(color);
            for (int i = 0; i < count; i++)
            {
                Vector3 v = (normal.normalized + Random.insideUnitSphere * 0.9f).normalized * speed * Random.Range(0.4f, 1.2f);
                sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = pos,
                    velocity = v,
                    startLifetime = Random.Range(0.15f, 0.4f),
                    startSize = Random.Range(0.04f, 0.09f),
                    startColor = c
                }, 1);
            }
        }

        public void Muzzle(Vector3 pos, Vector3 dir, Color color)
        {
            Sparks(pos, dir, color, 8, 7f);
            embers.Emit(new ParticleSystem.EmitParams { position = pos, velocity = dir * 1.5f, startLifetime = 0.12f, startSize = 0.5f, startColor = EchoSystem.Ldr(color) }, 1);
            LightFlash(pos + Vector3.up * 0.3f, EchoSystem.Ldr(color), 4.5f, 5f, 0.09f);
        }

        public void Explosion(Vector3 pos, Color color)
        {
            Color32 c = EchoSystem.Ldr(color);
            for (int i = 0; i < 40; i++)
            {
                embers.Emit(new ParticleSystem.EmitParams
                {
                    position = pos + Random.insideUnitSphere * 0.3f,
                    velocity = Random.insideUnitSphere * 5f + Vector3.up * 2f,
                    startLifetime = Random.Range(0.4f, 1.1f),
                    startSize = Random.Range(0.12f, 0.35f),
                    startColor = c
                }, 1);
            }
            Sparks(pos, Vector3.up, color, 30, 9f);
            LightFlash(pos + Vector3.up * 0.8f, c, 9f, 8f, 0.45f);
        }

        /// <summary>Обломки из кубиков (ящики).</summary>
        public void Debris(Vector3 pos, Vector3 scale, Material mat, Vector3 dir, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var r = Prims.Part("Chunk", PrimitiveType.Cube, null, pos + Random.insideUnitSphere * 0.3f, scale * Random.Range(0.18f, 0.32f), mat, Random.rotation);
                r.gameObject.layer = Layers.Debris;
                r.gameObject.AddComponent<BoxCollider>();
                var rb = r.gameObject.AddComponent<Rigidbody>();
                rb.mass = 0.2f;
                rb.AddForce((dir.normalized + Random.insideUnitSphere + Vector3.up) * 3f, ForceMode.Impulse);
                r.gameObject.AddComponent<PulyaDura.Debris>().Init(r, GameManager.Cfg.tempWallColor, 2.5f);
            }
        }

        public void LightFlash(Vector3 pos, Color color, float range, float intensity, float duration)
        {
            FlashLight slot = null;
            foreach (var f in flashes)
                if (!f.light.enabled) { slot = f; break; }
            if (slot == null) slot = flashes[0];
            slot.light.transform.position = pos;
            slot.light.color = color;
            slot.light.range = range;
            slot.intensity = intensity;
            slot.light.intensity = intensity;
            slot.life = duration;
            slot.t = 0f;
            slot.light.enabled = true;
        }

        public void Clear()
        {
            sparks.Clear();
            embers.Clear();
            foreach (var f in flashes) f.light.enabled = false;
        }

        void Update()
        {
            foreach (var f in flashes)
            {
                if (!f.light.enabled) continue;
                f.t += Time.deltaTime;
                float k = 1f - f.t / f.life;
                if (k <= 0f) { f.light.enabled = false; continue; }
                f.light.intensity = f.intensity * k * k;
            }
        }
    }
}
