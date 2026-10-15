using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HackAndSlash
{
    /// <summary>
    /// The game's outer loop: boot (Meridian is generated behind HALCYON's terminal), the title screen (the Knight
    /// fights on autopilot while the camera orbits), Story and Arcade modes, pause, results and credits.
    /// </summary>
    public class GameFlow : MonoBehaviour
    {
        public static GameFlow I { get; private set; }

        GameManager gm;
        MenuUI menu;
        Dialogue dialogue;
        StoryDirector story;
        Coroutine running;
        bool resultsShown, booting = true, transitioning;

        public MenuUI Menu => menu;
        public bool InStory => gm && gm.Mode == GameMode.Story;

        void Awake()
        {
            I = this;
            gm = GetComponent<GameManager>();
        }

        IEnumerator Start()
        {
            var uiRoot = gm.hud ? gm.hud.transform : transform;
            menu = new GameObject("Menu").AddComponent<MenuUI>();
            menu.transform.SetParent(uiRoot, false);
            dialogue = new GameObject("Dialogue").AddComponent<Dialogue>();
            dialogue.transform.SetParent(uiRoot, false);
            story = gameObject.AddComponent<StoryDirector>();
            if (!GetComponent<Music>()) gameObject.AddComponent<Music>();

            gm.Mode = GameMode.Title;
            gm.CombatOn = false;
            gm.hud.SetVisible(false);
            gm.hud.FadeTo(1f, 0f);
            gm.player.InputLocked = true;

            // boot: HALCYON's terminal while the city is generated on worker threads
            var gen = StartCoroutine(TexLibrary.Generate());
            float t = 0f;
            while (!TexLibrary.Ready)
            {
                t += Time.unscaledDeltaTime;
                gm.hud.Terminal(BootText(TexLibrary.Progress, t), 1f);
                yield return null;
            }
            yield return gm.Districts.Load(District.Rooftop);
            gm.hud.Terminal(BootText(1f, t), 1f);
            yield return Wait(0.4f);
            gm.hud.Terminal("", 0f);
            booting = false;
            EnterTitle(true);
        }

        static string BootText(float progress, float t)
        {
            int pct = Mathf.RoundToInt(progress * 100f);
            string bar = new string('|', pct / 4).PadRight(25, '.');
            string cursor = Mathf.Repeat(t, 0.8f) < 0.4f ? "_" : " ";
            return "MERIDIAN MUNICIPAL INTELLIGENCE\nHALCYON v11.59\n\n> GENERATING CITY ....... [" + bar + "] " + pct + "%\n> TEXTURES ............... PROCEDURAL\n> WEATHER ................ RAIN (SCHEDULED)\n> LOCAL TIME ............. 11:59 PM\n\n" + cursor;
        }

        // ---------------------------------------------------------------- title

        public void EnterTitle(bool first = false)
        {
            if (running != null) { StopCoroutine(running); running = null; }
            story.StopAllCoroutines();
            dialogue.ClearAll();
            running = StartCoroutine(TitleRoutine(first));
        }

        IEnumerator TitleRoutine(bool first)
        {
            transitioning = true;
            if (!first) { gm.hud.FadeTo(1f, 0.5f); yield return Wait(0.55f); }
            gm.SetPaused(false);
            menu.CloseAll();
            if (gm.Districts.CurrentDistrict == District.Hotel) yield return gm.Districts.Load(District.Rooftop);
            gm.Districts.SetPhase(0);
            gm.Mode = GameMode.Title;
            gm.CombatOn = true;
            gm.AutoWaves = true;
            gm.ArcadeDistrict = gm.Districts.CurrentDistrict;
            var plan = gm.Districts.Current != null ? gm.Districts.Current.plan : null;
            gm.ResetRun(plan != null ? plan.playerStart : Vector3.zero);
            gm.player.autopilot = true;
            gm.player.InputLocked = false;
            gm.hud.SetVisible(false);
            gm.hud.Objective("");
            gm.cameraRig.TitleOrbit = true;
            gm.cameraRig.OrbitCenter = Vector3.zero;
            gm.cameraRig.EndDolly();
            CameraRig.LockCursor(false);
            if (Music.I) Music.I.Play(Music.Cue.Title);
            menu.Push(TitlePage());
            gm.hud.FadeTo(0f, first ? 2.5f : 0.8f);
            transitioning = false;
            running = null;
        }

        MenuUI.Page TitlePage()
        {
            var p = new MenuUI.Page { title = "", showLogo = true };
            p.Add(new MenuUI.Item(() => Settings.StoryUnlocked > 0 && !Settings.StoryCompleted ? "CONTINUE STORY" : "STORY", () => menu.Push(StoryPage()), () => "Eleven years ago Meridian voted to switch its city AI off at midnight. It is still 11:59."));
            p.Add(new MenuUI.Item("ARCADE  ·  VOID ARENA", () => menu.Push(ArcadePage()), "Endless waves in any district of Meridian. Score, medals, and HALCYON's commentary."));
            p.Add(new MenuUI.Item("SETTINGS", () => menu.Push(SettingsPage()), "Difficulty, sensitivity, volume, commentary."));
            p.Add(new MenuUI.Item("CONTROLS", () => menu.Push(ControlsPage()), "Arkham FreeFlow meets Robo Recall."));
            p.Add(new MenuUI.Item("CREDITS", () => menu.Push(CreditsPage()), "Everything you see and hear is generated from a seed."));
            p.Add(new MenuUI.Item("QUIT", Quit, "HALCYON will keep the lights on."));
            return p;
        }

        MenuUI.Page StoryPage()
        {
            var p = new MenuUI.Page { title = "STORY" };
            p.back = menu.Pop;
            int unlocked = Mathf.Clamp(Settings.StoryUnlocked, 0, Settings.ChapterCount - 1);
            if (unlocked > 0)
                p.Add(new MenuUI.Item(() => "CONTINUE  ·  " + Lines.ChapterNumerals[unlocked] + "  " + Lines.ChapterTitles[unlocked], () => StartStory(unlocked), () => "Resume from the start of the chapter you reached."));
            p.Add(new MenuUI.Item("NEW STORY", () => StartStory(0), "Begin with the calibration. HALCYON has been expecting you."));
            p.Add(new MenuUI.Item("CHAPTER SELECT", () => menu.Push(ChapterPage()), "Replay any chapter you have reached."));
            p.Add(DifficultyItem());
            p.Add(new MenuUI.Item("BACK", menu.Pop));
            return p;
        }

        MenuUI.Page ChapterPage()
        {
            var p = new MenuUI.Page { title = "CHAPTERS" };
            p.back = menu.Pop;
            for (int i = 0; i < Settings.ChapterCount; i++)
            {
                int c = i;
                var item = new MenuUI.Item(() => Lines.ChapterNumerals[c] + "   " + Lines.ChapterTitles[c], () => StartStory(c), () => Settings.DistrictName(Lines.ChapterDistricts[c]) + "  —  " + Settings.DistrictBlurb(Lines.ChapterDistricts[c]));
                item.enabled = () => c <= Mathf.Max(Settings.StoryUnlocked, Settings.StoryCompleted ? Settings.ChapterCount : 0);
                p.Add(item);
            }
            p.Add(new MenuUI.Item("BACK", menu.Pop));
            return p;
        }

        MenuUI.Page ArcadePage()
        {
            var p = new MenuUI.Page { title = "VOID ARENA" };
            p.back = menu.Pop;
            foreach (District d in System.Enum.GetValues(typeof(District)))
            {
                var dd = d;
                p.Add(new MenuUI.Item(() =>
                {
                    long best = Settings.BestScore(dd, Settings.difficulty);
                    int medals = Settings.Medals(Settings.BestWave(dd, Settings.difficulty) - 1);
                    string m = medals == 3 ? "<color=#ffd34d>GOLD</color>" : medals == 2 ? "<color=#c8d0dc>SILVER</color>" : medals == 1 ? "<color=#d08a4a>BRONZE</color>" : "";
                    return Settings.DistrictName(dd) + (best > 0 ? "   <size=70%><color=#9fb3cc>BEST " + best.ToString("N0") + "</color>  " + m + "</size>" : "");
                }, () => StartArcade(dd), () => Settings.DistrictBlurb(dd)));
            }
            p.Add(DifficultyItem());
            p.Add(new MenuUI.Item("BACK", menu.Pop));
            return p;
        }

        MenuUI.Item DifficultyItem()
        {
            var it = new MenuUI.Item(() => "DIFFICULTY:  " + Settings.DifficultyName(Settings.difficulty), null, () => Settings.DifficultyBlurb(Settings.difficulty));
            it.adjust = d => { Settings.difficulty = (Difficulty)(((int)Settings.difficulty + d + 4) % 4); Settings.Save(); };
            return it;
        }

        MenuUI.Page SettingsPage()
        {
            var p = new MenuUI.Page { title = "SETTINGS" };
            p.back = () => { Settings.Save(); menu.Pop(); };
            p.Add(DifficultyItem());
            p.Add(Slider("MOUSE SENSITIVITY", () => Settings.mouseSensitivity, v => Settings.mouseSensitivity = v, 0.2f, 3f, 0.1f));
            p.Add(Slider("STICK SENSITIVITY", () => Settings.stickSensitivity, v => Settings.stickSensitivity = v, 0.3f, 2.5f, 0.1f));
            p.Add(Toggle("INVERT LOOK Y", () => Settings.invertY, v => Settings.invertY = v));
            p.Add(Slider("MUSIC VOLUME", () => Settings.musicVolume, v => Settings.musicVolume = v, 0f, 1f, 0.1f));
            p.Add(Slider("EFFECTS VOLUME", () => Settings.sfxVolume, v => Settings.sfxVolume = v, 0f, 1f, 0.1f));
            p.Add(Toggle("HALCYON COMMENTARY", () => Settings.commentary, v => Settings.commentary = v, "HALCYON's running remarks during fights. The story itself always speaks."));
            p.Add(Toggle("SCREEN SHAKE", () => Settings.screenShake, v => Settings.screenShake = v));
            p.Add(new MenuUI.Item("BACK", () => { Settings.Save(); menu.Pop(); }));
            return p;
        }

        static MenuUI.Item Slider(string name, System.Func<float> get, System.Action<float> set, float min, float max, float step)
        {
            var it = new MenuUI.Item(() =>
            {
                float v = get();
                int bars = Mathf.RoundToInt((v - min) / (max - min) * 10f);
                return name + ":  " + new string('|', bars).PadRight(10, '.') + "  " + v.ToString("0.0");
            }, null);
            it.adjust = d => set(Mathf.Clamp(Mathf.Round((get() + d * step) / step) * step, min, max));
            return it;
        }

        static MenuUI.Item Toggle(string name, System.Func<bool> get, System.Action<bool> set, string desc = null)
        {
            var it = new MenuUI.Item(() => name + ":  " + (get() ? "ON" : "OFF"), null, desc != null ? () => desc : (System.Func<string>)null);
            it.adjust = d => set(!get());
            return it;
        }

        MenuUI.Page ControlsPage()
        {
            var p = new MenuUI.Page { title = "CONTROLS" };
            p.back = menu.Pop;
            p.header = () =>
                "<b>KEYBOARD & MOUSE              GAMEPAD</b>\n" +
                "Sprint ..................... hold SHIFT   L3\n" +
                "Strike / FreeFlow ........ LMB / J      X\n" +
                "Counter ..................... RMB / K      Y\n" +
                "Evade · into a robot = vault .. SPACE   A\n" +
                "Arc Stun (cape stun) ...... E / MMB      B\n" +
                "Takedown (x8) / ground ....... F         RT  or  X+Y\n" +
                "Blaster: tap quickfire ........ Q         LT\n" +
                "     hold = aim, bullet time, LMB fires   LT + RT\n" +
                "Tether: pull & grab ............ R         RB\n" +
                "     holding: LMB rip, R throw            X / RB\n" +
                "Void Cascade (Void meter) .... C         LB\n" +
                "Tactical camera ................ V         VIEW\n" +
                "Pause ......................... ESC        START\n\n" +
                "<size=80%><color=#9fb3cc>Strike a stunned robot for a beatdown. Evade into a robot to vault it.\nRed lightning cannot be countered. Counter a bullet to send it back.\nNear a car, column or barrier, a takedown slams the robot into it.\n*RB takes down a downed robot when nothing is in tether range.</color></size>";
            p.Add(new MenuUI.Item("BACK", menu.Pop));
            return p;
        }

        MenuUI.Page CreditsPage()
        {
            var p = new MenuUI.Page { title = "CREDITS" };
            p.back = menu.Pop;
            p.header = () => string.Join("\n", Lines.Credits);
            p.Add(new MenuUI.Item("BACK", menu.Pop));
            return p;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------------------------------------------------------- story

        public void StartStory(int chapter)
        {
            if (transitioning) return;
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(StoryRoutine(chapter));
        }

        IEnumerator StoryRoutine(int chapter)
        {
            transitioning = true;
            menu.CloseAll();
            gm.hud.FadeTo(1f, 0.6f);
            yield return Wait(0.65f);
            gm.cameraRig.TitleOrbit = false;
            gm.Mode = GameMode.Story;
            gm.AutoWaves = false;
            gm.player.autopilot = false;
            gm.ClearField();
            CameraRig.LockCursor(true);
            transitioning = false;
            for (int c = chapter; c < Settings.ChapterCount; c++)
            {
                yield return story.RunChapter(c);
                Settings.StoryUnlocked = c + 1;
            }
            Settings.StoryCompleted = true;
            yield return story.Ending();
            EnterTitle();
        }

        // ---------------------------------------------------------------- arcade

        public void StartArcade(District d)
        {
            if (transitioning) return;
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(ArcadeRoutine(d));
        }

        IEnumerator ArcadeRoutine(District d)
        {
            transitioning = true;
            menu.CloseAll();
            gm.hud.FadeTo(1f, 0.6f);
            yield return Wait(0.65f);
            gm.SetPaused(false);
            gm.cameraRig.TitleOrbit = false;
            gm.AutoWaves = false;
            gm.ClearField();
            if (gm.Districts.CurrentDistrict != d || gm.Districts.Current == null) yield return gm.Districts.Load(d);
            gm.Districts.SetPhase(d == District.Construct ? (Random.value < 0.5f ? 0 : 1) : 0);
            gm.Mode = GameMode.Arcade;
            gm.ArcadeDistrict = d;
            gm.CombatOn = true;
            var plan = gm.Districts.Current.plan;
            gm.ResetRun(plan.playerStart, plan.playerYaw);
            gm.cameraRig.FaceYaw(plan.playerYaw);
            gm.cameraRig.SnapToTarget();
            gm.player.autopilot = false;
            gm.player.InputLocked = false;
            gm.hud.SetVisible(true);
            gm.AutoWaves = true;
            resultsShown = false;
            CameraRig.LockCursor(true);
            if (Music.I) Music.I.Play(Music.Cue.Combat);
            gm.hud.FadeTo(0f, 0.8f);
            transitioning = false;
            yield return Wait(1f);
            if (Dialogue.I) Dialogue.I.Bark(Lines.BarkArcade, true);
            running = null;
        }

        MenuUI.Page ResultsPage(bool record, int medals)
        {
            var p = new MenuUI.Page { title = record ? "NEW RECORD" : "RESULTS" };
            long score = gm.Score;
            int wave = gm.Wave, kills = gm.Kills, combo = gm.BestCombo;
            District d = gm.ArcadeDistrict;
            p.header = () =>
                Settings.DistrictName(d) + "  ·  " + Settings.DifficultyName(Settings.difficulty) + "\n\n" +
                "SCORE  <size=130%>" + score.ToString("N0") + "</size>\n" +
                "WAVE  " + wave + "     KILLS  " + kills + "     BEST COMBO  " + combo + "\n\n" +
                (medals == 3 ? "<color=#ffd34d>GOLD MEDAL</color>" : medals == 2 ? "<color=#c8d0dc>SILVER MEDAL</color>" : medals == 1 ? "<color=#d08a4a>BRONZE MEDAL</color>" : "<color=#7f8899>NO MEDAL  ·  CLEAR 4 WAVES FOR BRONZE</color>") +
                "\n\n<size=80%><color=#9fb3cc>BEST  " + Settings.BestScore(d, Settings.difficulty).ToString("N0") + "</color></size>";
            p.Add(new MenuUI.Item("RETRY", () => StartArcade(d)));
            p.Add(new MenuUI.Item("ANOTHER DISTRICT", () => { menu.CloseAll(); EnterTitle(); }));
            p.Add(new MenuUI.Item("TITLE", () => { menu.CloseAll(); EnterTitle(); }));
            return p;
        }

        // ---------------------------------------------------------------- pause

        MenuUI.Page PausePage()
        {
            var p = new MenuUI.Page { title = "PAUSED" };
            p.back = Resume;
            p.header = () => gm.Mode == GameMode.Story && story.ChapterIndex >= 0
                ? Lines.ChapterNumerals[story.ChapterIndex] + "   " + Lines.ChapterTitles[story.ChapterIndex] + "\n<size=80%><color=#9fb3cc>" + Settings.DifficultyName(Settings.difficulty) + "</color></size>"
                : Settings.DistrictName(gm.ArcadeDistrict) + "\n<size=80%><color=#9fb3cc>WAVE " + gm.Wave + "  ·  SCORE " + gm.Score.ToString("N0") + "</color></size>";
            p.Add(new MenuUI.Item("RESUME", Resume));
            if (gm.Mode == GameMode.Story)
            {
                var retry = new MenuUI.Item("RESTART CHECKPOINT", () => { Resume(); gm.RetryCheckpoint(); }, "Restart the current fight.");
                retry.enabled = () => gm.WaveActive || gm.IsGameOver;
                p.Add(retry);
                p.Add(new MenuUI.Item("RESTART CHAPTER", () => { Resume(); StartStory(story.ChapterIndex); }));
            }
            else p.Add(new MenuUI.Item("RESTART", () => { Resume(); StartArcade(gm.ArcadeDistrict); }));
            p.Add(new MenuUI.Item("SETTINGS", () => menu.Push(SettingsPage())));
            p.Add(new MenuUI.Item("CONTROLS", () => menu.Push(ControlsPage())));
            p.Add(new MenuUI.Item("QUIT TO TITLE", () => { Resume(); EnterTitle(); }));
            return p;
        }

        void Pause()
        {
            gm.SetPaused(true);
            CameraRig.LockCursor(false);
            menu.Push(PausePage());
        }

        void Resume()
        {
            menu.CloseAll();
            gm.SetPaused(false);
            if (gm.Mode != GameMode.Title) CameraRig.LockCursor(true);
        }

        // ---------------------------------------------------------------- loop

        void Update()
        {
            if (booting || gm == null) return;
            var kb = Keyboard.current; var gp = Gamepad.current; var ms = Mouse.current;
            bool pausePressed = (kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.startButton.wasPressedThisFrame);

            if (gm.Mode == GameMode.Title)
            {
                // the attract demo keeps itself running
                if (gm.IsGameOver && gm.GameOverTime > 4f)
                {
                    var plan = gm.Districts.Current != null ? gm.Districts.Current.plan : null;
                    gm.ResetRun(plan != null ? plan.playerStart : Vector3.zero);
                    gm.hud.SetVisible(false);
                }
                if (!menu.Open && !transitioning) menu.Push(TitlePage());
                return;
            }
            if (transitioning) return;

            if (gm.Paused)
            {
                if (!menu.Open) Resume();
                return;
            }
            if (pausePressed && !menu.Open) { Pause(); return; }

            // relock the cursor when the player clicks back into the game
            if (!menu.Open && Cursor.lockState != CursorLockMode.Locked && ms != null && ms.leftButton.wasPressedThisFrame && Application.isFocused) CameraRig.LockCursor(true);

            if (gm.IsGameOver)
            {
                if (gm.Mode == GameMode.Story && gm.GameOverTime > 1.6f && GameManager.RestartPressed()) gm.RetryCheckpoint();
                else if (gm.Mode == GameMode.Arcade && !resultsShown && gm.GameOverTime > 2.2f)
                {
                    resultsShown = true;
                    int cleared = Mathf.Max(0, gm.WavesCleared);
                    bool record = Settings.RecordArcade(gm.ArcadeDistrict, Settings.difficulty, gm.Score, gm.Wave);
                    gm.hud.HideGameOver();
                    CameraRig.LockCursor(false);
                    menu.Push(ResultsPage(record, Settings.Medals(cleared)));
                }
            }
        }

        /// <summary>Waits real seconds, frozen while paused.</summary>
        public static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                if (GameManager.I == null || !GameManager.I.Paused) t += Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }
}
