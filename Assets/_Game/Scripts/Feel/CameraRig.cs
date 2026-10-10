using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Камера под углом, вся комната в кадре при любом соотношении сторон окна.
    /// Тряска и толчок двигают только камеру, на симуляцию они не влияют.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig I { get; private set; }

        public float pitch = 58f;
        public float fieldOfView = 34f;
        [Tooltip("Максимальное смещение при тряске")] public float maxShake = 0.45f;

        Camera cam;
        Vector3 basePos;
        Quaternion baseRot;
        Bounds fitBounds;
        bool fitMode;
        float lastAspect;
        float trauma;
        Vector3 kick;
        float seed;

        void Awake()
        {
            I = this;
            cam = GetComponent<Camera>();
            cam.fieldOfView = fieldOfView;
            seed = Random.value * 100f;
            baseRot = Quaternion.Euler(pitch, 0f, 0f);
            basePos = transform.position;
        }

        public void Fit(Bounds bounds)
        {
            fitBounds = bounds;
            fitMode = true;
            Recalculate();
            transform.SetPositionAndRotation(basePos, baseRot);
        }

        public void LookAt(Vector3 target, float distance, float angle)
        {
            fitMode = false;
            baseRot = Quaternion.Euler(angle, 0f, 0f);
            basePos = target - baseRot * Vector3.forward * distance;
            transform.SetPositionAndRotation(basePos, baseRot);
        }

        public void Shake(float amount) => trauma = Mathf.Clamp01(trauma + amount);
        public void Kick(Vector3 dir, float amount) => kick += Prims.Flat(dir) * amount;

        void Recalculate()
        {
            lastAspect = cam.aspect;
            baseRot = Quaternion.Euler(pitch, 0f, 0f);
            Vector3 fwd = baseRot * Vector3.forward;
            Vector3 center = fitBounds.center;
            float lo = 4f, hi = 200f;
            var corners = new Vector3[8];
            Vector3 e = fitBounds.extents;
            int k = 0;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                corners[k++] = center + new Vector3(e.x * x, e.y * y, e.z * z);

            // Подбираем расстояние так, чтобы все углы комнаты влезли в кадр с отступами под HUD
            for (int i = 0; i < 24; i++)
            {
                float mid = (lo + hi) * 0.5f;
                transform.SetPositionAndRotation(center - fwd * mid, baseRot);
                bool fits = true;
                foreach (var c in corners)
                {
                    Vector3 v = cam.WorldToViewportPoint(c);
                    if (v.z <= 0f || v.x < 0.02f || v.x > 0.98f || v.y < 0.1f || v.y > 0.9f) { fits = false; break; }
                }
                if (fits) hi = mid; else lo = mid;
            }
            basePos = center - fwd * hi;
        }

        void LateUpdate()
        {
            if (fitMode && !Mathf.Approximately(cam.aspect, lastAspect)) Recalculate();

            float dt = Time.unscaledDeltaTime;
            trauma = Mathf.Max(0f, trauma - dt * 1.8f);
            kick = Vector3.Lerp(kick, Vector3.zero, 1f - Mathf.Exp(-dt * 14f));

            float s = trauma * trauma * maxShake;
            float tt = Time.unscaledTime * 28f;
            Vector3 shake = new Vector3(Mathf.PerlinNoise(seed, tt) - 0.5f, 0f, Mathf.PerlinNoise(seed + 7f, tt) - 0.5f) * 2f * s;
            float roll = (Mathf.PerlinNoise(seed + 13f, tt) - 0.5f) * 2f * s * 4f;

            transform.SetPositionAndRotation(basePos + shake + kick, baseRot * Quaternion.Euler(0f, 0f, roll));
        }
    }
}
