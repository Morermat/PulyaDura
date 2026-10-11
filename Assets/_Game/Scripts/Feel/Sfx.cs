using System;
using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    public enum SfxId
    {
        Shot, Plasma, Shotgun, RailCharge, RailFire, Tink, Hit, Explode, Dash, Mirror, WallUp,
        Click, Select, RoomClear, Death, CrateBreak, Empty, Hurt,
        Pickup, Teleport, Alarm, Clang
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
            for (int i = 0; i < 28; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.dopplerLevel = 0f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = 4f;
                src.maxDistance = 45f;
                src.spread = 40f;
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

        /// <summary>
        /// Звук в точке мира — объёмный: слышно, слева/справа и насколько далеко (слушатель стоит в танке игрока,
        /// см. AudioListenerRig), плюс реверберация комнаты. Без точки — плоский звук интерфейса.
        /// </summary>
        public void Play(SfxId id, Vector3? at = null, float vol = 1f, float pitch = 1f)
        {
            if (!clips.TryGetValue(id, out var clip)) return;
            var src = voices[next];
            next = (next + 1) % voices.Count;
            if (at.HasValue)
            {
                src.transform.position = at.Value + Vector3.up * 0.5f;
                src.spatialBlend = 0.85f; // немного «в центре», чтобы далёкие звуки не пропадали совсем
                src.reverbZoneMix = 1f;
            }
            else
            {
                src.spatialBlend = 0f;
                src.reverbZoneMix = 0f;
            }
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

        static readonly float[] PickupNotes = { 587.33f, 698.46f, 880f, 1174.66f };

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
            // Маримба на «ре» (587 Гц): основной тон + деревянный 4-й обертон, который гаснет быстрее.
            // Высоту каждого отскока задаёт ProjectileSystem.BouncePitch — получается гамма.
            clips[SfxId.Tink] = Make("marimba", 0.9f, (t, i) =>
            {
                const float f = 587.33f;
                float body = Sin(f * t) * Mathf.Exp(-t * 6f);
                float wood = Sin(f * 3.93f * t) * Mathf.Exp(-t * 22f) * 0.45f;
                float strike = Sin(f * 9.2f * t) * Mathf.Exp(-t * 70f) * 0.25f;
                float attack = Mathf.Clamp01(t / 0.002f);
                return (body + wood + strike) * attack * 0.5f;
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
            clips[SfxId.Pickup] = Make("pickup", 0.45f, (t, i) =>
            {
                // быстрое восходящее арпеджио ре-фа-ля-ре
                int k = Mathf.Min(3, (int)(t / 0.07f));
                float lt = t - k * 0.07f;
                return (Sin(PickupNotes[k] * t) + 0.3f * Sin(PickupNotes[k] * 2f * t)) * Mathf.Exp(-lt * 12f) * 0.35f;
            });
            clips[SfxId.Teleport] = Make("teleport", 0.5f, (t, i) =>
            {
                float sweep = Sweep(t, 300f, 1800f, 0.5f);
                return (Sin(sweep) * 0.3f + Sin(sweep * 1.5f) * 0.15f) * Mathf.Sin(Mathf.PI * t / 0.5f) * (0.6f + 0.4f * Mathf.Sin(t * 90f));
            });
            clips[SfxId.Alarm] = Make("alarm", 1.4f, (t, i) =>
            {
                // сирена: два тона чередуются, как у тревоги
                float f = (int)(t / 0.18f) % 2 == 0 ? 880f : 660f;
                float sq = Mathf.Sign(Sin(f * t)) * 0.18f + Sin(f * t) * 0.2f;
                return sq * Mathf.Clamp01((1.4f - t) / 0.3f);
            });
            clips[SfxId.Clang] = Make("clang", 0.5f, (t, i) =>
            {
                float env = Mathf.Exp(-t * 9f);
                return (Sin(523f * t) + Sin(1371f * t) * 0.6f + Sin(2213f * t) * 0.35f) * env * 0.35f + Noise() * Mathf.Exp(-t * 80f) * 0.4f;
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
