using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Внешний вид танка из сгенерированных мешей: у каждого корпуса и каждой пушки свой силуэт,
    /// читаемый сверху. Траки бегут при движении, корпус покачивается от разгона и отдачи.
    /// Художник может заменить детали на модели, сохранив объекты Turret и Muzzle.
    /// </summary>
    public class TankVisual : MonoBehaviour
    {
        public Transform HullRoot { get; private set; }
        public Transform TurretPivot { get; private set; }
        public Transform Muzzle { get; private set; }
        public bool ShowHealthBar { get; set; }
        public Color Accent => accent;

        /// <summary>
        /// Скрытый танк (враг) не рисуется. Видно только сенсор перед выстрелом,
        /// а эхо рисует на его месте «призрака» (EchoGhosts).
        /// </summary>
        public bool Hidden
        {
            get => hidden;
            set
            {
                hidden = value;
                ApplyVisibility(!value);
            }
        }

        /// <summary>Враг помечен пулей-меткой: виден целиком, пока не истечёт время.</summary>
        public bool Marked => Time.time < markUntil;

        /// <summary>Танк сейчас нарисован (скрытый враг виден только под меткой).</summary>
        public bool Shown => shownNow;

        void ApplyVisibility(bool visible)
        {
            shownNow = visible;
            foreach (var r in litParts) r.enabled = visible;
            foreach (var r in glowParts) r.enabled = visible;
            trackL.enabled = trackR.enabled = visible;
            sensor.enabled = visible;
        }

        public void Mark(float seconds)
        {
            markUntil = Time.time + seconds;
            reveal = 1f;
        }

        /// <summary>Пузырь бонуса «Щит» у игрока.</summary>
        public void SetShieldBubble(bool on)
        {
            if (shieldBubble != null) shieldBubble.gameObject.SetActive(on);
        }

        readonly List<MeshRenderer> litParts = new();
        readonly List<MeshRenderer> glowParts = new();
        float markUntil;
        bool shownNow = true;
        Transform shieldBubble;
        bool hidden;
        MeshRenderer trackL, trackR;
        MeshRenderer sensor;
        Transform barrels, mirrorBubble, barRoot, barFill;
        Light headLight;
        MaterialPropertyBlock mpb, trackMpb, glowMpb;
        GameConfig cfg;
        Team team;
        Color accent, revealColor, baseEmission;

        float reveal, flash, recoil, tell, health = 1f, lastHit = -10f;
        float barrelBaseZ, bubbleSize, halfWidth;
        float turretYaw;
        bool turretLocal;

        // анимация хода
        Vector3 lastPos;
        float lastYaw;
        Vector3 lastVelocity;
        float treadL, treadR;
        float pitch, roll, pitchVel, rollVel;
        float sparkTimer;
        Color lastEmission = new(-1f, 0f, 0f);
        float lastTreadL = float.NaN, lastTreadR = float.NaN, lastTell = -1f;

        static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");

        public void Build(HullConfig hull, TurretConfig turret, Team owner, EnemyConfig enemy, bool withLight)
        {
            cfg = GameManager.Cfg;
            team = owner;
            mpb = new MaterialPropertyBlock();
            trackMpb = new MaterialPropertyBlock();
            glowMpb = new MaterialPropertyBlock();
            bool player = owner == Team.Player;
            Color body = player ? cfg.playerBody : cfg.enemyBody;
            accent = player ? cfg.playerAccent : enemy != null && enemy.accent.maxColorComponent > 0f ? enemy.accent : cfg.enemyAccent;
            revealColor = player ? cfg.playerAccent * 0.15f : cfg.enemyReveal;
            baseEmission = player ? cfg.playerBody * 0.35f : Color.black;

            HullRoot = new GameObject("Hull").transform;
            HullRoot.SetParent(transform, false);
            BuildHull(hull, body);

            TurretPivot = new GameObject("Turret").transform;
            TurretPivot.SetParent(HullRoot, false);
            TurretPivot.localPosition = new Vector3(0f, HullTop(hull), hull.style == HullStyle.Heavy ? 0.12f : -0.02f); // у тяжёлого башня впереди моторного отсека
            BuildTurret(turret, body);
            if (enemy != null && enemy.frontShield) BuildFrontShield(hull);
            if (enemy != null && enemy.boss) BuildBossCrown(hull);

            BuildHelpers(hull, withLight);
            // Тени от фонаря игрока рендерят каждую деталь 6 раз: мелочь тени не отбрасывает
            foreach (var r in litParts)
                if (!CastsShadow(r.name)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lastPos = transform.position;
            lastYaw = transform.eulerAngles.y;
            turretYaw = lastYaw;
        }

        static bool CastsShadow(string part) => part is "Body" or "Octa" or "Dome" or "Box" or "Wedge" or "SkirtL" or "SkirtR";

        // ---------- Корпус ----------

        static float TrackHeight(HullConfig h) => h.style switch { HullStyle.Light => 0.3f, HullStyle.Heavy => 0.4f, _ => 0.34f };
        static float HullTop(HullConfig h) => 0.16f + h.height;

        void BuildHull(HullConfig hull, Color body)
        {
            float w = hull.size.x, l = hull.size.y, h = hull.height, y0 = 0.16f;
            float th = TrackHeight(hull);
            float tw = w * (hull.style == HullStyle.Light ? 0.22f : hull.style == HullStyle.Heavy ? 0.27f : 0.24f);
            halfWidth = w * 0.5f;
            string id = hull.id;

            // Гусеницы со скошенными концами и бегущей текстурой траков
            var trackMesh = MeshFactory.Extrude($"track_{id}", new[]
            {
                new Vector2(-l * 0.5f, th * 0.55f), new Vector2(-l * 0.5f + 0.14f, 0.02f), new Vector2(l * 0.5f - 0.14f, 0.02f),
                new Vector2(l * 0.5f, th * 0.55f), new Vector2(l * 0.5f - 0.06f, th), new Vector2(-l * 0.5f + 0.06f, th),
            }, tw);
            trackL = Mesh("TrackL", trackMesh, HullRoot, new Vector3(-(w - tw) * 0.5f, 0f, 0f), Mats.Tread);
            trackR = Mesh("TrackR", trackMesh, HullRoot, new Vector3((w - tw) * 0.5f, 0f, 0f), Mats.Tread);

            // Бронекорпус: профиль сбоку зависит от класса
            Vector2[] profile = hull.style switch
            {
                HullStyle.Light => new[]
                {
                    new Vector2(-l * 0.5f + 0.04f, y0), new Vector2(l * 0.5f - 0.02f, y0), new Vector2(l * 0.5f + 0.05f, y0 + h * 0.25f),
                    new Vector2(l * 0.5f - l * 0.4f, y0 + h), new Vector2(-l * 0.5f + 0.1f, y0 + h), new Vector2(-l * 0.5f, y0 + h * 0.6f),
                },
                HullStyle.Heavy => new[]
                {
                    new Vector2(-l * 0.5f, y0), new Vector2(l * 0.5f - 0.04f, y0), new Vector2(l * 0.5f + 0.04f, y0 + h * 0.55f),
                    new Vector2(l * 0.5f - l * 0.12f, y0 + h), new Vector2(-l * 0.5f + 0.06f, y0 + h), new Vector2(-l * 0.5f, y0 + h * 0.85f),
                },
                _ => new[]
                {
                    new Vector2(-l * 0.5f + 0.04f, y0), new Vector2(l * 0.5f - 0.06f, y0), new Vector2(l * 0.5f, y0 + h * 0.35f),
                    new Vector2(l * 0.5f - l * 0.22f, y0 + h), new Vector2(-l * 0.5f + 0.08f, y0 + h), new Vector2(-l * 0.5f, y0 + h * 0.75f),
                },
            };
            float bodyWidth = w * (hull.style == HullStyle.Heavy ? 0.9f : 0.84f);
            Lit("Body", MeshFactory.Extrude($"body_{id}", profile, bodyWidth), HullRoot, Vector3.zero, body);

            // Детали по классу
            switch (hull.style)
            {
                case HullStyle.Light:
                    // Хвост ласточки: два тонких киля сзади
                    var fin = MeshFactory.Extrude("fin", new[] { new Vector2(-0.22f, 0f), new Vector2(0.06f, 0f), new Vector2(-0.18f, 0.16f) }, 0.04f);
                    Lit("FinL", fin, HullRoot, new Vector3(-w * 0.28f, y0 + h, -l * 0.36f), body * 0.8f);
                    Lit("FinR", fin, HullRoot, new Vector3(w * 0.28f, y0 + h, -l * 0.36f), body * 0.8f);
                    Glow("Stripe", PrimitiveType.Cube, HullRoot, new Vector3(0f, y0 + h + 0.004f, -l * 0.05f), new Vector3(0.05f, 0.01f, l * 0.6f), accent * 0.7f);
                    break;
                case HullStyle.Heavy:
                    // Навесные экраны над гусеницами и выхлопные трубы с жаром
                    var skirt = MeshFactory.Extrude($"skirt_{id}", new[]
                    {
                        new Vector2(-l * 0.46f, th * 0.45f), new Vector2(l * 0.46f, th * 0.45f), new Vector2(l * 0.5f, th * 1.05f), new Vector2(-l * 0.5f, th * 1.05f),
                    }, 0.07f);
                    Lit("SkirtL", skirt, HullRoot, new Vector3(-w * 0.5f - 0.02f, 0f, 0f), body * 0.85f);
                    Lit("SkirtR", skirt, HullRoot, new Vector3(w * 0.5f + 0.02f, 0f, 0f), body * 0.85f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Lit("Exhaust", PrimitiveType.Cylinder, HullRoot, new Vector3(s * w * 0.22f, y0 + h * 0.75f, -l * 0.5f - 0.02f), new Vector3(0.1f, 0.06f, 0.1f), cfg.trackColor * 2f, Quaternion.Euler(90f, 0f, 0f));
                        Glow("Heat", PrimitiveType.Cylinder, HullRoot, new Vector3(s * w * 0.22f, y0 + h * 0.75f, -l * 0.5f - 0.08f), new Vector3(0.07f, 0.005f, 0.07f), new Color(2.4f, 0.3f, 0.2f), Quaternion.Euler(90f, 0f, 0f));
                    }
                    // Моторный отсек сзади ломает «плиту» и делает силуэт тяжёлым
                    Lit("Engine", PrimitiveType.Cube, HullRoot, new Vector3(0f, y0 + h + 0.07f, -l * 0.3f), new Vector3(w * 0.62f, 0.14f, l * 0.3f), body * 0.75f);
                    for (int g = -1; g <= 1; g++)
                        Lit("Grille", PrimitiveType.Cube, HullRoot, new Vector3(g * w * 0.16f, y0 + h + 0.145f, -l * 0.3f), new Vector3(w * 0.1f, 0.01f, l * 0.24f), cfg.trackColor);
                    Glow("Bar", PrimitiveType.Cube, HullRoot, new Vector3(0f, y0 + h + 0.004f, l * 0.3f), new Vector3(w * 0.5f, 0.01f, 0.05f), accent * 0.6f);
                    break;
                default:
                    var skirtM = MeshFactory.Extrude($"skirt_{id}", new[]
                    {
                        new Vector2(-l * 0.44f, th * 0.6f), new Vector2(l * 0.44f, th * 0.6f), new Vector2(l * 0.48f, th * 1.02f), new Vector2(-l * 0.48f, th * 1.02f),
                    }, 0.04f);
                    Lit("SkirtL", skirtM, HullRoot, new Vector3(-w * 0.5f - 0.01f, 0f, 0f), body * 0.9f);
                    Lit("SkirtR", skirtM, HullRoot, new Vector3(w * 0.5f + 0.01f, 0f, 0f), body * 0.9f);
                    Lit("Deck", PrimitiveType.Cube, HullRoot, new Vector3(0f, y0 + h + 0.03f, -l * 0.33f), new Vector3(w * 0.5f, 0.06f, l * 0.18f), body * 0.7f);
                    Glow("StripeL", PrimitiveType.Cube, HullRoot, new Vector3(-w * 0.3f, y0 + h + 0.004f, 0.02f), new Vector3(0.04f, 0.01f, l * 0.4f), accent * 0.6f);
                    Glow("StripeR", PrimitiveType.Cube, HullRoot, new Vector3(w * 0.3f, y0 + h + 0.004f, 0.02f), new Vector3(0.04f, 0.01f, l * 0.4f), accent * 0.6f);
                    break;
            }

            // Фары: в темноте по ним видно, куда смотрит корпус
            Color lamp = team == Team.Player ? new Color(2f, 1.85f, 1.4f) : accent;
            for (int s = -1; s <= 1; s += 2)
                Glow("Lamp", PrimitiveType.Cube, HullRoot, new Vector3(s * bodyWidth * 0.32f, y0 + h * 0.45f, l * 0.5f + 0.01f), new Vector3(0.1f, 0.05f, 0.03f), lamp);
        }

        // ---------- Башня и пушка ----------

        /// <summary>Изогнутый щит перед башней из трёх пластин; светится цветом типа врага.</summary>
        /// <summary>Босс узнаётся по силуэту: корона из шести светящихся шипов на башне и обруч по корпусу.</summary>
        void BuildBossCrown(HullConfig hull)
        {
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                Vector3 pos = new(Mathf.Cos(a) * 0.36f, 0.34f, Mathf.Sin(a) * 0.36f);
                Glow("Crown", PrimitiveType.Cube, TurretPivot, pos, new Vector3(0.07f, 0.26f, 0.07f), accent * 1.6f, Quaternion.Euler(Mathf.Sin(a) * 18f, 0f, -Mathf.Cos(a) * 18f));
            }
            float w = hull.size.x, l = hull.size.y;
            Glow("BossBand", PrimitiveType.Cube, HullRoot, new Vector3(0f, HullTop(hull) - 0.04f, -l * 0.5f - 0.01f), new Vector3(w * 0.9f, 0.05f, 0.02f), accent * 1.4f);
            Glow("BossBandL", PrimitiveType.Cube, HullRoot, new Vector3(-w * 0.5f - 0.01f, HullTop(hull) - 0.04f, 0f), new Vector3(0.02f, 0.05f, l * 0.9f), accent * 1.4f);
            Glow("BossBandR", PrimitiveType.Cube, HullRoot, new Vector3(w * 0.5f + 0.01f, HullTop(hull) - 0.04f, 0f), new Vector3(0.02f, 0.05f, l * 0.9f), accent * 1.4f);
        }

        void BuildFrontShield(HullConfig hull)
        {
            float r = Mathf.Max(hull.size.x, hull.size.y) * 0.62f;
            for (int i = -1; i <= 1; i++)
            {
                var rot = Quaternion.Euler(0f, i * 32f, 0f);
                Vector3 pos = rot * new Vector3(0f, 0.12f, r);
                Lit("Shield", PrimitiveType.Cube, TurretPivot, pos, new Vector3(0.5f, 0.42f, 0.06f), new Color(0.2f, 0.25f, 0.32f), rot);
                Glow("ShieldEdge", PrimitiveType.Cube, TurretPivot, pos + rot * new Vector3(0f, 0.22f, 0.01f), new Vector3(0.5f, 0.03f, 0.07f), accent, rot);
            }
        }

        void BuildTurret(TurretConfig t, Color body)
        {
            float ts = t.turretSize;
            float len = t.barrelLength;
            barrels = new GameObject("Barrels").transform;
            barrels.SetParent(TurretPivot, false);
            // Светлый металл стволов: ствол — главная деталь силуэта сверху, он должен читаться на тёмном полу
            Color metal = new Color(0.24f, 0.26f, 0.3f);

            switch (t.style)
            {
                case TurretStyle.Plasma:
                    Lit("Dome", MeshFactory.Taper("dome", MeshFactory.Polygon(12, ts * 0.5f, ts * 0.5f), 0.2f, 0.6f), TurretPivot, Vector3.zero, body * 1.15f);
                    barrelBaseZ = ts * 0.32f;
                    barrels.localPosition = new Vector3(0f, 0.11f, barrelBaseZ);
                    for (int i = 0; i < 2; i++)
                    {
                        float x = (i - 0.5f) * t.barrelSpacing;
                        Lit("Barrel", PrimitiveType.Cylinder, barrels, new Vector3(x, 0f, len * 0.5f), new Vector3(t.barrelWidth, len * 0.5f, t.barrelWidth), metal, Quaternion.Euler(90f, 0f, 0f));
                        for (int c = 0; c < 2; c++)
                            Glow("Coil", PrimitiveType.Cylinder, barrels, new Vector3(x, 0f, len * (0.35f + c * 0.35f)), new Vector3(t.barrelWidth * 1.6f, 0.02f, t.barrelWidth * 1.6f), accent * 0.9f, Quaternion.Euler(90f, 0f, 0f));
                    }
                    break;

                case TurretStyle.Shotgun:
                    Lit("Box", MeshFactory.Taper("box_turret", MeshFactory.Chamfered(ts * 1.05f, ts * 0.85f, 0.09f), 0.24f, 0.84f, -0.02f), TurretPivot, Vector3.zero, body * 1.1f);
                    barrelBaseZ = ts * 0.36f;
                    barrels.localPosition = new Vector3(0f, 0.12f, barrelBaseZ);
                    Lit("Barrel", PrimitiveType.Cube, barrels, new Vector3(0f, 0f, len * 0.5f), new Vector3(t.barrelWidth, 0.13f, len), metal);
                    Lit("Plate", PrimitiveType.Cube, barrels, new Vector3(0f, 0f, len), new Vector3(t.barrelWidth * 1.25f, 0.17f, 0.05f), body * 0.7f);
                    for (int i = -1; i <= 1; i++)
                        Lit("Hole", PrimitiveType.Cube, barrels, new Vector3(i * t.barrelWidth * 0.3f, 0f, len + 0.03f), new Vector3(0.045f, 0.045f, 0.02f), Color.black);
                    break;

                case TurretStyle.Rail:
                    var wedge = new[]
                    {
                        new Vector2(-ts * 0.45f, -ts * 0.45f), new Vector2(ts * 0.45f, -ts * 0.45f), new Vector2(ts * 0.42f, ts * 0.12f),
                        new Vector2(0f, ts * 0.6f), new Vector2(-ts * 0.42f, ts * 0.12f),
                    };
                    Lit("Wedge", MeshFactory.Taper("rail_turret", wedge, 0.18f, 0.72f, -0.02f), TurretPivot, Vector3.zero, body * 1.15f);
                    barrelBaseZ = ts * 0.2f;
                    barrels.localPosition = new Vector3(0f, 0.1f, barrelBaseZ);
                    for (int i = 0; i < 2; i++)
                        Lit("Rail", PrimitiveType.Cube, barrels, new Vector3((i - 0.5f) * t.barrelSpacing, 0f, len * 0.5f), new Vector3(0.05f, 0.08f, len), metal);
                    Glow("Core", PrimitiveType.Cube, barrels, new Vector3(0f, 0f, len * 0.48f), new Vector3(0.025f, 0.03f, len * 0.9f), accent);
                    Glow("CapL", PrimitiveType.Cube, TurretPivot, new Vector3(-ts * 0.32f, 0.1f, -ts * 0.1f), new Vector3(0.03f, 0.08f, ts * 0.4f), accent * 0.7f);
                    Glow("CapR", PrimitiveType.Cube, TurretPivot, new Vector3(ts * 0.32f, 0.1f, -ts * 0.1f), new Vector3(0.03f, 0.08f, ts * 0.4f), accent * 0.7f);
                    break;

                default: // Пушка
                    Lit("Octa", MeshFactory.Taper("octa_turret", MeshFactory.Polygon(8, ts * 0.5f, ts * 0.58f, 22.5f), 0.21f, 0.78f, -0.03f), TurretPivot, Vector3.zero, body * 1.15f);
                    Lit("Hatch", PrimitiveType.Cylinder, TurretPivot, new Vector3(-ts * 0.15f, 0.22f, -ts * 0.12f), new Vector3(ts * 0.26f, 0.015f, ts * 0.26f), body * 0.8f);
                    barrelBaseZ = ts * 0.42f;
                    barrels.localPosition = new Vector3(0f, 0.11f, barrelBaseZ);
                    Lit("Mantlet", PrimitiveType.Cube, barrels, new Vector3(0f, 0f, 0.02f), new Vector3(ts * 0.36f, 0.14f, 0.12f), body * 0.9f);
                    Lit("Barrel", PrimitiveType.Cylinder, barrels, new Vector3(0f, 0f, len * 0.5f), new Vector3(t.barrelWidth, len * 0.5f, t.barrelWidth), metal, Quaternion.Euler(90f, 0f, 0f));
                    Lit("Brake", PrimitiveType.Cylinder, barrels, new Vector3(0f, 0f, len - 0.06f), new Vector3(t.barrelWidth * 1.6f, 0.07f, t.barrelWidth * 1.6f), metal * 1.2f, Quaternion.Euler(90f, 0f, 0f));
                    break;
            }

            Muzzle = new GameObject("Muzzle").transform;
            Muzzle.SetParent(barrels, false);
            Muzzle.localPosition = new Vector3(0f, 0f, len);

            // Сенсор на башне: разгорается перед выстрелом врага
            sensor = Glow("Sensor", PrimitiveType.Cube, TurretPivot, new Vector3(0f, 0.24f, ts * 0.12f), new Vector3(0.16f, 0.03f, 0.05f), accent);
            glowParts.Remove(sensor);
        }

        // ---------- Служебное: маркер, свет, пузырь зеркала, полоска здоровья ----------

        void BuildHelpers(HullConfig hull, bool withLight)
        {
            float w = hull.size.x, l = hull.size.y;
            bubbleSize = ProjectileSystem.MirrorRadius * 2f;
            var shield = Prims.Part("ShieldBubble", PrimitiveType.Sphere, transform, new Vector3(0f, 0.45f, 0f), Vector3.one * (Mathf.Max(w, l) + 0.85f), Mats.Fx(new Color(0.3f, 0.9f, 2f) * 0.18f));
            shield.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shieldBubble = shield.transform;
            shieldBubble.gameObject.SetActive(false);
            var bubble = Prims.Part("Mirror", PrimitiveType.Sphere, transform, new Vector3(0f, 0.45f, 0f), Vector3.one * bubbleSize, Mats.Fx(new Color(0.4f, 1.2f, 2f) * 0.35f));
            bubble.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mirrorBubble = bubble.transform;
            mirrorBubble.gameObject.SetActive(false);

            barRoot = new GameObject("HealthBar").transform;
            barRoot.SetParent(transform, false);
            var bg = Prims.Part("Bg", PrimitiveType.Cube, barRoot, Vector3.zero, new Vector3(1f, 0.1f, 0.01f), Mats.Unlit(new Color(0.05f, 0.05f, 0.05f)));
            bg.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var fill = Prims.Part("Fill", PrimitiveType.Cube, barRoot, new Vector3(0f, 0f, -0.01f), new Vector3(1f, 0.1f, 0.01f), Mats.Unlit(accent * 0.7f));
            fill.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            barFill = fill.transform;
            barRoot.gameObject.SetActive(false);

            if (!withLight) return;

            // Кольцо под игроком: свой танк всегда легко найти в темноте
            var markerGo = new GameObject("Marker");
            markerGo.transform.SetParent(transform, false);
            markerGo.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            markerGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var ring = markerGo.AddComponent<LineRenderer>();
            ring.sharedMaterial = cfg.fxMaterial;
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.alignment = LineAlignment.TransformZ;
            ring.widthMultiplier = 0.06f;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            const int segments = 48;
            float radius = Mathf.Max(w, l) * 0.62f + 0.2f;
            ring.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
            Color rc = EchoSystem.Ldr(cfg.playerAccent);
            rc.a = 0.55f;
            ring.startColor = ring.endColor = rc;

            var lightGo = new GameObject("HeadLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            headLight = lightGo.AddComponent<Light>();
            headLight.type = LightType.Point;
            headLight.range = cfg.playerLightRange;
            headLight.intensity = cfg.playerLightIntensity;
            headLight.color = cfg.playerLightColor;
            headLight.shadows = LightShadows.Soft;
        }

        MeshRenderer Mesh(string name, Mesh mesh, Transform parent, Vector3 pos, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return r;
        }

        void Lit(string name, Mesh mesh, Transform parent, Vector3 pos, Color color)
            => litParts.Add(Mesh(name, mesh, parent, pos, Mats.Lit(color)));

        void Lit(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color, Quaternion? rot = null)
            => litParts.Add(Prims.Part(name, type, parent, pos, scale, Mats.Lit(color, 0.45f), rot));

        MeshRenderer Glow(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color hdr, Quaternion? rot = null)
        {
            var r = Prims.Part(name, type, parent, pos, scale, Mats.Unlit(hdr), rot);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glowParts.Add(r);
            return r;
        }

        // ---------- Управление ----------

        /// <summary>Башня смотрит в мировом направлении, независимо от поворота корпуса.</summary>
        public void SetTurretDirection(Vector3 dir)
        {
            dir = Prims.Flat(dir);
            if (dir.sqrMagnitude > 0.0001f) SetTurretYaw(Quaternion.LookRotation(dir).eulerAngles.y);
        }

        public void SetTurretYaw(float yaw)
        {
            turretYaw = yaw;
            turretLocal = false;
            ApplyTurret();
        }

        /// <summary>Башня повёрнута относительно корпуса и крутится вместе с ним (превью в гараже).</summary>
        public void SetTurretLocalYaw(float yaw)
        {
            turretYaw = yaw;
            turretLocal = true;
            ApplyTurret();
        }

        void ApplyTurret()
        {
            float local = turretLocal ? turretYaw : turretYaw - transform.eulerAngles.y;
            TurretPivot.localRotation = Quaternion.Euler(0f, local, 0f);
        }

        public void Kick()
        {
            recoil = 1f;
            // Отдача раскачивает корпус против направления ствола
            Vector3 local = HullRoot.InverseTransformDirection(Prims.Flat(TurretPivot.forward));
            pitchVel -= local.z * 60f;
            rollVel += local.x * 60f;
        }

        public void Flash() { flash = 1f; lastHit = Time.time; }
        public void Reveal(float amount) => reveal = Mathf.Max(reveal, Mathf.Clamp01(amount));
        /// <summary>0..1 — враг готовится выстрелить, сенсор разгорается.</summary>
        public void Tell(float amount) => tell = Mathf.Clamp01(amount);
        public void SetMirror(bool on) => mirrorBubble.gameObject.SetActive(on);
        public void SetHealth(float k) => health = Mathf.Clamp01(k);

        void Update()
        {
            float dt = Time.deltaTime;
            reveal = Mathf.Max(0f, reveal - dt / cfg.revealFade);
            flash = Mathf.Max(0f, flash - dt * 7f);
            recoil = Mathf.Max(0f, recoil - dt * 6f);
            barrels.localPosition = new Vector3(barrels.localPosition.x, barrels.localPosition.y, barrelBaseZ - recoil * recoil * 0.18f);

            AnimateMotion(dt);

            // Блоки свойств пишем только когда что-то поменялось: десятки деталей на танк
            Color e = baseEmission + revealColor * (reveal * reveal * 1.6f) + Color.white * flash * 1.2f;
            bool emissionChanged = e != lastEmission;
            if (emissionChanged)
            {
                lastEmission = e;
                mpb.SetColor(Mats.EmissionColor, e);
                foreach (var r in litParts) r.SetPropertyBlock(mpb);
            }

            if (emissionChanged || treadL != lastTreadL || treadR != lastTreadR)
            {
                lastTreadL = treadL;
                lastTreadR = treadR;
                trackMpb.SetColor(Mats.EmissionColor, e * 0.5f);
                trackMpb.SetVector(BaseMapST, new Vector4(3f, 1f, -treadL * 3f, 0f));
                trackL.SetPropertyBlock(trackMpb);
                trackMpb.SetVector(BaseMapST, new Vector4(3f, 1f, -treadR * 3f, 0f));
                trackR.SetPropertyBlock(trackMpb);
            }

            // Скрытый враг выдаёт себя только сенсором за миг до выстрела
            if (hidden)
            {
                bool marked = Marked;
                if (marked != shownNow) ApplyVisibility(marked);
                if (!marked) sensor.enabled = tell > 0.05f;
            }
            if (tell != lastTell)
            {
                lastTell = tell;
                glowMpb.SetColor(Mats.BaseColor, accent * (1f + tell * 2.5f));
                sensor.SetPropertyBlock(glowMpb);
                sensor.transform.localScale = new Vector3(0.16f, 0.03f + tell * 0.04f, 0.05f);
            }

            if (mirrorBubble.gameObject.activeSelf)
                mirrorBubble.localScale = Vector3.one * (bubbleSize + Mathf.Sin(Time.time * 18f) * 0.06f);
            if (headLight != null) headLight.intensity = cfg.playerLightIntensity * (1f + flash * 1.5f);

            // Подбитый танк искрит: состояние видно без взгляда на HUD
            if (health < 0.34f && health > 0f && (reveal > 0.1f || team == Team.Player))
            {
                sparkTimer -= dt;
                if (sparkTimer <= 0f)
                {
                    sparkTimer = Random.Range(0.2f, 0.5f);
                    Fx.I.Sparks(transform.position + Vector3.up * 0.55f + Random.insideUnitSphere * 0.3f, Vector3.up, cfg.bulletDanger, 4, 2.5f);
                }
            }
        }

        /// <summary>Траки бегут по пройденному пути (при повороте — в разные стороны), корпус качается на пружине.</summary>
        void AnimateMotion(float dt)
        {
            if (dt <= 0f) return;
            Vector3 pos = transform.position;
            float yaw = transform.eulerAngles.y;
            Vector3 delta = pos - lastPos;
            float forward = Vector3.Dot(delta, transform.forward);
            float turn = Mathf.DeltaAngle(lastYaw, yaw) * Mathf.Deg2Rad * halfWidth;
            treadL += forward + turn;
            treadR += forward - turn;

            Vector3 velocity = delta / dt;
            Vector3 accel = (velocity - lastVelocity) / dt;
            lastVelocity = velocity;
            lastPos = pos;
            lastYaw = yaw;

            float accForward = Vector3.Dot(accel, transform.forward);
            float accSide = Vector3.Dot(accel, transform.right);
            pitchVel += (-accForward * 0.25f - pitch * 140f - pitchVel * 14f) * dt;
            rollVel += (accSide * 0.2f - roll * 140f - rollVel * 14f) * dt;
            pitch = Mathf.Clamp(pitch + pitchVel * dt, -6f, 6f);
            roll = Mathf.Clamp(roll + rollVel * dt, -5f, 5f);
            HullRoot.localRotation = Quaternion.Euler(pitch, 0f, roll);
        }

        void LateUpdate()
        {
            ApplyTurret();

            bool show = ShowHealthBar && (reveal > 0.08f || Time.time - lastHit < 2f);
            if (barRoot.gameObject.activeSelf != show) barRoot.gameObject.SetActive(show);
            if (!show) return;
            var cam = CameraRig.I != null ? CameraRig.I.transform : null;
            barRoot.position = transform.position + new Vector3(0f, 1.35f, 0.4f);
            if (cam != null) barRoot.rotation = cam.rotation;
            barFill.localScale = new Vector3(health, 0.1f, 0.01f);
            barFill.localPosition = new Vector3((health - 1f) * 0.5f, 0f, -0.01f);
        }

        /// <summary>Сетки и мировые матрицы деталей: из них рисуется «призрак» эха.</summary>
        public void CollectGhostParts(List<(Mesh mesh, Matrix4x4 matrix)> parts)
        {
            foreach (var r in litParts) Add(r);
            Add(trackL);
            Add(trackR);

            void Add(MeshRenderer r)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) parts.Add((mf.sharedMesh, r.transform.localToWorldMatrix));
            }
        }

        /// <summary>Разлёт деталей: каждая получает физику и тлеет несколько секунд.</summary>
        public void Explode(Vector3 hitDir)
        {
            var center = transform.position + Vector3.up * 0.4f;
            Color ember = team == Team.Player ? cfg.bulletDanger : accent;
            var parts = new List<MeshRenderer>(litParts) { trackL, trackR };
            foreach (var r in parts)
            {
                if (r == null) continue;
                r.enabled = true; // смерть врага видна всегда
                var t = r.transform;
                t.SetParent(null, true);
                t.gameObject.layer = Layers.Debris;
                var bounds = r.GetComponent<MeshFilter>().sharedMesh.bounds;
                var col = t.gameObject.AddComponent<BoxCollider>();
                col.size = Vector3.Max(bounds.size, Vector3.one * 0.05f);
                col.center = bounds.center;
                var rb = t.gameObject.AddComponent<Rigidbody>();
                rb.mass = 0.3f;
                rb.AddExplosionForce(9f, center - hitDir.normalized * 0.4f, 4f, 1.2f, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * 4f, ForceMode.Impulse);
                t.gameObject.AddComponent<Debris>().Init(r, ember, 3.5f);
            }
        }
    }

    /// <summary>Обломок: остывает и исчезает.</summary>
    public class Debris : MonoBehaviour
    {
        MeshRenderer rend;
        MaterialPropertyBlock mpb;
        Color ember;
        float life, t;
        Vector3 scale;

        public void Init(MeshRenderer r, Color emberColor, float lifetime)
        {
            rend = r;
            mpb = new MaterialPropertyBlock();
            ember = emberColor;
            life = lifetime;
            scale = transform.localScale;
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = 1f - t / life;
            if (k <= 0f) { Destroy(gameObject); return; }
            mpb.SetColor(Mats.EmissionColor, ember * (k * k * 0.8f));
            rend.SetPropertyBlock(mpb);
            if (k < 0.2f) transform.localScale = scale * (k / 0.2f);
        }
    }
}
