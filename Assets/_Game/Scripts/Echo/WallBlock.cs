using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Стена в темноте. Невидима, пока её не коснётся эхо-волна: тогда контур вспыхивает и гаснет.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class WallBlock : MonoBehaviour
    {
        public static readonly List<WallBlock> All = new();

        [Tooltip("Постоянное свечение (временные стены игрока)")] public float baseGlow;
        public Color baseColor = Color.black;

        public float Reveal { get; private set; }
        Color color;
        float flash;
        MeshRenderer rend;
        MaterialPropertyBlock mpb;
        bool dirty = true;

        void Awake()
        {
            rend = GetComponent<MeshRenderer>();
            mpb = new MaterialPropertyBlock();
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void Show(float amount, Color c)
        {
            if (amount >= Reveal || amount > 0.6f)
            {
                Reveal = Mathf.Clamp01(Mathf.Max(amount, Reveal));
                color = c;
                dirty = true;
            }
        }

        /// <summary>Белая вспышка при попадании.</summary>
        public void Flash() { flash = 1f; dirty = true; }

        void Update()
        {
            if (Reveal > 0f)
            {
                Reveal = Mathf.Max(0f, Reveal - Time.deltaTime / GameManager.Cfg.revealFade);
                dirty = true;
            }
            if (flash > 0f)
            {
                flash = Mathf.Max(0f, flash - Time.deltaTime * 8f);
                dirty = true;
            }
            if (!dirty && baseGlow <= 0f) return;
            dirty = false;

            float k = Reveal * Reveal * 2.2f;
            Color e = color * k + baseColor * baseGlow + Color.white * flash * 1.5f;
            mpb.SetColor(Mats.EmissionColor, e);
            rend.SetPropertyBlock(mpb);
        }
    }
}
