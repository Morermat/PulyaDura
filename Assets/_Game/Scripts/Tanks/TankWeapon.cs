using UnityEngine;

namespace PulyaDura
{
    /// <summary>Перезарядка и выстрел. Тип пушки целиком задаётся TurretConfig.</summary>
    public class TankWeapon : MonoBehaviour
    {
        Tank tank;
        TurretConfig cfg;
        float cooldown;
        float charge;
        int burstLeft;
        float burstTimer;
        LineRenderer warning;
        readonly System.Collections.Generic.List<Vector3> warningPath = new();

        public bool Charging { get; private set; }
        /// <summary>Бонус «Ваншот»: сколько выстрелов убивают с одного попадания.</summary>
        public int PowerShots { get; set; }
        /// <summary>Бонус «Метка»: сколько выстрелов подсвечивают врага при попадании.</summary>
        public int MarkShots { get; set; }

        /// <summary>Бонусы заряжают выстрел, но тратятся только при попадании в танк (ConsumeMods):
        /// разведочные выстрелы в стену их не сжигают.</summary>
        ShotMods TakeMods() => new() { power = PowerShots > 0, mark = MarkShots > 0 };

        public void ConsumeMods(bool power, bool mark)
        {
            if (power && PowerShots > 0) PowerShots--;
            if (mark && MarkShots > 0) MarkShots--;
        }

        /// <summary>Заряд рельсы: у врагов дольше, чтобы игрок успел уйти с красной линии.</summary>
        float ChargeTime => tank.Team == Team.Enemy ? Mathf.Max(cfg.chargeTime, 1f) : cfg.chargeTime;
        public bool Ready => cooldown <= 0f && !Charging && burstLeft <= 0;
        public float Reload01 => cfg.reload <= 0f ? 1f : 1f - Mathf.Clamp01(cooldown / cfg.reload);
        public float Charge01 => ChargeTime <= 0f ? 0f : Mathf.Clamp01(charge / ChargeTime);

        public void Init(Tank owner)
        {
            tank = owner;
            cfg = owner.Turret;
        }

        public bool TryFire()
        {
            if (!Ready || tank.IsDead) return false;
            if (cfg.mode == FireMode.Rail)
            {
                Charging = true;
                charge = 0f;
                Sfx.I.Play(SfxId.RailCharge, tank.transform.position, 0.7f);
                return true;
            }
            FireProjectiles();
            return true;
        }

        void Update()
        {
            if (cooldown > 0f) cooldown -= Time.deltaTime;
            if (burstLeft > 0)
            {
                burstTimer -= Time.deltaTime;
                if (burstTimer <= 0f && !tank.IsDead)
                {
                    burstLeft--;
                    burstTimer = cfg.burstInterval;
                    FireVolley(false);
                }
            }
            if (!Charging)
            {
                if (warning != null && warning.enabled) warning.enabled = false;
                return;
            }
            charge += Time.deltaTime;
            tank.Visual.Tell(Charge01);
            if (tank.Team == Team.Enemy) DrawWarning();
            if (charge >= ChargeTime)
            {
                Charging = false;
                tank.Visual.Tell(0f);
                if (warning != null) warning.enabled = false;
                FireRail();
            }
        }

        void OnDisable()
        {
            if (warning != null) warning.enabled = false;
        }

        /// <summary>Враг заряжает рельсу: красная линия показывает, куда пройдёт луч (с отскоками). Мигает всё чаще.</summary>
        void DrawWarning()
        {
            if (warning == null)
            {
                var go = new GameObject("RailWarning");
                go.transform.SetParent(transform, false);
                warning = go.AddComponent<LineRenderer>();
                warning.sharedMaterial = GameManager.Cfg.fxMaterial;
                warning.useWorldSpace = true;
                warning.widthMultiplier = 0.06f;
                warning.numCapVertices = 2;
                warning.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Ricochet.TracePath(tank.ShotOrigin, AimDirection, cfg.range, cfg.bounces, cfg.projectileRadius, Layers.WallMask, warningPath);
            warning.enabled = true;
            warning.positionCount = warningPath.Count;
            for (int i = 0; i < warningPath.Count; i++) warning.SetPosition(i, warningPath[i]);
            float k = Charge01;
            float blink = Mathf.Repeat(Time.time * (4f + k * 14f), 1f) > 0.4f ? 1f : 0.35f;
            Color c = new Color(3f, 0.25f, 0.2f) * (0.4f + 0.8f * k) * blink;
            warning.startColor = warning.endColor = c;
            warning.widthMultiplier = 0.04f + 0.06f * k;
        }

        Vector3 AimDirection => Prims.Flat(tank.Visual.TurretPivot.forward).normalized;

        void FireProjectiles()
        {
            // Очередь (плазма игрока): первый выстрел сейчас, остальные через burstInterval. У врагов — одиночные
            int burst = tank.Team == Team.Player ? Mathf.Max(1, cfg.burst) : 1;
            cooldown = cfg.reload + (burst - 1) * cfg.burstInterval;
            burstLeft = burst - 1;
            burstTimer = cfg.burstInterval;
            FireVolley(true);
        }

        void FireVolley(bool first)
        {
            Vector3 origin = tank.ShotOrigin;
            Vector3 fwd = AimDirection;
            float muzzle = Vector3.Distance(Prims.Flat(tank.Visual.Muzzle.position), Prims.Flat(origin));
            var mods = TakeMods();
            if (tank.Team == Team.Player) mods.ladder = Sfx.I.AdvanceLadder();

            for (int i = 0; i < cfg.pellets; i++)
            {
                float angle = cfg.pellets > 1
                    ? Mathf.Lerp(-cfg.spread * 0.5f, cfg.spread * 0.5f, i / (cfg.pellets - 1f)) + Random.Range(-2f, 2f)
                    : 0f;
                ProjectileSystem.I.Spawn(tank, cfg, origin, Quaternion.Euler(0f, angle, 0f) * fwd, muzzle, mods);
            }
            AfterShot(fwd, cfg.pellets > 1 ? 0.22f : cfg.damage >= 2 ? 0.16f : first ? 0.06f : 0.03f, mods.ladder);
        }

        void FireRail()
        {
            cooldown = cfg.reload;
            Vector3 fwd = AimDirection;
            var mods = TakeMods();
            if (tank.Team == Team.Player) mods.ladder = Sfx.I.AdvanceLadder();
            ProjectileSystem.I.FireRail(tank, cfg, tank.ShotOrigin, fwd, mods);
            AfterShot(fwd, 0.35f, mods.ladder);
        }

        /// <param name="ladder">Ступень лестницы тонов: выстрелы игрока подряд звучат всё выше (на полтона за ступень вдвое мягче, чем отскоки)</param>
        void AfterShot(Vector3 fwd, float shake, int ladder = 0)
        {
            tank.Visual.Kick();
            Color c = tank.Team == Team.Player ? GameManager.Cfg.bulletPlayer : GameManager.Cfg.bulletEnemy;
            Fx.I.Muzzle(tank.Visual.Muzzle.position, fwd, c);
            float pitch = tank.Team == Team.Player ? Mathf.Sqrt(Sfx.LadderPitch(ladder)) * Random.Range(0.98f, 1.02f) : Random.Range(0.93f, 1.07f);
            Sfx.I.Play(ShotSound(), tank.transform.position, 0.9f, pitch);
            GameEvents.RaiseNoise(tank.transform.position, cfg.noiseRadius, tank.Team);
            GameEvents.RaiseShot(tank);
            if (tank.Team == Team.Player)
            {
                CameraRig.I.Shake(shake);
                CameraRig.I.Kick(-fwd, shake * 0.8f);
            }
        }

        SfxId ShotSound() => cfg.mode == FireMode.Rail ? SfxId.RailFire
            : cfg.pellets > 1 ? SfxId.Shotgun
            : cfg.damage <= 1 ? SfxId.Plasma
            : SfxId.Shot;
    }
}
