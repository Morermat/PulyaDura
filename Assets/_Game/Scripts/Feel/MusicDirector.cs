using System;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Адаптивная музыка из двух синхронных петель: «спокойная» (эмбиент) и «бой» (ударные и бас).
    /// Громкость боевого слоя растёт, когда враги знают, где игрок. Сетка пола пульсирует в такт.
    /// Свои треки из FL Studio: перетащите в Calm Loop / Combat Loop и укажите BPM (см. README).
    /// Пока треков нет, обе петли синтезируются кодом (MusicSynth).
    /// </summary>
    public class MusicDirector : MonoBehaviour
    {
        public static MusicDirector I { get; private set; }

        [Header("Свои петли (одинаковой длины и темпа)")]
        public AudioClip calmLoop;
        public AudioClip combatLoop;
        public float bpm = 100f;
        [Range(0f, 1f)] public float volume = 0.5f;
        [Tooltip("Насколько сильно сетка пола вспыхивает на долю")] public float floorPulse = 1.2f;

        AudioSource calm, combat;
        float intensity, duckUntil;
        int lastBeat = -1;

        public float BeatLength => 60f / bpm;
        public float BeatPulse { get; private set; }
        public float Intensity => intensity;
        public bool IsPlaying => calm != null && calm.isPlaying;
        public event Action<int> Beat;

        /// <summary>Секунд до следующей доли — чтобы попадать голосом и вспышками в такт.</summary>
        public float TimeToNextBeat
        {
            get
            {
                float beats = Beats();
                return (Mathf.Floor(beats) + 1f - beats) * BeatLength;
            }
        }

        void Awake()
        {
            I = this;
            if (calmLoop == null) calmLoop = MusicSynth.Calm(bpm);
            if (combatLoop == null) combatLoop = MusicSynth.Combat(bpm);
            calm = CreateSource(calmLoop);
            combat = CreateSource(combatLoop);
            combat.volume = 0f;
            double start = AudioSettings.dspTime + 0.2;
            calm.PlayScheduled(start);
            combat.PlayScheduled(start);
        }

        AudioSource CreateSource(AudioClip clip)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            return src;
        }

        /// <summary>Приглушить музыку на время голоса.</summary>
        public void Duck(float seconds) => duckUntil = Mathf.Max(duckUntil, Time.unscaledTime + seconds);

        float Beats() => calm == null || calm.clip == null ? 0f : (float)calm.timeSamples / calm.clip.frequency * bpm / 60f;

        void Update()
        {
            float target = TargetIntensity();
            intensity = Mathf.MoveTowards(intensity, target, Time.unscaledDeltaTime * (target > intensity ? 1.2f : 0.35f));
            float duck = Time.unscaledTime < duckUntil ? 0.45f : 1f;
            calm.volume = volume * (1f - 0.35f * intensity) * duck;
            combat.volume = volume * intensity * duck;

            float beats = Beats();
            int beat = Mathf.FloorToInt(beats);
            float strength = beat % 4 == 0 ? 1.6f : 1f; // первая доля такта сильнее
            BeatPulse = Mathf.Exp(-(beats - beat) * 6f) * strength;
            if (beat != lastBeat)
            {
                lastBeat = beat;
                Beat?.Invoke(beat);
            }

            if (Mats.Floor != null && GameManager.I != null)
                Mats.Floor.SetColor(Mats.EmissionColor, GameManager.Cfg.floorGridGlow * (1f + BeatPulse * floorPulse * (0.5f + intensity)));
        }

        /// <summary>Напряжение: в гараже тихо, в бою — по тревоге врагов.</summary>
        static float TargetIntensity()
        {
            var gm = GameManager.I;
            if (gm == null) return 0f;
            switch (gm.State)
            {
                case GameState.Garage: return 0.15f;
                case GameState.Result: return 0.1f;
                case GameState.RoomClear: return 0.7f;
                case GameState.Dead: return 0.25f;
            }
            foreach (var t in Tank.All)
                if (t.Team == Team.Enemy && t.Brain != null && t.Brain.Alerted) return 1f;
            return 0.3f;
        }
    }

    /// <summary>
    /// Заглушка музыки, синтезированная кодом: 8 тактов ре минора (Dm–B♭–F–C).
    /// Спокойная петля — пэд, суб-бас и «пинг сонара» раз в два такта; боевая — бочка, хэт, клэп и бас восьмыми.
    /// </summary>
    public static class MusicSynth
    {
        const int Rate = 22050;
        const int Bars = 8;

        // Аккорды по два такта и их тоники
        static readonly float[][] Chords =
        {
            new[] { 146.83f, 174.61f, 220.00f }, // Dm
            new[] { 116.54f, 146.83f, 174.61f }, // B♭
            new[] { 174.61f, 220.00f, 261.63f }, // F
            new[] { 130.81f, 164.81f, 196.00f }, // C
        };
        static readonly float[] Roots = { 73.42f, 58.27f, 87.31f, 65.41f };

        static float Sin(double phase) => (float)Math.Sin(phase * 2.0 * Math.PI);

        public static AudioClip Calm(float bpm)
        {
            float beat = 60f / bpm;
            float chordLen = beat * 8f;
            int n = Mathf.CeilToInt(beat * 4f * Bars * Rate);
            var data = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                int c = (int)(t / chordLen) % 4;
                float inChord = (float)(t % chordLen);
                // Плавный переход в следующий аккорд за 0,7 с до смены (по кругу — петля бесшовная)
                float x = Mathf.Clamp01((inChord - (chordLen - 0.7f)) / 0.7f);
                float pad = Pad(Chords[c], t) * (1f - x) + Pad(Chords[(c + 1) % 4], t) * x;
                float sub = Sin(Roots[c] * t) * (1f - x) + Sin(Roots[(c + 1) % 4] * t) * x;

                // Пинг сонара в начале каждого второго такта, с двумя отражениями
                float bar2 = (float)(t % (beat * 8f));
                float ping = Ping(bar2) + Ping(bar2 - 0.36f) * 0.4f + Ping(bar2 - 0.72f) * 0.16f;

                float v = pad * 0.16f + sub * 0.16f;
                lp += (v - lp) * 0.08f;
                data[i] = lp + ping * 0.12f;
            }
            return Clip("music_calm", data);
        }

        static float Pad(float[] chord, double t)
        {
            float s = 0f;
            float lfo = 0.75f + 0.25f * Sin(t * 0.12);
            foreach (var f in chord)
                s += (Sin(f * t) + 0.45f * Sin(f * 2.003 * t) + 0.2f * Sin(f * 3.01 * t)) * lfo;
            return s / chord.Length;
        }

        static float Ping(float t) => t < 0f || t > 1.2f ? 0f : Sin(880.0 * t) * Mathf.Exp(-t * 5f);

        public static AudioClip Combat(float bpm)
        {
            float beat = 60f / bpm;
            int n = Mathf.CeilToInt(beat * 4f * Bars * Rate);
            var data = new float[n];
            uint seed = 777;
            float hatLp = 0f, bassLp = 0f;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                float beats = (float)(t / beat);
                int b = (int)beats;
                float tb = (beats - b) * beat;             // время с начала доли
                float te = ((beats * 2f) % 1f) * beat * 0.5f; // время с начала восьмой
                int c = (b / 8) % 4;

                seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
                float noise = (seed & 0xFFFF) / 32768f - 1f;

                // Бочка на каждую долю
                float kickPhase = 110f * tb - 65f * tb * tb / 0.24f;
                float kick = tb < 0.3f ? Sin(kickPhase) * Mathf.Exp(-tb * 16f) : 0f;
                // Хэт на слабую восьмую
                hatLp += (noise - hatLp) * 0.5f;
                bool offbeat = (beats * 2f) % 2f >= 1f;
                float hat = offbeat ? (noise - hatLp) * Mathf.Exp(-te * 70f) : 0f;
                // Клэп на 2 и 4
                float clap = (b % 2 == 1 && tb < 0.25f) ? (noise * 0.7f + Sin(190.0 * tb) * 0.4f) * Mathf.Exp(-tb * 22f) : 0f;
                // Бас восьмыми по тонике
                float f = Roots[c];
                float saw = Sin(f * t) + 0.5f * Sin(2 * f * t) + 0.33f * Sin(3 * f * t) + 0.25f * Sin(4 * f * t);
                float bass = saw * Mathf.Exp(-te * 7f);
                bassLp += (bass - bassLp) * 0.18f;

                data[i] = kick * 0.55f + hat * 0.12f + clap * 0.22f + bassLp * 0.2f;
            }
            return Clip("music_combat", data);
        }

        static AudioClip Clip(string name, float[] data)
        {
            float peak = 0.001f;
            foreach (var v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            float k = 0.8f / peak;
            for (int i = 0; i < data.Length; i++) data[i] *= k;
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
