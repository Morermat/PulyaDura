using System;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Музыка. Два трека, оба играют по кругу:
    /// трек забега (Resources/Music/main_st) и трек гаража и итогов (Resources/Music/garage_st). Между ними плавный переход.
    /// В бою музыка «открывается»: пока враги не знают, где игрок, её приглушает фильтр низких частот.
    /// Сетка пола пульсирует в такт (BPM трека), голос «ПУЛЯ. ДУРА.» попадает в долю.
    /// Если треков нет, играет заглушка из кода (MusicSynth): спокойная и боевая петли.
    /// </summary>
    public class MusicDirector : MonoBehaviour
    {
        public static MusicDirector I { get; private set; }

        [Header("Треки (по кругу)")]
        public AudioClip gameLoop;
        public float gameBpm = 120f;
        [Range(0f, 1f)] public float gameVolume = 0.42f;
        public AudioClip garageLoop;
        public float garageBpm = 98f;
        [Range(0f, 1f)] public float garageVolume = 0.8f;

        [Header("Заглушка из кода, если треков нет")]
        public AudioClip calmLoop;
        public AudioClip combatLoop;
        public float bpm = 100f;
        [Range(0f, 1f)] public float volume = 0.5f;
        [Tooltip("Насколько сильно сетка пола вспыхивает на долю")] public float floorPulse = 1.2f;

        AudioSource calm, combat, game, garage;
        AudioLowPassFilter gameFilter;
        float intensity, duckUntil, garageMix;
        int lastBeat = -1;

        bool HasTracks => game != null;
        static bool InGarage
        {
            get
            {
                var gm = GameManager.I;
                return gm == null || gm.State is GameState.Garage or GameState.Result;
            }
        }
        /// <summary>Трек, по которому считаем доли: тот, что сейчас слышно.</summary>
        AudioSource BeatSource => !HasTracks ? calm : garage != null && garageMix > 0.5f ? garage : game;
        float CurrentBpm => !HasTracks ? bpm : BeatSource == garage ? garageBpm : gameBpm;

        public float BeatLength => 60f / CurrentBpm;
        public float BeatPulse { get; private set; }
        public float Intensity => intensity;
        public bool IsPlaying => HasTracks ? game.isPlaying || (garage != null && garage.isPlaying) : calm != null && calm.isPlaying;
        /// <summary>Сейчас слышен трек гаража (для автотеста).</summary>
        public bool GarageAudible => garage != null && garage.isPlaying && garage.volume > 0.05f;
        /// <summary>Сейчас слышен трек забега (для автотеста).</summary>
        public bool GameAudible => HasTracks ? game.isPlaying && game.volume > 0.05f : IsPlaying;
        public bool UsesTracks => HasTracks;
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
            if (gameLoop == null) gameLoop = Resources.Load<AudioClip>("Music/main_st");
            if (garageLoop == null) garageLoop = Resources.Load<AudioClip>("Music/garage_st");
            double start = AudioSettings.dspTime + 0.2;
            if (gameLoop != null)
            {
                game = CreateSource("Game", gameLoop);
                gameFilter = game.gameObject.AddComponent<AudioLowPassFilter>();
                gameFilter.cutoffFrequency = 22000f;
                game.volume = 0f;
                game.PlayScheduled(start);
                if (garageLoop != null)
                {
                    garage = CreateSource("Garage", garageLoop);
                    garage.volume = 0f;
                    garage.PlayScheduled(start);
                }
                garageMix = 1f;
                return;
            }
            if (calmLoop == null) calmLoop = MusicSynth.Calm(bpm);
            if (combatLoop == null) combatLoop = MusicSynth.Combat(bpm);
            calm = CreateSource("Calm", calmLoop);
            combat = CreateSource("Combat", combatLoop);
            combat.volume = 0f;
            calm.PlayScheduled(start);
            combat.PlayScheduled(start);
        }

        /// <summary>Каждый трек на своём дочернем объекте: фильтр действует только на свой источник.</summary>
        AudioSource CreateSource(string name, AudioClip clip)
        {
            var go = new GameObject("Music_" + name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.reverbZoneMix = 0f; // музыка «сухая», реверберация комнаты на неё не действует
            src.priority = 0;
            return src;
        }

        /// <summary>Приглушить музыку на время голоса.</summary>
        public void Duck(float seconds) => duckUntil = Mathf.Max(duckUntil, Time.unscaledTime + seconds);

        float Beats()
        {
            var src = BeatSource;
            if (src == null || src.clip == null) return 0f;
            return (float)src.timeSamples / src.clip.frequency * CurrentBpm / 60f;
        }

        void Update()
        {
            float target = TargetIntensity();
            float dt = Time.unscaledDeltaTime;
            intensity = Mathf.MoveTowards(intensity, target, dt * (target > intensity ? 1.2f : 0.35f));
            float duck = Time.unscaledTime < duckUntil ? 0.45f : 1f;

            if (HasTracks)
            {
                // Переход гараж ↔ забег за полторы секунды
                garageMix = Mathf.MoveTowards(garageMix, InGarage && garage != null ? 1f : 0f, dt / 1.5f);
                game.volume = gameVolume * (1f - garageMix) * duck * (0.75f + 0.25f * intensity);
                gameFilter.cutoffFrequency = Mathf.Lerp(900f, 22000f, intensity * intensity);
                if (garage != null)
                {
                    garage.volume = garageVolume * garageMix * duck;
                    // Трек гаража стоит на паузе, пока его не слышно, и потом продолжается с того же места
                    if (garageMix <= 0f && garage.isPlaying) garage.Pause();
                    else if (garageMix > 0f && !garage.isPlaying) garage.UnPause();
                    // В гараже трек забега остановлен: новый забег всегда начинается со вступления
                    if (garageMix >= 1f && game.isPlaying) game.Stop();
                    else if (garageMix < 1f && !game.isPlaying) game.Play();
                }
            }
            else
            {
                calm.volume = volume * (1f - 0.35f * intensity) * duck;
                combat.volume = volume * intensity * duck;
            }

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
