using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    public enum EnemyType { Grunt, Caster, Brute, Boss }

    /// <summary>
    /// Owns the run: waves, score, combo, time control (hit-stop / slow-mo), and the shared asset references.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        [Header("Scene")]
        public PlayerController player;
        public CameraRig cameraRig;
        public HUD hud;
        public Volume hitVolume;
        public Transform[] spawnPoints;
        public float arenaRadius = 16.2f;

        [Header("Characters")]
        public GameObject robotModel;
        public RuntimeAnimatorController characterController;
        public Material robotMaterial;

        [Header("FX")]
        public Material glowMaterial;
        public Material trailMaterial;
        public Material metalMaterial;

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
        public int Kills { get; private set; }
        public bool IsGameOver { get; private set; }
        public float RunTime { get; private set; }
        public Enemy Boss { get; set; }
        public int PendingSpawns => spawnQueue.Count;
        public float SlowMoScale => slowMoScale;
        public float ComboTimer01 => Mathf.Clamp01(comboTimer / ComboWindow);

        const float ComboWindow = 2.6f;
        const int MaxAlive = 13;

        readonly Queue<EnemyType> spawnQueue = new Queue<EnemyType>();
        readonly List<Enemy> tokenHolders = new List<Enemy>();
        float comboTimer, spawnTimer, waveBreakTimer, gameOverTimer;
        bool waveActive;
        float hitStopTimer, slowMoTimer, slowMoScale = 1f, pulse;
        float baseFixedDelta = 0.02f;
        Sfx sfx;

        void Awake()
        {
            I = this;
            baseFixedDelta = 0.02f;
            Application.runInBackground = true; // keep the arena alive (e.g. autopilot demo) when the editor loses focus
            Time.timeScale = 1f;
            Time.fixedDeltaTime = baseFixedDelta;
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
            FX.Cleanup();
            if (I == this) I = null;
        }

        public void ResetRun()
        {
            foreach (var e in enemies.ToArray()) if (e) Destroy(e.gameObject);
            foreach (var p in projectiles.ToArray()) if (p) Destroy(p.gameObject);
            foreach (var o in orbs.ToArray()) if (o) Destroy(o.gameObject);
            enemies.Clear(); projectiles.Clear(); orbs.Clear(); tokenHolders.Clear(); spawnQueue.Clear();
            Boss = null;
            Wave = 0; Score = 0; Combo = 0; BestCombo = 0; Kills = 0; RunTime = 0f;
            IsGameOver = false; waveActive = false; waveBreakTimer = 1.6f; comboTimer = 0f;
            hitStopTimer = 0f; slowMoTimer = 0f; slowMoScale = 1f; pulse = 0f;
            player.ResetPlayer(Vector3.zero);
            if (cameraRig) cameraRig.SnapToTarget();
            if (hud) hud.ResetHUD();
        }

        void Update()
        {
            UpdateTime();
            float dt = Time.deltaTime;

            if (hitVolume)
            {
                pulse = Mathf.MoveTowards(pulse, 0f, Time.unscaledDeltaTime * 2.6f);
                hitVolume.weight = pulse;
            }

            if (Combo > 0)
            {
                comboTimer -= dt;
                if (comboTimer <= 0f) Combo = 0;
            }

            if (IsGameOver)
            {
                gameOverTimer += Time.unscaledDeltaTime;
                if (gameOverTimer > 1.2f && RestartPressed()) ResetRun();
                else if (player.autopilot && gameOverTimer > 5f) ResetRun(); // attract mode: the demo keeps itself running
                return;
            }

            RunTime += dt;
            if (player.IsDead) return;

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
            spawnTimer = 0.6f;
            spawnQueue.Clear();
            var list = new List<EnemyType>();
            bool bossWave = n % 5 == 0;
            if (bossWave)
            {
                list.Add(EnemyType.Boss);
                for (int i = 0; i < 2 + n / 2; i++) list.Add(EnemyType.Grunt);
                for (int i = 0; i < n / 5; i++) list.Add(EnemyType.Caster);
            }
            else
            {
                int grunts = 3 + n;
                int casters = n >= 2 ? n / 2 : 0;
                int brutes = n >= 3 ? (n - 1) / 2 : 0;
                for (int i = 0; i < grunts; i++) list.Add(EnemyType.Grunt);
                for (int i = 0; i < casters; i++) list.Add(EnemyType.Caster);
                for (int i = 0; i < brutes; i++) list.Add(EnemyType.Brute);
                // shuffle, but keep a couple of grunts up front so the wave opens with melee pressure
                for (int i = list.Count - 1; i > 2; i--)
                {
                    int j = Random.Range(2, i + 1);
                    (list[i], list[j]) = (list[j], list[i]);
                }
            }
            foreach (var t in list) spawnQueue.Enqueue(t);

            if (bossWave) hud.Banner("BOSS WAVE", "THE IRON TYRANT AWAKENS", new Color(1f, 0.35f, 0.15f));
            else hud.Banner("WAVE " + n, n == 1 ? "SURVIVE THE VOID ARENA" : list.Count + " ENEMIES INCOMING", new Color(0.75f, 0.55f, 1f));
            Sfx.Play(Sfx.I.drum, 0.9f, 1f, 0f);
        }

        void WaveCleared()
        {
            waveActive = false;
            waveBreakTimer = 3.6f;
            player.Heal(25f);
            Score += 500 * Wave;
            hud.Banner("WAVE CLEARED", "+25 HP   +" + (500 * Wave) + " PTS", new Color(0.45f, 1f, 0.7f));
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
            if (type == EnemyType.Boss) pos = -ppos.normalized * 7f;
            var e = Enemy.Create(type, pos, this);
            enemies.Add(e);
            if (type == EnemyType.Boss) Boss = e;
            return e;
        }

        // ---------------------------------------------------------------- tokens (limit simultaneous melee attackers)

        public bool RequestToken(Enemy e)
        {
            tokenHolders.RemoveAll(x => x == null || x.IsDead);
            if (tokenHolders.Contains(e)) return true;
            if (tokenHolders.Count < 2 + Wave / 3) { tokenHolders.Add(e); return true; }
            return false;
        }

        public void ReleaseToken(Enemy e) => tokenHolders.Remove(e);

        // ---------------------------------------------------------------- scoring

        public void RegisterHit(float damage)
        {
            Combo++;
            comboTimer = ComboWindow;
            if (Combo > BestCombo) BestCombo = Combo;
            Score += (long)(damage * 10f * ComboMultiplier);
            hud.OnComboHit(Combo);
        }

        public float ComboMultiplier => 1f + Mathf.Min(Combo, 60) * 0.05f;

        public void BreakCombo()
        {
            if (Combo >= 5) hud.Popup("COMBO BROKEN", new Color(1f, 0.3f, 0.3f));
            Combo = 0;
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
                SlowMo(0.18f, 1.1f);
                cameraRig.Punch(-9f);
            }
        }

        public void OnPlayerDied()
        {
            SlowMo(0.25f, 1.6f);
            IsGameOver = true;
            gameOverTimer = 0f;
            hud.ShowGameOver();
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
