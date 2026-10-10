using System.Collections;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Голос игры в духе концовки SUPERHOT: в конце комнаты в такт музыке звучит
    /// «ПУЛЯ. ДУРА. ПУЛЯ. ДУРА.», и каждое слово во весь экран.
    /// Сейчас голос — заглушка из Windows TTS (Resources/Audio/voice_*.wav).
    /// Запишите своё в FL Studio и перетащите клипы в First / Second Word (см. README).
    /// </summary>
    public class Announcer : MonoBehaviour
    {
        public static Announcer I { get; private set; }

        public AudioClip firstWord;
        public AudioClip secondWord;
        public string firstText = "ПУЛЯ";
        public string secondText = "ДУРА";
        [Range(0f, 1f)] public float volume = 1f;

        AudioSource source;
        Coroutine running;

        public int WordsSpoken { get; private set; }

        void Awake()
        {
            I = this;
            if (firstWord == null) firstWord = Resources.Load<AudioClip>("Audio/voice_pulya");
            if (secondWord == null) secondWord = Resources.Load<AudioClip>("Audio/voice_dura");
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        float Beat => MusicDirector.I != null ? MusicDirector.I.BeatLength : 0.6f;

        /// <summary>Распев: words слов в такт. slam — каждое слово во весь экран. Возвращает длительность в секундах.</summary>
        public float Chant(int words, bool slam = true, float pitch = 1f, string subtitle = "")
        {
            Stop();
            float wait = MusicDirector.I != null ? MusicDirector.I.TimeToNextBeat : 0f;
            running = StartCoroutine(Run(words, slam, pitch, subtitle, wait));
            return wait + words * Beat / pitch;
        }

        public void Stop()
        {
            if (running != null) StopCoroutine(running);
            running = null;
            source.Stop();
        }

        IEnumerator Run(int words, bool slam, float pitch, string subtitle, float wait)
        {
            if (wait > 0f) yield return new WaitForSecondsRealtime(wait);
            for (int i = 0; i < words; i++)
            {
                bool first = i % 2 == 0;
                var clip = first ? firstWord : secondWord;
                source.pitch = pitch;
                if (clip != null) source.PlayOneShot(clip, volume);
                MusicDirector.I?.Duck(Beat * 1.5f);
                if (slam) GameManager.I.UI.Slam(first ? firstText : secondText, first ? UiKit.Accent : UiKit.Danger, subtitle);
                CameraRig.I.Shake(0.22f);
                WordsSpoken++;
                yield return new WaitForSecondsRealtime(Beat / pitch);
            }
            if (slam) GameManager.I.UI.EndSlam();
            running = null;
        }
    }
}
