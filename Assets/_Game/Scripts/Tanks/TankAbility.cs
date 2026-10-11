using UnityEngine;
using UnityEngine.AI;

namespace PulyaDura
{
    /// <summary>Скилл корпуса. Каждый так или иначе работает с рикошетом.</summary>
    public class TankAbility : MonoBehaviour
    {
        Tank tank;
        HullConfig hull;
        float cooldown;
        float active;
        bool wasReady = true;
        readonly System.Collections.Generic.HashSet<Tank> rammed = new();
        static readonly Collider[] ramBuffer = new Collider[8];

        /// <summary>Скилл только что перезарядился (для вспышки плитки в HUD).</summary>
        public float ReadySince { get; private set; } = -10f;

        public bool Ready => cooldown <= 0f;
        public float Cooldown01 => hull.abilityCooldown <= 0f ? 1f : 1f - Mathf.Clamp01(cooldown / hull.abilityCooldown);
        public string Title => hull.abilityTitle;

        public void Init(Tank owner)
        {
            tank = owner;
            hull = owner.Hull;
        }

        public void TryActivate(Vector3 moveDir, Vector3 aimDir)
        {
            if (tank.IsDead) return;
            if (!Ready)
            {
                Sfx.I.Play(SfxId.Empty, transform.position, 0.5f);
                return;
            }
            cooldown = hull.abilityCooldown;
            active = hull.abilityDuration;

            switch (hull.ability)
            {
                case AbilityType.Dash:
                    Vector3 dir = moveDir.sqrMagnitude > 0.01f ? moveDir.normalized : transform.forward;
                    GetComponent<PlayerController>().Dash(dir, 26f, 0.2f);
                    tank.Invulnerable = true;
                    tank.Dashing = true;
                    rammed.Clear();
                    Fx.I.Sparks(transform.position + Vector3.up * 0.2f, -dir, GameManager.Cfg.playerAccent, 18, 6f);
                    Sfx.I.Play(SfxId.Dash, transform.position);
                    CameraRig.I.Kick(dir, 0.25f);
                    break;

                case AbilityType.Mirror:
                    tank.MirrorActive = true;
                    tank.Visual.SetMirror(true);
                    Sfx.I.Play(SfxId.Mirror, transform.position);
                    break;

                case AbilityType.Wall:
                    TempWall.Spawn(transform.position + aimDir * 2.1f, aimDir, hull.abilityDuration);
                    Sfx.I.Play(SfxId.WallUp, transform.position);
                    CameraRig.I.Shake(0.2f);
                    break;
            }
        }

        void Update()
        {
            if (cooldown > 0f) cooldown -= Time.deltaTime;
            if (Ready && !wasReady && tank.Team == Team.Player)
            {
                // Скилл готов: короткий звук, плитка в HUD вспыхивает
                ReadySince = Time.time;
                Sfx.I.Play(SfxId.Select, transform.position, 0.5f, 1.5f);
            }
            wasReady = Ready;
            if (active <= 0f) return;
            if (tank.Dashing) Ram();
            active -= Time.deltaTime;
            if (active > 0f) return;
            if (tank.Dashing)
            {
                // В конце рывка — волна эха: видно, куда выскочил
                EchoSystem.I.Emit(transform.position, 6.5f, GameManager.Cfg.echoColor, 0.9f);
                Sfx.I.Play(SfxId.Tink, transform.position, 0.6f, 0.75f);
            }
            tank.Invulnerable = false;
            tank.Dashing = false;
            tank.MirrorActive = false;
            tank.Visual.SetMirror(false);
        }

        /// <summary>Таран: враг, которого задел рывок, получает 2 урона (один раз за рывок).</summary>
        void Ram()
        {
            int n = Physics.OverlapSphereNonAlloc(transform.position, 1.25f, ramBuffer, Layers.TankMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (!ramBuffer[i].TryGetComponent<Tank>(out var other) || other == tank || other.IsDead || other.Team == tank.Team) continue;
                if (!rammed.Add(other)) continue;
                Vector3 dir = Prims.Flat(other.transform.position - transform.position).normalized;
                other.TakeDamage(new DamageInfo { amount = 2, source = tank, point = other.transform.position, direction = dir });
                Fx.I.Sparks(other.transform.position + Vector3.up * 0.5f, dir, GameManager.Cfg.playerAccent, 22, 7f);
                Sfx.I.Play(SfxId.Hit, other.transform.position, 1f, 0.8f);
                CameraRig.I.Shake(0.35f);
                if (tank.Team == Team.Player) GameManager.I.UI.Floating(other.transform.position + Vector3.up * 1.2f, "ТАРАН", UiKit.Accent, 30);
            }
        }
    }

    /// <summary>Временная светящаяся стена «Бегемота»: укрытие и новый борт для рикошета.</summary>
    public class TempWall : MonoBehaviour
    {
        float life, t;
        WallBlock[] blocks;

        public static void Spawn(Vector3 center, Vector3 facing, float lifetime)
        {
            var cfg = GameManager.Cfg;
            facing = Prims.Flat(facing).normalized;
            var rot = Quaternion.LookRotation(facing);
            var root = new GameObject("TempWall");
            root.transform.SetParent(GameManager.I.LevelRoot, true);
            root.transform.SetPositionAndRotation(new Vector3(center.x, 0f, center.z), rot);

            var size = new Vector3(1f, cfg.wallHeight, 0.45f);
            int made = 0;
            for (int i = -1; i <= 1; i++)
            {
                Vector3 p = root.transform.TransformPoint(new Vector3(i * 1.02f, cfg.wallHeight * 0.5f, 0f));
                if (Physics.CheckBox(p, size * 0.48f, rot, Layers.SolidMask | Layers.FenceMask, QueryTriggerInteraction.Ignore)) continue;
                var r = Prims.Part("Block", PrimitiveType.Cube, root.transform, root.transform.InverseTransformPoint(p), size, Mats.Wall);
                r.gameObject.layer = Layers.Wall;
                r.gameObject.AddComponent<BoxCollider>();
                var wb = r.gameObject.AddComponent<WallBlock>();
                wb.baseColor = cfg.tempWallColor;
                wb.baseGlow = 0.9f;
                var obstacle = r.gameObject.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = Vector3.one;
                obstacle.carving = true;
                Fx.I.Sparks(p, Vector3.up, cfg.tempWallColor, 10, 4f);
                made++;
            }
            if (made == 0) { Destroy(root); return; }
            var tw = root.AddComponent<TempWall>();
            tw.life = lifetime;
            tw.blocks = root.GetComponentsInChildren<WallBlock>();
            EchoSystem.I.Emit(center, 3f, cfg.tempWallColor, 0.6f);
        }

        void Update()
        {
            t += Time.deltaTime;
            float left = life - t;
            if (left < 1.2f)
            {
                float blink = Mathf.PingPong(t * 8f, 1f);
                foreach (var b in blocks) if (b != null) b.baseGlow = 0.2f + blink * 0.8f;
            }
            if (left <= 0f)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
            }
        }
    }
}
