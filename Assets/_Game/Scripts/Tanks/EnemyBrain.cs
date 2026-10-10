using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace PulyaDura
{
    /// <summary>
    /// ИИ врага. Враг, как и игрок, почти слеп. Он узнаёт, где игрок, если:
    /// видит свет его фар вблизи (при тихом ходе — вдвое ближе), слышит выстрел или отскок,
    /// получает попадание или чувствует танк вплотную.
    ///
    /// Состояния: Патруль → (узнал) → Бой → (потерял) → Поиск → Патруль.
    /// В бою: короткая реакция, упреждение по видимой цели, выстрел через отскок (снайпер),
    /// подавляющий огонь на звук, не стреляет, если на линии огня свой. Охотник держит дистанцию и стрейфит.
    /// </summary>
    public class EnemyBrain : MonoBehaviour
    {
        enum Mode { Patrol, Fight, Search }

        Tank tank;
        EnemyConfig cfg;
        NavMeshAgent agent;
        Mode mode = Mode.Patrol;

        // Что враг знает об игроке
        Vector3 targetPos, targetVel;
        float targetTime = -999f, seenTime = -999f, reactUntil;
        bool HasTarget => Time.time - targetTime < cfg.memory;
        bool SeesTarget => Time.time - seenTime < 0.3f;
        public bool Alerted => HasTarget;

        float turretYaw, fireTimer, repathTimer, bankTimer, patrolTimer, strafeTimer, idlePhase;
        int strafeSide = 1, searchPointsLeft;
        Vector3 searchCenter;
        Vector3? bankDir;
        readonly List<Vector3> path = new();
        readonly RaycastHit[] hits = new RaycastHit[8];

        // След: где танк был недавно. Эхо показывает его вместе с «призраком»
        const int TrailSize = 10;
        readonly Vector3[] trail = new Vector3[TrailSize];
        readonly float[] trailTime = new float[TrailSize];
        int trailHead, trailCount;
        float trailTimer;

        public void Init(Tank owner)
        {
            tank = owner;
            cfg = owner.Enemy;
            turretYaw = transform.eulerAngles.y;
            fireTimer = Random.Range(1f, 1.8f); // фора в начале комнаты
            idlePhase = Random.value * 10f;

            if (cfg.behaviour == EnemyBehaviour.Hunter)
            {
                agent = gameObject.AddComponent<NavMeshAgent>();
                agent.speed = owner.Hull.speed * cfg.speedMultiplier;
                agent.angularSpeed = owner.Hull.turnSpeed * 0.5f;
                agent.acceleration = 16f;
                agent.radius = Mathf.Max(owner.Hull.size.x, owner.Hull.size.y) * 0.45f;
                agent.height = 1f;
                agent.stoppingDistance = 0.4f;
                agent.autoBraking = true;
            }

            GameEvents.Noise += OnNoise;
            GameEvents.TankDamaged += OnDamaged;
        }

        void OnDestroy()
        {
            GameEvents.Noise -= OnNoise;
            GameEvents.TankDamaged -= OnDamaged;
        }

        // ---------- Восприятие ----------

        void OnNoise(Vector3 pos, float radius, Team team)
        {
            if (team == Team.Enemy || cfg.behaviour == EnemyBehaviour.Dummy) return;
            float heard = radius * cfg.hearing;
            if (Prims.Flat(pos - transform.position).sqrMagnitude > heard * heard) return;
            Remember(pos, Vector3.zero, false);
        }

        void OnDamaged(Tank victim, DamageInfo info)
        {
            if (victim != tank || info.source == null || info.source.Team != Team.Player) return;
            Remember(info.source.transform.position, Vector3.zero, false);
        }

        void Sense(Tank player)
        {
            if (player == null || player.IsDead) return;
            Vector3 pos = player.transform.position;
            float dist = Prims.Flat(pos - transform.position).magnitude;
            Vector3 vel = player.Body != null ? Prims.Flat(player.Body.linearVelocity) : Vector3.zero;

            if (dist < cfg.senseRadius)
            {
                Remember(pos, vel, true);
                return;
            }
            // Фары игрока видны издалека, но только по прямой
            var pc = player.Controller;
            float sight = cfg.sight * (pc != null && pc.IsSneaking ? 0.5f : 1f);
            if (dist < sight && Ricochet.ClearLine(tank.ShotOrigin, player.ShotOrigin, 0.1f))
                Remember(pos, vel, true);
        }

        void Remember(Vector3 pos, Vector3 vel, bool seen)
        {
            if (!HasTarget) reactUntil = Time.time + Random.Range(0.3f, 0.6f); // первая реакция не мгновенна
            targetPos = pos;
            targetVel = vel;
            targetTime = Time.time;
            if (seen) seenTime = Time.time;
            bankTimer = 0f;
            mode = Mode.Fight;
        }

        // ---------- Цикл ----------

        void Update()
        {
            if (tank.IsDead) return;
            bool active = GameManager.I.State == GameState.Playing;
            if (agent != null) agent.isStopped = !active;
            if (!active) return;

            RecordTrail();

            if (cfg.behaviour == EnemyBehaviour.Dummy)
            {
                turretYaw += Time.deltaTime * 20f;
                tank.Visual.SetTurretYaw(turretYaw);
                return;
            }

            Sense(GameManager.I.Player);

            if (mode == Mode.Fight && !HasTarget) BeginSearch();
            switch (mode)
            {
                case Mode.Fight: Fight(); break;
                case Mode.Search: Search(); break;
                default: Patrol(); break;
            }
            tank.Visual.SetTurretYaw(turretYaw);
        }

        void Fight()
        {
            Vector3 origin = tank.ShotOrigin;
            float dist = Prims.Flat(targetPos - transform.position).magnitude;

            // Упреждение: целимся туда, где игрок будет, когда долетит снаряд
            Vector3 aim = targetPos;
            if (SeesTarget && cfg.lead > 0f && tank.Turret.mode == FireMode.Projectile)
                aim += targetVel * (dist / Mathf.Max(1f, tank.Turret.projectileSpeed)) * cfg.lead;

            bool clear = Ricochet.ClearLine(origin, aim, tank.Turret.projectileRadius);
            Vector3 desired = Prims.Flat(aim - origin).normalized;
            bool canShoot = clear;

            if (!clear && cfg.bankShots)
            {
                bankTimer -= Time.deltaTime;
                if (bankTimer <= 0f)
                {
                    bankTimer = 0.5f;
                    bankDir = FindBankShot(origin, aim);
                }
                if (bankDir.HasValue) { desired = bankDir.Value; canShoot = true; }
            }

            // Подавляющий огонь: стреляет на свежий звук, если выстрел не вернётся в него самого
            if (!canShoot && cfg.blindFire && Time.time - targetTime < 1.5f && SafeBlindShot(origin, desired))
                canShoot = true;

            if (canShoot && FriendlyInLine(origin, desired, dist)) canShoot = false;
            if (Time.time < reactUntil) canShoot = false;

            float wantYaw = Quaternion.LookRotation(desired).eulerAngles.y;
            turretYaw = Mathf.MoveTowardsAngle(turretYaw, wantYaw, cfg.turretTurnSpeed * Time.deltaTime);
            bool aimed = Mathf.Abs(Mathf.DeltaAngle(turretYaw, wantYaw)) < 4f;

            fireTimer -= Time.deltaTime;
            tank.Visual.Tell(canShoot ? 1f - Mathf.Clamp01(fireTimer / 0.45f) : 0f);
            if (canShoot && aimed && fireTimer <= 0f && tank.Weapon.Ready)
            {
                // Вблизи и по видимой цели точнее, вслепую — разброс больше
                float error = cfg.aimError * (SeesTarget ? 1f : 1.6f);
                float saved = turretYaw;
                tank.Visual.SetTurretYaw(turretYaw + Random.Range(-error, error));
                tank.Weapon.TryFire();
                tank.Visual.SetTurretYaw(saved);
                fireTimer = cfg.fireInterval * Random.Range(0.85f, 1.2f);
            }

            if (agent != null) MoveInFight(dist, clear);
        }

        /// <summary>Охотник: сближается, если цель далеко или за стеной; отступает вплотную; на своей дистанции стрейфит.</summary>
        void MoveInFight(float dist, bool clear)
        {
            repathTimer -= Time.deltaTime;
            strafeTimer -= Time.deltaTime;
            if (repathTimer > 0f) return;
            repathTimer = 0.3f;

            Vector3 toTarget = Prims.Flat(targetPos - transform.position).normalized;
            if (!clear || dist > cfg.preferRange + 2f)
            {
                MoveTo(targetPos);
                return;
            }
            if (dist < cfg.preferRange - 2.5f)
            {
                MoveTo(transform.position - toTarget * 3f);
                return;
            }
            if (strafeTimer <= 0f)
            {
                strafeSide = -strafeSide;
                strafeTimer = Random.Range(1.2f, 2.2f);
            }
            MoveTo(transform.position + Vector3.Cross(Vector3.up, toTarget) * (strafeSide * 2.5f));
        }

        void BeginSearch()
        {
            mode = Mode.Search;
            searchCenter = targetPos;
            searchPointsLeft = 3;
            patrolTimer = 0f;
            tank.Visual.Tell(0f);
        }

        /// <summary>Потерял игрока: едет к последней точке и осматривается вокруг неё, потом возвращается в патруль.</summary>
        void Search()
        {
            turretYaw += 70f * Time.deltaTime;
            if (agent == null)
            {
                patrolTimer += Time.deltaTime;
                if (patrolTimer > 4f) mode = Mode.Patrol;
                return;
            }
            patrolTimer -= Time.deltaTime;
            if (patrolTimer > 0f && agent.hasPath && agent.remainingDistance > 0.6f) return;
            if (searchPointsLeft-- <= 0) { mode = Mode.Patrol; return; }
            patrolTimer = 2.5f;
            MoveTo(searchCenter + Random.insideUnitSphere.normalized * 3.5f);
        }

        void Patrol()
        {
            tank.Visual.Tell(0f);
            float hullYaw = transform.eulerAngles.y;
            turretYaw = Mathf.MoveTowardsAngle(turretYaw, hullYaw + Mathf.Sin(Time.time * 0.7f + idlePhase) * 50f, 40f * Time.deltaTime);
            if (agent == null) return;

            patrolTimer -= Time.deltaTime;
            if (patrolTimer > 0f && agent.hasPath) return;
            patrolTimer = Random.Range(2.5f, 4.5f);
            MoveTo(transform.position + Random.insideUnitSphere * 6f);
        }

        void MoveTo(Vector3 point)
        {
            if (agent == null || !agent.isOnNavMesh) return;
            if (NavMesh.SamplePosition(point, out var hit, 2f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
        }

        // ---------- Проверки выстрела ----------

        /// <summary>Не стрелять, если первым на линии огня окажется другой враг.</summary>
        bool FriendlyInLine(Vector3 origin, Vector3 dir, float maxDist)
        {
            int n = Physics.SphereCastNonAlloc(origin, 0.25f, dir, hits, Mathf.Max(0.5f, maxDist - 0.5f), Layers.TankMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (hits[i].collider.TryGetComponent<Tank>(out var t) && t != tank && t.Team == Team.Enemy && !t.IsDead)
                    return true;
            return false;
        }

        /// <summary>Выстрел вслепую безопасен, если стена не вплотную и отскок не летит обратно в стрелка.</summary>
        bool SafeBlindShot(Vector3 origin, Vector3 dir)
        {
            Ricochet.TracePath(origin, dir, 30f, 1, tank.Turret.projectileRadius, Layers.WallMask, path);
            if (path.Count < 2 || Vector3.Distance(path[0], path[1]) < 2.5f) return false;
            return path.Count < 3 || Ricochet.DistanceToSegment(transform.position, path[1], path[2], out _) > 1.4f;
        }

        // ---------- След ----------

        void RecordTrail()
        {
            trailTimer -= Time.deltaTime;
            if (trailTimer > 0f) return;
            trailTimer = 0.35f;
            Vector3 p = transform.position;
            if (trailCount > 0 && (p - trail[(trailHead - 1 + TrailSize) % TrailSize]).sqrMagnitude < 0.09f) return;
            trail[trailHead] = p;
            trailTime[trailHead] = Time.time;
            trailHead = (trailHead + 1) % TrailSize;
            trailCount = Mathf.Min(trailCount + 1, TrailSize);
        }

        /// <summary>Недавние точки пути: позиция и возраст в секундах (от свежих к старым).</summary>
        public void ForEachTrailPoint(Action<Vector3, float> visit)
        {
            for (int i = 1; i <= trailCount; i++)
            {
                int k = (trailHead - i + TrailSize) % TrailSize;
                visit(trail[k], Time.time - trailTime[k]);
            }
        }

        // ---------- Выстрел через отскок ----------

        /// <summary>
        /// Поиск выстрела через отскок в два прохода: грубый перебор каждые 3°,
        /// затем уточнение с шагом 0,5° вокруг трёх лучших направлений.
        /// Выстрел должен пройти рядом с целью после отскока и не вернуться в самого стрелка.
        /// </summary>
        Vector3? FindBankShot(Vector3 origin, Vector3 target)
        {
            int bounces = Mathf.Min(tank.Turret.bounces, 2);
            const int keep = 3;
            var bestAngles = new float[keep];
            var bestMiss = new float[keep];
            for (int i = 0; i < keep; i++) bestMiss[i] = float.MaxValue;

            for (float a = 0f; a < 360f; a += 3f)
            {
                float miss = Evaluate(origin, target, a, bounces, out _);
                for (int i = 0; i < keep; i++)
                {
                    if (miss >= bestMiss[i]) continue;
                    for (int j = keep - 1; j > i; j--) { bestMiss[j] = bestMiss[j - 1]; bestAngles[j] = bestAngles[j - 1]; }
                    bestMiss[i] = miss;
                    bestAngles[i] = a;
                    break;
                }
            }

            float bestLength = float.MaxValue;
            Vector3? bestDir = null;
            for (int i = 0; i < keep; i++)
            {
                if (bestMiss[i] > 3f) continue;
                for (float a = bestAngles[i] - 3f; a <= bestAngles[i] + 3f; a += 0.5f)
                {
                    float miss = Evaluate(origin, target, a, bounces, out float length);
                    if (miss < 0.8f && length < bestLength)
                    {
                        bestLength = length;
                        bestDir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                    }
                }
            }
            return bestDir;
        }

        /// <summary>Насколько близко к цели проходит выстрел под углом a после первого отскока.</summary>
        float Evaluate(Vector3 origin, Vector3 target, float a, int bounces, out float length)
        {
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            Ricochet.TracePath(origin, dir, 40f, bounces, tank.Turret.projectileRadius, Layers.WallMask, path);
            Vector3 self = transform.position;
            float travelled = 0f;
            float bestMiss = float.MaxValue;
            length = float.MaxValue;
            for (int s = 0; s < path.Count - 1; s++)
            {
                Vector3 p0 = path[s], p1 = path[s + 1];
                if (s >= 1)
                {
                    if (Ricochet.DistanceToSegment(self, p0, p1, out _) < 1.1f) break; // вернётся в стрелка
                    float d = Ricochet.DistanceToSegment(target, p0, p1, out float along);
                    if (d < bestMiss)
                    {
                        bestMiss = d;
                        length = travelled + along;
                    }
                }
                travelled += Vector3.Distance(p0, p1);
            }
            return bestMiss;
        }
    }
}
