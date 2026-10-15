using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HackAndSlash
{
    /// <summary>
    /// Runtime-built uGUI/TMP HUD: health and Void bars, gadgets (blaster rounds, tether), combo and takedown meter,
    /// banners, objectives, boss bar, off-screen threat arrows, aiming reticle, damage vignette, letterbox, Kubrick
    /// intertitles, the boot terminal, fades and the game-over card.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        RectTransform hpFill, hpChip, voidFill, bossFill, bossChip, comboTimer, barTop, barBottom, hudRoot, tetherFill;
        Image voidImg, vignette, flash, fade, reticle, reticleRing, hitMarker;
        Image[] pips, ammo, voidSegs;
        TextMeshProUGUI specialText, tipText, objectiveText, voidText, gadgetText;
        CanvasGroup tipGroup, hudGroup, objectiveGroup, titleCardGroup, terminalGroup, aimGroup;
        float letterboxT, letterbox, tipT = 99f, objectiveT = 99f, fadeAlpha, fadeTarget, fadeSpeed = 1f, titleCardT = -1f, titleCardDur, hitMarkerT = 9f;
        TextMeshProUGUI hpText, waveText, enemiesText, scoreText, comboNum, comboLabel, comboMult;
        TextMeshProUGUI bannerTitle, bannerSub, popupText, hintText, autoText, bossName, goTitle, goStats, goPrompt;
        TextMeshProUGUI cardNumeral, cardTitle, terminalText;
        CanvasGroup bannerGroup, popupGroup, comboGroup, bossGroup, goGroup, hintGroup;
        float hpChipV = 1f, bossChipV = 1f, hpDelay, bossDelay, shownScore, bannerT = 99f, popupT = 99f, comboPunch, hurt, flashA, hintT, goT;
        bool gameOver, visible = true;
        GameMode goMode;
        Color comboColor = Color.white;
        readonly List<Image> threats = new List<Image>();
        Sprite arrowSprite;

        public bool Visible => visible;

        void Awake() => Build();

        public void ResetHUD()
        {
            gameOver = false;
            goT = 0f;
            goGroup.alpha = 0f;
            hpChipV = 1f; bossChipV = 1f; shownScore = 0f; hurt = 0f; flashA = 0f; hintT = 0f;
            comboGroup.alpha = 0f;
            popupT = 99f;
            letterbox = 0f; letterboxT = 0f; tipT = 99f;
        }

        public void SetVisible(bool v) => visible = v;

        public void Banner(string title, string sub, Color color)
        {
            bannerTitle.text = title;
            bannerTitle.color = color;
            bannerSub.text = sub;
            bannerT = 0f;
        }

        public void Popup(string text, Color color)
        {
            popupText.text = text;
            popupText.color = color;
            popupT = 0f;
        }

        public void OnComboHit(int combo) => comboPunch = 1f;

        /// <summary>Cinematic bars for takedowns, final blows and cutscenes.</summary>
        public void Letterbox(float seconds) => letterboxT = Mathf.Max(letterboxT, seconds);

        /// <summary>Contextual coaching line ("RED ATTACKS CAN'T BE COUNTERED", ...).</summary>
        public void Hint(string text)
        {
            tipText.text = text;
            tipT = 0f;
        }

        /// <summary>Persistent objective line; empty clears it.</summary>
        public void Objective(string text)
        {
            objectiveText.text = string.IsNullOrEmpty(text) ? "" : "<color=#ff5544>●</color>  " + text;
            objectiveT = string.IsNullOrEmpty(text) ? 99f : 0f;
        }

        public void OnPlayerHurt() => hurt = 1f;
        public void OnHeal() { }
        public void Flash(float a) => flashA = Mathf.Max(flashA, a);
        public void HitMarker() => hitMarkerT = 0f;

        /// <summary>Fades the screen to black (1) or back (0) over the given real seconds.</summary>
        public void FadeTo(float alpha, float seconds)
        {
            fadeTarget = alpha;
            fadeSpeed = seconds <= 0f ? 999f : Mathf.Abs(alpha - fadeAlpha) / seconds;
        }

        public bool Faded => fadeAlpha >= 0.99f;

        /// <summary>A Kubrick intertitle: black screen, wide white capitals.</summary>
        public void TitleCard(string numeral, string title, float seconds)
        {
            cardNumeral.text = numeral;
            cardTitle.text = title;
            titleCardT = 0f;
            titleCardDur = seconds;
        }

        public void Terminal(string text, float alpha)
        {
            terminalText.text = text;
            terminalGroup.alpha = alpha;
        }

        public void ShowGameOver(GameMode mode)
        {
            gameOver = true;
            goMode = mode;
            goT = 0f;
            goPrompt.text = mode == GameMode.Story ? "PRESS  R  /  (A)  TO RETRY FROM THE CHECKPOINT" : "PRESS  R  /  (A)  TO RISE AGAIN";
        }

        public void HideGameOver()
        {
            gameOver = false;
            goGroup.alpha = 0f;
        }

        // last values shown, so text (and its garbage) is only rebuilt when something changes
        int lastHp = -1, lastSegs = -1, lastWave = -1, lastRemaining = -1, lastCombo = -1;
        long lastScore = -1;
        bool lastHolding;
        Enemy lastBoss;

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null || gm.player == null) return;
            var p = gm.player;
            float udt = Time.unscaledDeltaTime;
            float time = Time.unscaledTime;

            hudGroup.alpha = Mathf.MoveTowards(hudGroup.alpha, visible && !gm.Paused ? 1f : 0f, udt * 4f);

            // health
            float hp = Mathf.Clamp01(p.Health / p.maxHealth);
            UIKit.SetBar(hpFill, hp);
            if (hp >= hpChipV) { hpChipV = hp; hpDelay = 0.4f; }
            else { hpDelay -= udt; if (hpDelay <= 0f) hpChipV = Mathf.MoveTowards(hpChipV, hp, udt * 0.6f); }
            UIKit.SetBar(hpChip, hpChipV);
            int hpNow = Mathf.CeilToInt(p.Health);
            if (hpNow != lastHp) { lastHp = hpNow; hpText.text = hpNow + " / " + Mathf.RoundToInt(p.maxHealth); }

            // Void meter: three segments, each one more robot in a Cascade
            float vm = p.Void / PlayerController.VoidMax;
            UIKit.SetBar(voidFill, vm);
            int segs = p.CascadeTargets;
            for (int i = 0; i < voidSegs.Length; i++)
            {
                bool on = vm >= (i + 1) / 3f - 0.001f;
                voidSegs[i].color = on ? Color.Lerp(new Color(0.65f, 0.3f, 1f), Color.white, 0.4f + 0.4f * Mathf.Sin(time * 8f + i)) : new Color(1f, 1f, 1f, 0.15f);
            }
            if (segs > 0)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(time * 8f);
                voidImg.color = Color.Lerp(new Color(0.65f, 0.3f, 1f), new Color(0.4f, 0.95f, 1f), k);
                if (segs != lastSegs) voidText.text = "<b>C</b>  VOID CASCADE  x" + segs;
                voidText.color = Color.Lerp(new Color(0.8f, 0.6f, 1f), Color.white, k);
            }
            else
            {
                voidImg.color = new Color(0.55f, 0.28f, 0.95f);
                if (segs != lastSegs) voidText.text = "VOID";
                voidText.color = new Color(0.7f, 0.6f, 0.9f, 0.8f);
            }

            // gadgets: blaster rounds and tether charge
            for (int i = 0; i < ammo.Length; i++)
            {
                bool loaded = i < p.Ammo;
                ammo[i].color = loaded ? new Color(1f, 0.72f, 0.3f) : new Color(1f, 1f, 1f, 0.12f);
            }
            UIKit.SetBar(tetherFill, p.TetherReady01);
            lastSegs = segs;
            if (p.Holding != lastHolding || string.IsNullOrEmpty(gadgetText.text))
            {
                lastHolding = p.Holding;
                gadgetText.text = p.Holding ? "<color=#7fe4ff><b>LMB</b> RIP   <b>R</b> THROW</color>" : "<b>Q</b> BLASTER   <b>R</b> TETHER";
            }

            // aiming reticle (bullet time)
            aimGroup.alpha = Mathf.MoveTowards(aimGroup.alpha, p.Aiming ? 1f : 0f, udt * 8f);
            if (p.Aiming)
            {
                float s = 1f + 0.6f * (1f - Mathf.Clamp01(p.AimTime / 0.25f));
                reticleRing.rectTransform.localScale = Vector3.one * s;
                reticle.color = p.AimTarget ? new Color(1f, 0.35f, 0.25f) : new Color(1f, 1f, 1f, 0.8f);
            }
            hitMarkerT += udt;
            hitMarker.color = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - hitMarkerT / 0.25f));

            // wave & score
            int waveShown = gm.Wave > 0 && gm.Mode == GameMode.Arcade ? gm.Wave : 0;
            if (waveShown != lastWave) { lastWave = waveShown; waveText.text = waveShown > 0 ? "WAVE " + waveShown : ""; }
            int remaining = gm.enemies.Count + gm.PendingSpawns;
            int remainingShown = gm.Wave > 0 && remaining > 0 && gm.WaveActive ? remaining : 0;
            if (remainingShown != lastRemaining) { lastRemaining = remainingShown; enemiesText.text = remainingShown > 0 ? remainingShown + (remainingShown == 1 ? " ASSOCIATE" : " ASSOCIATES") : ""; }
            shownScore = Mathf.Lerp(shownScore, gm.Score, 1f - Mathf.Exp(-9f * udt));
            if (Mathf.Abs(shownScore - gm.Score) < 1f) shownScore = gm.Score;
            long scoreNow = (long)shownScore;
            if (scoreNow != lastScore) { lastScore = scoreNow; scoreText.text = scoreNow.ToString("N0"); }

            // combo
            if (gm.Combo >= 2)
            {
                comboGroup.alpha = Mathf.MoveTowards(comboGroup.alpha, 1f, udt * 10f);
                bool comboChanged = gm.Combo != lastCombo;
                lastCombo = gm.Combo;
                if (comboChanged) comboNum.text = gm.Combo.ToString();
                string label;
                if (gm.Combo >= 50) { label = "VOID GOD"; comboColor = new Color(1f, 0.35f, 1f); }
                else if (gm.Combo >= 30) { label = "SAVAGE!!"; comboColor = new Color(1f, 0.35f, 0.25f); }
                else if (gm.Combo >= 15) { label = "BRUTAL!"; comboColor = new Color(1f, 0.65f, 0.2f); }
                else if (gm.Combo >= 6) { label = "FREEFLOW"; comboColor = new Color(1f, 0.92f, 0.4f); }
                else { label = "HITS"; comboColor = Color.white; }
                if (comboChanged) comboLabel.text = label;
                comboNum.color = comboColor;
                comboLabel.color = comboColor;
                if (comboChanged) comboMult.text = "x" + gm.ComboMultiplier.ToString("0.00");
                UIKit.SetBar(comboTimer, gm.ComboTimer01);
            }
            else comboGroup.alpha = Mathf.MoveTowards(comboGroup.alpha, 0f, udt * 3f);
            comboPunch = Mathf.MoveTowards(comboPunch, 0f, udt * 6f);

            // special-combo meter (takedown)
            int sp = gm.Special;
            for (int i = 0; i < pips.Length; i++)
            {
                bool on = i < sp;
                Color c = gm.SpecialReady ? Color.Lerp(new Color(1f, 0.75f, 0.2f), Color.white, 0.5f + 0.5f * Mathf.Sin(time * 10f)) : new Color(1f, 0.82f, 0.35f);
                pips[i].color = on ? c : new Color(1f, 1f, 1f, 0.12f);
            }
            specialText.text = gm.SpecialReady ? "<b>F</b>  TAKEDOWN READY" : "";
            specialText.alpha = 0.7f + 0.3f * Mathf.Sin(time * 8f);

            // letterbox
            letterboxT -= udt;
            letterbox = Mathf.MoveTowards(letterbox, letterboxT > 0f ? 1f : 0f, udt * (letterboxT > 0f ? 6f : 3f));
            float bh = 118f * Mathf.SmoothStep(0f, 1f, letterbox);
            barTop.sizeDelta = new Vector2(0f, bh);
            barBottom.sizeDelta = new Vector2(0f, bh);

            // coaching tip & objective
            tipT += udt;
            tipGroup.alpha = Mathf.Clamp01(tipT / 0.15f) * (1f - Mathf.Clamp01((tipT - 3.2f) / 0.6f));
            objectiveT += udt;
            objectiveGroup.alpha = objectiveT > 90f ? Mathf.MoveTowards(objectiveGroup.alpha, 0f, udt * 2f) : Mathf.Clamp01(objectiveT / 0.4f) * (0.85f + 0.15f * Mathf.Sin(time * 3f));
            comboNum.rectTransform.localScale = Vector3.one * (1f + comboPunch * comboPunch * 0.45f);

            // banner
            bannerT += udt;
            float bIn = Mathf.Clamp01(bannerT / 0.22f);
            float bOut = 1f - Mathf.Clamp01((bannerT - 2.1f) / 0.5f);
            bannerGroup.alpha = bIn * bOut;
            float bs = 1f + 0.7f * Mathf.Pow(1f - bIn, 2f);
            bannerTitle.rectTransform.localScale = Vector3.one * bs;
            bannerTitle.characterSpacing = Mathf.Lerp(40f, 12f, Mathf.Clamp01(bannerT / 2.6f));

            // popup
            popupT += udt;
            float pIn = Mathf.Clamp01(popupT / 0.12f);
            float pOut = 1f - Mathf.Clamp01((popupT - 0.8f) / 0.35f);
            popupGroup.alpha = pIn * pOut;
            popupText.rectTransform.anchoredPosition = new Vector2(0f, -150f + popupT * 40f);
            popupText.rectTransform.localScale = Vector3.one * (1f + 0.5f * Mathf.Pow(1f - pIn, 2f));

            // boss
            var boss = gm.Boss;
            if (boss != null && !boss.IsDead)
            {
                bossGroup.alpha = Mathf.MoveTowards(bossGroup.alpha, 1f, udt * 3f);
                if (boss != lastBoss) { lastBoss = boss; bossName.text = boss.displayName.ToUpperInvariant(); }
                float b = boss.Health01;
                UIKit.SetBar(bossFill, b);
                if (b >= bossChipV) { bossChipV = b; bossDelay = 0.4f; }
                else { bossDelay -= udt; if (bossDelay <= 0f) bossChipV = Mathf.MoveTowards(bossChipV, b, udt * 0.5f); }
                UIKit.SetBar(bossChip, bossChipV);
            }
            else bossGroup.alpha = Mathf.MoveTowards(bossGroup.alpha, 0f, udt * 2f);

            // hint & autopilot
            hintT += udt;
            hintGroup.alpha = hintT < 16f ? 0.85f : Mathf.MoveTowards(hintGroup.alpha, 0.25f, udt);
            autoText.enabled = p.autopilot && gm.Mode != GameMode.Title;
            if (autoText.enabled) autoText.alpha = 0.6f + 0.4f * Mathf.Sin(time * 4f);

            // vignette & flash
            hurt = Mathf.MoveTowards(hurt, 0f, udt * 2.2f);
            float low = hp < 0.3f && !p.IsDead ? (0.18f + 0.1f * Mathf.Sin(time * 6f)) * (1f - hp / 0.3f) : 0f;
            vignette.color = new Color(0.85f, 0.03f, 0.08f, Mathf.Clamp01(hurt * 0.8f + low) * hudGroup.alpha);
            flashA = Mathf.MoveTowards(flashA, 0f, udt * 2.5f);
            flash.color = new Color(0.85f, 0.75f, 1f, flashA);

            UpdateThreats(gm, visible && Settings.CounterIndicators && gm.Mode != GameMode.Title);

            // fades, intertitles
            fadeAlpha = Mathf.MoveTowards(fadeAlpha, fadeTarget, udt * fadeSpeed);
            fade.color = new Color(0f, 0f, 0f, fadeAlpha);
            if (titleCardT >= 0f)
            {
                titleCardT += udt;
                float a = Mathf.Clamp01(titleCardT / 0.6f) * (1f - Mathf.Clamp01((titleCardT - titleCardDur + 0.8f) / 0.8f));
                titleCardGroup.alpha = a;
                cardTitle.characterSpacing = Mathf.Lerp(28f, 34f, titleCardT / Mathf.Max(0.1f, titleCardDur));
                if (titleCardT > titleCardDur) { titleCardT = -1f; titleCardGroup.alpha = 0f; }
            }

            // game over
            if (gameOver)
            {
                goT += udt;
                goGroup.alpha = Mathf.Clamp01((goT - 0.8f) / 0.8f);
                goTitle.text = goMode == GameMode.Story ? "RE-INTEGRATION PENDING" : "YOU HAVE FALLEN";
                goStats.text = goMode == GameMode.Story ? "HALCYON IS RESTORING YOU FROM THE LAST CHECKPOINT"
                    : "WAVE  " + gm.Wave + "        SCORE  " + gm.Score.ToString("N0") + "\nKILLS  " + gm.Kills + "        BEST COMBO  " + gm.BestCombo;
                goPrompt.alpha = goT > 1.6f ? 0.55f + 0.45f * Mathf.Sin(time * 4f) : 0f;
            }
        }

        // ---------------------------------------------------------------- threat arrows (attacks from off-screen)

        void UpdateThreats(GameManager gm, bool show)
        {
            int used = 0;
            var cam = Camera.main;
            if (show && cam)
            {
                foreach (var e in gm.enemies)
                {
                    if (e == null || !(e.CanBeCountered || e.UnblockableIncoming)) continue;
                    Vector3 vp = cam.WorldToViewportPoint(e.Head);
                    bool onScreen = vp.z > 0f && vp.x > 0.04f && vp.x < 0.96f && vp.y > 0.04f && vp.y < 0.96f;
                    if (onScreen) continue;
                    if (used >= 8) break;
                    var img = Threat(used++);
                    Vector2 dir = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
                    if (vp.z < 0f) dir = -dir;
                    if (dir.sqrMagnitude < 1e-4f) dir = Vector2.down;
                    dir.Normalize();
                    var rt = img.rectTransform;
                    rt.anchoredPosition = new Vector2(dir.x * 420f, dir.y * 300f);
                    rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
                    float pulse = 1f + 0.25f * Mathf.Sin(Time.unscaledTime * 20f);
                    rt.localScale = Vector3.one * pulse;
                    img.color = e.UnblockableIncoming ? new Color(1f, 0.25f, 0.15f, 0.95f) : new Color(0.45f, 0.85f, 1f, 0.95f);
                    img.enabled = true;
                }
            }
            for (int i = used; i < threats.Count; i++) threats[i].enabled = false;
        }

        Image Threat(int i)
        {
            while (threats.Count <= i)
            {
                var img = UIKit.Img("Threat", hudRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46f, 46f), Color.white);
                img.sprite = arrowSprite;
                img.enabled = false;
                threats.Add(img);
            }
            return threats[i];
        }

        // ---------------------------------------------------------------- construction

        void Build()
        {
            var canvas = UIKit.Canvas("HUD_Canvas", transform, 10);
            var root = canvas.transform;
            arrowSprite = ArrowSprite();

            // full-screen overlays (behind everything else)
            vignette = UIKit.Img("Vignette", root, Vector2.zero, Vector2.one, new Color(0.85f, 0.03f, 0.08f, 0f));
            vignette.sprite = UIKit.RadialSprite();
            flash = UIKit.Img("Flash", root, Vector2.zero, Vector2.one, new Color(1f, 1f, 1f, 0f));

            hudRoot = UIKit.Stretch("HUDRoot", root);
            hudGroup = UIKit.Group(hudRoot);
            var h = hudRoot;

            // top-left: health & void
            UIKit.Label("Title", h, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -22f), new Vector2(600f, 30f), 20f, new Color(0.6f, 0.85f, 1f, 0.85f), TextAlignmentOptions.Left).text = "K-7  ·  VOID KNIGHT";
            var hpBg = UIKit.Img("HP", h, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -52f), new Vector2(520f, 30f), UIKit.Panel);
            hpChip = UIKit.Fill(hpBg.transform, new Color(1f, 0.9f, 0.7f, 0.9f));
            hpFill = UIKit.Fill(hpBg.transform, new Color(0.9f, 0.16f, 0.24f));
            hpText = UIKit.Label("HPText", hpBg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 30f), 20f, Color.white, TextAlignmentOptions.Center);
            var voidBg = UIKit.Img("Void", h, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -90f), new Vector2(420f, 14f), UIKit.Panel);
            voidFill = UIKit.Fill(voidBg.transform, new Color(0.55f, 0.28f, 0.95f));
            voidImg = voidFill.GetComponent<Image>();
            voidSegs = new Image[3];
            for (int i = 0; i < 3; i++)
                voidSegs[i] = UIKit.Img("VoidSeg" + i, h, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f + 140f * (i + 1) - 6f, -108f), new Vector2(10f, 10f), new Color(1f, 1f, 1f, 0.15f));
            foreach (var s in voidSegs) s.sprite = UIKit.CircleSprite();
            voidText = UIKit.Label("VoidText", h, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(472f, -82f), new Vector2(500f, 30f), 20f, Color.white, TextAlignmentOptions.Left);
            objectiveText = UIKit.Label("Objective", h, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -130f), new Vector2(900f, 34f), 24f, new Color(1f, 0.92f, 0.85f), TextAlignmentOptions.Left);
            objectiveText.characterSpacing = 4f;
            objectiveGroup = UIKit.Group(objectiveText.rectTransform, 0f);

            // bottom-left: gadgets
            gadgetText = UIKit.Label("Gadgets", h, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 96f), new Vector2(700f, 30f), 20f, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.Left);
            ammo = new Image[PlayerController.AmmoMax];
            for (int i = 0; i < ammo.Length; i++)
            {
                ammo[i] = UIKit.Img("Round" + i, h, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(42f + i * 22f, 70f), new Vector2(14f, 22f), Color.white);
            }
            var tBg = UIKit.Img("Tether", h, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(190f, 74f), new Vector2(160f, 12f), UIKit.Panel);
            tetherFill = UIKit.Fill(tBg.transform, new Color(0.45f, 0.9f, 1f));

            // top-center: wave
            waveText = UIKit.Label("Wave", h, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(700f, 64f), 48f, Color.white, TextAlignmentOptions.Center);
            waveText.characterSpacing = 10f;
            enemiesText = UIKit.Label("Enemies", h, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(700f, 30f), 22f, new Color(1f, 0.6f, 0.55f), TextAlignmentOptions.Center);

            // boss bar
            var bossRoot = UIKit.Rect("Boss", h);
            UIKit.Anchor(bossRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -118f), new Vector2(900f, 70f));
            bossGroup = UIKit.Group(bossRoot, 0f);
            bossName = UIKit.Label("BossName", bossRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(900f, 34f), 28f, new Color(1f, 0.55f, 0.3f), TextAlignmentOptions.Center);
            bossName.characterSpacing = 18f;
            var bossBg = UIKit.Img("BossBar", bossRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(900f, 22f), UIKit.Panel);
            bossChip = UIKit.Fill(bossBg.transform, new Color(1f, 0.9f, 0.7f, 0.9f));
            bossFill = UIKit.Fill(bossBg.transform, new Color(1f, 0.38f, 0.08f));

            // top-right: score
            UIKit.Label("ScoreLabel", h, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -22f), new Vector2(400f, 30f), 20f, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Right).text = "SCORE";
            scoreText = UIKit.Label("Score", h, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -46f), new Vector2(500f, 64f), 54f, Color.white, TextAlignmentOptions.Right);

            // right: combo
            var comboRoot = UIKit.Rect("Combo", h);
            UIKit.Anchor(comboRoot, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-60f, 80f), new Vector2(360f, 260f));
            comboGroup = UIKit.Group(comboRoot, 0f);
            comboNum = UIKit.Label("ComboNum", comboRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(360f, 130f), 120f, Color.white, TextAlignmentOptions.Right);
            comboNum.rectTransform.pivot = new Vector2(0.85f, 0.5f);
            comboNum.rectTransform.anchoredPosition = new Vector2(-54f, -65f);
            comboLabel = UIKit.Label("ComboLabel", comboRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -128f), new Vector2(360f, 44f), 36f, Color.white, TextAlignmentOptions.Right);
            comboLabel.characterSpacing = 8f;
            comboMult = UIKit.Label("ComboMult", comboRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -170f), new Vector2(360f, 34f), 26f, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.Right);
            var ctBg = UIKit.Img("ComboTimer", comboRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -210f), new Vector2(240f, 6f), UIKit.Panel);
            comboTimer = UIKit.Fill(ctBg.transform, new Color(1f, 1f, 1f, 0.85f));

            // special-combo pips sit under the combo counter and stay visible
            pips = new Image[GameManager.SpecialCost];
            for (int i = 0; i < pips.Length; i++)
                pips[i] = UIKit.Img("Pip" + i, h, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-60f - (pips.Length - 1 - i) * 32f, -170f), new Vector2(26f, 8f), new Color(1f, 1f, 1f, 0.12f));
            specialText = UIKit.Label("Special", h, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-60f, -200f), new Vector2(420f, 34f), 24f, new Color(1f, 0.85f, 0.4f), TextAlignmentOptions.Right);
            specialText.characterSpacing = 6f;

            // aiming reticle
            var aimRoot = UIKit.Rect("Aim", h);
            UIKit.Anchor(aimRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80f, 80f));
            aimGroup = UIKit.Group(aimRoot, 0f);
            reticle = UIKit.Img("Dot", aimRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8f, 8f), Color.white);
            reticle.sprite = UIKit.CircleSprite();
            reticleRing = UIKit.Img("Ring", aimRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f), new Color(1f, 1f, 1f, 0.6f));
            reticleRing.sprite = UIKit.RingSprite();
            hitMarker = UIKit.Img("HitMarker", aimRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f), new Color(1f, 1f, 1f, 0f));
            hitMarker.sprite = UIKit.RingSprite();

            // coaching tip
            var tipRoot = UIKit.Rect("Tip", h);
            UIKit.Anchor(tipRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 250f), new Vector2(1400f, 44f));
            tipGroup = UIKit.Group(tipRoot, 0f);
            tipText = UIKit.Label("TipText", tipRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 44f), 28f, new Color(0.75f, 0.92f, 1f), TextAlignmentOptions.Center);
            tipText.characterSpacing = 6f;

            // center: banner & popup
            var bannerRoot = UIKit.Rect("Banner", h);
            UIKit.Anchor(bannerRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(1600f, 220f));
            bannerGroup = UIKit.Group(bannerRoot, 0f);
            bannerTitle = UIKit.Label("BannerTitle", bannerRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(1600f, 150f), 120f, Color.white, TextAlignmentOptions.Center);
            bannerSub = UIKit.Label("BannerSub", bannerRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -62f), new Vector2(1600f, 50f), 32f, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Center);
            bannerSub.characterSpacing = 14f;

            var popupRoot = UIKit.Rect("Popup", h);
            UIKit.Anchor(popupRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 100f));
            popupGroup = UIKit.Group(popupRoot, 0f);
            popupText = UIKit.Label("PopupText", popupRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(1200f, 80f), 54f, Color.white, TextAlignmentOptions.Center);
            popupText.characterSpacing = 10f;

            // bottom: controls hint & autopilot
            var hintRoot = UIKit.Rect("Hint", h);
            UIKit.Anchor(hintRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(1840f, 40f));
            hintGroup = UIKit.Group(hintRoot);
            hintText = UIKit.Label("HintText", hintRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1840f, 40f), 20f, Color.white, TextAlignmentOptions.Center);
            hintText.text = "<b>LMB</b> Strike   <b>RMB</b> Counter   <b>SPACE</b> Evade/Vault   <b>E</b> Stun   <b>F</b> Takedown   <b>Q</b> Blaster (hold: aim)   <b>R</b> Tether   <b>C</b> Cascade   <b>ESC</b> Pause";
            autoText = UIKit.Label("Autopilot", h, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 130f), new Vector2(600f, 36f), 26f, new Color(0.4f, 0.95f, 1f), TextAlignmentOptions.Left);
            autoText.text = "AUTOPILOT  [P]";
            autoText.enabled = false;

            // cinematic letterbox (above the HUD, always available)
            barTop = UIKit.Img("LetterboxTop", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero, Color.black).rectTransform;
            barTop.anchorMin = new Vector2(0f, 1f); barTop.anchorMax = new Vector2(1f, 1f);
            barBottom = UIKit.Img("LetterboxBottom", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero, Color.black).rectTransform;
            barBottom.anchorMin = new Vector2(0f, 0f); barBottom.anchorMax = new Vector2(1f, 0f);

            // game over
            var goRoot = UIKit.Stretch("GameOver", root);
            goGroup = UIKit.Group(goRoot, 0f);
            UIKit.Img("GameOverBg", goRoot, Vector2.zero, Vector2.one, new Color(0.02f, 0f, 0.03f, 0.78f));
            goTitle = UIKit.Label("GameOverTitle", goRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 130f), new Vector2(1700f, 150f), 104f, new Color(1f, 0.22f, 0.28f), TextAlignmentOptions.Center);
            goTitle.characterSpacing = 16f;
            goStats = UIKit.Label("GameOverStats", goRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(1600f, 110f), 34f, Color.white, TextAlignmentOptions.Center);
            goPrompt = UIKit.Label("GameOverPrompt", goRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -140f), new Vector2(1600f, 50f), 30f, new Color(0.7f, 0.85f, 1f), TextAlignmentOptions.Center);
            goPrompt.characterSpacing = 10f;

            // fades, intertitles and the boot terminal live on their own canvas above the HUD (menus sit above them)
            var top = UIKit.Canvas("Overlay_Canvas", transform, 25).transform;
            fade = UIKit.Img("Fade", top, Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0f));
            var cardRoot = UIKit.Stretch("TitleCard", top);
            titleCardGroup = UIKit.Group(cardRoot, 0f);
            UIKit.Img("CardBg", cardRoot, Vector2.zero, Vector2.one, Color.black);
            cardNumeral = UIKit.Label("Numeral", cardRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(1600f, 60f), 34f, new Color(0.85f, 0.85f, 0.85f), TextAlignmentOptions.Center);
            cardNumeral.characterSpacing = 40f;
            cardTitle = UIKit.Label("CardTitle", cardRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(1800f, 110f), 78f, Color.white, TextAlignmentOptions.Center);
            cardTitle.characterSpacing = 28f;
            var termRoot = UIKit.Stretch("Terminal", top);
            terminalGroup = UIKit.Group(termRoot, 0f);
            UIKit.Img("TermBg", termRoot, Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 1f));
            terminalText = UIKit.Label("TermText", termRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(160f, -160f), new Vector2(1600f, 800f), 34f, new Color(1f, 0.32f, 0.26f), TextAlignmentOptions.TopLeft);
            terminalText.fontStyle = FontStyles.Normal;
            terminalText.characterSpacing = 4f;
            terminalText.lineSpacing = 18f;
        }

        static Sprite ArrowSprite()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "UI_Arrow" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n - 0.5f, v = (y + 0.5f) / n;
                // chevron pointing up
                float w = (1f - v) * 0.5f;
                float edge = Mathf.Abs(u) - w;
                float a = Mathf.Clamp01(-edge * n * 0.5f) * Mathf.Clamp01((v - 0.08f) * n * 0.25f) * Mathf.Clamp01((0.95f - v) * n * 0.5f);
                float notch = v < 0.45f && Mathf.Abs(u) < (0.45f - v) * 0.5f ? 0f : 1f;
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * notch * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }
    }
}
