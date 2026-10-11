using UnityEngine;
using UnityEngine.Rendering;

namespace PulyaDura
{
    /// <summary>Подиум гаража: свет, вращающийся танк и сонар-импульсы вокруг него.</summary>
    public class GarageStage : MonoBehaviour
    {
        static readonly Vector3 Position = new(0f, 0f, -300f);

        GameConfig cfg;
        GameObject tank;
        float pingTimer;

        public Transform Tank => tank != null ? tank.transform : null;

        public void Init(GameConfig config)
        {
            cfg = config;
            transform.position = Position;

            Prims.Part("Pedestal", PrimitiveType.Cylinder, transform, new Vector3(0f, -0.05f, 0f), new Vector3(2.6f, 0.05f, 2.6f), Mats.Lit(new Color(0.08f, 0.09f, 0.11f), 0.6f));
            var ring = Prims.Part("Ring", PrimitiveType.Cylinder, transform, new Vector3(0f, -0.06f, 0f), new Vector3(2.75f, 0.04f, 2.75f), Mats.Unlit(cfg.echoColor * 0.5f));
            ring.shadowCastingMode = ShadowCastingMode.Off;
            Prims.Part("Floor", PrimitiveType.Cube, transform, new Vector3(0f, -0.6f, 0f), new Vector3(40f, 1f, 40f), Mats.Floor);

            BuildDecor();

            var key = new GameObject("KeyLight").AddComponent<Light>();
            key.transform.SetParent(transform, false);
            key.transform.localPosition = new Vector3(1.5f, 6f, -2f);
            key.transform.LookAt(Position);
            key.type = LightType.Spot;
            key.spotAngle = 45f;
            key.range = 15f;
            key.intensity = 30f;
            key.color = new Color(1f, 0.92f, 0.8f);
            key.shadows = LightShadows.Soft;

            var rim = new GameObject("RimLight").AddComponent<Light>();
            rim.transform.SetParent(transform, false);
            rim.transform.localPosition = new Vector3(-2.5f, 1.5f, 2.5f);
            rim.type = LightType.Point;
            rim.range = 7f;
            rim.intensity = 4f;
            rim.color = EchoSystem.Ldr(cfg.echoColor);
        }

        /// <summary>
        /// Декорации подиума — кусочек игровой комнаты: ряд светящихся стеновых блоков, две круглые колонны
        /// и большое кольцо эха на полу. Сразу видно, как выглядит мир игры.
        /// </summary>
        void BuildDecor()
        {
            var glow = new MaterialPropertyBlock();
            glow.SetColor(Mats.EmissionColor, cfg.echoColor * 0.6f);
            for (int i = 0; i < 6; i++)
            {
                var r = Prims.Part("DecorWall", PrimitiveType.Cube, transform, new Vector3(-2.1f, 0.35f, 2.2f + i * 0.72f), Vector3.one * 0.7f, Mats.Wall);
                r.SetPropertyBlock(glow);
            }
            var pillarMesh = WallShapes.Get(new[] { 0.5f, 0.5f, 0.5f, 0.5f }, 0.8f, 0.8f);
            foreach (var pos in new[] { new Vector3(1.5f, 0f, 3.4f), new Vector3(2.4f, 0f, 2.1f) })
            {
                var go = new GameObject("DecorPillar");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = pos;
                go.AddComponent<MeshFilter>().sharedMesh = pillarMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Mats.Wall;
                mr.SetPropertyBlock(glow);
            }
            var circle = new GameObject("EchoCircle").AddComponent<LineRenderer>();
            circle.transform.SetParent(transform, false);
            circle.useWorldSpace = false;
            circle.loop = true;
            circle.sharedMaterial = cfg.fxMaterial;
            circle.widthMultiplier = 0.07f;
            circle.positionCount = 96;
            for (int i = 0; i < 96; i++)
            {
                float a = i * Mathf.PI * 2f / 96f;
                circle.SetPosition(i, new Vector3(Mathf.Cos(a) * 3.1f, 0.02f, Mathf.Sin(a) * 3.1f));
            }
            circle.startColor = circle.endColor = (Color)EchoSystem.Ldr(cfg.echoColor) * 0.45f;
            circle.shadowCastingMode = ShadowCastingMode.Off;
        }

        public void Show(HullConfig hull, TurretConfig turret)
        {
            gameObject.SetActive(true);
            float yaw = tank != null ? tank.transform.eulerAngles.y : 210f;
            if (tank != null) Destroy(tank);
            tank = new GameObject("GarageTank");
            tank.transform.SetParent(transform, false);
            tank.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var visual = tank.AddComponent<TankVisual>();
            visual.Build(hull, turret, Team.Player, null, false);
            visual.SetTurretLocalYaw(25f);
            visual.Reveal(0.6f);
            // Танк чуть правее центра: слева заголовок, справа карточка выбора
            CameraRig.I.LookAt(Position + new Vector3(-0.35f, 0.45f, 0f), 9.5f, 26f);
        }

        public void Hide() => gameObject.SetActive(false);

        void Update()
        {
            if (tank != null) tank.transform.Rotate(0f, 25f * Time.unscaledDeltaTime, 0f, Space.World);
            // Подиум «дышит» сонаром — тот же эффект, что и в игре
            pingTimer -= Time.unscaledDeltaTime;
            if (pingTimer > 0f) return;
            pingTimer = 1.8f;
            EchoSystem.I.NoiseRing(Position + Vector3.up * 0.05f, 3.2f, cfg.echoColor * 0.6f);
        }
    }
}
