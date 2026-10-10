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

        public bool Charging { get; private set; }
        public bool Ready => cooldown <= 0f && !Charging;
        public float Reload01 => cfg.reload <= 0f ? 1f : 1f - Mathf.Clamp01(cooldown / cfg.reload);
        public float Charge01 => cfg.chargeTime <= 0f ? 0f : Mathf.Clamp01(charge / cfg.chargeTime);

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
            if (!Charging) return;
            charge += Time.deltaTime;
            tank.Visual.Tell(Charge01);
            if (charge >= cfg.chargeTime)
            {
                Charging = false;
                tank.Visual.Tell(0f);
                FireRail();
            }
        }

        Vector3 AimDirection => Prims.Flat(tank.Visual.TurretPivot.forward).normalized;

        void FireProjectiles()
        {
            cooldown = cfg.reload;
            Vector3 origin = tank.ShotOrigin;
            Vector3 fwd = AimDirection;
            float muzzle = Vector3.Distance(Prims.Flat(tank.Visual.Muzzle.position), Prims.Flat(origin));

            for (int i = 0; i < cfg.pellets; i++)
            {
                float angle = cfg.pellets > 1
                    ? Mathf.Lerp(-cfg.spread * 0.5f, cfg.spread * 0.5f, i / (cfg.pellets - 1f)) + Random.Range(-2f, 2f)
                    : 0f;
                ProjectileSystem.I.Spawn(tank, cfg, origin, Quaternion.Euler(0f, angle, 0f) * fwd, muzzle);
            }
            AfterShot(fwd, cfg.pellets > 1 ? 0.22f : cfg.damage >= 2 ? 0.16f : 0.06f);
        }

        void FireRail()
        {
            cooldown = cfg.reload;
            Vector3 fwd = AimDirection;
            ProjectileSystem.I.FireRail(tank, cfg, tank.ShotOrigin, fwd);
            AfterShot(fwd, 0.35f);
        }

        void AfterShot(Vector3 fwd, float shake)
        {
            tank.Visual.Kick();
            Color c = tank.Team == Team.Player ? GameManager.Cfg.bulletPlayer : GameManager.Cfg.bulletEnemy;
            Fx.I.Muzzle(tank.Visual.Muzzle.position, fwd, c);
            Sfx.I.Play(ShotSound(), tank.transform.position, 0.9f, Random.Range(0.93f, 1.07f));
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
