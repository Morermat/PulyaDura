using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Подсветка решётки эхом. Лучи эха проходят сквозь решётку (у неё свой слой), поэтому она не может
    /// «поймать» луч, как стена. Вместо этого EchoSystem сообщает о каждой волне, и решётка вспыхивает,
    /// когда фронт волны до неё доходит. В темноте решётку не видно — как и всё остальное.
    /// </summary>
    public class FenceGlow : MonoBehaviour
    {
        static readonly List<FenceGlow> all = new();

        readonly List<MeshRenderer> bars = new();
        MaterialPropertyBlock mpb;
        float reveal, pendingTime = -1f, pendingStrength;
        Color color, pendingColor;

        public void Init(List<MeshRenderer> renderers)
        {
            bars.AddRange(renderers);
            mpb = new MaterialPropertyBlock();
            Apply();
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        /// <summary>Волна эха: решётки в радиусе вспыхнут, когда до них дойдёт фронт.</summary>
        public static void Ping(Vector3 origin, float radius, Color color, float strength, float speed)
        {
            foreach (var f in all)
            {
                float d = Prims.Flat(f.transform.position - origin).magnitude;
                if (d > radius + 0.8f) continue;
                f.pendingTime = Time.time + d / Mathf.Max(0.01f, speed);
                f.pendingStrength = strength * Mathf.Lerp(1f, 0.5f, d / radius);
                f.pendingColor = color;
            }
        }

        void Update()
        {
            if (pendingTime >= 0f && Time.time >= pendingTime)
            {
                pendingTime = -1f;
                reveal = Mathf.Max(reveal, pendingStrength);
                color = pendingColor;
            }
            if (reveal <= 0f) return;
            reveal = Mathf.Max(0f, reveal - Time.deltaTime / GameManager.Cfg.revealFade);
            Apply();
        }

        void Apply()
        {
            // Еле заметно всегда (0.04) — иначе решётку, которую уже видел, не найти; ярко — после эха
            Color c = Color.Lerp(new Color(0.9f, 1f, 1.15f) * 0.04f, Color.Lerp(color, Color.white, 0.4f) * 1.4f, reveal * reveal);
            mpb.SetColor(Mats.BaseColor, c);
            foreach (var b in bars) b.SetPropertyBlock(mpb);
        }
    }
}
