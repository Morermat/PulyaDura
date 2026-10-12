using System.Collections.Generic;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Стена в темноте. Невидима, пока её не коснётся эхо-волна: тогда контур вспыхивает и гаснет.
    /// Стен в комнате сотни, поэтому стена «спит» (компонент выключен, Update не вызывается),
    /// пока ничего не меняется, и просыпается от эха, вспышки или нового постоянного свечения.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class WallBlock : MonoBehaviour
    {
        public static readonly List<WallBlock> All = new();

        [Tooltip("Постоянное свечение (временные стены игрока)")] [SerializeField] float glow;

        /// <summary>Постоянное свечение (временные стены игрока, бочки). Изменение будит стену.</summary>
        public float baseGlow
        {
            get => glow;
            set
            {
                if (Mathf.Approximately(glow, value)) return;
                glow = value;
                Wake();
            }
        }
        public Color baseColor = Color.black;
        [Tooltip("Цвет вспышки от эха вместо цвета волны (зелёный ящик с бонусом)")] public Color revealTint = Color.clear;

        public float Reveal { get; private set; }
        /// <summary>Стену уже видели: она оставляет еле заметный контур («память эха»).</summary>
        public bool Seen { get; private set; }
        Color color;
        float flash;
        MeshRenderer rend;
        MaterialPropertyBlock mpb;
        bool dirty = true;
        bool registered;

        void Awake()
        {
            rend = GetComponent<MeshRenderer>();
            mpb = new MaterialPropertyBlock();
        }

        // В списке All стена остаётся и во сне: уходит из него, только когда выключен сам объект или он уничтожен
        void OnEnable()
        {
            if (registered) return;
            All.Add(this);
            registered = true;
        }

        void OnDisable()
        {
            if (!gameObject.activeInHierarchy) Unregister();
        }

        void OnDestroy() => Unregister();

        void Unregister()
        {
            if (!registered) return;
            All.Remove(this);
            registered = false;
        }

        void Wake()
        {
            dirty = true;
            if (!enabled) enabled = true;
        }

        /// <param name="remember">false для общей вспышки в начале комнаты: она не должна рисовать карту за игрока</param>
        public void Show(float amount, Color c, bool remember = true)
        {
            if (remember && amount >= 0.25f && !Seen)
            {
                Seen = true;
                Wake();
            }
            if (amount >= Reveal || amount > 0.6f)
            {
                Reveal = Mathf.Clamp01(Mathf.Max(amount, Reveal));
                color = revealTint.maxColorComponent > 0f ? revealTint : c;
                Wake();
            }
        }

        /// <summary>Белая вспышка при попадании.</summary>
        public void Flash()
        {
            flash = 1f;
            Wake();
        }

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
            if (!dirty)
            {
                // Нечему меняться — засыпаем до следующего эха или вспышки
                enabled = false;
                return;
            }
            dirty = false;

            float k = Reveal * Reveal * 2.2f;
            Color memory = Seen ? (revealTint.maxColorComponent > 0f ? revealTint * 0.12f : GameManager.Cfg.echoColor * GameManager.Cfg.memoryGlow) : Color.black;
            Color e = color * k + memory + baseColor * baseGlow + Color.white * flash * 1.5f;
            mpb.SetColor(Mats.EmissionColor, e);
            rend.SetPropertyBlock(mpb);
        }
    }
}
