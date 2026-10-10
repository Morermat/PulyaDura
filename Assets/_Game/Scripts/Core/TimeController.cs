using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Единственное место, которое меняет Time.timeScale: пауза, hit-stop и замедление.
    /// Все таймеры UI и эффектов, которые должны идти во время стоп-кадра, используют unscaled time.
    /// </summary>
    public class TimeController : MonoBehaviour
    {
        public static TimeController I { get; private set; }

        public bool Paused { get; set; }
        float hitStopUntil;
        float slowUntil;
        float slowScale = 1f;

        void Awake() => I = this;

        public void HitStop(float seconds) => hitStopUntil = Mathf.Max(hitStopUntil, Time.unscaledTime + seconds);

        public void SlowMo(float scale, float seconds)
        {
            slowScale = scale;
            slowUntil = Time.unscaledTime + seconds;
        }

        public void ResetAll()
        {
            hitStopUntil = slowUntil = 0f;
            Paused = false;
        }

        void Update()
        {
            float s = 1f;
            if (Paused) s = 0f;
            else if (Time.unscaledTime < hitStopUntil) s = 0.03f;
            else if (Time.unscaledTime < slowUntil) s = slowScale;

            Time.timeScale = s;
            if (s > 0f) Time.fixedDeltaTime = 0.02f * Mathf.Clamp(s, 0.05f, 1f);
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
        }
    }
}
