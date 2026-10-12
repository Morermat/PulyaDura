using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace PulyaDura
{
    /// <summary>
    /// Голос игры в духе концовки SUPERHOT: в конце комнаты в такт музыке звучит пара слов
    /// («ПУЛЯ. ДУРА.», «ШТЫК. МОЛОДЕЦ.», «ВСЁ. ТИХО.»), каждое слово во весь экран.
    /// Вариант выбирается случайно, два раза подряд один и тот же не звучит.
    /// Сейчас голоса — заглушки из Windows TTS (Resources/Audio/voice_*.wav).
    /// Свои записи перетащите в поля вариантов (см. README).
    /// </summary>
    public class Announcer : MonoBehaviour
    {
        public static Announcer I { get; private set; }

        [Serializable]
        public class Variant
        {
            public string firstText;
            public string secondText;
            public AudioClip first;
            public AudioClip second;
            [Tooltip("Имя заглушки в Resources/Audio, если клип не задан")] public string firstResource;
            public string secondResource;
        }

        public List<Variant> variants = new()
        {
            new Variant { firstText = "ПУЛЯ", secondText = "ДУРА", firstResource = "voice_pulya", secondResource = "voice_dura" },
            new Variant { firstText = "ШТЫК", secondText = "МОЛОДЕЦ", firstResource = "voice_shtyk", secondResource = "voice_molodets" },
            new Variant { firstText = "ВСЁ", secondText = "ТИХО", firstResource = "voice_vse", secondResource = "voice_tiho" },
        };
        [Range(0f, 1f)] public float volume = 1f;

        AudioSource source, lineSource;
        readonly Dictionary<string, AudioClip> lines = new();
        Coroutine running;
        /// <summary>Доли на слово распева: голос медленный и пафосный, слову нужно время прозвучать.</summary>
        const int BeatsPerWord = 2;
        /// <summary>Сколько секунд в конце каждого клипа — хвост реверберации зала (не речь).</summary>
        const float ReverbTail = 2.2f;

        public int LinesSpoken { get; private set; }
        public string LastLine { get; private set; }
        int lastVariant = -1;

        public int WordsSpoken { get; private set; }
        public int LastVariant => lastVariant;
        public int VariantCount => variants.Count;

        void Awake()
        {
            I = this;
            foreach (var v in variants)
            {
                if (v.first == null && !string.IsNullOrEmpty(v.firstResource)) v.first = Resources.Load<AudioClip>("Audio/" + v.firstResource);
                if (v.second == null && !string.IsNullOrEmpty(v.secondResource)) v.second = Resources.Load<AudioClip>("Audio/" + v.secondResource);
            }
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.reverbZoneMix = 0f; // зал уже «вшит» в запись голоса
            lineSource = gameObject.AddComponent<AudioSource>();
            lineSource.playOnAwake = false;
            lineSource.spatialBlend = 0f;
            lineSource.reverbZoneMix = 0f;
        }

        /// <summary>
        /// Реплика диктора (Resources/Audio/line_*.wav) с субтитром внизу экрана.
        /// Новая реплика обрывает предыдущую; музыка приглушается на время речи.
        /// Если клипа нет, показывается только субтитр. Возвращает длительность речи (без хвоста зала).
        /// </summary>
        public float Say(string clipName, string subtitle = "")
        {
            if (!lines.TryGetValue(clipName, out var clip))
                lines[clipName] = clip = Resources.Load<AudioClip>("Audio/" + clipName);
            float speech = clip != null ? Mathf.Max(0.6f, clip.length - ReverbTail) : 2.5f;
            if (clip != null)
            {
                lineSource.Stop();
                lineSource.clip = clip;
                lineSource.volume = volume;
                lineSource.Play();
                MusicDirector.I?.Duck(speech + 0.6f);
            }
            if (!string.IsNullOrEmpty(subtitle) && GameManager.I != null) GameManager.I.UI.Subtitle(subtitle, speech + 1.5f);
            LinesSpoken++;
            LastLine = clipName;
            return speech;
        }

        public void StopLine()
        {
            if (lineSource != null) lineSource.Stop();
        }

        float Beat => MusicDirector.I != null ? MusicDirector.I.BeatLength : 0.6f;

        /// <summary>
        /// Распев: words слов в такт. slam — каждое слово во весь экран. variant &lt; 0 — случайный вариант.
        /// Возвращает длительность в секундах.
        /// </summary>
        public float Chant(int words, bool slam = true, float pitch = 1f, string subtitle = "", int variant = -1)
        {
            Stop();
            if (variants.Count == 0) return 0f;
            if (variant < 0)
            {
                variant = Random.Range(0, variants.Count);
                if (variants.Count > 1 && variant == lastVariant) variant = (variant + 1) % variants.Count;
            }
            lastVariant = variant;
            float wait = MusicDirector.I != null ? MusicDirector.I.TimeToNextBeat : 0f;
            running = StartCoroutine(Run(variants[variant], words, slam, pitch, subtitle, wait));
            return wait + words * Beat * BeatsPerWord / pitch;
        }

        public void Stop()
        {
            if (running != null) StopCoroutine(running);
            running = null;
            if (source != null) source.Stop();
        }

        IEnumerator Run(Variant v, int words, bool slam, float pitch, string subtitle, float wait)
        {
            if (wait > 0f) yield return new WaitForSecondsRealtime(wait);
            for (int i = 0; i < words; i++)
            {
                bool first = i % 2 == 0;
                var clip = first ? v.first : v.second;
                source.pitch = pitch;
                if (clip != null) source.PlayOneShot(clip, volume);
                MusicDirector.I?.Duck(Beat * BeatsPerWord * 1.2f);
                if (slam) GameManager.I.UI.Slam(first ? v.firstText : v.secondText, first ? UiKit.Accent : UiKit.Danger, subtitle);
                CameraRig.I.Shake(0.22f);
                WordsSpoken++;
                yield return new WaitForSecondsRealtime(Beat * BeatsPerWord / pitch);
            }
            if (slam) GameManager.I.UI.EndSlam();
            running = null;
        }
    }
}
