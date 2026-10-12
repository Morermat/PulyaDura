using System;
using UnityEngine;
using UnityEngine.UI;

namespace PulyaDura
{
    public enum Weight { Regular, Medium, Bold, Black, Mono }

    /// <summary>
    /// Мини-конструктор uGUI: интерфейс собирается кодом, без префабов.
    /// Стиль: тёмные панели со срезанными углами, тонкая рамка, моноширинные цифры, иконки вместо слов.
    /// Правила, общие для всего интерфейса:
    /// - весь текст ЗАГЛАВНЫМИ (CapsText);
    /// - три начертания: Black — заголовки, Bold — текст и кнопки, Mono — цифры, клавиши и подписи;
    /// - кегли из одной шкалы (TypeScale);
    /// - два цвета: синий — игрок и интерфейс, красный — враги и опасность, остальное белое и серое.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color PanelFill = new(0.025f, 0.05f, 0.08f, 0.9f);
        public static readonly Color Line = new(0.36f, 0.82f, 1f, 0.38f);
        /// <summary>Заливка выбранной кнопки и главной кнопки действия.</summary>
        public static readonly Color SelectedFill = new(0.08f, 0.42f, 0.56f, 1f);
        public static readonly Color IdleFill = new(0.05f, 0.1f, 0.15f, 0.95f);
        public static readonly Color Accent = new(0.36f, 0.88f, 1f);
        public static readonly Color AccentDim = new(0.36f, 0.88f, 1f, 0.18f);
        public static readonly Color Danger = new(1f, 0.36f, 0.32f);
        public static readonly Color Enemy = new(1f, 0.32f, 0.28f);
        public static readonly Color TextColor = new(0.92f, 0.95f, 0.98f);
        public static readonly Color Muted = new(0.55f, 0.62f, 0.7f);
        public static readonly Color Faint = new(1f, 1f, 1f, 0.1f);

        static readonly Font[] fonts = new Font[5];

        /// <summary>Шкала кеглей: любой размер притягивается к ближайшему шагу (крупные заголовки — как есть).</summary>
        static readonly int[] TypeScale = { 14, 16, 18, 20, 24, 28, 34, 40, 48, 56, 72, 96, 112 };

        public static int Snap(int size)
        {
            if (size > 112) return size;
            int best = TypeScale[0];
            foreach (int s in TypeScale) if (Mathf.Abs(s - size) < Mathf.Abs(best - size)) best = s;
            return best;
        }

        public static Font FontOf(Weight w)
        {
            int i = (int)w;
            if (fonts[i] != null) return fonts[i];
            string name = w switch
            {
                // Три начертания на весь интерфейс: Regular/Medium/Bold сведены к одному Bold
                Weight.Black => "Roboto-Black", Weight.Mono => "RobotoMono-Bold", _ => "Roboto-Bold"
            };
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

        /// <summary>Панель со срезанными углами и тонкой рамкой. Рамка — дочерний объект Frame.</summary>
        public static Image Panel(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color? fill = null, Color? frame = null)
        {
            var img = Box(name, parent, anchor, pivot, pos, size, fill ?? PanelFill);
            img.sprite = Icons.Panel;
            img.type = Image.Type.Sliced;
            var f = Fill(Stretch("Frame", img.transform), frame ?? Line);
            f.sprite = Icons.Frame;
            f.type = Image.Type.Sliced;
            return img;
        }

        public static Image FrameOf(Component panel) => panel.transform.Find("Frame").GetComponent<Image>();

        /// <summary>Мягкое свечение вокруг панели (дочерний объект Glow, рисуется под содержимым).</summary>
        public static Image Glow(Component panel, Color color, float alpha = 0.55f)
        {
            var existing = panel.transform.Find("Glow");
            if (existing != null) return existing.GetComponent<Image>();
            var rt = Stretch("Glow", panel.transform);
            rt.offsetMin = new Vector2(-Icons.GlowMargin, -Icons.GlowMargin);
            rt.offsetMax = new Vector2(Icons.GlowMargin, Icons.GlowMargin);
            rt.SetAsFirstSibling();
            var img = Fill(rt, new Color(color.r, color.g, color.b, alpha));
            img.sprite = Icons.Glow;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Состояние кнопки: выбрана — яркая заливка, рамка цвета акцента и свечение.</summary>
        public static void SetSelected(Component button, bool on)
        {
            button.GetComponent<Image>().color = on ? SelectedFill : IdleFill;
            FrameOf(button).color = on ? Accent : Line;
            Glow(button, Accent, 0.6f).gameObject.SetActive(on);
            var icon = button.transform.Find("Icon");
            if (icon != null) icon.GetComponent<Image>().color = on ? Color.white : Muted;
        }

        /// <summary>Строка меню: иконка слева, подпись по центру (пауза, итоги).</summary>
        public static Button MenuButton(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size,
            string text, IconId icon, Action onClick, int fontSize = 28)
        {
            var btn = Button(name, parent, anchor, pivot, pos, size, text, fontSize, onClick, out var label);
            var ic = Icon("Icon", btn.transform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(size.y * 0.62f, 0f), size.y * 0.5f, icon, TextColor);
            ic.raycastTarget = false;
            SetSelected(btn, false);
            return btn;
        }

        public static Image Icon(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, float size, IconId id, Color color)
        {
            var img = Box(name, parent, anchor, pivot, pos, new Vector2(size, size), color);
            img.sprite = Icons.Get(id);
            img.preserveAspect = true;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align, Weight weight = Weight.Regular)
        {
            var t = parent.gameObject.AddComponent<CapsText>();
            t.font = FontOf(weight);
            t.text = text;
            t.fontSize = Snap(size);
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

        /// <summary>Кнопка-панель. Если задана иконка, она стоит слева от подписи.</summary>
        public static Button Button(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size,
            string text, int fontSize, Action onClick, out Text label, Weight weight = Weight.Bold, IconId? icon = null)
        {
            var img = Panel(name, parent, anchor, pivot, pos, size);
            img.raycastTarget = true;
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f);
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
            lt.offsetMin = new Vector2(10f, 2f);
            lt.offsetMax = new Vector2(-10f, -2f);
            if (icon.HasValue)
            {
                // Иконка в верхней части, подпись в нижней; между ними и от рамки — воздух
                float s = Mathf.Min(size.y * 0.34f, 30f);
                Icon("Icon", img.transform, new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.5f), Vector2.zero, s, icon.Value, TextColor);
                lt.anchorMax = new Vector2(1f, 0.46f);
                lt.offsetMin = new Vector2(6f, 10f);
                lt.offsetMax = new Vector2(-6f, 0f);
            }
            label = Label(lt, text, fontSize, TextColor, TextAnchor.MiddleCenter, weight);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            return btn;
        }

        /// <summary>Тонкая полоска значения 0..1.</summary>
        public static Image Bar(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color fill)
        {
            var bg = Box(name, parent, anchor, pivot, pos, size, Faint);
            var img = Fill(Stretch("Fill", bg.transform), fill);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.sprite = WhiteSprite;
            img.fillAmount = 1f;
            return img;
        }

        /// <summary>Строка подсказок управления: клавиша в рамке и короткая подпись.</summary>
        public static RectTransform KeyHints(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, (string key, string label)[] items, int size)
        {
            var row = Rect(name, parent, anchor, pivot, pos, new Vector2(10f, size + 18f));
            float x = 0f;
            float h = size + 18f;
            foreach (var (keys, label) in items)
            {
                // Несколько клавиш через пробел («W A S D») — каждая в своей рамке
                foreach (var key in keys.Split(' '))
                {
                    var kt = Text("Key", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(400f, h), key, size - 2, TextColor, TextAnchor.MiddleCenter, Weight.Mono);
                    float kw = Mathf.Max(kt.preferredWidth + 22f, h);
                    var box = Panel("KeyBox", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(kw, h), new Color(0.36f, 0.82f, 1f, 0.08f), new Color(0.36f, 0.82f, 1f, 0.5f));
                    kt.rectTransform.SetParent(box.transform, false);
                    kt.rectTransform.anchorMin = Vector2.zero;
                    kt.rectTransform.anchorMax = Vector2.one;
                    kt.rectTransform.offsetMin = kt.rectTransform.offsetMax = Vector2.zero;
                    x += kw + 6f;
                }
                x += 6f;
                if (!string.IsNullOrEmpty(label))
                {
                    var lt = Text("Label", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(400f, h), label, size, Muted, TextAnchor.MiddleLeft, Weight.Medium);
                    float lw = lt.preferredWidth;
                    lt.rectTransform.sizeDelta = new Vector2(lw + 4f, h);
                    x += lw;
                }
                x += 30f;
            }
            row.sizeDelta = new Vector2(x - 30f, h);
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
