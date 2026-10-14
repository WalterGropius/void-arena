using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    public enum EnemyType { Grunt, Caster, Brute, Boss, Shield }

    /// <summary>Physics layers used by the arena (indices are set up by the scene builder).</summary>
    public static class Layers
    {
        public const int Default = 0, Water = 4, Environment = 8, Debris = 9, Floor = 10;
        public static int CameraBlockers => 1 << Environment;
    }

    /// <summary>
    /// Owns the run: waves, score, the FreeFlow combo and special-combo meter, the Arkham-style attack director
    /// (how many robots may swing at once), time control (hit-stop / slow-mo / cinematics), haptics, and shared assets.
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
        public Transform[] spawnPoints;
        public float arenaRadius = 16.2f;
        public ArenaEnvironment environment;
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

        [Header("Audio")]
        public AudioClip music;
        public AudioClip pickupClip;
        public AudioClip[] footsteps;

        public readonly List<Enemy> enemies = new List<Enemy>();
        public readonly List<Projectile> projectiles = new List<Projectile>();
        public readonly List<HealthOrb> orbs = new List<HealthOrb>();

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

        const float ComboWindow = 2.4f;
        const int MaxAlive = 12;

        readonly Queue<EnemyType> spawnQueue = new Queue<EnemyType>();
        readonly List<Enemy> tokenHolders = new List<Enemy>();
        readonly HashSet<string> movesThisWave = new HashSet<string>();
        readonly List<KeyValuePair<float, Enemy>> candidates = new List<KeyValuePair<float, Enemy>>();
        float comboTimer, spawnTimer, waveBreakTimer, gameOverTimer, directorTimer;
        bool waveActive, hitThisWave;
        float hitStopTimer, slowMoTimer, slowMoScale = 1f, pulse, cinematicTimer, cinematicWeight;
        float rumbleTimer, rumbleLow, rumbleHigh;
        bool rumbling;
        float baseFixedDelta = 0.02f;
        Sfx sfx;

        void Awake()
        {
            I = this;
            baseFixedDelta = 0.02f;
            Application.runInBackground = true; // keep the arena alive (e.g. autopilot demo) when the editor loses focus
            Time.timeScale = 1f;
            Time.fixedDeltaTime = baseFixedDelta;
            Physics.IgnoreLayerCollision(Layers.Debris, Layers.Default, true);
            Physics.IgnoreLayerCollision(Layers.Debris, Layers.Water, true);
            FX.Init(this);
            sfx = GetComponent<Sfx>();
            if (sfx == null) sfx = gameObject.AddComponent<Sfx>();
            sfx.Init(this);
        }

        void Start()
        {
            ResetRun();
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = baseFixedDelta;
            StopRumble();
            FX.Cleanup();
            if (I == this) I = null;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) StopRumble();
        }

        public void ResetRun()
        {
            foreach (var e in enemies.ToArray()) if (e) Destroy(e.gameObject);
            foreach (var p in projectiles.ToArray()) if (p) Destroy(p.gameObject);
            foreach (var o in orbs.ToArray()) if (o) Destroy(o.gameObject);
            enemies.Clear(); projectiles.Clear(); orbs.Clear(); tokenHolders.Clear(); spawnQueue.Clear();
            Boss = null;
            Wave = 0; Score = 0; Combo = 0; BestCombo = 0; Special = 0; Kills = 0; RunTime = 0f;
            IsGameOver = false; waveActive = false; waveBreakTimer = 1.8f; comboTimer = 0f;
            hitStopTimer = 0f; slowMoTimer = 0f; slowMoScale = 1f; pulse = 0f; cinematicTimer = 0f;
            player.ResetPlayer(Vector3.zero);
            if (cameraRig) cameraRig.SnapToTarget();
            if (hud) hud.ResetHUD();
        }

        void Update()
        {
            UpdateTime();
            UpdateRumble();
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
                if (comboTimer <= 0f) { Combo = 0; Special = 0; }
            }

            if (IsGameOver)
            {
                gameOverTimer += udt;
                if (gameOverTimer > 1.2f && RestartPressed()) ResetRun();
                else if (player.autopilot && gameOverTimer > 5f) ResetRun(); // attract mode: the demo keeps itself running
                return;
            }

            RunTime += dt;
            if (player.IsDead) return;
            UpdateDirector(dt);

            if (!waveActive)
            {
                waveBreakTimer -= dt;
                if (waveBreakTimer <= 0f) StartWave(Wave + 1);
            }
            else
            {
                spawnTimer -= dt;
                if (spawnQueue.Count > 0 && spawnTimer <= 0f && enemies.Count < MaxAlive)
                {
                    SpawnEnemy(spawnQueue.Dequeue());
                    spawnTimer = Wave % 5 == 0 ? 0.55f : 0.42f;
                }
                if (spawnQueue.Count == 0 && enemies.Count == 0) WaveCleared();
            }
        }

        static bool RestartPressed()
        {
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            return (kb != null && (kb.rKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                || (gp != null && (gp.startButton.wasPressedThisFrame || gp.buttonSouth.wasPressedThisFrame));
        }

        // ---------------------------------------------------------------- waves

        void StartWave(int n)
        {
            Wave = n;
            waveActive = true;
            hitThisWave = false;
            movesThisWave.Clear();
            spawnTimer = 0.6f;
            directorTimer = 2.2f;
            spawnQueue.Clear();
            var list = new List<EnemyType>();
            bool bossWave = n % 5 == 0;
            if (bossWave)
            {
                list.Add(EnemyType.Boss);
                for (int i = 0; i < 2 + n / 2; i++) list.Add(EnemyType.Grunt);
                for (int i = 0; i < n / 5; i++) list.Add(EnemyType.Caster);
                for (int i = 0; i < n / 10; i++) list.Add(EnemyType.Shield);
            }
            else
            {
                int grunts = 3 + n;
                int casters = n >= 2 ? n / 2 : 0;
                int shields = n >= 3 ? 1 + (n - 3) / 2 : 0;
                int brutes = n >= 4 ? (n - 2) / 2 : 0;
                for (int i = 0; i < grunts; i++) list.Add(EnemyType.Grunt);
                for (int i = 0; i < casters; i++) list.Add(EnemyType.Caster);
                for (int i = 0; i < shields; i++) list.Add(EnemyType.Shield);
                for (int i = 0; i < brutes; i++) list.Add(EnemyType.Brute);
                // shuffle, but keep a couple of grunts up front so the wave opens with melee pressure
                for (int i = list.Count - 1; i > 2; i--)
                {
                    int j = Random.Range(2, i + 1);
                    (list[i], list[j]) = (list[j], list[i]);
                }
            }
            foreach (var t in list) spawnQueue.Enqueue(t);

            if (bossWave) hud.Banner("BOSS WAVE", "THE IRON TYRANT DESCENDS", new Color(1f, 0.35f, 0.15f));
            else hud.Banner("WAVE " + n, n == 1 ? "FREEFLOW: STRIKE · COUNTER · EVADE" : list.Count + " ROBOTS INCOMING", new Color(0.6f, 0.82f, 1f));
            Sfx.Play(Sfx.I.drum, 0.9f, 1f, 0f);
            if (environment) environment.Lightning(bossWave ? 1.6f : 1f);
        }

        void WaveCleared()
        {
            waveActive = false;
            waveBreakTimer = 3.8f;
            player.Heal(25f);
            long bonus = 500 * Wave;
            string sub = "+25 HP   +" + bonus + " PTS";
            if (!hitThisWave)
            {
                long f = 1000 * Wave;
                bonus += f;
                sub += "   FLAWLESS FREEFLOW +" + f;
            }
            if (movesThisWave.Count >= 5)
            {
                long v = 120 * movesThisWave.Count * Wave;
                bonus += v;
                sub += "   VARIATION x" + movesThisWave.Count + " +" + v;
            }
            Score += bonus;
            hud.Banner("WAVE CLEARED", sub, new Color(0.45f, 1f, 0.7f));
            Sfx.Play(Sfx.I.drum, 0.8f, 1.25f, 0f);
        }

        public Enemy SpawnEnemy(EnemyType type)
        {
            Vector3 pos = Vector3.zero;
            Vector3 ppos = player.transform.position;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var sp = spawnPoints[Random.Range(0, spawnPoints.Length)];
                Vector2 jitter = Random.insideUnitCircle * 1.6f;
                pos = sp.position + new Vector3(jitter.x, 0f, jitter.y);
                pos.y = 0f;
                if ((pos - ppos).sqrMagnitude > 36f) break;
            }
            if (type == EnemyType.Boss)
            {
                pos = -ppos.normalized * 7f;
                if (ppos.sqrMagnitude < 1f) pos = new Vector3(0f, 0f, 7f);
            }
            var e = Enemy.Create(type, pos, this);
            enemies.Add(e);
            if (type == EnemyType.Boss) Boss = e;
            return e;
        }

        // ---------------------------------------------------------------- attack director

        /// <summary>
        /// Arkham's secret sauce: robots don't all swing at once. Every beat, the director picks a group of one to
        /// three melee robots (often synchronised, so multi-counters happen) and hands them attack tokens.
        /// </summary>
        void UpdateDirector(float dt)
        {
            tokenHolders.RemoveAll(x => x == null || x.IsDead || !x.HoldsToken);
            directorTimer -= dt;
            if (directorTimer > 0f || player.IsDead) return;
            int maxConcurrent = Mathf.Min(1 + (Wave + 1) / 2, 4);
            int free = maxConcurrent - tokenHolders.Count;
            float pace = Mathf.Lerp(1.6f, 0.65f, Mathf.Clamp01((Wave - 1) / 10f));
            if (free <= 0) { directorTimer = 0.25f; return; }

            float r = Random.value;
            int group = r < 0.55f ? 1 : r < 0.87f ? 2 : 3;
            if (Wave <= 1) group = 1;
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

        static float AttackPriority(Enemy e, Vector3 pp) => (e.transform.position - pp).magnitude + Random.value * 2.5f - Mathf.Min(3f, Time.time - e.LastAttackTime) * 0.4f;

        public void ReleaseToken(Enemy e) => tokenHolders.Remove(e);

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
            Score += (long)(damage * 10f * ComboMultiplier);
            hud.OnComboHit(Combo);
        }

        public void KeepComboAlive()
        {
            if (Combo > 0) comboTimer = ComboWindow;
        }

        public void ConsumeSpecial() => Special = 0;

        public void RegisterMove(string move) => movesThisWave.Add(move);

        public void NotifyPlayerHit() => hitThisWave = true;

        public float ComboMultiplier => 1f + Mathf.Min(Combo, 60) * 0.05f;

        public void BreakCombo()
        {
            if (Combo >= 5) hud.Popup("COMBO BROKEN", new Color(1f, 0.3f, 0.3f));
            Combo = 0;
            Special = 0;
        }

        public void RegisterKill(Enemy e)
        {
            Kills++;
            Score += (long)(e.scoreValue * ComboMultiplier);
            enemies.Remove(e);
            ReleaseToken(e);
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
                if (environment && e.type == EnemyType.Boss) environment.Lightning(2f);
            }
        }

        public void OnPlayerDied()
        {
            SlowMo(0.25f, 1.6f);
            IsGameOver = true;
            gameOverTimer = 0f;
            hud.ShowGameOver();
            Cinematic(player.Chest, (Vector3.up * 0.8f - cameraRig.PlanarForward).normalized, 5f, -5f, 2.5f);
        }

        /// <summary>Camera shot + letterbox + cinematic grade (depth of field) for takedowns and final blows.</summary>
        public void Cinematic(Vector3 focus, Vector3 viewDir, float distance, float fovDelta, float seconds)
        {
            if (cameraRig) cameraRig.Cinematic(focus, viewDir, distance, fovDelta, seconds);
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

        public void Pulse(float amount) => pulse = Mathf.Clamp01(Mathf.Max(pulse, amount));

        void UpdateTime()
        {
            float udt = Time.unscaledDeltaTime;
            if (slowMoTimer > 0f)
            {
                slowMoTimer -= udt;
                if (slowMoTimer <= 0f) slowMoScale = 1f;
            }
            float target = slowMoScale;
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
            if (rumbleTimer > 0f)
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

        public Vector3 ClampToArena(Vector3 p, float margin = 0f)
        {
            Vector3 flat = new Vector3(p.x, 0f, p.z);
            float max = arenaRadius - margin;
            if (flat.sqrMagnitude > max * max)
            {
                flat = flat.normalized * max;
                p.x = flat.x; p.z = flat.z;
            }
            return p;
        }

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
    }
}
