using System.Text;
using UnityEngine.UI;

namespace PulyaDura
{
    /// <summary>
    /// Текст интерфейса всегда ЗАГЛАВНЫМИ: один регистр во всех меню, что бы ни передал код.
    /// Тэги разметки (&lt;color=…&gt;, &lt;size=…&gt;) не трогаем.
    /// </summary>
    public class CapsText : Text
    {
        public override string text
        {
            get => base.text;
            set => base.text = Upper(value);
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
    }
}
