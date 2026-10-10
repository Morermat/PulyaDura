using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    public class Projectile
    {
        public Tank owner;
        public Team team;
        public TurretConfig cfg;
        public Vector3 pos;
        public Vector3 dir;
        public float distanceLeft;
        public int bouncesLeft;
        public int bouncesDone;
        public bool dead;
        public ProjectileVisual visual;
    }

    /// <summary>
    /// Все снаряды в одном месте. Снаряд кинематический: каждый кадр SphereCast на длину шага,
    /// при касании стены направление отражается через Vector3.Reflect. Физический материал
    /// не используется: он теряет энергию на касательных ударах, а нам нужен предсказуемый рикошет.
    /// </summary>
    public class ProjectileSystem : MonoBehaviour
    {
        public static ProjectileSystem I { get; private set; }

        readonly List<Projectile> active = new();
        readonly Stack<ProjectileVisual> pool = new();
        readonly Stack<RailBeam> beamPool = new();
        readonly RaycastHit[] hits = new RaycastHit[16];
        readonly List<Vector3> railPath = new();
        readonly HashSet<Tank> railHit = new();
        GameConfig cfg;
        int lights;

        public int ActiveCount => active.Count;

        void Awake() => I = this;
        public void Init(GameConfig config) => cfg = config;

        public void ClearAll()
        {
            foreach (var p in active) Release(p);
            active.Clear();
        }

        public void Spawn(Tank owner, TurretConfig turret, Vector3 origin, Vector3 dir, float advance)
        {
            var p = new Projectile
            {
                owner = owner,
                team = owner.Team,
                cfg = turret,
                pos = origin,
                dir = Prims.Flat(dir).normalized,
                distanceLeft = turret.range,
                bouncesLeft = turret.bounces,
            };
            p.visual = pool.Count > 0 ? pool.Pop() : CreateVisual();
            bool withLight = lights < 14;
            if (withLight) lights++;
            p.visual.Show(p.pos, turret.projectileRadius, ColorOf(p), withLight);
            Step(p, advance);
            if (p.dead) { Release(p); return; }
            p.visual.Move(p.pos, ColorOf(p));
            p.visual.ResetTrail();
            active.Add(p);
        }

        /// <summary>Каждый отскок делает пулю ярче: видно, что она «заряжена» и опасна.</summary>
        Color ColorOf(Projectile p)
        {
            Color c = p.team == Team.Enemy ? cfg.bulletEnemy : p.bouncesDone > 0 ? cfg.bulletDanger : cfg.bulletPlayer;
            return c * (1f + cfg.glowGrowthPerBounce * p.bouncesDone);
        }

        float EchoRadius(Projectile p) => p.cfg.echoRadius * (1f + cfg.echoGrowthPerBounce * p.bouncesDone);

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var p = active[i];
                Step(p, p.cfg.projectileSpeed * dt);
                if (p.dead)
                {
                    Release(p);
                    active.RemoveAt(i);
                }
                else
                {
                    p.visual.Move(p.pos, ColorOf(p));
                }
            }
        }

        void Step(Projectile p, float dist)
        {
            int guard = 0;
            while (dist > 0f && !p.dead && guard++ < 8)
            {
                int n = Physics.SphereCastNonAlloc(p.pos, p.cfg.projectileRadius, p.dir, hits, dist, Layers.SolidMask, QueryTriggerInteraction.Ignore);
                int best = -1;
                float bestDist = float.MaxValue;
                for (int k = 0; k < n; k++)
                {
                    var h = hits[k];
                    bool overlapAtStart = h.distance <= 0f && h.point == Vector3.zero;
                    if (h.collider.TryGetComponent<Tank>(out var t))
                    {
                        if (t.IsDead || t.Invulnerable) continue;
                        if (t == p.owner && p.bouncesDone == 0) continue; // своя пуля не бьёт до первого отскока
                    }
                    else if (overlapAtStart) continue;
                    if (h.distance < bestDist) { bestDist = h.distance; best = k; }
                }

                if (best < 0)
                {
                    p.pos += p.dir * dist;
                    p.distanceLeft -= dist;
                    if (p.distanceLeft <= 0f) Die(p, true);
                    return;
                }

                var hit = hits[best];
                p.pos += p.dir * hit.distance;
                p.distanceLeft -= hit.distance;
                dist -= hit.distance;
                Vector3 normal = Prims.Flat(hit.normal).normalized;
                if (normal.sqrMagnitude < 0.01f) normal = -p.dir;

                if (hit.collider.TryGetComponent<Tank>(out var tank))
                {
                    if (tank.MirrorActive)
                    {
                        // Зеркало: пуля отражается и становится пулей владельца зеркала
                        p.dir = Prims.Flat(Vector3.Reflect(p.dir, normal)).normalized;
                        p.pos += normal * 0.05f;
                        p.owner = tank;
                        p.team = tank.Team;
                        p.bouncesDone = 0;
                        Sfx.I.Play(SfxId.Mirror, p.pos, 0.6f, 1.4f);
                        Fx.I.Sparks(p.pos, normal, cfg.bulletPlayer, 10, 5f);
                        EchoSystem.I.Emit(p.pos, EchoRadius(p), cfg.echoColor);
                        continue;
                    }
                    tank.TakeDamage(new DamageInfo { amount = p.cfg.damage, source = p.owner, point = p.pos, direction = p.dir, bounces = p.bouncesDone });
                    EchoSystem.I.Emit(p.pos, EchoRadius(p) * 0.6f, EchoColor(p), 0.8f);
                    Fx.I.Sparks(p.pos, -p.dir, ColorOf(p), 14 + 8 * p.bouncesDone, 6f);
                    Die(p, false);
                    return;
                }

                if (hit.collider.TryGetComponent<Crate>(out var crate))
                {
                    crate.TakeDamage(new DamageInfo { amount = p.cfg.damage, source = p.owner, point = p.pos, direction = p.dir, bounces = p.bouncesDone });
                    EchoSystem.I.Emit(p.pos, EchoRadius(p) * 0.6f, EchoColor(p), 0.7f);
                    Die(p, false);
                    return;
                }

                // Стена: отскок, эхо, звук «тинь» (каждый следующий выше — отскоки слышно на слух)
                EchoSystem.I.Emit(p.pos, EchoRadius(p), EchoColor(p));
                Fx.I.Sparks(p.pos, normal, ColorOf(p), 8 + 4 * p.bouncesDone, 4f);
                GameEvents.RaiseNoise(p.pos, p.cfg.noiseRadius * 0.6f, p.team);
                if (p.bouncesLeft <= 0)
                {
                    Sfx.I.Play(SfxId.Tink, p.pos, 0.5f, 0.7f);
                    Die(p, true);
                    return;
                }
                p.bouncesLeft--;
                p.bouncesDone++;
                p.dir = Prims.Flat(Vector3.Reflect(p.dir, normal)).normalized;
                p.pos += normal * 0.02f;
                p.visual.SetRadius(p.cfg.projectileRadius * (1f + 0.2f * p.bouncesDone));
                Sfx.I.Play(SfxId.Tink, p.pos, 0.7f, 1f + p.bouncesDone * 0.12f);
            }
        }

        Color EchoColor(Projectile p) => p.team == Team.Player ? cfg.echoColor : cfg.enemyEchoColor;

        void Die(Projectile p, bool fizzle)
        {
            p.dead = true;
            if (fizzle) Fx.I.Sparks(p.pos, -p.dir, ColorOf(p), 5, 2f);
        }

        void Release(Projectile p)
        {
            if (p.visual == null) return;
            if (p.visual.HasLight) lights--;
            p.visual.Hide();
            pool.Push(p.visual);
            p.visual = null;
        }

        ProjectileVisual CreateVisual()
        {
            var go = new GameObject("Projectile");
            go.transform.SetParent(transform, false);
            var v = go.AddComponent<ProjectileVisual>();
            v.Init(cfg);
            return v;
        }

        /// <summary>Рельса: мгновенный луч по той же траектории, что у пуль. Пробивает танки.</summary>
        public void FireRail(Tank owner, TurretConfig turret, Vector3 origin, Vector3 dir)
        {
            Ricochet.TracePath(origin, dir, turret.range, turret.bounces, turret.projectileRadius, Layers.WallMask, railPath);
            railHit.Clear();
            Color echo = owner.Team == Team.Player ? cfg.echoColor : cfg.enemyEchoColor;

            for (int s = 0; s < railPath.Count - 1; s++)
            {
                Vector3 a = railPath[s], b = railPath[s + 1];
                Vector3 d = b - a;
                float len = d.magnitude;
                if (len < 0.01f) continue;
                int n = Physics.SphereCastNonAlloc(a, 0.35f, d / len, hits, len, Layers.TankMask, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    if (!hits[k].collider.TryGetComponent<Tank>(out var t) || t.IsDead || t.Invulnerable) continue;
                    if (t == owner && s == 0) continue;
                    if (!railHit.Add(t)) continue;
                    t.TakeDamage(new DamageInfo { amount = turret.damage, source = owner, point = t.transform.position, direction = d / len, bounces = s });
                    Fx.I.Sparks(t.transform.position + Vector3.up * 0.5f, -d / len, cfg.bulletDanger, 18, 7f);
                }
                if (s + 1 < railPath.Count)
                {
                    EchoSystem.I.Emit(b, turret.echoRadius, echo);
                    Fx.I.Sparks(b, -d / len, owner.Team == Team.Player ? cfg.bulletPlayer : cfg.bulletEnemy, 10, 5f);
                }
            }

            var beam = beamPool.Count > 0 ? beamPool.Pop() : CreateBeam();
            beam.Play(railPath, owner.Team == Team.Player ? cfg.bulletPlayer : cfg.bulletEnemy);
            GameEvents.RaiseNoise(railPath[railPath.Count - 1], turret.noiseRadius * 0.6f, owner.Team);
        }

        RailBeam CreateBeam()
        {
            var go = new GameObject("RailBeam");
            go.transform.SetParent(transform, false);
            var b = go.AddComponent<RailBeam>();
            b.Init(cfg.fxMaterial, x => beamPool.Push(x));
            return b;
        }
    }

    public class ProjectileVisual : MonoBehaviour
    {
        MeshRenderer ball;
        TrailRenderer trail;
        Light glow;
        MaterialPropertyBlock mpb;
        Color current;
        public bool HasLight => glow.enabled;

        public void Init(GameConfig cfg)
        {
            mpb = new MaterialPropertyBlock();
            ball = Prims.Part("Ball", PrimitiveType.Sphere, transform, Vector3.zero, Vector3.one, cfg.unlitMaterial);
            ball.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail = gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = cfg.fxMaterial;
            trail.time = 0.22f;
            trail.minVertexDistance = 0.05f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = 2.6f;
            glow.intensity = 2.5f;
            glow.shadows = LightShadows.None;
        }

        public void Show(Vector3 pos, float radius, Color c, bool withLight)
        {
            gameObject.SetActive(true);
            transform.position = pos;
            SetRadius(radius);
            glow.enabled = withLight;
            current = Color.clear;
            SetColor(c);
            trail.Clear();
        }

        public void ResetTrail() => trail.Clear();

        public void SetRadius(float radius)
        {
            ball.transform.localScale = Vector3.one * radius * 2f;
            trail.widthMultiplier = radius * 1.8f;
        }

        public void Move(Vector3 pos, Color c)
        {
            transform.position = pos;
            if (c != current) SetColor(c);
        }

        void SetColor(Color c)
        {
            current = c;
            mpb.SetColor(Mats.BaseColor, c * 1.4f);
            ball.SetPropertyBlock(mpb);
            Color ldr = EchoSystem.Ldr(c);
            trail.startColor = ldr;
            trail.endColor = new Color(ldr.r, ldr.g, ldr.b, 0f);
            glow.color = ldr;
        }

        public void Hide()
        {
            trail.Clear();
            gameObject.SetActive(false);
        }
    }

    public class RailBeam : MonoBehaviour
    {
        LineRenderer line;
        System.Action<RailBeam> release;
        float t;
        Color color;
        const float Life = 0.5f;

        public void Init(Material fx, System.Action<RailBeam> onRelease)
        {
            release = onRelease;
            line = gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = fx;
            line.useWorldSpace = true;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public void Play(List<Vector3> points, Color c)
        {
            gameObject.SetActive(true);
            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
            color = EchoSystem.Ldr(c);
            t = 0f;
            Tick();
        }

        void Update()
        {
            t += Time.deltaTime;
            if (t >= Life)
            {
                gameObject.SetActive(false);
                release(this);
                return;
            }
            Tick();
        }

        void Tick()
        {
            float k = 1f - t / Life;
            line.widthMultiplier = 0.35f * k + 0.03f;
            var c = color;
            c.a = k;
            line.startColor = line.endColor = c;
        }
    }
}
