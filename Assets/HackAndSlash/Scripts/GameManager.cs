using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    /// <summary>
    /// Scrapper (blade), Hexer (bolts), Juggernaut (unblockable slam), Iron Tyrant (boss), Bulwark (riot shield),
    /// Enforcer (rifle: deflect its bullets), Auditor (Matrix agent: dodges strikes, counters yours), Hornet (drone),
    /// Mender (revives and electrifies the fallen).
    /// </summary>
    public enum EnemyType { Grunt, Caster, Brute, Boss, Shield, Gunner, Agent, Drone, Medic }

    /// <summary>Physics layers used by the arena (indices are set up by the scene builder).</summary>
    public static class Layers
    {
        public const int Default = 0, Water = 4, Environment = 8, Debris = 9, Floor = 10;
        public static int CameraBlockers => 1 << Environment;
    }

    /// <summary>One wave of robots, in spawn order.</summary>
    public sealed class Wave
    {
        public EnemyType[] enemies;
        public string title, subtitle;
        public float interval = 0.45f;
        public int maxAlive = 10;
        public bool boss;

        public Wave(string title, string subtitle, params EnemyType[] enemies)
        {
            this.title = title; this.subtitle = subtitle; this.enemies = enemies;
            foreach (var e in enemies) if (e == EnemyType.Boss) boss = true;
        }
    }

    /// <summary>
    /// Owns the fight: waves (scripted in story, generated in arcade), score, the FreeFlow combo and special-combo
    /// meter, the Arkham-style attack director, time control (hit-stop / slow-mo / cinematics / pause), haptics,
    /// the arena shape, and the events HALCYON comments on.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }
        public const int SpecialCost = 8;

        [Header("Scene")]
        public PlayerController player;
        public CameraRig cameraRig;
        public HUD hud;
        public Volume hitVolume;
        public Volume cinematicVolume;
        public Volume mainVolume;
        public Transform[] spawnPoints;
        public float arenaRadius = 16.2f;
        public ArenaEnvironment environment;
        public Weather weather;
        public Light moon;
        public GameObject rooftopRoot;
        public int builderVersion;

        [Header("Characters")]
        public GameObject robotModel;
        public RuntimeAnimatorController characterController;
        public Material robotMaterial;
        public Material suitMaterial;
        public Material armorPaintMaterial;

        [Header("FX")]
        public Material glowMaterial;
        public Material trailMaterial;
        public Material metalMaterial;
        public Material spiderSenseMaterial;
        [Tooltip("URP Lit template for generated city materials (normal/metal/occlusion/emission variants).")]
        public Material cityMaterial;
        public Material wetMaterial;
        public Material holoMaterial;
        public Material rainMaterial;
        public Material beamMaterial;
        public Material clothMaterial;

        [Header("Audio")]
        public AudioClip music;
        public AudioClip pickupClip;
        public AudioClip[] footsteps;

        public readonly List<Enemy> enemies = new List<Enemy>();
        public readonly List<Projectile> projectiles = new List<Projectile>();
        public readonly List<HealthOrb> orbs = new List<HealthOrb>();

        // ---------------------------------------------------------------- state

        public GameMode Mode { get; set; } = GameMode.Title;
        public bool Paused { get; private set; }
        public int Wave { get; private set; }
        public long Score { get; private set; }
        public int Combo { get; private set; }
        public int BestCombo { get; private set; }
        public int Special { get; private set; }
        public int Kills { get; private set; }
        public bool IsGameOver { get; private set; }
        public float RunTime { get; private set; }
        public Enemy Boss { get; set; }
        public int PendingSpawns => spawnQueue.Count;
        public float SlowMoScale => slowMoScale;
        public float ComboTimer01 => Mathf.Clamp01(comboTimer / ComboWindow);
        public bool SpecialReady => Special >= SpecialCost;
        public float CinematicWeight => cinematicWeight;
        public bool WaveActive => waveActive;
        public int WavesCleared { get; private set; }
        public bool HitThisWave => hitThisWave;
        /// <summary>Robots fight only while combat is on (it is off in cutscenes).</summary>
        public bool CombatOn { get; set; } = true;
        /// <summary>Arcade (and the title attract mode) roll the next wave by themselves.</summary>
        public bool AutoWaves { get; set; }
        public District ArcadeDistrict { get; set; } = District.Rooftop;
        /// <summary>Caps how many robots may swing at once (0 = the director decides). The story's tutorials use it.</summary>
        public int MaxAttackers { get; set; }
        public ArenaShape Arena => arena;
        public int SpawnStyle => spawnStyle;
        public DistrictManager Districts => districts;
        public IReadOnlyList<AnchorSpec> Anchors => districts != null && districts.Current != null ? (IReadOnlyList<AnchorSpec>)districts.Current.anchors : noAnchors;

        // ---------------------------------------------------------------- events (HALCYON's commentary, the story)

        public System.Action<Enemy> OnKill;
        public System.Action OnPlayerHurt;
        public System.Action OnPlayerDeath;
        public System.Action<int> OnWaveCleared;
        public System.Action<string> OnMove;
        public System.Action<int> OnCombo;

        const float ComboWindow = 2.4f;
        const int MaxAliveCap = 14;

        readonly Queue<EnemyType> spawnQueue = new Queue<EnemyType>();
        readonly List<Enemy> tokenHolders = new List<Enemy>();
        readonly List<Enemy> shooters = new List<Enemy>();
        readonly HashSet<string> movesThisWave = new HashSet<string>();
        readonly List<KeyValuePair<float, Enemy>> candidates = new List<KeyValuePair<float, Enemy>>();
        readonly List<Vector3> spawnList = new List<Vector3>();
        static readonly List<AnchorSpec> noAnchors = new List<AnchorSpec>();
        ArenaShape arena;
        int spawnStyle;
        Wave currentWave, checkpointWave;
        Vector3 checkpointPos;
        float comboTimer, spawnTimer, waveBreakTimer, gameOverTimer, directorTimer;
        bool waveActive, hitThisWave;
        float hitStopTimer, slowMoTimer, slowMoScale = 1f, pulse, cinematicTimer, cinematicWeight;
        float rumbleTimer, rumbleLow, rumbleHigh;
        bool rumbling;
        float baseFixedDelta = 0.02f;
        int lastComboMilestone;
        Sfx sfx;
        DistrictManager districts;

        void Awake()
        {
            I = this;
            Settings.Load();
            baseFixedDelta = 0.02f;
            Application.runInBackground = true; // keep the arena alive (e.g. the attract demo) when the editor loses focus
            Time.timeScale = 1f;
            Time.fixedDeltaTime = baseFixedDelta;
            Physics.IgnoreLayerCollision(Layers.Debris, Layers.Default, true);
            Physics.IgnoreLayerCollision(Layers.Debris, Layers.Water, true);
            arena = ArenaShape.Circle(arenaRadius);
            if (spawnPoints != null) foreach (var sp in spawnPoints) if (sp) spawnList.Add(sp.position);
            FX.Init(this);
            sfx = GetComponent<Sfx>();
            if (sfx == null) sfx = gameObject.AddComponent<Sfx>();
            sfx.Init(this);
            districts = GetComponent<DistrictManager>();
            if (districts == null) districts = gameObject.AddComponent<DistrictManager>();
            if (GetComponent<GameFlow>() == null) gameObject.AddComponent<GameFlow>();
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = baseFixedDelta;
            AudioListener.pause = false;
            StopRumble();
            FX.Cleanup();
            Ambient.Clear();
            if (I == this) I = null;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) StopRumble();
        }

        // ---------------------------------------------------------------- arena

        public void SetArena(ArenaShape shape, List<Vector3> spawns, int style)
        {
            arena = shape;
            arenaRadius = shape.Extent;
            spawnStyle = style;
            spawnList.Clear();
            if (spawns != null) spawnList.AddRange(spawns);
            if (spawnList.Count == 0)
                for (int i = 0; i < 8; i++) spawnList.Add(shape.Edge(Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward, 0.74f));
        }

        public Vector3 ClampToArena(Vector3 p, float margin = 0f) => arena.Clamp(p, margin);

        // ---------------------------------------------------------------- run control

        /// <summary>Clears the field and resets the run (score, combo, player).</summary>
        public void ResetRun(Vector3 playerPos, float playerYaw = 0f)
        {
            ClearField();
            Wave = 0; Score = 0; Combo = 0; BestCombo = 0; Special = 0; Kills = 0; RunTime = 0f; WavesCleared = 0;
            IsGameOver = false; waveActive = false; waveBreakTimer = 2.2f; comboTimer = 0f; lastComboMilestone = 0;
            hitStopTimer = 0f; slowMoTimer = 0f; slowMoScale = 1f; pulse = 0f; cinematicTimer = 0f;
            currentWave = checkpointWave = null;
            checkpointPos = playerPos;
            player.ResetPlayer(playerPos, playerYaw);
            if (cameraRig) cameraRig.SnapToTarget();
            if (hud) hud.ResetHUD();
        }

        public void ClearField()
        {
            foreach (var e in enemies.ToArray()) if (e) Destroy(e.gameObject);
            foreach (var p in projectiles.ToArray()) if (p) Destroy(p.gameObject);
            foreach (var o in orbs.ToArray()) if (o) Destroy(o.gameObject);
            enemies.Clear(); projectiles.Clear(); orbs.Clear(); tokenHolders.Clear(); shooters.Clear(); spawnQueue.Clear();
            Boss = null;
            waveActive = false;
            Debris.ClearAll();
        }

        public void SetPaused(bool p)
        {
            Paused = p;
            if (p) StopRumble();
            AudioListener.pause = p;
        }

        void Update()
        {
            UpdateTime();
            UpdateRumble();
            if (Paused) return;
            float dt = Time.deltaTime;
            float udt = Time.unscaledDeltaTime;

            if (hitVolume)
            {
                pulse = Mathf.MoveTowards(pulse, 0f, udt * 2.6f);
                hitVolume.weight = pulse;
            }
            cinematicTimer -= udt;
            cinematicWeight = Mathf.MoveTowards(cinematicWeight, cinematicTimer > 0f ? 1f : 0f, udt * (cinematicTimer > 0f ? 6f : 2.5f));
            if (cinematicVolume) cinematicVolume.weight = cinematicWeight;

            if (Combo > 0)
            {
                comboTimer -= dt;
                if (comboTimer <= 0f) { Combo = 0; Special = 0; lastComboMilestone = 0; }
            }

            if (IsGameOver) { gameOverTimer += udt; return; }

            RunTime += dt;
            if (player.IsDead) return;
            if (CombatOn) UpdateDirector(dt);

            if (!waveActive)
            {
                if (AutoWaves)
                {
                    waveBreakTimer -= dt;
                    if (waveBreakTimer <= 0f) BeginWave(ArcadeWave(Wave + 1, ArcadeDistrict));
                }
            }
            else
            {
                spawnTimer -= dt;
                int maxAlive = currentWave != null ? Mathf.Min(MaxAliveCap, currentWave.maxAlive) : 10;
                if (spawnQueue.Count > 0 && spawnTimer <= 0f && enemies.Count < maxAlive)
                {
                    SpawnEnemy(spawnQueue.Dequeue());
                    spawnTimer = currentWave != null ? currentWave.interval : 0.45f;
                }
                if (spawnQueue.Count == 0 && enemies.Count == 0) WaveCleared();
            }
        }

        public float GameOverTime => gameOverTimer;

        public static bool RestartPressed()
        {
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            return (kb != null && (kb.rKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                || (gp != null && (gp.startButton.wasPressedThisFrame || gp.buttonSouth.wasPressedThisFrame));
        }

        // ---------------------------------------------------------------- waves

        public void BeginWave(Wave w)
        {
            Wave++;
            currentWave = w;
            checkpointWave = w;
            checkpointPos = player ? player.transform.position : Vector3.zero;
            waveActive = true;
            hitThisWave = false;
            movesThisWave.Clear();
            spawnTimer = 0.6f;
            directorTimer = 2f;
            spawnQueue.Clear();
            foreach (var t in w.enemies) spawnQueue.Enqueue(t);
            if (!string.IsNullOrEmpty(w.title)) hud.Banner(w.title, w.subtitle ?? "", w.boss ? new Color(1f, 0.35f, 0.15f) : new Color(0.6f, 0.82f, 1f));
            Sfx.Play(Sfx.I.drum, 0.9f, 1f, 0f);
            if (environment && environment.stormEnabled) environment.Lightning(w.boss ? 1.6f : 1f);
            if (Music.I) Music.I.SetIntensity(w.boss ? 3 : 2);
        }

        /// <summary>Arcade escalation, flavoured by the district (Auditors in the Construct, Enforcers in the streets...).</summary>
        public static Wave ArcadeWave(int n, District d)
        {
            var list = new List<EnemyType>();
            bool bossWave = n % 5 == 0;
            void Add(EnemyType t, int c) { for (int i = 0; i < c; i++) list.Add(t); }
            if (bossWave)
            {
                list.Add(EnemyType.Boss);
                Add(EnemyType.Grunt, 2 + n / 3);
                Add(EnemyType.Gunner, n / 5);
                Add(EnemyType.Caster, n / 10);
                Add(EnemyType.Drone, n / 10);
            }
            else
            {
                int grunts = 3 + n;
                int casters = n >= 2 ? n / 3 : 0;
                int shields = n >= 3 ? 1 + (n - 3) / 3 : 0;
                int brutes = n >= 4 ? (n - 2) / 3 : 0;
                int gunners = n >= 2 ? 1 + (n - 2) / 3 : 0;
                int agents = n >= 6 ? (n - 4) / 3 : 0;
                int drones = n >= 5 ? (n - 3) / 3 : 0;
                int medics = n >= 7 ? 1 + (n - 7) / 4 : 0;
                switch (d)
                {
                    case District.Streets: gunners += 1 + n / 4; grunts -= 1; break;
                    case District.Hotel: shields += 1; brutes += n >= 3 ? 1 : 0; break;
                    case District.Construct: agents += 1 + n / 3; grunts -= 1 + n / 3; break;
                    case District.Spire: drones += 1 + n / 4; break;
                }
                Add(EnemyType.Grunt, Mathf.Max(2, grunts));
                Add(EnemyType.Caster, casters);
                Add(EnemyType.Shield, shields);
                Add(EnemyType.Brute, brutes);
                Add(EnemyType.Gunner, gunners);
                Add(EnemyType.Agent, agents);
                Add(EnemyType.Drone, drones);
                Add(EnemyType.Medic, medics);
                if (list.Count > 28) list.RemoveRange(28, list.Count - 28);
                // shuffle, keeping a couple of melee robots up front so the wave opens with pressure
                for (int i = list.Count - 1; i > 2; i--)
                {
                    int j = Random.Range(2, i + 1);
                    (list[i], list[j]) = (list[j], list[i]);
                }
            }
            string sub = n == 1 ? "FREEFLOW: STRIKE  ·  COUNTER  ·  EVADE" : list.Count + " ROBOTS INCOMING";
            var w = new Wave(bossWave ? "BOSS WAVE" : "WAVE " + n, bossWave ? "THE IRON TYRANT DESCENDS" : sub, list.ToArray());
            w.maxAlive = Mathf.Min(MaxAliveCap, 7 + n / 2);
            w.interval = bossWave ? 0.55f : 0.42f;
            return w;
        }

        void WaveCleared()
        {
            waveActive = false;
            WavesCleared++;
            waveBreakTimer = 3.8f;
            float heal = Settings.WaveHeal;
            if (heal > 0f) player.Heal(heal);
            long bonus = (long)(500 * Wave * Settings.ScoreMultiplier);
            string sub = (heal > 0f ? "+" + heal + " HP   " : "") + "+" + bonus + " PTS";
            if (!hitThisWave)
            {
                long f = (long)(1000 * Wave * Settings.ScoreMultiplier);
                bonus += f;
                sub += "   FLAWLESS FREEFLOW +" + f;
            }
            if (movesThisWave.Count >= 5)
            {
                long v = (long)(120 * movesThisWave.Count * Wave * Settings.ScoreMultiplier);
                bonus += v;
                sub += "   VARIATION x" + movesThisWave.Count + " +" + v;
            }
            Score += bonus;
            hud.Banner("AREA RESTORED", sub, new Color(0.45f, 1f, 0.7f));
            Sfx.Play(Sfx.I.drum, 0.8f, 1.25f, 0f);
            if (Music.I) Music.I.SetIntensity(1);
            OnWaveCleared?.Invoke(WavesCleared);
        }

        /// <summary>Story checkpoint: clears the robots and restarts the wave the player died in.</summary>
        public void RetryCheckpoint()
        {
            ClearField();
            IsGameOver = false;
            Combo = 0; Special = 0;
            slowMoScale = 1f; slowMoTimer = 0f; cinematicTimer = 0f;
            player.ResetPlayer(arena.Clamp(checkpointPos, 1f), player.transform.eulerAngles.y);
            if (cameraRig) cameraRig.SnapToTarget();
            if (hud) hud.HideGameOver();
            Wave = Mathf.Max(0, Wave - 1);
            if (checkpointWave != null) BeginWave(checkpointWave);
        }

        public Enemy SpawnEnemy(EnemyType type)
        {
            Vector3 pos = Vector3.zero;
            Vector3 ppos = player.transform.position;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var sp = spawnList.Count > 0 ? spawnList[Random.Range(0, spawnList.Count)] : arena.Edge(Random.onUnitSphere, 0.7f);
                Vector2 jitter = Random.insideUnitCircle * 1.6f;
                pos = arena.Clamp(sp + new Vector3(jitter.x, 0f, jitter.y), 1.5f);
                pos.y = 0f;
                if ((pos - ppos).sqrMagnitude > 36f) break;
            }
            if (type == EnemyType.Boss)
            {
                pos = arena.Clamp(-ppos.normalized * 7f, 3f);
                if (ppos.sqrMagnitude < 1f) pos = new Vector3(0f, 0f, 7f);
            }
            var e = Enemy.Create(type, pos, this, spawnStyle);
            enemies.Add(e);
            if (type == EnemyType.Boss) Boss = e;
            return e;
        }

        // ---------------------------------------------------------------- attack director

        /// <summary>
        /// Arkham's secret sauce: robots don't all swing at once. Every beat the director picks a group of one to
        /// three melee robots (often synchronised, so multi-counters happen) and hands them attack tokens.
        /// </summary>
        void UpdateDirector(float dt)
        {
            tokenHolders.RemoveAll(x => x == null || x.IsDead || !x.HoldsToken);
            shooters.RemoveAll(x => x == null || x.IsDead || !x.HoldsShot);
            directorTimer -= dt;
            if (directorTimer > 0f || player.IsDead) return;
            int maxConcurrent = Mathf.Clamp(1 + (Wave + 1) / 2 + Settings.AggressionBonus, 1, 5);
            if (Mode == GameMode.Story) maxConcurrent = Mathf.Clamp(2 + WavesCleared / 3 + Settings.AggressionBonus, 1, 5);
            if (MaxAttackers > 0) maxConcurrent = Mathf.Min(maxConcurrent, MaxAttackers);
            int free = maxConcurrent - tokenHolders.Count;
            float pace = Mathf.Lerp(1.6f, 0.65f, Mathf.Clamp01((Wave - 1) / 10f)) * (Settings.difficulty == Difficulty.Easy ? 1.3f : Settings.difficulty == Difficulty.Knightmare ? 0.75f : 1f);
            if (free <= 0) { directorTimer = 0.25f; return; }

            float r = Random.value;
            int group = r < 0.55f ? 1 : r < 0.87f ? 2 : 3;
            if (Wave <= 1 || MaxAttackers == 1) group = 1;
            group = Mathf.Min(group, free);

            candidates.Clear();
            Vector3 pp = player.transform.position;
            foreach (var e in enemies)
            {
                if (e == null || !e.WantsToken) continue;
                if ((e.transform.position - pp).sqrMagnitude > 100f) continue;
                candidates.Add(new KeyValuePair<float, Enemy>(AttackPriority(e, pp), e)); // score once: a random comparer would break List.Sort
            }
            if (candidates.Count == 0) { directorTimer = 0.3f; return; }
            candidates.Sort((a, b) => a.Key.CompareTo(b.Key));
            float delay = 0f;
            for (int i = 0; i < group && i < candidates.Count; i++)
            {
                candidates[i].Value.GrantToken(delay);
                tokenHolders.Add(candidates[i].Value);
                delay += Random.Range(0.05f, 0.3f);
            }
            directorTimer = pace * Random.Range(0.8f, 1.25f) + (group - 1) * 0.35f;
        }

        // robots behind the player are favoured a little, so watching your back matters (Arkham's rear attacks)
        float AttackPriority(Enemy e, Vector3 pp)
        {
            Vector3 to = e.transform.position - pp;
            float behind = Vector3.Dot(to.normalized, player.transform.forward) < -0.3f ? -1.2f : 0f;
            return to.magnitude + Random.value * 2.5f - Mathf.Min(3f, Time.time - e.LastAttackTime) * 0.4f + behind;
        }

        public void ReleaseToken(Enemy e) => tokenHolders.Remove(e);

        /// <summary>Ranged robots ask before aiming, so the player is never shot from five directions at once.</summary>
        public bool TryReserveShot(Enemy e)
        {
            if (!CombatOn || player.IsDead) return false;
            shooters.RemoveAll(x => x == null || x.IsDead || !x.HoldsShot);
            int max = Mathf.Clamp(1 + Wave / 5 + Settings.AggressionBonus, 1, 3);
            if (shooters.Contains(e)) return true;
            if (shooters.Count >= max) return false;
            shooters.Add(e);
            return true;
        }

        public void ReleaseShot(Enemy e) => shooters.Remove(e);

        // ---------------------------------------------------------------- scoring

        public void RegisterHit(float damage)
        {
            Combo++;
            if (Special < SpecialCost)
            {
                Special++;
                if (Special == SpecialCost)
                {
                    hud.Popup("TAKEDOWN READY  [F]", new Color(1f, 0.85f, 0.35f));
                    Sfx.Play(Sfx.I.ting, 0.5f, 0.9f, 0f);
                }
            }
            comboTimer = ComboWindow;
            if (Combo > BestCombo) BestCombo = Combo;
            Score += (long)(damage * 10f * ComboMultiplier * Settings.ScoreMultiplier);
            hud.OnComboHit(Combo);
            int milestone = Combo >= 50 ? 50 : Combo >= 30 ? 30 : Combo >= 15 ? 15 : 0;
            if (milestone > lastComboMilestone) { lastComboMilestone = milestone; OnCombo?.Invoke(milestone); }
        }

        public void KeepComboAlive()
        {
            if (Combo > 0) comboTimer = ComboWindow;
        }

        public void ConsumeSpecial() => Special = 0;

        public void RegisterMove(string move)
        {
            movesThisWave.Add(move);
            OnMove?.Invoke(move);
        }

        public void NotifyPlayerHit()
        {
            hitThisWave = true;
            OnPlayerHurt?.Invoke();
        }

        public float ComboMultiplier => 1f + Mathf.Min(Combo, 60) * 0.05f;

        public void BreakCombo()
        {
            if (Combo >= 5) hud.Popup("COMBO BROKEN", new Color(1f, 0.3f, 0.3f));
            Combo = 0;
            Special = 0;
            lastComboMilestone = 0;
        }

        public void RegisterKill(Enemy e)
        {
            Kills++;
            Score += (long)(e.scoreValue * ComboMultiplier * Settings.ScoreMultiplier);
            enemies.Remove(e);
            ReleaseToken(e);
            ReleaseShot(e);
            if (Boss == e) { Boss = null; hud.Banner("TYRANT SLAIN", "+" + (long)(e.scoreValue * ComboMultiplier) + " PTS", new Color(1f, 0.8f, 0.3f)); }

            bool lastOfWave = waveActive && spawnQueue.Count == 0 && enemies.Count == 0;
            if (lastOfWave || e.type == EnemyType.Boss)
            {
                // Arkham's final-blow camera: slow motion close on the last robot
                SlowMo(0.15f, 1.3f);
                cameraRig.Punch(-9f);
                Vector3 view = cameraRig.transform.position - e.Chest; view.y = 0f;
                view = view.sqrMagnitude > 0.01f ? view.normalized : Vector3.back;
                view = Quaternion.Euler(0f, Random.value < 0.5f ? 50f : -50f, 0f) * view + Vector3.up * 0.3f;
                Cinematic(e.Chest, view.normalized, e.type == EnemyType.Boss ? 7f : 3.8f, -8f, 1.35f);
                if (environment && environment.stormEnabled && e.type == EnemyType.Boss) environment.Lightning(2f);
            }
            OnKill?.Invoke(e);
        }

        public void OnPlayerDied()
        {
            SlowMo(0.25f, 1.6f);
            IsGameOver = true;
            gameOverTimer = 0f;
            hud.ShowGameOver(Mode);
            Cinematic(player.Chest, (Vector3.up * 0.8f - cameraRig.PlanarForward).normalized, 5f, -5f, 2.5f);
            if (Music.I) Music.I.SetIntensity(0);
            OnPlayerDeath?.Invoke();
        }

        /// <summary>Camera shot + letterbox + cinematic grade (depth of field) for takedowns and final blows.</summary>
        public void Cinematic(Vector3 focus, Vector3 viewDir, float distance, float fovDelta, float seconds)
        {
            if (cameraRig) cameraRig.Cinematic(focus, viewDir, distance, fovDelta, seconds);
            cinematicTimer = Mathf.Max(cinematicTimer, seconds);
            if (hud) hud.Letterbox(seconds);
        }

        /// <summary>Holds the cinematic grade (depth of field, letterbox) for scripted scenes.</summary>
        public void CinematicGrade(float seconds)
        {
            cinematicTimer = Mathf.Max(cinematicTimer, seconds);
            if (hud) hud.Letterbox(seconds);
        }

        // ---------------------------------------------------------------- time control

        public void HitStop(float seconds) => hitStopTimer = Mathf.Max(hitStopTimer, seconds);

        public void SlowMo(float scale, float realSeconds)
        {
            slowMoScale = Mathf.Min(scale, slowMoTimer > 0f ? slowMoScale : 1f);
            slowMoTimer = Mathf.Max(slowMoTimer, realSeconds);
        }

        public void CancelSlowMo() { slowMoTimer = 0f; slowMoScale = 1f; }

        public void Pulse(float amount) => pulse = Mathf.Clamp01(Mathf.Max(pulse, amount));

        void UpdateTime()
        {
            if (Paused)
            {
                Time.timeScale = 0f;
                return;
            }
            float udt = Time.unscaledDeltaTime;
            if (slowMoTimer > 0f)
            {
                slowMoTimer -= udt;
                if (slowMoTimer <= 0f) slowMoScale = 1f;
            }
            float target = slowMoScale;
            if (player && player.FocusScale < target) target = player.FocusScale;
            if (hitStopTimer > 0f)
            {
                hitStopTimer -= udt;
                target = 0.03f;
            }
            Time.timeScale = target;
            Time.fixedDeltaTime = baseFixedDelta * Mathf.Max(target, 0.05f);
        }

        // ---------------------------------------------------------------- haptics

        public void Rumble(float low, float high, float seconds)
        {
            rumbleLow = Mathf.Max(rumbleLow * (rumbleTimer > 0f ? 1f : 0f), low);
            rumbleHigh = Mathf.Max(rumbleHigh * (rumbleTimer > 0f ? 1f : 0f), high);
            rumbleTimer = Mathf.Max(rumbleTimer, seconds);
        }

        void UpdateRumble()
        {
            var gp = Gamepad.current;
            if (gp == null) return;
            if (rumbleTimer > 0f && !Paused)
            {
                rumbleTimer -= Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(rumbleTimer * 6f);
                gp.SetMotorSpeeds(rumbleLow * k, rumbleHigh * k);
                rumbling = true;
                if (rumbleTimer <= 0f) StopRumble();
            }
        }

        void StopRumble()
        {
            rumbleTimer = 0f;
            rumbleLow = rumbleHigh = 0f;
            if (!rumbling) return;
            rumbling = false;
            var gp = Gamepad.current;
            if (gp != null) gp.ResetHaptics();
        }

        // ---------------------------------------------------------------- helpers

        public Enemy NearestEnemy(Vector3 from, float maxDist, bool targetableOnly = true)
        {
            Enemy best = null;
            float bestD = maxDist * maxDist;
            foreach (var e in enemies)
            {
                if (e == null || (targetableOnly && !e.Targetable)) continue;
                float d = (e.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        /// <summary>The closest environmental-takedown anchor within reach of a point (Arkham's wall slams).</summary>
        public bool NearestAnchor(Vector3 p, float reach, out AnchorSpec anchor)
        {
            anchor = default;
            float best = reach * reach;
            bool found = false;
            foreach (var a in Anchors)
            {
                Vector3 d = a.pos - p; d.y = 0f;
                float m = d.sqrMagnitude;
                if (m < best) { best = m; anchor = a; found = true; }
            }
            return found;
        }
    }
}
