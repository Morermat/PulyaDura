using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace PulyaDura
{
    /// <summary>
    /// Текст интерфейса всегда ЗАГЛАВНЫМИ: один регистр во всех меню, что бы ни передал код.
    /// Тэги разметки (&lt;color=…&gt;, &lt;size=…&gt;) не трогаем.
    /// Ещё одна правка: uGUI центрирует строку с учётом «хвостов» строчных букв (у, д, р),
    /// которых у заглавных нет, поэтому заглавные визуально проседают вниз. Поднимаем их на долю кегля.
    /// </summary>
    public class CapsText : Text
    {
        string raw;

        public override string text
        {
            get => base.text;
            set
            {
                // Тот же текст, что в прошлый раз, — ничего не делаем (без новой строки и пересборки меша)
                if (ReferenceEquals(value, raw) || value == raw) return;
                raw = value;
                base.text = Upper(value);
            }
        }

        static readonly StringBuilder sb = new();

        public static string Upper(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.IndexOf('<') < 0) return value.ToUpperInvariant();
            sb.Clear();
            bool tag = false;
            foreach (char c in value)
            {
                if (c == '<') tag = true;
                sb.Append(tag ? c : char.ToUpperInvariant(c));
                if (c == '>') tag = false;
            }
            return sb.ToString();
        }

        protected override void OnPopulateMesh(VertexHelper toFill)
        {
            base.OnPopulateMesh(toFill);
            float k = alignment switch
            {
                TextAnchor.MiddleLeft or TextAnchor.MiddleCenter or TextAnchor.MiddleRight => 0.09f,
                TextAnchor.LowerLeft or TextAnchor.LowerCenter or TextAnchor.LowerRight => 0.16f,
                _ => 0f,
            };
            if (k <= 0f) return;
            float shift = fontSize * k;
            var v = new UIVertex();
            for (int i = 0; i < toFill.currentVertCount; i++)
            {
                toFill.PopulateUIVertex(ref v, i);
                v.position.y += shift;
                toFill.SetUIVertex(v, i);
            }
        }
    }
}
