using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    public enum PickupType { Heal, Shield, OneShot, Mark }

    /// <summary>Зелёный ящик снабжения: ломается как обычный, из него выпадает бонус.</summary>
    public class SupplyBox : Crate
    {
        static readonly Color Green = new(0.4f, 2.8f, 0.7f);
        protected override Color BreakColor => Green;

        protected override void OnBroken(DamageInfo info)
        {
            var type = (PickupType)Random.Range(0, 4);
            Pickup.Spawn(type, transform.position, transform.parent);
        }
    }

    /// <summary>
    /// Бонус из зелёного ящика. Подбирается наездом.
    /// Аптечка +2 брони, Щит гасит следующее попадание, Ваншот — следующий выстрел убивает,
    /// Метка — следующие 3 попадания делают врага видимым на пару секунд.
    /// </summary>
    public class Pickup : MonoBehaviour
    {
        PickupType type;
        float age;
        Transform core;

        public static readonly List<Pickup> All = new();
        public PickupType Type => type;

        public static Color ColorOf(PickupType t) => t switch
        {
            // Только синий, красный и белый: аптечка белая, щит синий, ваншот красный, метка голубовато-белая
            PickupType.Heal => new Color(0.4f, 2.8f, 0.6f),
            PickupType.Shield => new Color(0.4f, 1.4f, 3f),
            PickupType.OneShot => new Color(3f, 0.35f, 0.3f),
            _ => new Color(1.3f, 2f, 3f),
        };

        public static string TitleOf(PickupType t) => t switch
        {
            PickupType.Heal => "+2 БРОНИ",
            PickupType.Shield => "ЩИТ",
            PickupType.OneShot => "ВАНШОТ",
            _ => "МЕТКА ×3",
        };

        public static Pickup Spawn(PickupType type, Vector3 pos, Transform parent)
        {
            var go = new GameObject("Pickup_" + type);
            go.transform.SetParent(parent, true);
            go.transform.position = new Vector3(pos.x, 0f, pos.z);
            var p = go.AddComponent<Pickup>();
            p.type = type;
            Color c = ColorOf(type);
            var r = Prims.Part("Core", PrimitiveType.Cube, go.transform, new Vector3(0f, 0.55f, 0f), Vector3.one * 0.38f, Mats.Unlit(c), Quaternion.Euler(45f, 0f, 45f));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            p.core = r.transform;
            var halo = Prims.Part("Halo", PrimitiveType.Sphere, go.transform, new Vector3(0f, 0.55f, 0f), Vector3.one * 0.9f, Mats.Fx(c * 0.18f));
            halo.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 2.6f;
            light.intensity = 2f;
            light.color = EchoSystem.Ldr(c);
            Sfx.I.Play(SfxId.Select, pos, 0.8f, 1.3f);
            return p;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Update()
        {
            age += Time.deltaTime;
            core.Rotate(0f, 120f * Time.deltaTime, 0f, Space.World);
            core.localPosition = new Vector3(0f, 0.55f + Mathf.Sin(age * 3f) * 0.08f, 0f);
            float life = GameManager.Cfg.pickupLifetime;
            if (age > life) { Destroy(gameObject); return; }
            if (age > life - 3f) core.gameObject.SetActive(Mathf.Repeat(age * 6f, 1f) > 0.35f); // мигает перед исчезновением

            var player = GameManager.I.Player;
            if (player == null || player.IsDead) return;
            if (Prims.Flat(player.transform.position - transform.position).sqrMagnitude > 1.1f * 1.1f) return;
            Apply(player);
        }

        public void Apply(Tank player)
        {
            switch (type)
            {
                case PickupType.Heal: player.Heal(2); break;
                case PickupType.Shield: player.ShieldCharges = 1; break;
                case PickupType.OneShot: player.Weapon.PowerShots = 1; break;
                default: player.Weapon.MarkShots = 3; break;
            }
            GameManager.I.OnPickup(this);
            GameManager.I.UI.Floating(transform.position + Vector3.up * 1.2f, TitleOf(type), EchoSystem.Ldr(ColorOf(type)), 40);
            Sfx.I.Play(SfxId.Pickup, transform.position);
            Fx.I.Sparks(transform.position + Vector3.up * 0.5f, Vector3.up, ColorOf(type), 22, 5f);
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Красная бочка: от любого попадания взрывается, ранит всех рядом и выпускает кольцо шрапнели,
    /// которая тоже отскакивает. Соседние бочки детонируют цепочкой. Взрыв — громкий и яркий: его видно и слышно.
    /// </summary>
    public class Barrel : MonoBehaviour, IDamageable
    {
        static readonly Collider[] buffer = new Collider[24];
        bool exploded;

        public void TakeDamage(DamageInfo info) => Explode(info.source);

        /// <summary>Детонация от соседнего взрыва — с короткой задержкой, чтобы цепочка читалась глазом.</summary>
        public void Ignite(Tank source, float delay)
        {
            if (exploded) return;
            StartCoroutine(After(delay, source));
        }

        IEnumerator After(float delay, Tank source)
        {
            yield return new WaitForSeconds(delay);
            Explode(source);
        }

        void Explode(Tank source)
        {
            if (exploded) return;
            exploded = true;
            var cfg = GameManager.Cfg;
            Vector3 pos = transform.position;
            Vector3 center = new(pos.x, Ricochet.ShotHeight, pos.z);
            gameObject.SetActive(false);

            Color fire = new(3f, 0.9f, 0.2f);
            Fx.I.Explosion(center, fire);
            Sfx.I.Play(SfxId.Explode, pos, 1f, 0.8f);
            EchoSystem.I.Emit(center, 8f, fire, 1f);
            GameEvents.RaiseNoise(pos, 22f, Team.Player);
            CameraRig.I.Shake(0.55f);

            int n = Physics.OverlapSphereNonAlloc(center, cfg.barrelRadius, buffer, Layers.SolidMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = buffer[i];
                if (col.TryGetComponent<Tank>(out var t))
                {
                    float d = Prims.Flat(t.transform.position - pos).magnitude;
                    int dmg = d < cfg.barrelRadius * 0.5f ? cfg.barrelDamage : Mathf.Max(1, cfg.barrelDamage - 1);
                    t.TakeDamage(new DamageInfo { amount = dmg, source = source, point = t.transform.position, direction = Prims.Flat(t.transform.position - pos).normalized, explosion = true });
                }
                else if (col.TryGetComponent<Barrel>(out var other) && other != this) other.Ignite(source, 0.12f);
                else if (col.TryGetComponent<Crate>(out var crate)) crate.TakeDamage(new DamageInfo { amount = 2, source = source, point = col.transform.position, direction = Vector3.up });
            }

            ProjectileSystem.I.SpawnShrapnel(source, center, cfg.shrapnel, cfg.shrapnelCount);
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Телепорт: пара светящихся колец. Переносит и танки (наезд), и пули (пролёт через центр),
    /// сохраняя направление. Пуля, ушедшая в портал, может вылететь врагу в спину.
    /// </summary>
    public class Teleporter : MonoBehaviour
    {
        public static readonly List<Teleporter> All = new();
        public const float Radius = 0.6f;

        public Teleporter Partner { get; set; }
        public Color Color { get; private set; }
        LineRenderer ring;
        float spin;

        static readonly Color[] PairColors =
        {
            new(0.5f, 1.0f, 3f), new(2.6f, 0.4f, 0.6f), new(2.2f, 2.4f, 2.8f), new(1.2f, 0.7f, 3f),
        };

        public static Teleporter Create(Vector3 pos, int pairIndex, Transform parent)
        {
            var go = new GameObject("Teleporter" + pairIndex);
            go.transform.SetParent(parent, true);
            go.transform.position = new Vector3(pos.x, 0f, pos.z);
            var t = go.AddComponent<Teleporter>();
            t.Color = PairColors[Mathf.Abs(pairIndex) % PairColors.Length];

            var disk = Prims.Part("Disk", PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0.02f, 0f), new Vector3(1.6f, 0.01f, 1.6f), Mats.Unlit(t.Color * 0.25f));
            disk.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(go.transform, false);
            ringGo.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            ringGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            t.ring = ringGo.AddComponent<LineRenderer>();
            t.ring.sharedMaterial = GameManager.Cfg.fxMaterial;
            t.ring.useWorldSpace = false;
            t.ring.loop = true;
            t.ring.alignment = LineAlignment.TransformZ;
            t.ring.widthMultiplier = 0.08f;
            t.ring.positionCount = 40;
            for (int i = 0; i < 40; i++)
            {
                float a = i * Mathf.PI * 2f / 40f;
                float r = Radius * 1.4f * (i % 4 < 2 ? 1f : 0.86f); // зубчатое кольцо — видно вращение
                t.ring.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
            t.ring.startColor = t.ring.endColor = EchoSystem.Ldr(t.Color);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 2.2f;
            light.intensity = 1.5f;
            light.color = EchoSystem.Ldr(t.Color);

            var trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = Radius;
            trigger.center = new Vector3(0f, 0.5f, 0f);
            return t;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Update()
        {
            spin += Time.deltaTime * 90f;
            ring.transform.localRotation = Quaternion.Euler(90f, spin, 0f);
        }

        void OnTriggerEnter(Collider other)
        {
            if (Partner == null || !other.TryGetComponent<Tank>(out var tank) || tank.IsDead) return;
            if (Time.time - tank.LastTeleport < 0.8f) return;
            tank.TeleportTo(Partner.transform.position);
            Flash(tank.Team == Team.Player ? 1f : 0.6f);
        }

        /// <summary>Вспышка и эхо на обоих концах — телепорт выдаёт того, кто им воспользовался.</summary>
        public void Flash(float strength)
        {
            foreach (var t in new[] { this, Partner })
            {
                Vector3 p = t.transform.position + Vector3.up * Ricochet.ShotHeight;
                EchoSystem.I.Emit(p, 3f * strength + 1f, t.Color, 0.7f);
                Fx.I.Sparks(p, Vector3.up, t.Color, 12, 4f);
            }
            Sfx.I.Play(SfxId.Teleport, transform.position, 0.8f);
            Sfx.I.Play(SfxId.Teleport, Partner.transform.position, 0.8f, 1.2f);
        }
    }

    /// <summary>
    /// Шумовая ловушка: наехал — она воет и зовёт всех врагов комнаты к себе.
    /// Её можно использовать и против врагов: выманить их и зайти с тыла.
    /// Ловушка скрыта: в темноте её не видно, на секунду-две её высвечивает только волна эха (Ping).
    /// Ставятся у стен — там, где чаще всего бывают отскоки.
    /// </summary>
    public class NoiseTrap : MonoBehaviour
    {
        float last = -10f;
        float reveal, pendingTime = -1f, pendingStrength;
        static readonly List<NoiseTrap> all = new();
        MeshRenderer plate;
        MaterialPropertyBlock mpb;
        static readonly Color Amber = new(2.8f, 0.3f, 0.25f);

        public static NoiseTrap Create(Vector3 pos, Transform parent)
        {
            var go = new GameObject("NoiseTrap");
            go.transform.SetParent(parent, true);
            go.transform.position = new Vector3(pos.x, 0f, pos.z);
            var trap = go.AddComponent<NoiseTrap>();
            Prims.Part("Base", PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0.02f, 0f), new Vector3(1f, 0.02f, 1f), Mats.Lit(new Color(0.07f, 0.075f, 0.09f), 0.6f));
            trap.plate = Prims.Part("Plate", PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0.045f, 0f), new Vector3(0.55f, 0.01f, 0.55f), Mats.Unlit(Color.black));
            trap.plate.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trap.mpb = new MaterialPropertyBlock();
            var trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.55f;
            trigger.center = new Vector3(0f, 0.5f, 0f);
            return trap;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!other.TryGetComponent<Tank>(out var tank) || tank.IsDead) return;
            if (Time.time - last < 3f) return;
            last = Time.time;
            Vector3 p = transform.position + Vector3.up * Ricochet.ShotHeight;
            // Шум «от игрока»: на него едут все враги, даже если ловушку задел враг
            GameEvents.RaiseNoise(transform.position, GameManager.Cfg.trapNoiseRadius, Team.Player);
            EchoSystem.I.Emit(p, 6f, Amber, 0.9f);
            Fx.I.LightFlash(p + Vector3.up, EchoSystem.Ldr(Amber), 6f, 6f, 0.6f);
            Sfx.I.Play(SfxId.Alarm, transform.position);
            if (tank.Team == Team.Player)
            {
                CameraRig.I.Shake(0.25f);
                GameManager.I.UI.Floating(p + Vector3.up, "ЛОВУШКА!", UiKit.Danger, 40);
            }
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        /// <summary>Волна эха: ловушки в радиусе вспыхнут, когда до них дойдёт фронт волны.</summary>
        public static void Ping(Vector3 origin, float radius, float strength, float speed)
        {
            foreach (var t in all)
            {
                float d = Prims.Flat(t.transform.position - origin).magnitude;
                if (d > radius + 0.6f) continue;
                t.pendingTime = Time.time + d / Mathf.Max(0.01f, speed);
                t.pendingStrength = strength * Mathf.Lerp(1f, 0.5f, d / radius);
            }
        }

        /// <summary>Ловушку уже высвечивало эхо (для автотеста).</summary>
        public bool Revealed => reveal > 0.05f;

        void Update()
        {
            if (pendingTime >= 0f && Time.time >= pendingTime)
            {
                pendingTime = -1f;
                reveal = Mathf.Max(reveal, pendingStrength);
            }
            if (reveal > 0f) reveal = Mathf.Max(0f, reveal - Time.deltaTime / GameManager.Cfg.revealFade);
            // Сработала — воет и мигает; иначе видна только после эха
            float k = Mathf.Clamp01(1f - (Time.time - last) / 1.5f);
            float blink = k > 0f ? (Mathf.Repeat(Time.time * 8f, 1f) > 0.5f ? 1f : 0.3f) : 0f;
            mpb.SetColor(Mats.BaseColor, Amber * (reveal * reveal * 1.2f + blink * k * 1.5f));
            plate.SetPropertyBlock(mpb);
        }
    }
}
