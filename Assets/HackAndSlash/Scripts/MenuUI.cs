using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HackAndSlash
{
    /// <summary>
    /// A small code-built menu system: stacked pages of items navigated with keyboard, mouse or gamepad on unscaled
    /// time (so it works while the game is paused). Pages are defined by <see cref="GameFlow"/>.
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        public sealed class Item
        {
            public Func<string> label;
            public Action submit;
            public Action<int> adjust;
            public Func<string> desc;
            public Func<bool> enabled;
            public Item(Func<string> label, Action submit, Func<string> desc = null) { this.label = label; this.submit = submit; this.desc = desc; }
            public Item(string label, Action submit, string desc = null) { this.label = () => label; this.submit = submit; if (desc != null) this.desc = () => desc; }
        }

        public sealed class Page
        {
            public string title;
            public readonly List<Item> items = new List<Item>();
            public Action back;
            public Func<string> header;
            public bool showLogo;
            public int selected;
            public Page Add(Item i) { items.Add(i); return this; }
        }

        public bool Open => stack.Count > 0;
        public Page Current => stack.Count > 0 ? stack[stack.Count - 1] : null;

        readonly List<Page> stack = new List<Page>();
        readonly List<TextMeshProUGUI> rows = new List<TextMeshProUGUI>();
        readonly List<Image> rowBars = new List<Image>();
        readonly List<Image> strips = new List<Image>();
        RectTransform list, root;
        CanvasGroup group, logoGroup;
        TextMeshProUGUI pageTitle, description, header, logo, logoSub, tagline, footer;
        Image dim;
        Canvas canvas;
        float repeatT, show, taglineT;
        int taglineIdx;
        Vector2 lastMouse;
        bool mouseActive;

        void Awake() => Build();

        public void Push(Page p)
        {
            stack.Add(p);
            p.selected = FirstEnabled(p, Mathf.Clamp(p.selected, 0, Mathf.Max(0, p.items.Count - 1)), 1);
            Sfx.Play(Sfx.I ? Sfx.I.uiMove : null, 0.25f, 1f, 0f);
        }

        public void Replace(Page p)
        {
            if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            Push(p);
        }

        public void Pop()
        {
            if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
        }

        public void CloseAll() => stack.Clear();

        void Update()
        {
            float udt = Time.unscaledDeltaTime;
            show = Mathf.MoveTowards(show, Open ? 1f : 0f, udt * 5f);
            group.alpha = show;
            group.blocksRaycasts = false;
            var page = Current;
            logoGroup.alpha = Mathf.MoveTowards(logoGroup.alpha, page != null && page.showLogo ? 1f : 0f, udt * 3f);
            dim.color = new Color(0f, 0f, 0f, page != null && page.showLogo ? 0.15f : 0.62f);
            taglineT += udt;
            if (taglineT > 4.5f) { taglineT = 0f; taglineIdx = (taglineIdx + 1) % Lines.TitleTaglines.Length; }
            tagline.text = Lines.TitleTaglines[taglineIdx];
            tagline.alpha = Mathf.Clamp01(taglineT / 0.6f) * Mathf.Clamp01((4.5f - taglineT) / 0.6f) * 0.8f;
            if (page == null) return;
            Render(page);
            HandleInput(page, udt);
        }

        void Render(Page page)
        {
            pageTitle.text = page.title ?? "";
            header.text = page.header != null ? page.header() : "";
            while (rows.Count < page.items.Count) AddRow();
            for (int i = 0; i < rows.Count; i++)
            {
                bool used = i < page.items.Count;
                rows[i].gameObject.SetActive(used);
                rowBars[i].gameObject.SetActive(used);
                if (!used) continue;
                var it = page.items[i];
                bool en = it.enabled == null || it.enabled();
                bool sel = i == page.selected;
                string label = it.label();
                if (it.adjust != null) label = (sel ? "<  " : "   ") + label + (sel ? "  >" : "");
                rows[i].text = label;
                rows[i].color = !en ? new Color(1f, 1f, 1f, 0.25f) : sel ? new Color(1f, 0.95f, 0.9f) : new Color(0.8f, 0.85f, 0.95f, 0.8f);
                rows[i].characterSpacing = sel ? 12f : 8f;
                rowBars[i].color = sel ? new Color(1f, 0.12f, 0.08f, 0.2f) : new Color(1f, 1f, 1f, 0f);
                strips[i].color = sel ? new Color(1f, 0.2f, 0.15f, 1f) : new Color(1f, 1f, 1f, 0f);
            }
            var cur = page.items.Count > 0 ? page.items[page.selected] : null;
            description.text = cur != null && cur.desc != null ? cur.desc() : "";
        }

        void HandleInput(Page page, float udt)
        {
            var kb = Keyboard.current; var gp = Gamepad.current; var ms = Mouse.current;
            int move = 0, adjust = 0;
            bool submit = false, back = false;
            if (kb != null)
            {
                if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) move = -1;
                if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) move = 1;
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) adjust = -1;
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) adjust = 1;
                if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) submit = true;
                if (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) back = true;
            }
            if (gp != null)
            {
                Vector2 st = gp.leftStick.ReadValue() + gp.dpad.ReadValue();
                repeatT -= udt;
                if (Mathf.Abs(st.y) > 0.6f && repeatT <= 0f) { move = st.y > 0f ? -1 : 1; repeatT = 0.2f; }
                else if (Mathf.Abs(st.x) > 0.6f && repeatT <= 0f) { adjust = st.x > 0f ? 1 : -1; repeatT = 0.2f; }
                if (Mathf.Abs(st.x) < 0.3f && Mathf.Abs(st.y) < 0.3f) repeatT = 0f;
                if (gp.buttonSouth.wasPressedThisFrame) submit = true;
                if (gp.buttonEast.wasPressedThisFrame || gp.startButton.wasPressedThisFrame && page.back != null) back = true;
            }
            if (ms != null)
            {
                Vector2 mp = ms.position.ReadValue();
                if ((mp - lastMouse).sqrMagnitude > 4f) mouseActive = true;
                lastMouse = mp;
                if (mouseActive && Cursor.lockState != CursorLockMode.Locked)
                {
                    for (int i = 0; i < page.items.Count; i++)
                    {
                        if (!RectTransformUtility.RectangleContainsScreenPoint(rowBars[i].rectTransform, mp, null)) continue;
                        if (page.selected != i && Enabled(page, i)) { page.selected = i; Sfx.Play(Sfx.I ? Sfx.I.uiMove : null, 0.18f, 1.1f, 0f); }
                        if (ms.leftButton.wasPressedThisFrame) submit = true;
                        break;
                    }
                    if (ms.rightButton.wasPressedThisFrame) back = true;
                }
            }
            if (move != 0 && page.items.Count > 0)
            {
                mouseActive = false;
                page.selected = FirstEnabled(page, (page.selected + move + page.items.Count) % page.items.Count, move);
                Sfx.Play(Sfx.I ? Sfx.I.uiMove : null, 0.2f, 1f, 0f);
            }
            var cur = page.items.Count > 0 ? page.items[page.selected] : null;
            if (adjust != 0 && cur != null && cur.adjust != null)
            {
                cur.adjust(adjust);
                Sfx.Play(Sfx.I ? Sfx.I.uiMove : null, 0.2f, adjust > 0 ? 1.15f : 0.9f, 0f);
            }
            if (submit && cur != null && Enabled(page, page.selected))
            {
                Sfx.Play(Sfx.I ? Sfx.I.uiSelect : null, 0.4f, 1f, 0f);
                if (cur.submit != null) cur.submit();
                else if (cur.adjust != null) cur.adjust(1);
            }
            else if (back && page.back != null)
            {
                Sfx.Play(Sfx.I ? Sfx.I.uiMove : null, 0.25f, 0.8f, 0f);
                page.back();
            }
        }

        static bool Enabled(Page p, int i) => i >= 0 && i < p.items.Count && (p.items[i].enabled == null || p.items[i].enabled());

        static int FirstEnabled(Page p, int start, int dir)
        {
            if (p.items.Count == 0) return 0;
            dir = dir >= 0 ? 1 : -1;
            for (int k = 0; k < p.items.Count; k++)
            {
                int i = ((start + k * dir) % p.items.Count + p.items.Count) % p.items.Count;
                if (Enabled(p, i)) return i;
            }
            return 0;
        }

        // ---------------------------------------------------------------- construction

        void Build()
        {
            canvas = UIKit.Canvas("Menu_Canvas", transform, 30);
            root = UIKit.Stretch("MenuRoot", canvas.transform);
            group = UIKit.Group(root, 0f);
            dim = UIKit.Img("Dim", root, Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.6f));

            var logoRoot = UIKit.Stretch("Logo", root);
            logoGroup = UIKit.Group(logoRoot, 0f);
            logo = UIKit.Label("Logo", logoRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -150f), new Vector2(1400f, 180f), 150f, Color.white, TextAlignmentOptions.Left);
            logo.text = "VOID KNIGHT";
            logo.characterSpacing = 22f;
            logoSub = UIKit.Label("LogoSub", logoRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(160f, -320f), new Vector2(1400f, 60f), 44f, new Color(1f, 0.25f, 0.2f), TextAlignmentOptions.Left);
            logoSub.text = "ELEVEN  FIFTY-NINE";
            logoSub.characterSpacing = 30f;
            tagline = UIKit.Label("Tagline", logoRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(162f, -380f), new Vector2(1400f, 40f), 24f, new Color(0.8f, 0.86f, 1f), TextAlignmentOptions.Left);
            tagline.characterSpacing = 12f;
            footer = UIKit.Label("Footer", logoRoot, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(160f, 40f), new Vector2(1600f, 30f), 18f, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.Left);
            footer.text = "HALCYON MUNICIPAL INTELLIGENCE v11.59   ·   MERIDIAN CIVIC RESTORATION PROGRAM   ·   ALL SYSTEMS NOMINAL";
            footer.characterSpacing = 6f;

            pageTitle = UIKit.Label("PageTitle", root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(150f, 240f), new Vector2(1200f, 70f), 52f, new Color(1f, 1f, 1f, 0.95f), TextAlignmentOptions.Left);
            pageTitle.characterSpacing = 18f;
            header = UIKit.Label("Header", root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-150f, 60f), new Vector2(900f, 600f), 30f, new Color(0.9f, 0.92f, 1f), TextAlignmentOptions.TopRight);
            header.fontStyle = FontStyles.Normal;
            header.lineSpacing = 12f;
            list = UIKit.Rect("List", root);
            UIKit.Anchor(list, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(150f, 160f), new Vector2(900f, 700f));
            description = UIKit.Label("Description", root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(150f, 100f), new Vector2(1500f, 60f), 24f, new Color(0.75f, 0.82f, 0.95f, 0.85f), TextAlignmentOptions.Left);
            description.fontStyle = FontStyles.Italic;
        }

        void AddRow()
        {
            int i = rows.Count;
            var bar = UIKit.Img("Row" + i, list, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(-24f, -i * 58f), new Vector2(900f, 52f), new Color(1f, 1f, 1f, 0f));
            var strip = UIKit.Img("Strip", bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(6f, 40f), new Color(1f, 1f, 1f, 0f));
            var t = UIKit.Label("Label" + i, list, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -i * 58f), new Vector2(860f, 52f), 34f, Color.white, TextAlignmentOptions.Left);
            t.characterSpacing = 8f;
            rows.Add(t);
            rowBars.Add(bar);
            strips.Add(strip);
        }
    }
}
