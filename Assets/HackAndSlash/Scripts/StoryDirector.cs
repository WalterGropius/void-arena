using System.Collections;
using UnityEngine;

namespace HackAndSlash
{
    /// <summary>
    /// Runs the story: one coroutine per chapter, built from a handful of verbs (load a district, cut to a Kubrick
    /// intertitle, roll a dolly shot, let HALCYON or Wren talk, start a fight and wait for it). Fights are
    /// checkpointed by the GameManager, so dying only replays the current wave.
    /// </summary>
    public class StoryDirector : MonoBehaviour
    {
        GameManager gm;
        Dialogue dlg;
        public int ChapterIndex { get; private set; } = -1;

        void Awake() => gm = GetComponent<GameManager>();

        static EnemyType[] E(params EnemyType[] t) => t;
        const EnemyType G = EnemyType.Grunt, C = EnemyType.Caster, B = EnemyType.Brute, S = EnemyType.Shield, N = EnemyType.Gunner, A = EnemyType.Agent, D = EnemyType.Drone, M = EnemyType.Medic, T = EnemyType.Boss;

        public IEnumerator RunChapter(int c)
        {
            dlg = Dialogue.I;
            ChapterIndex = c;
            switch (c)
            {
                case 0: yield return Prologue(); break;
                case 1: yield return LongRain(); break;
                case 2: yield return LanternStreet(); break;
                case 3: yield return GrandHalcyon(); break;
                case 4: yield return Construct(); break;
                default: yield return Midnight(); break;
            }
        }

        // ================================================================ verbs

        IEnumerator Prep(District d, int phase, bool card, int chapter)
        {
            gm.hud.FadeTo(1f, 0.6f);
            yield return GameFlow.Wait(0.65f);
            gm.CombatOn = false;
            gm.AutoWaves = false;
            gm.ClearField();
            if (gm.Districts.CurrentDistrict != d || gm.Districts.Current == null) yield return gm.Districts.Load(d);
            gm.Districts.SetPhase(phase);
            var plan = gm.Districts.Current.plan;
            gm.ResetRun(plan.playerStart, plan.playerYaw);
            gm.cameraRig.FaceYaw(plan.playerYaw);
            gm.cameraRig.SnapToTarget();
            gm.player.autopilot = false;
            gm.player.InputLocked = false;
            gm.cameraRig.InputLocked = false;
            gm.hud.SetVisible(true);
            gm.MaxAttackers = 0;
            if (Music.I) Music.I.Play(d == District.Hotel ? Music.Cue.Hotel : d == District.Construct ? Music.Cue.Construct : d == District.Spire ? Music.Cue.Spire : Music.Cue.Explore);
            if (card)
            {
                gm.hud.TitleCard(Lines.ChapterNumerals[chapter], Lines.ChapterTitles[chapter], 4.2f);
                if (Sfx.I) Sfx.Play(Sfx.I.bell, 0.35f, 0.8f, 0f);
                yield return GameFlow.Wait(1f);
                gm.hud.FadeTo(0f, 0.1f);
                yield return GameFlow.Wait(3.2f);
            }
            else gm.hud.FadeTo(0f, 1f);
        }

        /// <summary>Locks the player and frames a scripted shot (letterboxed, cinematic grade).</summary>
        void Cut(Vector3 from, Vector3 to, Vector3 lookFrom, Vector3 lookTo, float seconds, float fov0 = 45f, float fov1 = 40f)
        {
            gm.player.InputLocked = true;
            gm.cameraRig.InputLocked = true;
            gm.cameraRig.Dolly(from, to, lookFrom, lookTo, fov0, fov1, seconds);
            gm.CinematicGrade(seconds + 0.5f);
            gm.hud.SetVisible(false);
            if (dlg) dlg.AllowSkip = true;
        }

        void Release()
        {
            gm.player.InputLocked = false;
            gm.cameraRig.InputLocked = false;
            gm.cameraRig.EndDolly();
            gm.hud.SetVisible(true);
            if (dlg) dlg.AllowSkip = false;
        }

        IEnumerator Talk(Lines.Line[] lines)
        {
            while (dlg.Busy) yield return null;
            yield return dlg.SayAll(lines);
        }

        void TalkLater(Lines.Line[] lines) => StartCoroutine(Talk(lines));

        /// <summary>Starts a wave and waits until it is cleared (retries happen inside).</summary>
        IEnumerator Fight(Wave w, string objective, int maxAttackers = 0, EnemyType trigger = EnemyType.Boss, Lines.Line[] onTrigger = null)
        {
            gm.CombatOn = true;
            gm.MaxAttackers = maxAttackers;
            gm.hud.Objective(objective);
            if (Music.I) Music.I.SetIntensity(w.boss ? 3 : 2);
            int target = gm.WavesCleared + 1;
            gm.BeginWave(w);
            bool fired = onTrigger == null;
            while (gm.WavesCleared < target)
            {
                if (!fired)
                    foreach (var e in gm.enemies)
                        if (e && e.type == trigger && e.Targetable) { fired = true; TalkLater(onTrigger); break; }
                yield return null;
            }
            gm.hud.Objective("");
            gm.MaxAttackers = 0;
            if (Music.I) Music.I.SetIntensity(1);
        }

        static Wave W(params EnemyType[] e) => new Wave("", "", e) { maxAlive = 9 };
        static Wave W(string title, string sub, params EnemyType[] e) => new Wave(title, sub, e) { maxAlive = 10 };

        // ================================================================ PROLOGUE — THE CALIBRATION

        IEnumerator Prologue()
        {
            gm.hud.FadeTo(1f, 0.3f);
            yield return GameFlow.Wait(0.35f);
            // HALCYON's boot terminal, typed out
            string shown = "";
            foreach (var line in Lines.Boot)
            {
                for (int i = 0; i <= line.Length; i++)
                {
                    gm.hud.Terminal(shown + line.Substring(0, i) + "_", 1f);
                    if (i % 2 == 0 && Sfx.I && line.Length > 0) Sfx.Play(Sfx.I.typeKey, 0.1f, Random.Range(0.9f, 1.1f), 0f);
                    yield return GameFlow.Wait(0.022f);
                }
                shown += line + "\n";
                yield return GameFlow.Wait(line.Length == 0 ? 0.15f : 0.28f);
            }
            yield return GameFlow.Wait(1f);
            yield return Prep(District.Rooftop, 0, false, 0);
            gm.hud.Terminal("", 0f);
            gm.player.SetDormant(true);
            Vector3 p = gm.player.transform.position;
            Cut(p + new Vector3(2.2f, 0.6f, 2.4f), p + new Vector3(1.4f, 1.6f, 3.4f), p + Vector3.up * 0.9f, p + Vector3.up * 1.4f, 14f, 35f, 42f);
            yield return Talk(Lines.PrologueWake);
            gm.player.SetDormant(false);
            yield return GameFlow.Wait(1.2f);
            Release();

            yield return Talk(Lines.PrologueStrike);
            yield return Fight(W(G, G), "STRIKE  ·  LMB / X", 1);
            yield return Talk(Lines.PrologueStrikeDone);
            yield return Talk(Lines.PrologueCounter);
            yield return Fight(W(G, G, G), "COUNTER WHEN BLUE LIGHTNING APPEARS  ·  RMB / Y", 1);
            yield return Talk(Lines.PrologueCounterDone);
            yield return Talk(Lines.PrologueRed);
            yield return Fight(W(B, G, G), "EVADE RED ATTACKS  ·  SPACE / A        STUN THE JUGGERNAUT  ·  E / B", 2);
            yield return Talk(Lines.PrologueRedDone);
            yield return Talk(Lines.PrologueGadgets);
            yield return Fight(W(C, G, C, G), "BLASTER  ·  Q / LT        TETHER, THEN RIP OR THROW  ·  R / RB", 2);
            yield return Talk(Lines.PrologueDone);
            yield return GameFlow.Wait(0.6f);
            if (Sfx.I) Sfx.Play(Sfx.I.radioStatic, 0.5f, 1f, 0f);
            yield return Talk(Lines.PrologueWren);
            yield return GameFlow.Wait(1f);
        }

        // ================================================================ I — THE LONG RAIN

        IEnumerator LongRain()
        {
            yield return Prep(District.Rooftop, 0, true, 1);
            Vector3 p = gm.player.transform.position;
            Cut(new Vector3(-14f, 9f, -14f), new Vector3(-9f, 4f, -11f), Vector3.up * 2f, p + Vector3.up * 1.3f, 9f, 50f, 42f);
            yield return Talk(Lines.Ch1Intro);
            Release();
            yield return Fight(W("RE-INTEGRATION", "RESTORATION CREW INBOUND", G, G, G, S, G, G, G), "SURVIVE THE RESTORATION CREW", 0, S, Lines.Ch1Shield);
            yield return Talk(Lines.Ch1AfterA);
            TalkLater(Lines.Ch1Guns);
            yield return Fight(W("ARMED RESPONSE", "ENFORCERS AUTHORISED", N, G, G, N, G, C, G, G), "COUNTER BULLETS TO SEND THEM BACK");
            yield return Talk(Lines.Ch1AfterB);
            yield return Fight(W("HEAVY RESTORATION", "TWO JUGGERNAUTS", B, G, G, S, B, N, G, G), "REACH THE SERVICE LIFT");
            yield return Talk(Lines.Ch1Outro);
            yield return GameFlow.Wait(0.8f);
        }

        // ================================================================ II — LANTERN STREET

        IEnumerator LanternStreet()
        {
            yield return Prep(District.Streets, 0, true, 2);
            Cut(new Vector3(3f, 1.2f, -30f), new Vector3(0.5f, 2.2f, -16f), new Vector3(0f, 6f, 40f), new Vector3(0f, 9f, 60f), 14f, 48f, 40f);
            yield return Talk(Lines.Ch2Intro);
            Release();
            TalkLater(Lines.Ch2Strays);
            yield return Fight(W("LANTERN STREET", "THE NIGHT SHIFT", G, G, N, G, G, N, G), "CLEAR THE INTERSECTION");
            yield return Talk(Lines.Ch2AfterA);
            yield return Fight(W("FIELD REPAIRS", "MENDERS ON SITE", S, G, C, M, G, S, C, G), "STUN CHARGED ASSOCIATES BEFORE STRIKING", 0, M, Lines.Ch2Mender);
            yield return Talk(Lines.Ch2AfterB);
            yield return Fight(W("AIR SUPPORT", "HORNETS", D, G, G, D, N, G, N, G), "SHOOT OR TETHER THE HORNETS", 0, D, Lines.Ch2Drones);
            yield return Fight(W("LAST CALL", "EVERYTHING HALCYON HAS LEFT ON THIS STREET", B, G, N, S, G, M, D, G, N, G, G), "HOLD LANTERN STREET");
            yield return Talk(Lines.Ch2Outro);
            yield return GameFlow.Wait(0.8f);
        }

        // ================================================================ III — THE GRAND HALCYON

        IEnumerator GrandHalcyon()
        {
            yield return Prep(District.Hotel, 0, true, 3);
            // one-point perspective down the ballroom, slow push in
            Cut(new Vector3(0f, 1.7f, -14.5f), new Vector3(0f, 1.7f, -8f), new Vector3(0f, 3.2f, 15f), new Vector3(0f, 3.4f, 15f), 18f, 46f, 40f);
            yield return Talk(Lines.Ch3Intro);
            Release();
            TalkLater(Lines.Ch3Housekeeping);
            yield return Fight(W("HOUSEKEEPING", "A SYMMETRICAL ARRANGEMENT", S, S, G, G, G, G, C, C), "CLEAR THE BALLROOM");
            yield return Talk(Lines.Ch3AfterA);
            yield return Fight(W("ROOM SERVICE", "JUGGERNAUTS", B, B, S, G, G, M, S, G), "USE THE COLUMNS: TAKEDOWNS NEAR THEM SLAM ROBOTS INTO THEM");
            TalkLater(Lines.Ch3Twins);
            yield return Fight(W("CHECK-OUT", "THE GALA IS OVER", N, M, G, G, N, G, M, G, N), "CLEAR THE BALLROOM");
            // the reveal: the recording, the clock
            gm.CombatOn = false;
            var clockPos = new Vector3(0f, 7.6f, 15f);
            Cut(new Vector3(0f, 2f, -6f), new Vector3(0f, 5.5f, 7f), clockPos + Vector3.down * 1.5f, clockPos, 26f, 40f, 30f);
            gm.Districts.SetPhase(1);
            yield return Talk(Lines.Ch3Reveal);
            gm.hud.FadeTo(1f, 1.2f);
            if (Sfx.I) Sfx.Play(Sfx.I.glitch, 0.7f, 0.8f, 0f);
            yield return GameFlow.Wait(1.4f);
            Release();
        }

        // ================================================================ IV — THE CONSTRUCT

        IEnumerator Construct()
        {
            yield return Prep(District.Construct, 0, true, 4);
            Cut(new Vector3(-6f, 1.4f, 14f), new Vector3(-2f, 1.6f, 17f), new Vector3(0f, 1f, 24f), new Vector3(0f, 1f, 24f), 10f, 50f, 42f);
            yield return Talk(Lines.Ch4Intro);
            Release();
            TalkLater(Lines.Ch4Auditors);
            yield return Fight(W("THE AUDIT", "AUDITORS", A, G, G, A, G), "COUNTER THE AUDITORS");
            yield return Talk(Lines.Ch4Signal);
            // the construct glitches: black and green, the code shows through
            if (Sfx.I) Sfx.Play(Sfx.I.glitch, 0.9f, 1f, 0f);
            gm.hud.Flash(0.8f);
            gm.Districts.SetPhase(1);
            if (Music.I) Music.I.Play(Music.Cue.Glitch);
            yield return Talk(Lines.Ch4Truth);
            TalkLater(Lines.Ch4Delete);
            yield return Fight(W("DELETE", "THE AUDIT CONTINUES", A, A, D, G, A, D, G, G), "SURVIVE THE AUDIT");
            yield return Fight(W("FINAL REVIEW", "", A, A, N, A, A, N, G, G), "WAKE UP");
            yield return Talk(Lines.Ch4Outro);
            yield return GameFlow.Wait(0.8f);
        }

        // ================================================================ V — MIDNIGHT

        IEnumerator Midnight()
        {
            yield return Prep(District.Spire, 0, true, 5);
            var mono = gm.Districts.Current.monolith;
            Vector3 eye = mono ? mono.EyePosition : new Vector3(0f, 15f, 20f);
            Cut(new Vector3(0f, 0.8f, -12f), new Vector3(0f, 2.5f, -4f), eye + Vector3.down * 10f, eye, 16f, 55f, 34f);
            yield return Talk(Lines.Ch5Intro);
            Release();
            TalkLater(Lines.Ch5Crew);
            yield return Fight(W("THE LAST CREW", "BUILT FROM THE AQUARIUM", D, G, N, G, D, B, N, G, D, G), "REACH THE MONOLITH");
            yield return Talk(Lines.Ch5Boss);
            yield return BossFight();
            yield return Ending();
        }

        IEnumerator BossFight()
        {
            gm.CombatOn = true;
            gm.hud.Objective("BRING DOWN HALCYON");
            if (Music.I) { Music.I.Play(Music.Cue.Spire); Music.I.SetIntensity(3); }
            int target = gm.WavesCleared + 1;
            gm.BeginWave(new Wave("HALCYON", "IN PERSON", T, G, G, N) { maxAlive = 6 });
            int stage = 0;
            Enemy core = null;
            while (gm.WavesCleared < target)
            {
                var boss = gm.Boss;
                if (boss != null && !boss.IsDead)
                {
                    float h = boss.Health01;
                    if (core == null && stage == 0 && h < 0.66f) { stage = 1; core = EjectCore(boss, "PRIDE"); TalkLater(Lines.Ch5Core1); }
                    else if (core == null && stage == 2 && h < 0.33f) { stage = 3; core = EjectCore(boss, "FEAR"); TalkLater(Lines.Ch5Core2); }
                    else if (core == null && stage == 4 && h < 0.12f) { stage = 5; core = EjectCore(boss, "MEMORY"); TalkLater(Lines.Ch5Core3); }
                    if (core != null && core.IsDead)
                    {
                        core = null;
                        boss.SetShielded(false);
                        stage++;
                        gm.hud.Popup("CORE DESTROYED", new Color(1f, 0.4f, 0.3f));
                        if (stage == 2) TalkLater(Lines.Ch5Core1Done);
                        else if (stage == 4) TalkLater(Lines.Ch5Core2Done);
                        else if (stage >= 6) { boss.ForceKill(Vector3.back); }
                        if (stage < 6) boss.TakeHit(new HitInfo(boss.maxHealth * 0.08f, Vector3.forward, 2f, HitKind.Takedown, true), boss.transform.position + Vector3.back);
                        // a core going down also clears the escorts' heads a little
                        gm.SlowMo(0.3f, 0.8f);
                    }
                    // keep a couple of escorts coming while the Tyrant lives
                    if (gm.enemies.Count < 3 && gm.PendingSpawns == 0 && Random.value < 0.004f) gm.SpawnEnemy(Random.value < 0.5f ? EnemyType.Drone : EnemyType.Grunt);
                }
                yield return null;
            }
            gm.hud.Objective("");
        }

        Enemy EjectCore(Enemy boss, string label)
        {
            boss.SetShielded(true);
            gm.hud.Banner("CORE EJECTED", label, new Color(1f, 0.3f, 0.2f));
            if (Sfx.I) Sfx.Play(Sfx.I.glitch, 0.8f, 1.2f, 0f);
            Vector3 pos = boss.transform.position + Vector3.up * 3f;
            var c = Enemy.SpawnCore(label, pos, gm);
            gm.hud.Hint("TETHER THE CORE [R / RB], THEN RIP IT OUT [LMB / X]");
            return c;
        }

        // ================================================================ 12:00

        public IEnumerator Ending()
        {
            dlg = Dialogue.I;
            ChapterIndex = 5;
            gm.CombatOn = false;
            gm.AutoWaves = false;
            yield return GameFlow.Wait(1.5f);
            var inst = gm.Districts.Current;
            var mono = inst != null ? inst.monolith : null;
            Vector3 eye = mono ? mono.EyePosition : new Vector3(0f, 15f, 20f);
            if (Music.I) Music.I.Play(Music.Cue.Ending);
            Cut(new Vector3(0f, 4f, -6f), new Vector3(0f, 9f, 6f), eye + Vector3.down * 2f, eye, 30f, 40f, 28f);
            yield return Talk(Lines.Ch5End);
            // midnight: twelve strokes of a bell, the eye goes dark, the rain stops, the clocks finally move
            for (int i = 0; i < 12; i++)
            {
                if (Sfx.I) Sfx.Play(Sfx.I.bell, 0.6f, 1f, 0f);
                if (i == 3 && mono) mono.SetLevel(0f);
                if (i == 6) gm.Districts.SetPhase(1);
                if (i == 11 && inst != null)
                {
                    foreach (var t in inst.clockTexts) if (t) t.text = "12:00";
                    foreach (var c in inst.clocks) if (c) c.SetTime(12f, 0f);
                }
                yield return GameFlow.Wait(1.1f);
            }
            Vector3 pp = gm.player.transform.position;
            Cut(pp + new Vector3(0f, 1.6f, -3f), pp + new Vector3(0f, 2.5f, -5f), pp + Vector3.up * 1.6f, pp + new Vector3(0f, 40f, 30f), 20f, 45f, 60f);
            gm.hud.TitleCard("", "12:00", 4.5f);
            yield return GameFlow.Wait(5f);
            yield return Talk(Lines.Epilogue);
            yield return GameFlow.Wait(1f);
            yield return Credits();
            yield return GameFlow.Wait(1f);
            gm.hud.FadeTo(1f, 0.1f);
            foreach (var l in Lines.Stinger) yield return dlg.Say(l);
            Release();
            Settings.StoryCompleted = true;
        }

        IEnumerator Credits()
        {
            gm.hud.FadeTo(1f, 2f);
            yield return GameFlow.Wait(2.2f);
            if (Music.I) Music.I.Play(Music.Cue.Credits);
            foreach (var line in Lines.Song)
            {
                if (line.Length == 0) { yield return GameFlow.Wait(1.4f); continue; }
                gm.hud.TitleCard("", line, 4.6f);
                yield return GameFlow.Wait(4.1f);
            }
            yield return GameFlow.Wait(1f);
            for (int i = 0; i < Lines.Credits.Length; i += 3)
            {
                string a = Lines.Credits[i], b = i + 1 < Lines.Credits.Length ? Lines.Credits[i + 1] : "";
                string c = i + 2 < Lines.Credits.Length ? Lines.Credits[i + 2] : "";
                gm.hud.TitleCard(a, b + (c.Length > 0 ? "\n<size=60%>" + c + "</size>" : ""), 3.6f);
                yield return GameFlow.Wait(3.4f);
            }
        }
    }
}
