using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HackAndSlash
{
    /// <summary>Runtime-built uGUI/TMP HUD: health & rage bars, combo meter, wave banners, boss bar, damage vignette, game over.</summary>
    public class HUD : MonoBehaviour
    {
        RectTransform hpFill, hpChip, rageFill, bossFill, bossChip, comboTimer;
        Image rageImg, vignette, flash, goBg;
        TextMeshProUGUI hpText, rageText, waveText, enemiesText, scoreText, comboNum, comboLabel, comboMult;
        TextMeshProUGUI bannerTitle, bannerSub, popupText, hintText, autoText, bossName, goTitle, goStats, goPrompt;
        CanvasGroup bannerGroup, popupGroup, comboGroup, bossGroup, goGroup, hintGroup;
        float hpChipV = 1f, bossChipV = 1f, hpDelay, bossDelay, shownScore, bannerT = 99f, popupT = 99f, comboPunch, hurt, flashA, hintT, goT;
        bool gameOver;
        Color comboColor = Color.white;

        static readonly Color Panel = new Color(0.03f, 0.02f, 0.06f, 0.72f);

        void Awake() => Build();

        public void ResetHUD()
        {
            gameOver = false;
            goT = 0f;
            goGroup.alpha = 0f;
            hpChipV = 1f; bossChipV = 1f; shownScore = 0f; hurt = 0f; flashA = 0f; hintT = 0f;
            comboGroup.alpha = 0f;
            popupT = 99f;
            Banner("VOID ARENA", "HACK  ·  SLASH  ·  SURVIVE", new Color(0.55f, 0.85f, 1f));
        }

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
        public void OnPlayerHurt() => hurt = 1f;
        public void OnHeal() { }
        public void Flash(float a) => flashA = Mathf.Max(flashA, a);

        public void ShowGameOver()
        {
            gameOver = true;
            goT = 0f;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null || gm.player == null) return;
            var p = gm.player;
            float udt = Time.unscaledDeltaTime;
            float time = Time.unscaledTime;

            // health
            float hp = Mathf.Clamp01(p.Health / p.maxHealth);
            SetBar(hpFill, hp);
            if (hp >= hpChipV) { hpChipV = hp; hpDelay = 0.4f; }
            else { hpDelay -= udt; if (hpDelay <= 0f) hpChipV = Mathf.MoveTowards(hpChipV, hp, udt * 0.6f); }
            SetBar(hpChip, hpChipV);
            hpText.text = Mathf.CeilToInt(p.Health) + " / " + Mathf.RoundToInt(p.maxHealth);

            // rage
            float rage = p.Rage / 100f;
            SetBar(rageFill, rage);
            if (p.NovaReady)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(time * 8f);
                rageImg.color = Color.Lerp(new Color(0.65f, 0.3f, 1f), new Color(0.4f, 0.95f, 1f), k);
                rageText.text = "<b>Q</b>  VOID NOVA READY";
                rageText.color = Color.Lerp(new Color(0.8f, 0.6f, 1f), Color.white, k);
            }
            else
            {
                rageImg.color = new Color(0.55f, 0.28f, 0.95f);
                rageText.text = "RAGE";
                rageText.color = new Color(0.7f, 0.6f, 0.9f, 0.8f);
            }

            // wave & score
            waveText.text = gm.Wave > 0 ? "WAVE " + gm.Wave : "";
            int remaining = gm.enemies.Count + gm.PendingSpawns;
            enemiesText.text = gm.Wave > 0 && remaining > 0 ? remaining + (remaining == 1 ? " ENEMY" : " ENEMIES") : "";
            shownScore = Mathf.Lerp(shownScore, gm.Score, 1f - Mathf.Exp(-9f * udt));
            if (Mathf.Abs(shownScore - gm.Score) < 1f) shownScore = gm.Score;
            scoreText.text = ((long)shownScore).ToString("N0");

            // combo
            if (gm.Combo >= 2)
            {
                comboGroup.alpha = Mathf.MoveTowards(comboGroup.alpha, 1f, udt * 10f);
                comboNum.text = gm.Combo.ToString();
                string label;
                if (gm.Combo >= 50) { label = "VOID GOD"; comboColor = new Color(1f, 0.35f, 1f); }
                else if (gm.Combo >= 30) { label = "SAVAGE!!"; comboColor = new Color(1f, 0.35f, 0.25f); }
                else if (gm.Combo >= 15) { label = "BRUTAL!"; comboColor = new Color(1f, 0.65f, 0.2f); }
                else if (gm.Combo >= 6) { label = "NICE"; comboColor = new Color(1f, 0.92f, 0.4f); }
                else { label = "HITS"; comboColor = Color.white; }
                comboLabel.text = label;
                comboNum.color = comboColor;
                comboLabel.color = comboColor;
                comboMult.text = "x" + gm.ComboMultiplier.ToString("0.00");
                SetBar(comboTimer, gm.ComboTimer01);
            }
            else comboGroup.alpha = Mathf.MoveTowards(comboGroup.alpha, 0f, udt * 3f);
            comboPunch = Mathf.MoveTowards(comboPunch, 0f, udt * 6f);
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
                bossName.text = boss.displayName.ToUpperInvariant();
                float b = boss.Health01;
                SetBar(bossFill, b);
                if (b >= bossChipV) { bossChipV = b; bossDelay = 0.4f; }
                else { bossDelay -= udt; if (bossDelay <= 0f) bossChipV = Mathf.MoveTowards(bossChipV, b, udt * 0.5f); }
                SetBar(bossChip, bossChipV);
            }
            else bossGroup.alpha = Mathf.MoveTowards(bossGroup.alpha, 0f, udt * 2f);

            // hint & autopilot
            hintT += udt;
            hintGroup.alpha = hintT < 16f ? 0.9f : Mathf.MoveTowards(hintGroup.alpha, 0.25f, udt);
            autoText.enabled = p.autopilot;
            if (p.autopilot) autoText.alpha = 0.6f + 0.4f * Mathf.Sin(time * 4f);

            // vignette & flash
            hurt = Mathf.MoveTowards(hurt, 0f, udt * 2.2f);
            float low = hp < 0.3f && !p.IsDead ? (0.18f + 0.1f * Mathf.Sin(time * 6f)) * (1f - hp / 0.3f) : 0f;
            vignette.color = new Color(0.85f, 0.03f, 0.08f, Mathf.Clamp01(hurt * 0.8f + low));
            flashA = Mathf.MoveTowards(flashA, 0f, udt * 2.5f);
            flash.color = new Color(0.85f, 0.75f, 1f, flashA);

            // game over
            if (gameOver)
            {
                goT += udt;
                goGroup.alpha = Mathf.Clamp01((goT - 0.8f) / 0.8f);
                goStats.text = "WAVE  " + gm.Wave + "        SCORE  " + gm.Score.ToString("N0") + "\nKILLS  " + gm.Kills + "        BEST COMBO  " + gm.BestCombo;
                goPrompt.alpha = goT > 1.6f ? 0.55f + 0.45f * Mathf.Sin(time * 4f) : 0f;
            }
        }

        // ---------------------------------------------------------------- construction

        void Build()
        {
            var canvasGo = new GameObject("HUD_Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            // full-screen overlays (behind everything else)
            vignette = Img("Vignette", root, Vector2.zero, Vector2.one, new Color(0.85f, 0.03f, 0.08f, 0f));
            vignette.sprite = RadialSprite();
            flash = Img("Flash", root, Vector2.zero, Vector2.one, new Color(1f, 1f, 1f, 0f));

            // top-left: health & rage
            Label("Title", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -22f), new Vector2(600f, 30f), 20f, new Color(0.6f, 0.85f, 1f, 0.85f), TextAlignmentOptions.Left).text = "VOID KNIGHT";
            var hpBg = Img("HP", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -52f), new Vector2(520f, 30f), Panel);
            hpChip = Fill(hpBg.transform, new Color(1f, 0.9f, 0.7f, 0.9f));
            hpFill = Fill(hpBg.transform, new Color(0.9f, 0.16f, 0.24f));
            hpText = Label("HPText", hpBg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 30f), 20f, Color.white, TextAlignmentOptions.Center);
            var rageBg = Img("Rage", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -90f), new Vector2(420f, 14f), Panel);
            rageFill = Fill(rageBg.transform, new Color(0.55f, 0.28f, 0.95f));
            rageImg = rageFill.GetComponent<Image>();
            rageText = Label("RageText", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(472f, -82f), new Vector2(500f, 30f), 20f, Color.white, TextAlignmentOptions.Left);

            // top-center: wave
            waveText = Label("Wave", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(700f, 64f), 48f, Color.white, TextAlignmentOptions.Center);
            waveText.characterSpacing = 10f;
            enemiesText = Label("Enemies", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(700f, 30f), 22f, new Color(1f, 0.6f, 0.55f), TextAlignmentOptions.Center);

            // boss bar
            var bossRoot = new GameObject("Boss", typeof(RectTransform)).GetComponent<RectTransform>();
            bossRoot.SetParent(root, false);
            Anchor(bossRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -118f), new Vector2(900f, 70f));
            bossGroup = bossRoot.gameObject.AddComponent<CanvasGroup>();
            bossGroup.alpha = 0f;
            bossName = Label("BossName", bossRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(900f, 34f), 28f, new Color(1f, 0.55f, 0.3f), TextAlignmentOptions.Center);
            bossName.characterSpacing = 18f;
            var bossBg = Img("BossBar", bossRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(900f, 22f), Panel);
            bossChip = Fill(bossBg.transform, new Color(1f, 0.9f, 0.7f, 0.9f));
            bossFill = Fill(bossBg.transform, new Color(1f, 0.38f, 0.08f));

            // top-right: score
            Label("ScoreLabel", root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -22f), new Vector2(400f, 30f), 20f, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Right).text = "SCORE";
            scoreText = Label("Score", root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -46f), new Vector2(500f, 64f), 54f, Color.white, TextAlignmentOptions.Right);

            // right: combo
            var comboRoot = new GameObject("Combo", typeof(RectTransform)).GetComponent<RectTransform>();
            comboRoot.SetParent(root, false);
            Anchor(comboRoot, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-60f, 80f), new Vector2(360f, 260f));
            comboGroup = comboRoot.gameObject.AddComponent<CanvasGroup>();
            comboGroup.alpha = 0f;
            comboNum = Label("ComboNum", comboRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(360f, 130f), 120f, Color.white, TextAlignmentOptions.Right);
            comboNum.rectTransform.pivot = new Vector2(0.85f, 0.5f);
            comboNum.rectTransform.anchoredPosition = new Vector2(-54f, -65f);
            comboLabel = Label("ComboLabel", comboRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -128f), new Vector2(360f, 44f), 36f, Color.white, TextAlignmentOptions.Right);
            comboLabel.characterSpacing = 8f;
            comboMult = Label("ComboMult", comboRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -170f), new Vector2(360f, 34f), 26f, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.Right);
            var ctBg = Img("ComboTimer", comboRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -210f), new Vector2(240f, 6f), Panel);
            comboTimer = Fill(ctBg.transform, new Color(1f, 1f, 1f, 0.85f));

            // center: banner & popup
            var bannerRoot = new GameObject("Banner", typeof(RectTransform)).GetComponent<RectTransform>();
            bannerRoot.SetParent(root, false);
            Anchor(bannerRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(1600f, 220f));
            bannerGroup = bannerRoot.gameObject.AddComponent<CanvasGroup>();
            bannerTitle = Label("BannerTitle", bannerRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(1600f, 150f), 120f, Color.white, TextAlignmentOptions.Center);
            bannerSub = Label("BannerSub", bannerRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -62f), new Vector2(1600f, 50f), 32f, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Center);
            bannerSub.characterSpacing = 14f;

            var popupRoot = new GameObject("Popup", typeof(RectTransform)).GetComponent<RectTransform>();
            popupRoot.SetParent(root, false);
            Anchor(popupRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 100f));
            popupGroup = popupRoot.gameObject.AddComponent<CanvasGroup>();
            popupGroup.alpha = 0f;
            popupText = Label("PopupText", popupRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(1200f, 80f), 54f, Color.white, TextAlignmentOptions.Center);
            popupText.characterSpacing = 10f;

            // bottom: hint & autopilot
            var hintRoot = new GameObject("Hint", typeof(RectTransform)).GetComponent<RectTransform>();
            hintRoot.SetParent(root, false);
            Anchor(hintRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(1800f, 40f));
            hintGroup = hintRoot.gameObject.AddComponent<CanvasGroup>();
            hintText = Label("HintText", hintRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1800f, 40f), 22f, Color.white, TextAlignmentOptions.Center);
            hintText.text = "<b>WASD</b> Move     <b>LMB / J</b> Combo     <b>RMB / K</b> Spin     <b>SPACE</b> Dash     <b>Q</b> Void Nova     <b>P</b> Autopilot";
            autoText = Label("Autopilot", root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 70f), new Vector2(600f, 36f), 26f, new Color(0.4f, 0.95f, 1f), TextAlignmentOptions.Left);
            autoText.text = "AUTOPILOT  [P]";
            autoText.enabled = false;

            // game over
            var goRoot = new GameObject("GameOver", typeof(RectTransform)).GetComponent<RectTransform>();
            goRoot.SetParent(root, false);
            goRoot.anchorMin = Vector2.zero; goRoot.anchorMax = Vector2.one; goRoot.offsetMin = Vector2.zero; goRoot.offsetMax = Vector2.zero;
            goGroup = goRoot.gameObject.AddComponent<CanvasGroup>();
            goGroup.alpha = 0f;
            goBg = Img("GameOverBg", goRoot, Vector2.zero, Vector2.one, new Color(0.02f, 0f, 0.03f, 0.78f));
            goTitle = Label("GameOverTitle", goRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 130f), new Vector2(1600f, 150f), 118f, new Color(1f, 0.22f, 0.28f), TextAlignmentOptions.Center);
            goTitle.text = "YOU HAVE FALLEN";
            goTitle.characterSpacing = 16f;
            goStats = Label("GameOverStats", goRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(1600f, 110f), 34f, Color.white, TextAlignmentOptions.Center);
            goPrompt = Label("GameOverPrompt", goRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -140f), new Vector2(1600f, 50f), 30f, new Color(0.7f, 0.85f, 1f), TextAlignmentOptions.Center);
            goPrompt.text = "PRESS  R  /  ENTER  TO RISE AGAIN";
            goPrompt.characterSpacing = 10f;
        }

        static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        static Image Img(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color c)
        {
            var rt = Rect(name, parent);
            Anchor(rt, anchor, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        // stretched variant (anchorMin/anchorMax)
        static Image Img(string name, Transform parent, Vector2 min, Vector2 max, Color c)
        {
            var rt = Rect(name, parent);
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        static RectTransform Fill(Transform parent, Color c)
        {
            var img = Img("Fill", parent, Vector2.zero, Vector2.one, c);
            var rt = img.rectTransform;
            rt.offsetMin = new Vector2(3f, 3f);
            rt.offsetMax = new Vector2(-3f, -3f);
            return rt;
        }

        static void SetBar(RectTransform fill, float v)
        {
            fill.anchorMax = new Vector2(Mathf.Clamp01(v), 1f);
        }

        static TextMeshProUGUI Label(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, float fontSize, Color c, TextAlignmentOptions align)
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

        static Sprite RadialSprite()
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Vignette" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 1f, r));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }
    }
}
