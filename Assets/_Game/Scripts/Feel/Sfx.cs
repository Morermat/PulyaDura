using System;
using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    public enum SfxId
    {
        Shot, Plasma, Shotgun, RailCharge, RailFire, Tink, Hit, Explode, Dash, Mirror, WallUp,
        Click, Select, RoomClear, Death, CrateBreak, Empty, Hurt
    }

    /// <summary>
    /// Звуки синтезируются кодом при запуске, поэтому у каждого действия есть звук ещё до работы звукаря.
    /// Чтобы заменить звук на свой, перетащите клип в поле overrides (по имени SfxId).
    /// Звук панорамируется по положению на экране: в темноте слышно, откуда стреляли.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        public static Sfx I { get; private set; }

        [Serializable]
        public struct Override
        {
            public SfxId id;
            public AudioClip clip;
        }

        public List<Override> overrides = new();
        [Range(0f, 1f)] public float volume = 0.8f;
        [Tooltip("Гул окружения. 0 — выключен (по умолчанию играет музыка MusicDirector)")]
        [Range(0f, 1f)] public float ambienceVolume = 0f;

        const int SampleRate = 44100;
        readonly Dictionary<SfxId, AudioClip> clips = new();
        readonly List<AudioSource> voices = new();
        AudioSource ambience;
        int next;

        void Awake()
        {
            I = this;
            Generate();
            foreach (var o in overrides) if (o.clip != null) clips[o.id] = o.clip;
            for (int i = 0; i < 20; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                voices.Add(src);
            }
            if (ambienceVolume > 0f)
            {
                ambience = gameObject.AddComponent<AudioSource>();
                ambience.clip = Drone();
                ambience.loop = true;
                ambience.volume = ambienceVolume;
                ambience.Play();
            }
        }

        public void Play(SfxId id, Vector3? at = null, float vol = 1f, float pitch = 1f)
        {
            if (!clips.TryGetValue(id, out var clip)) return;
            var src = voices[next];
            next = (next + 1) % voices.Count;
            float pan = 0f;
            var cam = CameraRig.I;
            if (at.HasValue && cam != null)
            {
                pan = Mathf.Clamp((at.Value.x - cam.transform.position.x) / 14f, -0.85f, 0.85f);
                var player = GameManager.I != null ? GameManager.I.Player : null;
                if (player != null)
                {
                    float d = Vector3.Distance(player.transform.position, at.Value);
                    vol *= Mathf.Lerp(1f, 0.35f, d / 25f);
                }
            }
            src.panStereo = pan;
            src.pitch = pitch;
            src.PlayOneShot(clip, vol * volume);
        }

        // ---------- Синтез ----------

        delegate float Wave(float t, int i);

        AudioClip Make(string name, float seconds, Wave f)
        {
            int n = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float fade = Mathf.Clamp01((seconds - t) / 0.01f); // без щелчка в конце
                data[i] = Mathf.Clamp(f(t, i) * fade, -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static uint seed = 12345;
        static float Noise()
        {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            return (seed & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
        }

        static float Sin(float phase) => Mathf.Sin(phase * Mathf.PI * 2f);

        /// <summary>Фаза для частоты, меняющейся от f0 до f1 по экспоненте за время len.</summary>
        static float Sweep(float t, float f0, float f1, float len)
        {
            float k = Mathf.Log(f1 / f0) / len;
            return Mathf.Abs(k) < 1e-4f ? f0 * t : f0 * (Mathf.Exp(k * t) - 1f) / k;
        }

        void Generate()
        {
            float lp = 0f, lp2 = 0f, brown = 0f;

            clips[SfxId.Shot] = Make("shot", 0.4f, (t, i) =>
            {
                lp += (Noise() - lp) * 0.25f;
                return lp * Mathf.Exp(-t * 16f) * 1.1f + Sin(Sweep(t, 150f, 45f, 0.4f)) * Mathf.Exp(-t * 9f) * 0.9f;
            });
            clips[SfxId.Plasma] = Make("plasma", 0.18f, (t, i) =>
            {
                float ph = Sweep(t, 700f, 1900f, 0.18f);
                float sq = Mathf.Sign(Sin(ph)) * 0.25f + Sin(ph * 0.5f) * 0.3f;
                return sq * Mathf.Exp(-t * 18f);
            });
            clips[SfxId.Shotgun] = Make("shotgun", 0.5f, (t, i) =>
            {
                lp += (Noise() - lp) * 0.35f;
                return lp * Mathf.Exp(-t * 8f) * 1.2f + Sin(Sweep(t, 100f, 40f, 0.5f)) * Mathf.Exp(-t * 7f) * 0.8f;
            });
            clips[SfxId.RailCharge] = Make("railcharge", 0.5f, (t, i) =>
            {
                float x = t / 0.5f;
                float f = 220f + 1100f * x * x;
                return Sin(f * t + Mathf.Sin(t * 60f) * 0.6f) * (0.08f + 0.3f * x);
            });
            clips[SfxId.RailFire] = Make("railfire", 0.7f, (t, i) =>
            {
                lp += (Noise() - lp) * 0.5f;
                return lp * Mathf.Exp(-t * 7f) * 0.7f + Sin(Sweep(t, 2600f, 180f, 0.7f)) * Mathf.Exp(-t * 5f) * 0.5f;
            });
            clips[SfxId.Tink] = Make("tink", 0.35f, (t, i) =>
            {
                float env = Mathf.Exp(-t * 18f);
                // три копии с задержкой — короткое «эхо»
                float a = Sin(1500f * t) + Sin(2380f * t) * 0.35f;
                float b = t > 0.09f ? (Sin(1500f * (t - 0.09f)) * 0.35f) * Mathf.Exp(-(t - 0.09f) * 14f) : 0f;
                return a * env * 0.45f + b;
            });
            clips[SfxId.Hit] = Make("hit", 0.3f, (t, i) =>
            {
                float env = Mathf.Exp(-t * 16f);
                return (Sin(310f * t) + Sin(770f * t) * 0.6f + Sin(1230f * t) * 0.4f) * env * 0.4f + Noise() * Mathf.Exp(-t * 60f) * 0.5f;
            });
            clips[SfxId.Hurt] = Make("hurt", 0.4f, (t, i) =>
            {
                lp += (Noise() - lp) * 0.2f;
                return Sin(Sweep(t, 300f, 90f, 0.4f)) * Mathf.Exp(-t * 7f) * 0.6f + lp * Mathf.Exp(-t * 10f);
            });
            clips[SfxId.Explode] = Make("explode", 1.2f, (t, i) =>
            {
                brown = Mathf.Clamp(brown + Noise() * 0.08f, -1f, 1f) * 0.995f;
                lp2 += (Noise() - lp2) * 0.15f;
                return brown * Mathf.Exp(-t * 3f) * 1.4f + lp2 * Mathf.Exp(-t * 6f) * 0.8f + Sin(Sweep(t, 70f, 28f, 1.2f)) * Mathf.Exp(-t * 4f) * 0.9f;
            });
            clips[SfxId.Dash] = Make("dash", 0.3f, (t, i) =>
            {
                float n = Noise();
                lp += (n - lp) * 0.4f;
                lp2 += (n - lp2) * 0.05f;
                return (lp - lp2) * Mathf.Sin(Mathf.PI * t / 0.3f) * 1.2f;
            });
            clips[SfxId.Mirror] = Make("mirror", 0.5f, (t, i) =>
            {
                float trem = 0.6f + 0.4f * Mathf.Sin(t * 70f);
                return (Sin(880f * t) + Sin(1320f * t) * 0.6f + Sin(1760f * t) * 0.4f) * trem * Mathf.Exp(-t * 5f) * 0.25f;
            });
            clips[SfxId.WallUp] = Make("wallup", 0.4f, (t, i) =>
            {
                lp += (Noise() - lp) * 0.1f;
                return Sin(Sweep(t, 70f, 150f, 0.4f)) * Mathf.Exp(-t * 6f) * 0.8f + lp * Mathf.Exp(-t * 12f) * 1.2f;
            });
            clips[SfxId.Click] = Make("click", 0.06f, (t, i) => Sin(1300f * t) * Mathf.Exp(-t * 70f) * 0.5f);
            clips[SfxId.Select] = Make("select", 0.14f, (t, i) => Sin(Sweep(t, 620f, 990f, 0.14f)) * Mathf.Exp(-t * 18f) * 0.4f);
            clips[SfxId.Empty] = Make("empty", 0.08f, (t, i) => Noise() * Mathf.Exp(-t * 80f) * 0.4f);
            clips[SfxId.CrateBreak] = Make("crate", 0.4f, (t, i) =>
            {
                lp += (Noise() - lp) * 0.3f;
                return lp * Mathf.Exp(-t * 11f) * 1.3f + Sin(Sweep(t, 220f, 90f, 0.4f)) * Mathf.Exp(-t * 14f) * 0.5f;
            });
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            clips[SfxId.RoomClear] = Make("clear", 1.0f, (t, i) =>
            {
                int k = Mathf.Min(3, (int)(t / 0.11f));
                float lt = t - k * 0.11f;
                float tone = Sin(notes[k] * t) * 0.6f + Sin(notes[k] * 2f * t) * 0.15f;
                return tone * Mathf.Exp(-lt * (k == 3 ? 3f : 10f)) * 0.4f;
            });
            clips[SfxId.Death] = Make("death", 1.1f, (t, i) =>
            {
                lp += (Noise() - lp) * 0.2f;
                float ph = Sweep(t, 620f, 70f, 1.1f) + Mathf.Sin(t * 40f) * 0.3f;
                return Sin(ph) * Mathf.Exp(-t * 2.5f) * 0.5f + lp * Mathf.Exp(-t * 4f) * 0.6f;
            });
        }

        AudioClip Drone()
        {
            float len = 8f;
            float lp = 0f;
            return Make("drone", len, (t, i) =>
            {
                lp += (Noise() - lp) * 0.01f;
                float edge = Mathf.Clamp01(t / 0.5f) * Mathf.Clamp01((len - t) / 0.5f);
                float hum = Sin(55f * t) * 0.25f + Sin(82.4f * t) * 0.12f * (0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f / len));
                return (hum + lp * 2.5f) * 0.35f * Mathf.Lerp(0.6f, 1f, edge);
            });
        }
    }
}
