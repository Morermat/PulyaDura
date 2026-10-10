using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PulyaDura
{
    /// <summary>
    /// «Призраки» эха. Когда волна задевает невидимого врага, на этом месте остаётся
    /// светящийся слепок танка, а за ним — точки его недавнего пути. Сам танк едет дальше,
    /// поэтому игрок видит, где враг БЫЛ и куда двигался, но не где он сейчас.
    /// Слепки рисуются без GameObject-ов через Graphics.RenderMesh.
    /// </summary>
    public class EchoGhosts : MonoBehaviour
    {
        public static EchoGhosts I { get; private set; }

        class Ghost
        {
            public readonly List<(Mesh mesh, Matrix4x4 matrix)> parts = new();
            public readonly MaterialPropertyBlock props = new();
            public Color color;
            public float t, life;
        }

        readonly List<Ghost> ghosts = new();
        readonly Stack<Ghost> pool = new();
        readonly Dictionary<Tank, float> lastSnapshot = new();
        GameConfig cfg;
        Material material;
        ParticleSystem trailDots;

        public int Count => ghosts.Count;
        const int MaxGhosts = 24;

        /// <summary>Когда эхо в последний раз сняло «призрака» с этого танка (для QA и подсказок).</summary>
        public float LastSnapshotOf(Tank t) => t != null && lastSnapshot.TryGetValue(t, out float v) ? v : -1f;

        void Awake() => I = this;

        public void Init(GameConfig config)
        {
            cfg = config;
            material = cfg.fxMaterial;
            trailDots = Fx.CreateParticles("TrailDots", transform, Mats.Fx(Color.white * 1.6f), 2000, 0.16f);
        }

        /// <summary>Слепок танка в текущей позе. Не чаще раза в 0,15 с на танк, чтобы волны не плодили копии.</summary>
        public void Snapshot(Tank tank, Color color, float strength)
        {
            if (tank == null) return;
            if (lastSnapshot.TryGetValue(tank, out float last) && Time.time - last < 0.25f) return;
            lastSnapshot[tank] = Time.time;
            // Ограничение на экране: самый старый призрак уступает место новому
            if (ghosts.Count >= MaxGhosts)
            {
                int oldest = 0;
                for (int i = 1; i < ghosts.Count; i++) if (ghosts[i].t > ghosts[oldest].t) oldest = i;
                pool.Push(ghosts[oldest]);
                ghosts[oldest] = ghosts[ghosts.Count - 1];
                ghosts.RemoveAt(ghosts.Count - 1);
            }

            var g = pool.Count > 0 ? pool.Pop() : new Ghost();
            g.parts.Clear();
            tank.Visual.CollectGhostParts(g.parts);
            g.color = (Color)EchoSystem.Ldr(color) * (1.3f + strength);
            g.t = 0f;
            g.life = cfg.revealFade * 0.75f;
            ghosts.Add(g);

            // След: точки недавнего пути, старые бледнее
            if (tank.Brain == null) return;
            Color32 c = EchoSystem.Ldr(color);
            tank.Brain.ForEachTrailPoint((pos, age) =>
            {
                if (age > 3.5f || age < 0.2f) return;
                float k = 1f - age / 3.5f;
                trailDots.Emit(new ParticleSystem.EmitParams
                {
                    position = pos + Vector3.up * 0.06f,
                    velocity = Vector3.zero,
                    startLifetime = g.life * 0.8f,
                    startSize = 0.1f + 0.12f * k,
                    startColor = new Color32(c.r, c.g, c.b, (byte)(255 * k)),
                }, 1);
            });
        }

        public void Clear()
        {
            foreach (var g in ghosts) pool.Push(g);
            ghosts.Clear();
            lastSnapshot.Clear();
            if (trailDots != null) trailDots.Clear();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                var g = ghosts[i];
                g.t += dt;
                if (g.t >= g.life)
                {
                    pool.Push(g);
                    ghosts[i] = ghosts[ghosts.Count - 1];
                    ghosts.RemoveAt(ghosts.Count - 1);
                    continue;
                }
                float k = 1f - g.t / g.life;
                // Короткая вспышка в начале, потом ровное угасание
                float flash = g.t < 0.08f ? 1.6f : 1f;
                var c = g.color * (k * flash);
                c.a = k;
                g.props.SetColor(Mats.BaseColor, c);
                var rp = new RenderParams(material)
                {
                    matProps = g.props,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                };
                foreach (var (mesh, matrix) in g.parts) Graphics.RenderMesh(rp, mesh, 0, matrix);
            }
        }
    }
}
