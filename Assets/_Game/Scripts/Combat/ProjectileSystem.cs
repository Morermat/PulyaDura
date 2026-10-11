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
        public Vector3 prevPos;
        public Vector3 dir;
        public float distanceLeft;
        public int bouncesLeft;
        public int bouncesDone;
        public bool dead;
        public bool power;          // ваншот: урон 99
        public bool mark;           // метка: попадание делает врага видимым
        public int ladder;          // ступень лестницы тонов, с которой выпущена пуля
        public float portalCooldown;
        public ProjectileVisual visual;
    }

    /// <summary>Модификаторы выстрела от бонусов.</summary>
    public struct ShotMods
    {
        public bool power;
        public bool mark;
        public int ladder;
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


        /// <summary>Урон выстрела: у слабых врагов (новичок, турель) свой, меньше, чем у пушки.</summary>
        static int DamageOf(Tank owner, TurretConfig turret) =>
            owner != null && owner.Enemy != null && owner.Enemy.damageOverride > 0 ? owner.Enemy.damageOverride : turret.damage;

        /// <summary>Высота отскока: у игрока продолжает лестницу тонов от ступени выстрела, у врагов — своя гамма октавой ниже.</summary>
        public static float BouncePitch(int bounce, Team team, int ladder = 0) =>
            team == Team.Player ? Sfx.LadderPitch(ladder + bounce) : Sfx.LadderPitch(bounce) * 0.5f;

        /// <summary>Кольцо шрапнели (красная бочка). Владелец — тот, кто подорвал: ему и засчитываются убийства.</summary>
        public void SpawnShrapnel(Tank source, Vector3 center, TurretConfig shrapnel, int count)
        {
            float offset = Random.value * 360f;
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, offset + i * 360f / count, 0f) * Vector3.forward;
                Spawn(source, shrapnel, center, dir, 0.3f, default, source != null ? source.Team : Team.Enemy);
            }
        }

        public void Spawn(Tank owner, TurretConfig turret, Vector3 origin, Vector3 dir, float advance, ShotMods mods = default, Team? team = null)
        {
            var p = new Projectile
            {
                owner = owner,
                team = team ?? (owner != null ? owner.Team : Team.Enemy),
                power = mods.power,
                mark = mods.mark,
                ladder = mods.ladder,
                cfg = turret,
                pos = origin,
                prevPos = origin,
                dir = Prims.Flat(dir).normalized,
                distanceLeft = turret.range,
                bouncesLeft = turret.bounces,
            };
            p.visual = pool.Count > 0 ? pool.Pop() : CreateVisual();
            bool withLight = lights < 14;
            if (withLight) lights++;
            p.visual.Show(p.pos, turret.projectileRadius * (p.power ? 1.7f : 1f), ColorOf(p), withLight);
            Step(p, advance);
            if (p.dead) { Release(p); return; }
            p.prevPos = p.pos;
            p.visual.Move(p.pos, ColorOf(p));
            p.visual.ResetTrail();
            active.Add(p);
        }

        /// <summary>Каждый отскок делает пулю ярче: видно, что она «заряжена» и опасна.</summary>
        Color ColorOf(Projectile p)
        {
            if (p.power) return new Color(3.5f, 0.5f, 0.3f) * (1f + cfg.glowGrowthPerBounce * p.bouncesDone);
            if (p.mark) return new Color(2.8f, 2.4f, 0.3f) * (1f + cfg.glowGrowthPerBounce * p.bouncesDone);
            Color c = p.team == Team.Enemy ? cfg.bulletEnemy : p.bouncesDone > 0 ? cfg.bulletDanger : cfg.bulletPlayer;
            return c * (1f + cfg.glowGrowthPerBounce * p.bouncesDone);
        }

        float EchoRadius(Projectile p) => p.cfg.echoRadius * (1f + cfg.echoGrowthPerBounce * p.bouncesDone);

        void Update()
        {
            float dt = Time.deltaTime;
            // По индексу: пуля может породить новые (бочка → шрапнель) прямо во время шага
            int count = active.Count;
            for (int i = 0; i < count; i++)
            {
                var p = active[i];
                p.prevPos = p.pos;
                Step(p, p.cfg.projectileSpeed * dt);
                p.portalCooldown -= dt;
                if (!p.dead) TryTeleport(p);
            }
            CancelCollidingShells();
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var p = active[i];
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

        /// <summary>
        /// Снаряды разных владельцев гасят друг друга (как в Wii Tanks): летящую в тебя пулю можно сбить.
        /// Проверяем сближение за весь кадр, а не только конечные точки, чтобы встречные пули не проскакивали.
        /// </summary>
        void CancelCollidingShells()
        {
            for (int i = 0; i < active.Count; i++)
            {
                var a = active[i];
                if (a.dead) continue;
                for (int j = i + 1; j < active.Count; j++)
                {
                    var b = active[j];
                    if (b.dead || a.owner == b.owner) continue;
                    Vector3 p0 = a.prevPos - b.prevPos;
                    Vector3 v = (a.pos - a.prevPos) - (b.pos - b.prevPos);
                    float t = v.sqrMagnitude < 1e-6f ? 1f : Mathf.Clamp01(-Vector3.Dot(p0, v) / v.sqrMagnitude);
                    float r = a.cfg.projectileRadius + b.cfg.projectileRadius + 0.05f;
                    if ((p0 + v * t).sqrMagnitude > r * r) continue;

                    Vector3 at = Vector3.Lerp(a.prevPos, a.pos, t);
                    Die(a, false);
                    Die(b, false);
                    Fx.I.Sparks(at, Vector3.up, Color.white * 2f, 18, 6f);
                    Fx.I.LightFlash(at + Vector3.up * 0.3f, Color.white, 3.5f, 4f, 0.12f);
                    EchoSystem.I.Emit(at, 3f, cfg.echoColor, 0.7f);
                    Sfx.I.Play(SfxId.Hit, at, 0.7f, 1.6f);
                    break;
                }
            }
        }

        /// <summary>Пуля, пролетевшая через центр портала, выходит из парного портала с тем же направлением.</summary>
        void TryTeleport(Projectile p)
        {
            if (p.portalCooldown > 0f) return;
            foreach (var t in Teleporter.All)
            {
                if (t.Partner == null) continue;
                if (Ricochet.DistanceToSegment(t.transform.position, p.prevPos, p.pos, out _) > Teleporter.Radius * 0.8f) continue;
                Vector3 exit = t.Partner.transform.position + p.dir * (Teleporter.Radius + 0.15f);
                p.pos = p.prevPos = new Vector3(exit.x, Ricochet.ShotHeight, exit.z);
                p.portalCooldown = 0.35f;
                p.visual.Move(p.pos, ColorOf(p));
                p.visual.ResetTrail();
                t.Flash(0.6f);
                return;
            }
        }

        void Step(Projectile p, float dist)
        {
            int guard = 0;
            while (dist > 0f && !p.dead && guard++ < 8)
            {
                if (!FindHit(p, dist, out var hit))
                {
                    p.pos += p.dir * dist;
                    p.distanceLeft -= dist;
                    if (p.distanceLeft <= 0f) Die(p, true);
                    return;
                }

                p.pos += p.dir * hit.distance;
                p.distanceLeft -= hit.distance;
                dist -= hit.distance;
                Vector3 normal = Prims.Flat(hit.normal).normalized;
                if (normal.sqrMagnitude < 0.01f) normal = -p.dir;

                if (hit.collider.TryGetComponent<Tank>(out var tank)) HitTank(p, tank, normal);
                else if (hit.collider.TryGetComponent<IDamageable>(out var breakable)) HitBreakable(p, breakable);
                else HitWall(p, normal);
            }
        }

        /// <summary>Ближайшее препятствие на пути. Своя пуля не бьёт владельца до первого отскока.</summary>
        bool FindHit(Projectile p, float dist, out RaycastHit result)
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
                    if (t == p.owner && p.bouncesDone == 0) continue;
                }
                else if (overlapAtStart) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = k; }
            }
            result = best >= 0 ? hits[best] : default;
            return best >= 0;
        }

        void HitTank(Projectile p, Tank tank, Vector3 normal)
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
                return;
            }
            if (tank.ShieldFacing(p.dir))
            {
                // Щит на башне: пуля отлетает как от стены и теперь опасна и для стрелка
                p.dir = Prims.Flat(Vector3.Reflect(p.dir, tank.Visual.TurretPivot.forward)).normalized;
                p.pos += p.dir * 0.1f;
                p.bouncesDone++;
                Sfx.I.Play(SfxId.Clang, p.pos, 0.9f, Random.Range(0.95f, 1.05f));
                Fx.I.Sparks(p.pos, -p.dir, Color.white * 2f, 14, 6f);
                EchoSystem.I.Emit(p.pos, EchoRadius(p), cfg.echoColor, 0.8f);
                GameManager.I.UI.Floating(p.pos + Vector3.up, "ЩИТ", UiKit.Accent, 34);
                return;
            }
            tank.TakeDamage(new DamageInfo { amount = p.power ? 99 : DamageOf(p.owner, p.cfg), source = p.owner, point = p.pos, direction = p.dir, bounces = p.bouncesDone });
            if (p.mark && !tank.IsDead) tank.Visual.Mark(cfg.markDuration);
            if ((p.power || p.mark) && p.owner != null && p.owner != tank) p.owner.Weapon.ConsumeMods(p.power, p.mark);
            EchoSystem.I.Emit(p.pos, EchoRadius(p) * 0.6f, EchoColor(p), 0.8f);
            Fx.I.Sparks(p.pos, -p.dir, ColorOf(p), 14 + 8 * p.bouncesDone, 6f);
            Die(p, false);
        }

        /// <summary>Ящик, белый ящик или бочка: всё, что ломается, но не танк.</summary>
        void HitBreakable(Projectile p, IDamageable target)
        {
            target.TakeDamage(new DamageInfo { amount = DamageOf(p.owner, p.cfg), source = p.owner, point = p.pos, direction = p.dir, bounces = p.bouncesDone });
            EchoSystem.I.Emit(p.pos, EchoRadius(p) * 0.6f, EchoColor(p), 0.7f);
            Die(p, false);
        }

        /// <summary>Стена: эхо, шум, отскок. Каждый следующий «тинь» выше — отскоки слышно на слух.</summary>
        void HitWall(Projectile p, Vector3 normal)
        {
            EchoSystem.I.Emit(p.pos, EchoRadius(p), EchoColor(p));
            Fx.I.Sparks(p.pos, normal, ColorOf(p), 8 + 4 * p.bouncesDone, 4f);
            GameEvents.RaiseNoise(p.pos, p.cfg.noiseRadius * 0.6f, p.team);
            if (p.bouncesLeft <= 0)
            {
                Sfx.I.Play(SfxId.Tink, p.pos, 0.45f, BouncePitch(p.bouncesDone + 1, p.team, p.ladder) * 0.5f);
                Die(p, true);
                return;
            }
            p.bouncesLeft--;
            p.bouncesDone++;
            p.dir = Prims.Flat(Vector3.Reflect(p.dir, normal)).normalized;
            p.pos += normal * 0.02f;
            p.visual.SetRadius(p.cfg.projectileRadius * (1f + 0.2f * p.bouncesDone));
            Sfx.I.Play(SfxId.Tink, p.pos, 0.75f, BouncePitch(p.bouncesDone, p.team, p.ladder));
        }

        /// <summary>Сколько снарядов сейчас в полёте (для QA).</summary>
        public int CountAlive()
        {
            int n = 0;
            foreach (var p in active) if (!p.dead) n++;
            return n;
        }

        void Die(Projectile p, bool fizzle)
        {
            p.dead = true;
            if (fizzle) Fx.I.Sparks(p.pos, -p.dir, ColorOf(p), 5, 2f);
        }

        Color EchoColor(Projectile p) => p.team == Team.Player ? cfg.echoColor : cfg.enemyEchoColor;

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
        public void FireRail(Tank owner, TurretConfig turret, Vector3 origin, Vector3 dir, ShotMods mods = default)
        {
            // Ящики и бочки на пути луча разлетаются с одного выстрела, и луч идёт дальше сквозь них
            for (int pass = 0; pass < 6; pass++)
            {
                Ricochet.TracePath(origin, dir, turret.range, turret.bounces, turret.projectileRadius, Layers.WallMask, railPath);
                if (!BreakOnPath(owner, turret, railPath)) break;
            }
            railHit.Clear();
            Color echo = owner.Team == Team.Player ? cfg.echoColor : cfg.enemyEchoColor;
            bool consumed = false;

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
                    if (t.ShieldFacing(d / len))
                    {
                        Sfx.I.Play(SfxId.Clang, t.transform.position, 1f, 0.8f);
                        Fx.I.Sparks(t.transform.position + Vector3.up * 0.5f, -d / len, Color.white * 2f, 18, 7f);
                        continue;
                    }
                    t.TakeDamage(new DamageInfo { amount = mods.power ? 99 : DamageOf(owner, turret), source = owner, point = t.transform.position, direction = d / len, bounces = s });
                    if (mods.mark && !t.IsDead) t.Visual.Mark(cfg.markDuration);
                    if ((mods.power || mods.mark) && t != owner && !consumed)
                    {
                        owner.Weapon.ConsumeMods(mods.power, mods.mark);
                        consumed = true;
                    }
                    Fx.I.Sparks(t.transform.position + Vector3.up * 0.5f, -d / len, cfg.bulletDanger, 18, 7f);
                }
                // Луч светит на всём пути: небольшие волны вдоль отрезка и большая — в точке отскока
                for (float along = 2f; along < len - 1f; along += 4f)
                    EchoSystem.I.Emit(a + d / len * along, 3.6f, echo, 0.75f, ring: false);
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

        /// <summary>Первый ящик или бочка, в которые упирается луч, ломается целиком. true — путь надо пересчитать.</summary>
        bool BreakOnPath(Tank owner, TurretConfig turret, List<Vector3> path)
        {
            for (int s = 0; s < path.Count - 1; s++)
            {
                Vector3 d = path[s + 1] - path[s];
                float len = d.magnitude;
                if (len < 0.01f) continue;
                if (!Physics.SphereCast(path[s], turret.projectileRadius, d / len, out var hit, len + 0.1f, Layers.WallMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.collider.GetComponent<Tank>() != null || !hit.collider.TryGetComponent<IDamageable>(out var target)) continue;
                target.TakeDamage(new DamageInfo { amount = 99, source = owner, point = hit.point, direction = d / len });
                return true;
            }
            return false;
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
