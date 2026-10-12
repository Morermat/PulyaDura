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
        /// Композиция вокруг подиума — уголок игровой комнаты, а не случайные блоки:
        /// слева угол из стеновых блоков, справа решётка и круглая колонна, впереди бочка и зелёный ящик,
        /// на полу кольцо эха. Всё подсвечено так, как это выглядит в игре после эха.
        /// </summary>
        void BuildDecor()
        {
            var wallGlow = new MaterialPropertyBlock();
            wallGlow.SetColor(Mats.EmissionColor, cfg.echoColor * 0.7f);
            const float s = 0.75f;
            void Block(Vector3 p)
            {
                var r = Prims.Part("DecorWall", PrimitiveType.Cube, transform, p + Vector3.up * s * 0.5f, Vector3.one * s, Mats.Wall);
                r.SetPropertyBlock(wallGlow);
            }
            // Всё стоит в «окне» между левой колонкой интерфейса и карточкой — прямо за танком.
            // Задняя стена из трёх блоков с загнутым краем слева
            for (int i = 0; i < 3; i++) Block(new Vector3(-1.1f + i * s, 0f, 3.6f));
            Block(new Vector3(-1.1f, 0f, 3.6f - s));

            // Решётка продолжает заднюю стену вправо: стойки и две светлые перекладины
            var dark = Mats.Lit(new Color(0.07f, 0.08f, 0.1f), 0.4f);
            var bar = Mats.Unlit(new Color(0.9f, 1f, 1.15f) * 0.6f);
            for (int i = 0; i <= 2; i++)
                Prims.Part("FencePost", PrimitiveType.Cube, transform, new Vector3(1.15f + i * 0.5f, 0.38f, 3.6f), new Vector3(0.07f, 0.76f, 0.07f), dark);
            foreach (float y in new[] { 0.32f, 0.66f })
                Prims.Part("FenceBar", PrimitiveType.Cube, transform, new Vector3(1.65f, y, 3.6f), new Vector3(1.06f, 0.035f, 0.035f), bar).shadowCastingMode = ShadowCastingMode.Off;

            // Круглая колонна справа
            var pillar = new GameObject("DecorPillar");
            pillar.transform.SetParent(transform, false);
            pillar.transform.localPosition = new Vector3(1.7f, 0f, 2.5f);
            pillar.AddComponent<MeshFilter>().sharedMesh = WallShapes.Get(new[] { 0.5f, 0.5f, 0.5f, 0.5f }, 0.8f, 0.9f);
            var pr = pillar.AddComponent<MeshRenderer>();
            pr.sharedMaterial = Mats.Wall;
            pr.SetPropertyBlock(wallGlow);

            // Красная бочка и зелёный ящик с бонусом
            Prims.Part("DecorBarrel", PrimitiveType.Cylinder, transform, new Vector3(-0.2f, 0.36f, 2.7f), new Vector3(0.55f, 0.36f, 0.55f), Mats.Lit(new Color(0.35f, 0.05f, 0.04f), 0.5f));
            foreach (float y in new[] { 0.18f, 0.54f })
                Prims.Part("DecorHoop", PrimitiveType.Cylinder, transform, new Vector3(-0.2f, y, 2.7f), new Vector3(0.58f, 0.025f, 0.58f), Mats.Unlit(new Color(2.4f, 0.25f, 0.1f))).shadowCastingMode = ShadowCastingMode.Off;
            var box = Prims.Part("DecorSupply", PrimitiveType.Cube, transform, new Vector3(0.75f, 0.25f, 2.9f), new Vector3(0.5f, 0.5f, 0.5f), Mats.Crate, Quaternion.Euler(0f, 18f, 0f));
            var boxGlow = new MaterialPropertyBlock();
            boxGlow.SetColor(Mats.BaseColor, new Color(0.1f, 0.3f, 0.14f));
            boxGlow.SetColor(Mats.EmissionColor, new Color(0.4f, 2.8f, 0.7f) * 0.8f);
            box.SetPropertyBlock(boxGlow);

            // Кольцо эха на полу
            var circle = new GameObject("EchoCircle").AddComponent<LineRenderer>();
            circle.transform.SetParent(transform, false);
            circle.useWorldSpace = false;
            circle.loop = true;
            circle.sharedMaterial = cfg.fxMaterial;
            circle.widthMultiplier = 0.05f;
            circle.positionCount = 96;
            for (int i = 0; i < 96; i++)
            {
                float a = i * Mathf.PI * 2f / 96f;
                circle.SetPosition(i, new Vector3(Mathf.Cos(a) * 2.2f, 0.02f, Mathf.Sin(a) * 2.2f));
            }
            circle.startColor = circle.endColor = (Color)EchoSystem.Ldr(cfg.echoColor) * 0.45f;
            circle.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>Камера гаража медленно покачивается — сцена живая. Автотест выключает это для своих снимков.</summary>
        public static bool SwayEnabled = true;
        static readonly Vector3 LookTarget = Position + new Vector3(-0.15f, 0.45f, 0.9f);

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
            CameraRig.I.LookAt(LookTarget, 7.8f, 27f);
        }

        public void Hide() => gameObject.SetActive(false);

        void Update()
        {
            if (tank != null) tank.transform.Rotate(0f, 25f * Time.unscaledDeltaTime, 0f, Space.World);
            if (SwayEnabled)
            {
                float t = Time.unscaledTime;
                CameraRig.I.LookAt(LookTarget, 7.8f + Mathf.Sin(t * 0.31f) * 0.25f, 27f + Mathf.Sin(t * 0.23f) * 1.5f, Mathf.Sin(t * 0.17f) * 7f);
            }
            // Подиум «дышит» сонаром — тот же эффект, что и в игре
            pingTimer -= Time.unscaledDeltaTime;
            if (pingTimer > 0f) return;
            pingTimer = 1.8f;
            EchoSystem.I.NoiseRing(Position + Vector3.up * 0.05f, 3.2f, cfg.echoColor * 0.6f);
        }
    }
}
