using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PulyaDura
{
    /// <summary>
    /// Враги тоже слепые. Они знают, где игрок, только если:
    /// услышали выстрел или отскок, получили попадание или игрок подъехал вплотную.
    /// Поэтому каждый выстрел игрока — это и свет, и шум.
    /// </summary>
    public class EnemyBrain : MonoBehaviour
    {
        Tank tank;
        EnemyConfig cfg;
        NavMeshAgent agent;

        Vector3 targetPos;
        float targetTime = -999f;
        bool HasTarget => Time.time - targetTime < cfg.memory;

        float turretYaw;
        float fireTimer;
        float repathTimer;
        float bankTimer;
        Vector3? bankDir;
        float wanderTimer;
        float idlePhase;
        readonly List<Vector3> path = new();

        public void Init(Tank owner)
        {
            tank = owner;
            cfg = owner.Enemy;
            turretYaw = transform.eulerAngles.y;
            fireTimer = Random.Range(1.2f, 2.2f); // фора в начале комнаты
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

        void OnNoise(Vector3 pos, float radius, Team team)
        {
            if (team == Team.Enemy || cfg.behaviour == EnemyBehaviour.Dummy) return;
            float heard = radius * cfg.hearing;
            if (Prims.Flat(pos - transform.position).sqrMagnitude > heard * heard) return;
            Remember(pos);
        }

        void OnDamaged(Tank victim, DamageInfo info)
        {
            if (victim != tank || info.source == null || info.source.Team != Team.Player) return;
            Remember(info.source.transform.position);
        }

        void Remember(Vector3 pos)
        {
            targetPos = pos;
            targetTime = Time.time;
            bankTimer = 0f;
        }

        void Update()
        {
            if (tank.IsDead) return;
            bool active = GameManager.I.State == GameState.Playing;
            if (agent != null) agent.isStopped = !active;
            if (!active) return;

            var player = GameManager.I.Player;
            if (cfg.behaviour == EnemyBehaviour.Dummy)
            {
                turretYaw += Time.deltaTime * 20f;
                tank.Visual.SetTurretYaw(turretYaw);
                return;
            }

            if (player != null && !player.IsDead &&
                Prims.Flat(player.transform.position - transform.position).sqrMagnitude < cfg.senseRadius * cfg.senseRadius)
                Remember(player.transform.position);

            if (HasTarget) Engage();
            else Idle();

            tank.Visual.SetTurretYaw(turretYaw);
        }

        void Engage()
        {
            Vector3 origin = tank.ShotOrigin;
            bool clear = Ricochet.ClearLine(origin, targetPos, tank.Turret.projectileRadius);
            Vector3 desired = Prims.Flat(targetPos - origin).normalized;
            bool canShoot = clear;

            if (!clear && cfg.bankShots)
            {
                bankTimer -= Time.deltaTime;
                if (bankTimer <= 0f)
                {
                    bankTimer = 0.6f;
                    bankDir = FindBankShot(origin, targetPos);
                }
                if (bankDir.HasValue)
                {
                    desired = bankDir.Value;
                    canShoot = true;
                }
            }

            float wantYaw = Quaternion.LookRotation(desired).eulerAngles.y;
            turretYaw = Mathf.MoveTowardsAngle(turretYaw, wantYaw, cfg.turretTurnSpeed * Time.deltaTime);
            bool aimed = Mathf.Abs(Mathf.DeltaAngle(turretYaw, wantYaw)) < 4f;

            fireTimer -= Time.deltaTime;
            tank.Visual.Tell(canShoot ? 1f - Mathf.Clamp01(fireTimer / 0.45f) : 0f);
            if (canShoot && aimed && fireTimer <= 0f && tank.Weapon.Ready)
            {
                float saved = turretYaw;
                tank.Visual.SetTurretYaw(turretYaw + Random.Range(-cfg.aimError, cfg.aimError));
                tank.Weapon.TryFire();
                tank.Visual.SetTurretYaw(saved);
                fireTimer = cfg.fireInterval * Random.Range(0.85f, 1.2f);
            }

            if (agent == null) return;
            float dist = Prims.Flat(targetPos - transform.position).magnitude;
            repathTimer -= Time.deltaTime;
            if (repathTimer > 0f) return;
            repathTimer = 0.35f;
            if (!clear || dist > 8f) agent.SetDestination(targetPos);
            else agent.ResetPath();
        }

        void Idle()
        {
            tank.Visual.Tell(0f);
            float hullYaw = transform.eulerAngles.y;
            turretYaw = Mathf.MoveTowardsAngle(turretYaw, hullYaw + Mathf.Sin(Time.time * 0.7f + idlePhase) * 50f, 40f * Time.deltaTime);
            if (agent == null) return;

            wanderTimer -= Time.deltaTime;
            if (wanderTimer > 0f && agent.hasPath) return;
            wanderTimer = Random.Range(2.5f, 4.5f);
            Vector3 random = transform.position + Random.insideUnitSphere * 6f;
            if (NavMesh.SamplePosition(random, out var hit, 2f, NavMesh.AllAreas))
                agent.SetDestination(hit.position);
        }

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
