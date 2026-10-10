using System;
using UnityEngine;
using UnityEngine.UI;

namespace PulyaDura
{
    public enum Weight { Regular, Medium, Bold, Black }

    /// <summary>Мини-конструктор uGUI: интерфейс собирается кодом, без префабов. Шрифт — Roboto.</summary>
    public static class UiKit
    {
        public static readonly Color Panel = new(0.05f, 0.07f, 0.1f, 0.78f);
        public static readonly Color Line = new(1f, 1f, 1f, 0.08f);
        public static readonly Color Accent = new(0.36f, 0.88f, 1f);
        public static readonly Color AccentDim = new(0.36f, 0.88f, 1f, 0.16f);
        public static readonly Color Danger = new(1f, 0.56f, 0.22f);
        public static readonly Color Enemy = new(1f, 0.32f, 0.28f);
        public static readonly Color TextColor = new(0.92f, 0.95f, 0.98f);
        public static readonly Color Muted = new(0.55f, 0.62f, 0.7f);
        public static readonly Color Faint = new(1f, 1f, 1f, 0.1f);

        static readonly Font[] fonts = new Font[4];

        public static Font FontOf(Weight w)
        {
            int i = (int)w;
            if (fonts[i] != null) return fonts[i];
            string name = w switch { Weight.Black => "Roboto-Black", Weight.Bold => "Roboto-Bold", Weight.Medium => "Roboto-Medium", _ => "Roboto-Regular" };
            fonts[i] = Resources.Load<Font>("Fonts/" + name);
            if (fonts[i] == null) fonts[i] = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return fonts[i];
        }

        public static Canvas Canvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(string name, Transform parent)
        {
            var rt = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Fill(Transform target, Color color)
        {
            // Не через ??: в редакторе GetComponent возвращает «фальшивый null» Unity
            var img = target.gameObject.GetComponent<Image>();
            if (img == null) img = target.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Image Box(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
            => Fill(Rect(name, parent, anchor, pivot, pos, size), color);

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align, Weight weight = Weight.Regular)
        {
            var t = parent.gameObject.AddComponent<Text>();
            t.font = FontOf(weight);
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.lineSpacing = 1.05f;
            return t;
        }

        public static Text Text(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size,
            string text, int fontSize, Color color, TextAnchor align, Weight weight = Weight.Regular)
            => Label(Rect(name, parent, anchor, pivot, pos, size), text, fontSize, color, align, weight);

        public static void Shadow(Graphic g, float dist = 2f, float alpha = 0.8f)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, alpha);
            s.effectDistance = new Vector2(dist, -dist);
        }

        public static void Border(Graphic g, Color color, float width = 1.5f)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = color;
            o.effectDistance = new Vector2(width, -width);
        }

        public static Button Button(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size,
            string text, int fontSize, Action onClick, out Text label, Weight weight = Weight.Bold)
        {
            var img = Box(name, parent, anchor, pivot, pos, size, Panel);
            img.raycastTarget = true;
            Border(img, Line);
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.06f;
            btn.colors = colors;
            btn.onClick.AddListener(() =>
            {
                Sfx.I.Play(SfxId.Click);
                onClick();
            });
            var lt = Stretch("Label", img.transform);
            lt.offsetMin = new Vector2(10f, 4f);
            lt.offsetMax = new Vector2(-10f, -4f);
            label = Label(lt, text, fontSize, TextColor, TextAnchor.MiddleCenter, weight);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            return btn;
        }

        /// <summary>Тонкая полоска значения 0..1.</summary>
        public static Image Bar(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color fill)
        {
            var bg = Box(name, parent, anchor, pivot, pos, size, Faint);
            var f = Stretch("Fill", bg.transform);
            var img = Fill(f, fill);
            img.type = UnityEngine.UI.Image.Type.Filled;
            img.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            img.sprite = WhiteSprite;
            img.fillAmount = 1f;
            return img;
        }

        /// <summary>
        /// Строка подсказок управления: клавиша в рамке и короткая подпись.
        /// Возвращает общую ширину, чтобы строку можно было центрировать.
        /// </summary>
        public static RectTransform KeyHints(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, (string key, string label)[] items, int size)
        {
            var row = Rect(name, parent, anchor, pivot, pos, new Vector2(10f, size + 18f));
            float x = 0f;
            float h = size + 18f;
            foreach (var (key, label) in items)
            {
                var kt = Text("Key", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(400f, h), key, size, TextColor, TextAnchor.MiddleCenter, Weight.Bold);
                float kw = kt.preferredWidth + 22f;
                var box = Box("KeyBox", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(kw, h), Faint);
                Border(box, new Color(1f, 1f, 1f, 0.18f), 1f);
                kt.rectTransform.SetParent(box.transform, false);
                kt.rectTransform.anchorMin = Vector2.zero;
                kt.rectTransform.anchorMax = Vector2.one;
                kt.rectTransform.offsetMin = kt.rectTransform.offsetMax = Vector2.zero;
                x += kw + 10f;
                var lt = Text("Label", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(400f, h), label, size, Muted, TextAnchor.MiddleLeft, Weight.Medium);
                float lw = lt.preferredWidth;
                lt.rectTransform.sizeDelta = new Vector2(lw + 4f, h);
                x += lw + 34f;
            }
            row.sizeDelta = new Vector2(x - 34f, h);
            return row;
        }

        static Sprite white;
        /// <summary>Filled-картинке нужен спрайт, иначе fillAmount не работает.</summary>
        public static Sprite WhiteSprite
        {
            get
            {
                if (white != null) return white;
                var tex = Texture2D.whiteTexture;
                white = Sprite.Create(tex, new UnityEngine.Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                return white;
            }
        }
    }
}
