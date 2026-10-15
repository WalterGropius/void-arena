using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HackAndSlash
{
    /// <summary>Tiny helpers for building uGUI/TMP interfaces from code (HUD, menus, subtitles).</summary>
    public static class UIKit
    {
        public static readonly Color Panel = new Color(0.03f, 0.02f, 0.06f, 0.72f);

        public static Canvas Canvas(string name, Transform parent, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(string name, Transform parent)
        {
            var rt = Rect(name, parent);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Img(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color c)
        {
            var rt = Rect(name, parent);
            Anchor(rt, anchor, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Stretched variant (anchorMin/anchorMax).</summary>
        public static Image Img(string name, Transform parent, Vector2 min, Vector2 max, Color c)
        {
            var rt = Rect(name, parent);
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        public static RectTransform Fill(Transform parent, Color c)
        {
            var img = Img("Fill", parent, Vector2.zero, Vector2.one, c);
            var rt = img.rectTransform;
            rt.offsetMin = new Vector2(3f, 3f);
            rt.offsetMax = new Vector2(-3f, -3f);
            return rt;
        }

        public static void SetBar(RectTransform fill, float v) => fill.anchorMax = new Vector2(Mathf.Clamp01(v), 1f);

        public static TextMeshProUGUI Label(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, float fontSize, Color c, TextAlignmentOptions align)
        {
            var rt = Rect(name, parent);
            Anchor(rt, anchor, pivot, pos, size);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.fontSize = fontSize;
            t.color = c;
            t.alignment = align;
            t.fontStyle = FontStyles.Bold;
            t.raycastTarget = false;
            t.richText = true;
            t.text = "";
            return t;
        }

        public static CanvasGroup Group(RectTransform rt, float alpha = 1f)
        {
            var g = rt.gameObject.AddComponent<CanvasGroup>();
            g.alpha = alpha;
            g.blocksRaycasts = false;
            g.interactable = false;
            return g;
        }

        static Sprite radial, circle, ring;

        /// <summary>Soft radial falloff (vignette).</summary>
        public static Sprite RadialSprite()
        {
            if (radial) return radial;
            radial = MakeSprite(256, (dx, dy) =>
            {
                float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 1f, r));
            });
            return radial;
        }

        /// <summary>Anti-aliased disc (HALCYON's eye, pips, cursors).</summary>
        public static Sprite CircleSprite()
        {
            if (circle) return circle;
            circle = MakeSprite(128, (dx, dy) => 1f - Mathf.SmoothStep(0.94f, 1f, Mathf.Sqrt(dx * dx + dy * dy)));
            return circle;
        }

        /// <summary>Thin ring.</summary>
        public static Sprite RingSprite()
        {
            if (ring) return ring;
            ring = MakeSprite(128, (dx, dy) =>
            {
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                return Mathf.Clamp01(1f - Mathf.Abs(r - 0.9f) / 0.08f);
            });
            return ring;
        }

        static Sprite MakeSprite(int n, System.Func<float, float, float> alpha)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "UI_Gen" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(dx, dy)) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }
    }
}
