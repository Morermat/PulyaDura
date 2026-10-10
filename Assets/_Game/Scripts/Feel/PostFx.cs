using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PulyaDura
{
    /// <summary>Пост-обработка: свечение, виньетка, плёнка. Пульс боли при попадании по игроку.</summary>
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
            bloom.intensity.Override(1.3f);
            bloom.threshold.Override(0.8f);
            bloom.scatter.Override(0.72f);
            vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.38f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(Color.black);
            aberration = profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(0.08f);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            profile.Add<FilmGrain>(true).intensity.Override(0.18f);
            volume.profile = profile;
        }

        public void Hurt() => hurt = 1f;

        void Update()
        {
            hurt = Mathf.Max(0f, hurt - Time.unscaledDeltaTime * 3f);
            vignette.intensity.value = 0.38f + hurt * 0.12f;
            vignette.color.value = Color.Lerp(Color.black, new Color(0.35f, 0.02f, 0f), hurt);
            aberration.intensity.value = 0.08f + hurt * 0.8f;
        }
    }
}
