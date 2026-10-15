using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HackAndSlash
{
    /// <summary>
    /// Subtitles with a voice: HALCYON speaks in soft synthesised chimes behind a red eye (HAL 9000), Wren crackles
    /// in over pirate radio on 88.9. Story lines block the director; barks (HALCYON's running commentary on the fight)
    /// never do, and are dropped when something more important is being said.
    /// </summary>
    public class Dialogue : MonoBehaviour
    {
        public static Dialogue I { get; private set; }

        TextMeshProUGUI speaker, body, systemText;
        Image eye, eyeGlow, radioBg;
        TextMeshProUGUI radioText;
        CanvasGroup group, systemGroup;
        RectTransform panel;
        float eyeLevel, show, systemShow;
        bool storyLine;
        Coroutine barkRoutine;
        float nextBark;
        readonly Dictionary<string[], int> barkCursor = new Dictionary<string[], int>();
        GameManager gm;

        /// <summary>A story line is on screen (barks wait).</summary>
        public bool Busy => storyLine;
        /// <summary>Cutscenes let the player hurry lines along.</summary>
        public bool AllowSkip { get; set; }

        void Awake()
        {
            I = this;
            gm = GameManager.I;
            Build();
        }

        void Start()
        {
            gm = GameManager.I;
            if (gm == null) return;
            gm.OnKill += e => { if (Random.value < 0.22f) Bark(Lines.BarkKill); };
            gm.OnPlayerHurt += () => { if (Random.value < 0.3f) Bark(Lines.BarkHurt); };
            gm.OnPlayerDeath += () => Bark(Lines.BarkDeath, true);
            gm.OnWaveCleared += n => { if (Random.value < 0.55f) Bark(gm.Mode == GameMode.Arcade && Random.value < 0.4f ? Lines.BarkArcade : Lines.BarkWave); };
            gm.OnCombo += c => Bark(c >= 50 ? Lines.BarkCombo50 : c >= 30 ? Lines.BarkCombo30 : Lines.BarkCombo15);
            gm.OnMove += OnMove;
        }

        void OnDestroy() { if (I == this) I = null; }

        void OnMove(string m)
        {
            switch (m)
            {
                case "counter": if (Random.value < 0.12f) Bark(Lines.BarkCounter); break;
                case "multicounter": if (Random.value < 0.5f) Bark(Lines.BarkMultiCounter); break;
                case "takedown": if (Random.value < 0.35f) Bark(Lines.BarkTakedown); break;
                case "rip": if (Random.value < 0.4f) Bark(Lines.BarkRip); break;
                case "throw": if (Random.value < 0.45f) Bark(Lines.BarkThrow); break;
                case "deflect": if (Random.value < 0.4f) Bark(Lines.BarkDeflect); break;
                case "envtakedown": if (Random.value < 0.6f) Bark(Lines.BarkEnvironment); break;
                case "cascade": if (Random.value < 0.7f) Bark(Lines.BarkCascade); break;
                case "perfectdodge": if (Random.value < 0.25f) Bark(Lines.BarkDodge); break;
            }
        }

        // ---------------------------------------------------------------- public API

        public IEnumerator SayAll(Lines.Line[] lines)
        {
            foreach (var l in lines) yield return Say(l);
        }

        public IEnumerator Say(Lines.Line line)
        {
            if (barkRoutine != null) { StopCoroutine(barkRoutine); barkRoutine = null; }
            storyLine = true;
            yield return Speak(line.who, line.text, true);
            storyLine = false;
            nextBark = Time.unscaledTime + 4f;
        }

        /// <summary>HALCYON's commentary: rate-limited, never interrupts the story, never repeats back to back.</summary>
        public void Bark(string[] pool, bool force = false)
        {
            if (pool == null || pool.Length == 0 || gm == null) return;
            if (!Settings.commentary || gm.Mode == GameMode.Title || storyLine) return;
            if (!force && (barkRoutine != null || Time.unscaledTime < nextBark)) return;
            if (!barkCursor.TryGetValue(pool, out int c)) c = Random.Range(0, pool.Length);
            barkCursor[pool] = c + 1;
            if (barkRoutine != null) StopCoroutine(barkRoutine);
            barkRoutine = StartCoroutine(BarkRoutine(pool[c % pool.Length]));
            nextBark = Time.unscaledTime + Random.Range(9f, 15f);
        }

        IEnumerator BarkRoutine(string text)
        {
            yield return Speak(Lines.Who.Halcyon, text, false);
            barkRoutine = null;
        }

        public void ClearAll()
        {
            StopAllCoroutines();
            barkRoutine = null;
            storyLine = false;
            show = 0f;
            systemShow = 0f;
        }

        // ---------------------------------------------------------------- speaking

        IEnumerator Speak(Lines.Who who, string text, bool story)
        {
            if (who == Lines.Who.System)
            {
                systemText.text = text;
                systemText.maxVisibleCharacters = 0;
                systemShow = 1f;
                for (int i = 0; i <= text.Length; i++)
                {
                    systemText.maxVisibleCharacters = i;
                    if (i % 2 == 0 && Sfx.I) Sfx.Play(Sfx.I.typeKey, 0.12f, Random.Range(0.9f, 1.1f), 0f);
                    yield return WaitReal(0.03f);
                }
                yield return Hold(1.2f + text.Length * 0.03f);
                systemShow = 0f;
                yield return WaitReal(0.3f);
                yield break;
            }
            bool hal = who == Lines.Who.Halcyon;
            bool rec = who == Lines.Who.Recording;
            speaker.text = hal ? "HALCYON" : rec ? "<color=#ffb27a>RECORDING  ·  </color>W. AVERY" : "WREN  <size=70%><color=#ffcf6b>88.9 FM</color></size>";
            speaker.color = hal ? new Color(1f, 0.35f, 0.3f) : rec ? new Color(1f, 0.82f, 0.6f) : new Color(1f, 0.8f, 0.42f);
            body.text = text;
            body.color = rec ? new Color(1f, 0.92f, 0.8f) : Color.white;
            body.fontStyle = rec ? FontStyles.Italic : FontStyles.Normal;
            body.maxVisibleCharacters = 0;
            eye.enabled = eyeGlow.enabled = hal;
            radioBg.enabled = radioText.enabled = !hal;
            radioText.text = rec ? "REC" : "88.9";
            show = 1f;
            if (!hal && Sfx.I) Sfx.Play(Sfx.I.radioStatic, 0.22f, Random.Range(0.9f, 1.1f), 0f);
            float cps = hal ? 38f : 44f;
            float t = 0f;
            int shown = 0;
            while (shown < text.Length)
            {
                if (gm && gm.Paused) { yield return null; continue; }
                t += Time.unscaledDeltaTime * cps;
                int target = Mathf.Min(text.Length, Mathf.FloorToInt(t));
                while (shown < target)
                {
                    char ch = text[shown++];
                    if (shown % 3 == 0 && char.IsLetter(ch)) Blip(who, ch);
                    if (ch == '.' || ch == '?' || ch == '!') t -= cps * 0.18f; // a beat after each sentence
                    else if (ch == ',') t -= cps * 0.06f;
                }
                body.maxVisibleCharacters = shown;
                eyeLevel = 0.6f + 0.4f * Random.value;
                if (story && AllowSkip && SkipPressed()) { shown = text.Length; body.maxVisibleCharacters = shown; break; }
                yield return null;
            }
            eyeLevel = 0.3f;
            yield return Hold(story ? 0.9f + text.Length * 0.028f : 1.4f + text.Length * 0.02f, story);
            show = 0f;
            yield return WaitReal(0.25f);
        }

        void Blip(Lines.Who who, char ch)
        {
            if (Sfx.I == null) return;
            bool vowel = "AEIOUaeiou".IndexOf(ch) >= 0;
            var pool = who == Lines.Who.Halcyon ? Sfx.I.halBlips : Sfx.I.wrenBlips;
            if (pool == null || pool.Length == 0) return;
            var clip = pool[(ch * 7 + (vowel ? 3 : 0)) % pool.Length];
            Sfx.Play(clip, who == Lines.Who.Halcyon ? 0.16f : 0.13f, vowel ? 0.94f : 1.04f, 0.03f);
            if (who == Lines.Who.Halcyon && gm && gm.Districts && gm.Districts.Current != null && gm.Districts.Current.monolith)
                gm.Districts.Current.monolith.Pulse(0.6f);
        }

        IEnumerator Hold(float seconds, bool skippable = false)
        {
            float t = 0f;
            while (t < seconds)
            {
                if (!(gm && gm.Paused))
                {
                    t += Time.unscaledDeltaTime;
                    if (skippable && AllowSkip && SkipPressed()) break;
                }
                yield return null;
            }
        }

        static IEnumerator WaitReal(float s) => GameFlow.Wait(s);

        static bool SkipPressed()
        {
            var kb = Keyboard.current; var gp = Gamepad.current; var ms = Mouse.current;
            return (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame))
                || (gp != null && (gp.buttonSouth.wasPressedThisFrame || gp.selectButton.wasPressedThisFrame))
                || (ms != null && ms.leftButton.wasPressedThisFrame);
        }

        void Update()
        {
            float udt = Time.unscaledDeltaTime;
            group.alpha = Mathf.MoveTowards(group.alpha, show, udt * 5f);
            systemGroup.alpha = Mathf.MoveTowards(systemGroup.alpha, systemShow, udt * 4f);
            float e = eyeLevel * (0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 2f));
            eye.color = new Color(1f, 0.12f + 0.25f * e, 0.08f, 1f);
            eyeGlow.color = new Color(1f, 0.1f, 0.05f, 0.25f + 0.45f * e);
            eyeGlow.rectTransform.localScale = Vector3.one * (1f + 0.25f * e);
            eyeLevel = Mathf.MoveTowards(eyeLevel, 0.3f, udt * 2f);
            radioBg.color = new Color(1f, 0.8f, 0.4f, 0.12f + 0.1f * Random.value);
        }

        // ---------------------------------------------------------------- construction

        void Build()
        {
            var canvas = UIKit.Canvas("Dialogue_Canvas", transform, 20);
            var root = canvas.transform;
            panel = UIKit.Rect("Subtitles", root);
            UIKit.Anchor(panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(1300f, 150f));
            group = UIKit.Group(panel, 0f);
            var bg = UIKit.Img("Bg", panel, Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.55f));
            bg.rectTransform.offsetMin = new Vector2(-20f, -10f);
            bg.rectTransform.offsetMax = new Vector2(20f, 10f);
            eyeGlow = UIKit.Img("EyeGlow", panel, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(62f, 0f), new Vector2(120f, 120f), new Color(1f, 0.1f, 0.05f, 0.4f));
            eyeGlow.sprite = UIKit.RadialSprite();
            var bezel = UIKit.Img("Bezel", panel, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(62f, 0f), new Vector2(76f, 76f), new Color(0.75f, 0.77f, 0.82f, 1f));
            bezel.sprite = UIKit.CircleSprite();
            var black = UIKit.Img("Glass", panel, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(62f, 0f), new Vector2(66f, 66f), new Color(0.02f, 0.02f, 0.03f, 1f));
            black.sprite = UIKit.CircleSprite();
            eye = UIKit.Img("Eye", panel, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(62f, 0f), new Vector2(40f, 40f), Color.red);
            eye.sprite = UIKit.CircleSprite();
            var core = UIKit.Img("Core", eye.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f), new Color(1f, 0.85f, 0.5f));
            core.sprite = UIKit.CircleSprite();
            radioBg = UIKit.Img("Radio", panel, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(62f, 0f), new Vector2(84f, 44f), new Color(1f, 0.8f, 0.4f, 0.15f));
            radioText = UIKit.Label("RadioText", radioBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(84f, 44f), 26f, new Color(1f, 0.82f, 0.45f), TextAlignmentOptions.Center);
            speaker = UIKit.Label("Speaker", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(130f, -6f), new Vector2(1100f, 34f), 24f, Color.red, TextAlignmentOptions.Left);
            speaker.characterSpacing = 10f;
            body = UIKit.Label("Body", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(130f, -40f), new Vector2(1150f, 110f), 30f, Color.white, TextAlignmentOptions.TopLeft);
            body.fontStyle = FontStyles.Normal;
            body.textWrappingMode = TextWrappingModes.Normal;

            var sys = UIKit.Rect("System", root);
            UIKit.Anchor(sys, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1600f, 80f));
            systemGroup = UIKit.Group(sys, 0f);
            systemText = UIKit.Label("SystemText", sys, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1600f, 80f), 34f, new Color(1f, 0.35f, 0.3f), TextAlignmentOptions.Center);
            systemText.characterSpacing = 12f;
        }
    }
}
