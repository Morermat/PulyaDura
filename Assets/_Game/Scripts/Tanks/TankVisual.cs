using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Внешний вид танка, собранный из примитивов по параметрам корпуса и пушки.
    /// Художник может заменить детали на модели, сохранив TurretPivot и Muzzle.
    /// </summary>
    public class TankVisual : MonoBehaviour
    {
        public Transform HullRoot { get; private set; }
        public Transform TurretPivot { get; private set; }
        public Transform Muzzle { get; private set; }
        public bool ShowHealthBar { get; set; }

        readonly List<MeshRenderer> litParts = new();
        readonly List<MeshRenderer> allParts = new();
        MaterialPropertyBlock mpb;
        Transform barrels;
        MeshRenderer eye;
        Transform mirrorBubble;
        Transform barRoot, barFill;
        Light headLight;
        Team team;
        GameConfig cfg;

        float reveal, flash, recoil, tell, health = 1f, lastHit = -10f;
        Color revealColor;
        float barrelBaseZ;
        float bubbleSize;

        public void Build(HullConfig hull, TurretConfig turret, Team owner, bool withLight)
        {
            cfg = GameManager.Cfg;
            team = owner;
            mpb = new MaterialPropertyBlock();
            bool player = owner == Team.Player;
            Color body = player ? cfg.playerBody : cfg.enemyBody;
            Color accent = player ? cfg.playerAccent : cfg.enemyAccent;
            revealColor = player ? cfg.playerAccent * 0.15f : cfg.enemyReveal;

            float w = hull.size.x, l = hull.size.y, hh = hull.height;
            HullRoot = new GameObject("Hull").transform;
            HullRoot.SetParent(transform, false);

            Lit("Body", PrimitiveType.Cube, HullRoot, new Vector3(0f, 0.2f + hh * 0.5f, 0f), new Vector3(w * 0.74f, hh, l * 0.9f), body);
            Lit("Nose", PrimitiveType.Cube, HullRoot, new Vector3(0f, 0.2f + hh * 0.35f, l * 0.43f), new Vector3(w * 0.62f, hh * 0.6f, l * 0.14f), body * 0.8f);
            Lit("TrackL", PrimitiveType.Cube, HullRoot, new Vector3(-w * 0.4f, 0.2f, 0f), new Vector3(w * 0.22f, 0.36f, l), cfg.trackColor, 0.6f);
            Lit("TrackR", PrimitiveType.Cube, HullRoot, new Vector3(w * 0.4f, 0.2f, 0f), new Vector3(w * 0.22f, 0.36f, l), cfg.trackColor, 0.6f);
            Glow("Stripe", PrimitiveType.Cube, HullRoot, new Vector3(0f, 0.2f + hh + 0.005f, -l * 0.3f), new Vector3(w * 0.5f, 0.02f, 0.07f), accent * 0.6f);
            Glow("Tail", PrimitiveType.Cube, HullRoot, new Vector3(0f, 0.2f + hh * 0.5f, -l * 0.455f), new Vector3(w * 0.5f, 0.06f, 0.02f), accent * 0.4f);

            TurretPivot = new GameObject("Turret").transform;
            TurretPivot.SetParent(transform, false);
            TurretPivot.localPosition = new Vector3(0f, 0.2f + hh, 0f);

            float ts = turret.turretSize;
            Lit("TurretBase", PrimitiveType.Cylinder, TurretPivot, new Vector3(0f, 0.08f, 0f), new Vector3(ts, 0.08f, ts), body * 1.15f);
            Lit("TurretTop", PrimitiveType.Cube, TurretPivot, new Vector3(0f, 0.2f, -0.03f), new Vector3(ts * 0.68f, 0.14f, ts * 0.78f), body);

            barrels = new GameObject("Barrels").transform;
            barrels.SetParent(TurretPivot, false);
            barrelBaseZ = ts * 0.3f;
            barrels.localPosition = new Vector3(0f, 0.16f, barrelBaseZ);
            for (int i = 0; i < turret.barrels; i++)
            {
                float x = (i - (turret.barrels - 1) * 0.5f) * turret.barrelSpacing;
                Lit("Barrel", PrimitiveType.Cylinder, barrels, new Vector3(x, 0f, turret.barrelLength * 0.5f),
                    new Vector3(turret.barrelWidth, turret.barrelLength * 0.5f, turret.barrelWidth), cfg.trackColor * 1.6f, 0.7f, Quaternion.Euler(90f, 0f, 0f));
            }
            Muzzle = new GameObject("Muzzle").transform;
            Muzzle.SetParent(barrels, false);
            Muzzle.localPosition = new Vector3(0f, 0f, turret.barrelLength);

            eye = Glow("Eye", PrimitiveType.Cube, TurretPivot, new Vector3(0f, 0.29f, ts * 0.22f), new Vector3(0.14f, 0.04f, 0.06f), accent);

            bubbleSize = Mathf.Max(w, l) + 0.6f;
            var bubble = Prims.Part("Mirror", PrimitiveType.Sphere, transform, new Vector3(0f, 0.45f, 0f), Vector3.one * bubbleSize, Mats.Fx(new Color(0.4f, 1.2f, 2f) * 0.35f));
            bubble.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mirrorBubble = bubble.transform;
            mirrorBubble.gameObject.SetActive(false);

            // Полоска здоровья над врагом: видна, пока враг подсвечен или недавно ранен
            barRoot = new GameObject("HealthBar").transform;
            barRoot.SetParent(transform, false);
            var bg = Prims.Part("Bg", PrimitiveType.Cube, barRoot, Vector3.zero, new Vector3(1f, 0.1f, 0.01f), Mats.Unlit(new Color(0.05f, 0.05f, 0.05f)));
            bg.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var fill = Prims.Part("Fill", PrimitiveType.Cube, barRoot, new Vector3(0f, 0f, -0.01f), new Vector3(1f, 0.1f, 0.01f), Mats.Unlit(accent * 0.7f));
            fill.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            barFill = fill.transform;
            barRoot.gameObject.SetActive(false);

            if (withLight)
            {
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
        }

        MeshRenderer Lit(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color, float smooth = 0.35f, Quaternion? rot = null)
        {
            var r = Prims.Part(name, type, parent, pos, scale, Mats.Lit(color, smooth), rot);
            litParts.Add(r);
            allParts.Add(r);
            return r;
        }

        MeshRenderer Glow(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color hdr)
        {
            var r = Prims.Part(name, type, parent, pos, scale, Mats.Unlit(hdr));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            allParts.Add(r);
            return r;
        }

        public void SetTurretDirection(Vector3 dir)
        {
            dir = Prims.Flat(dir);
            if (dir.sqrMagnitude > 0.0001f) TurretPivot.rotation = Quaternion.LookRotation(dir);
        }

        public void SetTurretYaw(float yaw) => TurretPivot.rotation = Quaternion.Euler(0f, yaw, 0f);

        public void Kick() => recoil = 1f;
        public void Flash() { flash = 1f; lastHit = Time.time; }
        public void Reveal(float amount) => reveal = Mathf.Max(reveal, Mathf.Clamp01(amount));
        /// <summary>0..1 — враг готовится выстрелить, глаз разгорается.</summary>
        public void Tell(float amount) => tell = Mathf.Clamp01(amount);
        public void SetMirror(bool on) => mirrorBubble.gameObject.SetActive(on);
        public void SetHealth(float k) => health = Mathf.Clamp01(k);

        void Update()
        {
            float dt = Time.deltaTime;
            reveal = Mathf.Max(0f, reveal - dt / cfg.revealFade);
            flash = Mathf.Max(0f, flash - dt * 7f);
            recoil = Mathf.Max(0f, recoil - dt * 6f);

            barrels.localPosition = new Vector3(0f, 0.16f, barrelBaseZ - recoil * recoil * 0.18f);
            eye.transform.localScale = new Vector3(0.14f, 0.04f + tell * 0.05f, 0.06f);

            float k = reveal * reveal;
            Color e = revealColor * k * 1.6f + Color.white * flash * 1.2f;
            mpb.SetColor(Mats.EmissionColor, e);
            foreach (var r in litParts) r.SetPropertyBlock(mpb);

            if (mirrorBubble.gameObject.activeSelf)
                mirrorBubble.localScale = Vector3.one * (bubbleSize + Mathf.Sin(Time.time * 18f) * 0.06f);

            if (headLight != null) headLight.intensity = cfg.playerLightIntensity * (1f + flash * 1.5f);
        }

        void LateUpdate()
        {
            bool show = ShowHealthBar && (reveal > 0.08f || Time.time - lastHit < 2f);
            if (barRoot.gameObject.activeSelf != show) barRoot.gameObject.SetActive(show);
            if (!show) return;
            var cam = CameraRig.I != null ? CameraRig.I.transform : null;
            barRoot.position = transform.position + new Vector3(0f, 1.35f, 0.4f);
            if (cam != null) barRoot.rotation = cam.rotation;
            barFill.localScale = new Vector3(health, 0.1f, 0.01f);
            barFill.localPosition = new Vector3((health - 1f) * 0.5f, 0f, -0.01f);
        }

        /// <summary>Разлёт деталей: каждая получает физику и тлеет несколько секунд.</summary>
        public void Explode(Vector3 hitDir)
        {
            var center = transform.position + Vector3.up * 0.4f;
            Color ember = team == Team.Player ? cfg.bulletDanger : cfg.enemyAccent;
            foreach (var r in litParts)
            {
                if (r == null) continue;
                var t = r.transform;
                t.SetParent(null, true);
                t.gameObject.layer = Layers.Debris;
                var col = t.gameObject.AddComponent<BoxCollider>();
                col.size = Vector3.one;
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
