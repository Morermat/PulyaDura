using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PulyaDura
{
    /// <summary>
    /// Пост-обработка в духе хоррора: приглушённые холодные цвета, тяжёлая виньетка, плёночное зерно,
    /// мягкое (не неоновое) свечение. Пульс боли при попадании по игроку.
    /// </summary>
    public class PostFx : MonoBehaviour
    {
        public static PostFx I { get; private set; }

        Vignette vignette;
        ChromaticAberration aberration;
        float hurt;

        void Awake()
        {
            I = this;
            var volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.7f);
            bloom.threshold.Override(0.95f);
            bloom.scatter.Override(0.6f);
            bloom.tint.Override(new Color(0.75f, 0.85f, 1f));
            vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.5f);
            vignette.smoothness.Override(0.5f);
            vignette.color.Override(Color.black);
            aberration = profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(0.08f);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.32f);
            // Цвет: насыщенность вниз, холодный сдвиг, чуть плотнее тени — меньше «неона», больше холода
            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(-35f);
            color.contrast.Override(12f);
            color.colorFilter.Override(new Color(0.86f, 0.93f, 1f));
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(-18f);
            volume.profile = profile;
        }

        public void Hurt() => hurt = 1f;

        void Update()
        {
            hurt = Mathf.Max(0f, hurt - Time.unscaledDeltaTime * 3f);
            vignette.intensity.value = 0.5f + hurt * 0.12f;
            vignette.color.value = Color.Lerp(Color.black, new Color(0.35f, 0.02f, 0f), hurt);
            aberration.intensity.value = 0.08f + hurt * 0.8f;
        }
    }
}
