using System;
using UnityEngine;
using UnityEngine.UI;

namespace PulyaDura
{
    /// <summary>Мини-конструктор uGUI: интерфейс собирается кодом, без префабов.</summary>
    public static class UiKit
    {
        public static readonly Color Bg = new(0.03f, 0.04f, 0.06f, 0.88f);
        public static readonly Color Panel = new(0.07f, 0.09f, 0.12f, 0.92f);
        public static readonly Color Accent = new(0.35f, 0.9f, 1f);
        public static readonly Color Danger = new(1f, 0.55f, 0.2f);
        public static readonly Color Enemy = new(1f, 0.3f, 0.25f);
        public static readonly Color TextColor = new(0.9f, 0.93f, 0.97f);
        public static readonly Color Muted = new(0.55f, 0.6f, 0.68f);

        static Font font;
        public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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

        public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(string name, Transform parent)
        {
            var rt = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return rt;
        }

        public static Image Image(Transform parent, Color color, string name = "Image")
        {
            var img = parent.gameObject.GetComponent<Image>();
            if (img == null) img = parent.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Image Box(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
        {
            var rt = Rect(name, parent, anchor, anchor, pivot, pos, size);
            return Image(rt, color);
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align, FontStyle style = FontStyle.Normal)
        {
            var t = parent.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        public static Text Text(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, string text, int fontSize, Color color, TextAnchor align, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(name, parent, anchor, anchor, pivot, pos, size);
            return Label(rt, text, fontSize, color, align, style);
        }

        public static void Shadow(Graphic g, float dist = 2f)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.8f);
            s.effectDistance = new Vector2(dist, -dist);
        }

        public static void Border(Graphic g, Color color, float width = 2f)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = color;
            o.effectDistance = new Vector2(width, -width);
        }

        public static Button Button(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, string text, int fontSize, Action onClick, out Text label)
        {
            var img = Box(name, parent, anchor, pivot, pos, size, Panel);
            img.raycastTarget = true;
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f);
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
            lt.offsetMin = new Vector2(12f, 6f);
            lt.offsetMax = new Vector2(-12f, -6f);
            label = Label(lt, text, fontSize, TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            return btn;
        }

        /// <summary>Горизонтальная полоска значения 0..1.</summary>
        public static Image Bar(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color fill)
        {
            var bg = Box(name, parent, anchor, pivot, pos, size, new Color(1f, 1f, 1f, 0.08f));
            var f = Rect("Fill", bg.transform, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            f.anchorMax = new Vector2(1f, 1f);
            var img = Image(f, fill);
            img.type = UnityEngine.UI.Image.Type.Filled;
            img.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            img.sprite = WhiteSprite;
            img.fillAmount = 1f;
            return img;
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
