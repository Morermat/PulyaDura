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
                    GetComponent<PlayerController>().Dash(dir, 20f, 0.16f);
                    tank.Invulnerable = true;
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
            if (active <= 0f) return;
            active -= Time.deltaTime;
            if (active > 0f) return;
            tank.Invulnerable = false;
            tank.MirrorActive = false;
            tank.Visual.SetMirror(false);
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
