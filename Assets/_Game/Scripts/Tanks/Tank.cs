using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Танк игрока и врага устроен одинаково: корпус (HullConfig) + пушка (TurretConfig).
    /// Различается только «мозг»: PlayerController или EnemyBrain.
    /// </summary>
    public class Tank : MonoBehaviour, IDamageable
    {
        public static readonly List<Tank> All = new();

        public Team Team { get; private set; }
        public HullConfig Hull { get; private set; }
        public TurretConfig Turret { get; private set; }
        public EnemyConfig Enemy { get; private set; }

        public int Hp { get; private set; }
        public int MaxHp { get; private set; }
        public bool IsDead { get; private set; }

        public TankVisual Visual { get; private set; }
        public TankWeapon Weapon { get; private set; }
        public TankAbility Ability { get; private set; }
        public PlayerController Controller { get; private set; }
        public EnemyBrain Brain { get; private set; }
        public Rigidbody Body { get; private set; }
        public BoxCollider Collider { get; private set; }

        /// <summary>Пули проходят насквозь (рывок).</summary>
        public bool Invulnerable { get; set; }
        /// <summary>Пули отражаются от брони (зеркало).</summary>
        public bool MirrorActive { get; set; }

        public Vector3 ShotOrigin
        {
            get { var p = transform.position; p.y = Ricochet.ShotHeight; return p; }
        }

        public static Tank Spawn(Team team, HullConfig hull, TurretConfig turret, EnemyConfig enemy, Vector3 position, Quaternion rotation, Transform parent)
        {
            var go = new GameObject(team == Team.Player ? "Player" : $"Enemy_{enemy.title}");
            go.layer = Layers.Tank;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            var tank = go.AddComponent<Tank>();
            tank.Setup(team, hull, turret, enemy);
            return tank;
        }

        void Setup(Team team, HullConfig hull, TurretConfig turret, EnemyConfig enemy)
        {
            Team = team;
            Hull = hull;
            Turret = turret;
            Enemy = enemy;
            MaxHp = enemy != null && enemy.hpOverride > 0 ? enemy.hpOverride : hull.maxHp;
            Hp = MaxHp;

            Collider = gameObject.AddComponent<BoxCollider>();
            Collider.size = new Vector3(hull.size.x, 0.85f, hull.size.y);
            Collider.center = new Vector3(0f, 0.43f, 0f);

            Body = gameObject.AddComponent<Rigidbody>();
            Body.useGravity = false;
            Body.mass = 2f + hull.maxHp;
            Body.linearDamping = 0f;
            Body.angularDamping = 10f;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            Body.isKinematic = team == Team.Enemy; // врагов двигает NavMeshAgent

            Visual = gameObject.AddComponent<TankVisual>();
            Visual.Build(hull, turret, team, enemy, team == Team.Player);
            // Враги невидимы: их выдают только «призраки» эха, след и вспышки выстрелов
            Visual.Hidden = team == Team.Enemy;

            Weapon = gameObject.AddComponent<TankWeapon>();
            Weapon.Init(this);

            if (team == Team.Player)
            {
                Ability = gameObject.AddComponent<TankAbility>();
                Ability.Init(this);
                Controller = gameObject.AddComponent<PlayerController>();
                Controller.Init(this);
            }
            else
            {
                Brain = gameObject.AddComponent<EnemyBrain>();
                Brain.Init(this);
            }
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void TakeDamage(DamageInfo info)
        {
            if (IsDead || Invulnerable) return;
            Hp = Mathf.Max(0, Hp - info.amount);
            Visual.Flash();
            if (Visual.Hidden) EchoGhosts.I.Snapshot(this, Color.white, 1f);
            else Visual.Reveal(1f);
            Visual.SetHealth((float)Hp / MaxHp);
            GameEvents.RaiseDamaged(this, info);
            if (Hp == 0) Die(info);
        }

        void Die(DamageInfo info)
        {
            IsDead = true;
            Visual.Explode(info.direction);
            GameEvents.RaiseKilled(this, info);
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();
    }
}
