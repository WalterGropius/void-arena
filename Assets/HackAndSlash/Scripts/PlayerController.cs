using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HackAndSlash
{
    /// <summary>
    /// The Void Knight: 3-hit sword combo, spin attack, dash with perfect-dodge slow-mo,
    /// the Void Nova ultimate, projectile parries, and an optional autopilot for demos.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public enum State { Move, Attack, Dash, Spin, Nova, Dead }

        struct AttackDef
        {
            public float total, activeStart, activeEnd, damage, range, lunge, knockback, shake, hitstop;
            public int kind; // 0 = slash right->left, 1 = backhand left->right, 2 = overhead smash
        }

        static readonly AttackDef[] ComboChain =
        {
            new AttackDef { total = 0.42f, activeStart = 0.10f, activeEnd = 0.21f, damage = 12f, range = 2.9f, lunge = 1.3f, knockback = 4.5f, shake = 0.22f, hitstop = 0.055f, kind = 0 },
            new AttackDef { total = 0.42f, activeStart = 0.10f, activeEnd = 0.21f, damage = 14f, range = 2.9f, lunge = 1.3f, knockback = 4.5f, shake = 0.26f, hitstop = 0.06f, kind = 1 },
            new AttackDef { total = 0.70f, activeStart = 0.22f, activeEnd = 0.32f, damage = 30f, range = 3.5f, lunge = 2.1f, knockback = 11f, shake = 0.6f, hitstop = 0.11f, kind = 2 },
        };

        const float SpinDuration = 0.62f;
        const float NovaSlamTime = 0.52f;
        const float NovaDuration = 0.95f;
        const float NovaRadius = 10.5f;

        [Header("Rig")]
        public Animator animator;
        public Transform model;
        public WeaponRig rig;
        public SkinnedMeshRenderer body;

        [Header("Tuning")]
        public float maxHealth = 100f;
        public float moveSpeed = 6.8f;
        public float dashSpeed = 22f;
        public float dashTime = 0.19f;
        public float dashCooldown = 0.42f;
        public float spinCooldown = 1.7f;
        public bool autopilot;

        public float Health { get; private set; }
        public float Rage { get; private set; }
        public bool IsDead => state == State.Dead;
        public State CurrentState => state;
        public bool NovaReady => Rage >= 100f;
        public float SpinReady01 => 1f - Mathf.Clamp01(spinCd / spinCooldown);
        public Vector3 Velocity => moveVel;
        public Vector3 Chest => transform.position + Vector3.up * 1.25f;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        static readonly int FreeFallId = Animator.StringToHash("FreeFall");
        static readonly int JumpId = Animator.StringToHash("Jump");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        CharacterController cc;
        GameManager gm;
        State state;
        float stateTime;
        Vector3 moveInput, moveVel, knock, facing = Vector3.forward, attackDir = Vector3.forward, dashDir;
        float vy;
        int comboIndex;
        bool queuedAttack, impactDone, whooshDone;
        float attackBuf, heavyBuf, dashBuf, novaBuf;
        float dashCd, spinCd, invuln, dashIframes, hurtFlash, afterImageTimer, perfectDodgeCd;
        bool dashPerfectUsed;
        float swingPrevAngle;
        float spinStartYaw;
        int spinRound;
        readonly HashSet<Enemy> hitThisSwing = new HashSet<Enemy>();
        readonly Dictionary<Enemy, int> spinHits = new Dictionary<Enemy, int>();
        Material bodyMat;

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (body)
            {
                bodyMat = body.material;
                bodyMat.EnableKeyword("_EMISSION");
            }
            if (rig) rig.InitTrail(new Color(0.35f, 1.6f, 3.6f, 1f), "PlayerTrail");
        }

        void OnDestroy()
        {
            if (bodyMat) Destroy(bodyMat);
        }

        public void ResetPlayer(Vector3 pos)
        {
            gm = GameManager.I;
            cc.enabled = false;
            transform.SetPositionAndRotation(pos, Quaternion.identity);
            cc.enabled = true;
            Health = maxHealth;
            Rage = 0f;
            state = State.Move;
            stateTime = 0f;
            moveVel = knock = Vector3.zero;
            vy = 0f;
            facing = Vector3.forward;
            comboIndex = 0;
            queuedAttack = false;
            attackBuf = heavyBuf = dashBuf = novaBuf = 0f;
            dashCd = spinCd = invuln = dashIframes = hurtFlash = 0f;
            animator.enabled = true;
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            rig.SetTrail(false);
            rig.rightHandWeight = 1f;
            rig.leftHandWeight = 0f;
            rig.lookWeight = 0f;
            rig.Snap(RestGrip(0f), RestDir());
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            Health = Mathf.Min(maxHealth, Health + amount);
            FX.Burst(Chest, new Color(0.4f, 1f, 0.5f), new Color(0.8f, 1f, 0.6f), 22, new Vector2(1.5f, 4f), new Vector2(0.08f, 0.18f), new Vector2(0.4f, 0.8f), -0.4f, false, 0.5f);
            if (gm && gm.hud) gm.hud.OnHeal();
        }

        public void AddRage(float amount)
        {
            if (IsDead) return;
            bool wasReady = NovaReady;
            Rage = Mathf.Min(100f, Rage + amount);
            if (!wasReady && NovaReady)
            {
                gm.hud.Popup("VOID NOVA READY  [Q]", new Color(0.75f, 0.5f, 1f));
                Sfx.Play(Sfx.I.charge, 0.5f, 1.3f, 0f);
            }
        }

        // ------------------------------------------------------------------ main loop

        void Update()
        {
            if (gm == null) gm = GameManager.I;
            if (gm == null) return;
            float dt = Time.deltaTime;
            float udt = Time.unscaledDeltaTime;

            ReadInput(udt);
            if (autopilot && !IsDead && !gm.IsGameOver) Autopilot();

            dashCd -= dt; spinCd -= dt; invuln -= dt; dashIframes -= dt;
            perfectDodgeCd -= udt;
            stateTime += dt;

            Vector3 planar = Vector3.zero;
            switch (state)
            {
                case State.Move: planar = UpdateMove(dt); break;
                case State.Attack: planar = UpdateAttack(dt); break;
                case State.Dash: planar = UpdateDash(dt); break;
                case State.Spin: planar = UpdateSpin(dt); break;
                case State.Nova: planar = UpdateNova(); break;
                case State.Dead: UpdateDead(); break;
            }

            // integrate movement
            knock = Vector3.MoveTowards(knock, Vector3.zero, 30f * dt);
            vy = cc.isGrounded ? -3f : vy - 30f * dt;
            if (state != State.Dead) cc.Move((planar + knock) * dt + Vector3.up * (vy * dt));
            Vector3 clamped = gm.ClampToArena(transform.position, 0.4f);
            if ((clamped - transform.position).sqrMagnitude > 1e-6f)
            {
                cc.enabled = false;
                transform.position = clamped;
                cc.enabled = true;
            }

            if (state != State.Spin && state != State.Dead && facing.sqrMagnitude > 0.001f)
            {
                float sharp = state == State.Move ? 16f : 40f;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(facing), 1f - Mathf.Exp(-sharp * dt));
            }

            if (animator.enabled)
            {
                float animSpeed;
                if (state == State.Move) animSpeed = new Vector3(moveVel.x, 0f, moveVel.z).magnitude;
                else if (state == State.Dash) animSpeed = 6f;
                else if (state == State.Spin) animSpeed = Mathf.Min(planar.magnitude, 2f);
                else animSpeed = Mathf.Min(planar.magnitude * 0.6f, 2.5f);
                animator.SetFloat(SpeedId, animSpeed, 0.06f, Mathf.Max(dt, 0.0001f));
                animator.SetFloat(MotionSpeedId, 1f);
                animator.SetBool(GroundedId, true);
                animator.SetBool(FreeFallId, false);
                animator.SetBool(JumpId, false);
            }

            rig.Tick(dt);
            UpdateBodyGlow(dt);
        }

        // ------------------------------------------------------------------ input

        void ReadInput(float udt)
        {
            attackBuf -= udt; heavyBuf -= udt; dashBuf -= udt; novaBuf -= udt;
            Vector2 mv = Vector2.zero;
            const float buffer = 0.24f;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) mv.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) mv.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) mv.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) mv.x -= 1f;
                if (kb.jKey.wasPressedThisFrame) attackBuf = buffer;
                if (kb.kKey.wasPressedThisFrame) heavyBuf = buffer;
                if (kb.spaceKey.wasPressedThisFrame || kb.leftShiftKey.wasPressedThisFrame) dashBuf = buffer;
                if (kb.qKey.wasPressedThisFrame || kb.lKey.wasPressedThisFrame) novaBuf = buffer;
                if (kb.pKey.wasPressedThisFrame)
                {
                    autopilot = !autopilot;
                    if (gm && gm.hud) gm.hud.Popup(autopilot ? "AUTOPILOT ENGAGED" : "MANUAL CONTROL", new Color(0.5f, 0.9f, 1f));
                }
            }
            var ms = Mouse.current;
            if (ms != null)
            {
                if (ms.leftButton.wasPressedThisFrame) attackBuf = buffer;
                if (ms.rightButton.wasPressedThisFrame) heavyBuf = buffer;
            }
            var gp = Gamepad.current;
            if (gp != null)
            {
                Vector2 stick = gp.leftStick.ReadValue();
                if (stick.sqrMagnitude > mv.sqrMagnitude) mv = stick;
                if (gp.buttonWest.wasPressedThisFrame) attackBuf = buffer;
                if (gp.buttonNorth.wasPressedThisFrame) heavyBuf = buffer;
                if (gp.buttonSouth.wasPressedThisFrame || gp.rightShoulder.wasPressedThisFrame) dashBuf = buffer;
                if (gp.rightTrigger.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame) novaBuf = buffer;
            }

            mv = Vector2.ClampMagnitude(mv, 1f);
            Vector3 camF = Vector3.forward, camR = Vector3.right;
            var cam = gm && gm.cameraRig ? gm.cameraRig.transform : null;
            if (cam)
            {
                camF = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                camR = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            }
            moveInput = camF * mv.y + camR * mv.x;
        }

        // ------------------------------------------------------------------ states

        bool TryStartAction()
        {
            if (novaBuf > 0f && NovaReady) { StartNova(); return true; }
            if (dashBuf > 0f && dashCd <= 0f) { StartDash(); return true; }
            if (heavyBuf > 0f && spinCd <= 0f) { StartSpin(); return true; }
            if (attackBuf > 0f) { StartAttack(0); return true; }
            return false;
        }

        Vector3 UpdateMove(float dt)
        {
            if (TryStartAction()) return Vector3.zero;
            Vector3 desired = moveInput * moveSpeed;
            moveVel = Vector3.MoveTowards(moveVel, desired, 70f * dt);
            if (moveInput.sqrMagnitude > 0.01f) facing = moveInput.normalized;
            rig.SetTarget(RestGrip(moveVel.magnitude), RestDir(), 14f);
            rig.leftHandWeight = Mathf.MoveTowards(rig.leftHandWeight, 0f, dt * 5f);
            rig.lookWeight = Mathf.MoveTowards(rig.lookWeight, 0f, dt * 4f);
            return moveVel;
        }

        void StartAttack(int index)
        {
            comboIndex = index;
            state = State.Attack;
            stateTime = 0f;
            queuedAttack = false;
            attackBuf = 0f;
            impactDone = false;
            whooshDone = false;
            hitThisSwing.Clear();
            attackDir = ChooseAttackDir();
            facing = attackDir;
            transform.rotation = Quaternion.LookRotation(attackDir);
            moveVel = Vector3.zero;
            var a = ComboChain[index];
            swingPrevAngle = a.kind == 0 ? 118f : -112f;
        }

        Vector3 UpdateAttack(float dt)
        {
            var a = ComboChain[comboIndex];
            float t = stateTime;

            if (attackBuf > 0f && t > 0.05f) { queuedAttack = true; attackBuf = 0f; }
            if (t >= a.activeEnd)
            {
                if (novaBuf > 0f && NovaReady) { StartNova(); return Vector3.zero; }
                if (dashBuf > 0f && dashCd <= 0f) { StartDash(); return Vector3.zero; }
                if (heavyBuf > 0f && spinCd <= 0f) { StartSpin(); return Vector3.zero; }
            }
            bool last = comboIndex == ComboChain.Length - 1;
            if (queuedAttack && !last && t >= a.activeEnd + 0.03f) { StartAttack(comboIndex + 1); return Vector3.zero; }
            if (queuedAttack && last && t >= a.total - 0.08f) { StartAttack(0); return Vector3.zero; }
            if (t >= a.total)
            {
                state = State.Move;
                rig.SetTrail(false);
                return Vector3.zero;
            }

            if (!whooshDone && t >= a.activeStart * 0.6f)
            {
                whooshDone = true;
                Sfx.Play(a.kind == 2 ? Sfx.I.swingHeavy : Sfx.I.swing, 0.7f, a.kind == 1 ? 1.12f : 1f);
            }

            UpdateSwingPose(a, t, dt);

            if (t >= a.activeStart && t <= a.activeEnd + 0.02f)
            {
                if (a.kind == 2) { if (!impactDone && t >= a.activeEnd) OverheadImpact(a); }
                else SweepHits(a);
                ParryProjectiles(2.8f, 110f);
            }

            Vector3 vel = Vector3.zero;
            if (t < a.activeEnd)
            {
                float speed = a.lunge * 2f / a.activeEnd * (1f - t / a.activeEnd);
                var near = gm.NearestEnemy(transform.position, 1.3f);
                if (near != null && Vector3.Dot((near.transform.position - transform.position).normalized, attackDir) > 0.5f) speed *= 0.15f;
                vel = attackDir * speed;
            }
            return vel;
        }

        void UpdateSwingPose(AttackDef a, float t, float dt)
        {
            float ws = a.activeStart, we = a.activeEnd;
            rig.lookWeight = Mathf.MoveTowards(rig.lookWeight, 0.85f, dt * 8f);
            rig.lookTargetWorld = Chest + attackDir * 4f;

            if (a.kind != 2)
            {
                float a0 = a.kind == 0 ? 118f : -112f;
                float a1 = a.kind == 0 ? -112f : 120f;
                float h0 = a.kind == 0 ? 1.34f : 1.4f;
                float h1 = a.kind == 0 ? 1.1f : 1.18f;
                rig.leftHandWeight = Mathf.MoveTowards(rig.leftHandWeight, 0f, dt * 6f);
                if (t < ws)
                {
                    rig.SetTarget(WeaponRig.HGrip(a0, h0), WeaponRig.HDir(a0, 0.2f), 40f);
                    rig.SetTrail(false);
                }
                else if (t <= we)
                {
                    float u = Mathf.InverseLerp(ws, we, t);
                    u = 1f - (1f - u) * (1f - u) * (1f - u);
                    float ang = Mathf.Lerp(a0, a1, u);
                    rig.Snap(WeaponRig.HGrip(ang, Mathf.Lerp(h0, h1, u)), WeaponRig.HDir(ang, Mathf.Lerp(0.15f, -0.12f, u)));
                    rig.SetTrail(true);
                }
                else
                {
                    if (t > we + 0.05f) rig.SetTrail(false);
                    if (t < we + 0.1f) rig.SetTarget(WeaponRig.HGrip(a1, h1), WeaponRig.HDir(a1, -0.12f), 12f);
                    else rig.SetTarget(RestGrip(0f), RestDir(), 10f);
                }
            }
            else
            {
                const float p0 = -52f, p1 = 140f;
                rig.leftHandWeight = Mathf.MoveTowards(rig.leftHandWeight, t < a.total - 0.15f ? 1f : 0f, dt * 8f);
                if (t < ws)
                {
                    rig.SetTarget(WeaponRig.VGrip(p0), WeaponRig.VDir(p0), 24f);
                    rig.SetTrail(false);
                }
                else if (t <= we)
                {
                    float u = Mathf.InverseLerp(ws, we, t);
                    u *= u;
                    float phi = Mathf.Lerp(p0, p1, u);
                    rig.Snap(WeaponRig.VGrip(phi), WeaponRig.VDir(phi));
                    rig.SetTrail(true);
                }
                else
                {
                    if (t > we + 0.06f) rig.SetTrail(false);
                    if (t < a.total - 0.16f) rig.SetTarget(WeaponRig.VGrip(p1), WeaponRig.VDir(p1), 20f);
                    else rig.SetTarget(RestGrip(0f), RestDir(), 10f);
                }
            }
        }

        void SweepHits(AttackDef a)
        {
            // the blade sweeps an arc; hit each enemy at the moment the blade crosses its bearing
            float ws = a.activeStart, we = a.activeEnd;
            float a0 = a.kind == 0 ? 118f : -112f;
            float a1 = a.kind == 0 ? -112f : 120f;
            float u = Mathf.Clamp01(Mathf.InverseLerp(ws, we, stateTime));
            u = 1f - (1f - u) * (1f - u) * (1f - u);
            float cur = Mathf.Lerp(a0, a1, u);
            float lo = Mathf.Min(swingPrevAngle, cur) - 12f, hi = Mathf.Max(swingPrevAngle, cur) + 12f;
            swingPrevAngle = cur;

            var list = gm.enemies;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (i >= list.Count) continue;
                var e = list[i];
                if (e == null || !e.Targetable || hitThisSwing.Contains(e)) continue;
                Vector3 to = e.transform.position - transform.position; to.y = 0f;
                float d = to.magnitude;
                if (d > a.range + e.Radius) continue;
                float bearing = Vector3.SignedAngle(attackDir, to, Vector3.up);
                if (d > 0.9f && (bearing < lo || bearing > hi)) continue;
                hitThisSwing.Add(e);
                HitEnemy(e, a.damage, a.knockback, a.shake, a.hitstop, false);
            }
        }

        void OverheadImpact(AttackDef a)
        {
            impactDone = true;
            Vector3 impact = transform.position + attackDir * 1.9f;
            impact.y = 0f;
            FX.Shockwave(impact, 3.8f, new Color(0.4f, 1.4f, 3.2f), 0.45f);
            FX.Burst(impact + Vector3.up * 0.1f, new Color(0.6f, 0.9f, 1f), new Color(0.3f, 0.5f, 1f), 36, new Vector2(3f, 9f), new Vector2(0.05f, 0.12f), new Vector2(0.25f, 0.55f), 2f, true, 0.6f, Vector3.up, 60f);
            FX.Burst(impact, new Color(0.25f, 0.22f, 0.3f, 0.7f), new Color(0.15f, 0.12f, 0.2f, 0.5f), 14, new Vector2(1f, 3f), new Vector2(0.6f, 1.2f), new Vector2(0.5f, 0.9f), -0.05f, false, 1f);
            FX.LightFlash(impact + Vector3.up, new Color(0.4f, 0.8f, 1f), 9f, 9f, 0.25f);
            gm.cameraRig.Shake(a.shake);
            gm.cameraRig.Punch(-3f);
            gm.Pulse(0.3f);
            Sfx.Play(Sfx.I.slam, 0.75f, 1.25f);

            var list = gm.enemies;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (i >= list.Count) continue;
                var e = list[i];
                if (e == null || !e.Targetable) continue;
                Vector3 to = e.transform.position - transform.position; to.y = 0f;
                Vector3 toImpact = e.transform.position - impact; toImpact.y = 0f;
                bool inCone = to.magnitude <= a.range + e.Radius && Vector3.Angle(attackDir, to) < 100f;
                bool inBlast = toImpact.magnitude <= 2.3f + e.Radius;
                if (inCone || inBlast) HitEnemy(e, a.damage, a.knockback, a.shake, a.hitstop, true);
            }
        }

        void HitEnemy(Enemy e, float baseDamage, float knockback, float shake, float hitstop, bool heavy)
        {
            bool crit = Random.value < 0.15f;
            float dmg = baseDamage * (crit ? 1.8f : 1f) * Random.Range(0.92f, 1.08f);
            Vector3 dir = e.transform.position - transform.position; dir.y = 0f;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward;
            e.TakeHit(dmg, dir, knockback, heavy, crit);
            AddRage(crit ? 7f : 4.5f);
            gm.HitStop(crit ? hitstop * 1.5f : hitstop);
            gm.cameraRig.Shake(crit ? shake * 1.4f : shake);
            if (crit) gm.Pulse(0.25f);
        }

        void ParryProjectiles(float range, float arc)
        {
            var list = gm.projectiles;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (i >= list.Count) continue;
                var p = list[i];
                if (p == null || p.reflected) continue;
                Vector3 to = p.transform.position - Chest;
                Vector3 flat = new Vector3(to.x, 0f, to.z);
                if (to.magnitude > range) continue;
                if (state != State.Spin && Vector3.Angle(transform.forward, flat) > arc) continue;
                var target = gm.NearestEnemy(transform.position, 25f);
                Vector3 dir = target ? (target.Chest - p.transform.position) : transform.forward;
                p.Reflect(dir);
                AddRage(10f);
                gm.HitStop(0.07f);
                gm.cameraRig.Shake(0.3f);
                gm.hud.Popup("PARRY!", new Color(0.4f, 0.95f, 1f));
                Sfx.Play(Sfx.I.ting, 0.8f, 1.1f);
                FX.Sparks(p.transform.position, -flat.normalized, true, false);
            }
        }

        // ------------------------------------------------------------------ spin

        void StartSpin()
        {
            state = State.Spin;
            stateTime = 0f;
            spinCd = spinCooldown;
            heavyBuf = 0f;
            spinHits.Clear();
            spinRound = 0;
            spinStartYaw = transform.eulerAngles.y;
            rig.SetTrail(true);
            Sfx.Play(Sfx.I.swingHeavy, 0.8f, 0.85f);
        }

        Vector3 UpdateSpin(float dt)
        {
            float u = Mathf.Clamp01(stateTime / SpinDuration);
            float total = 720f * u;
            transform.rotation = Quaternion.Euler(0f, spinStartYaw + total, 0f);
            rig.Snap(WeaponRig.HGrip(80f, 1.16f, 0.56f), WeaponRig.HDir(80f, 0.03f));
            rig.SetTrail(true);
            rig.leftHandWeight = 0f;
            rig.lookWeight = 0f;

            int round = Mathf.FloorToInt(total / 360f);
            if (round != spinRound && round < 2)
            {
                spinRound = round;
                Sfx.Play(Sfx.I.swingHeavy, 0.7f, 0.95f);
            }

            Vector3 swordDir = transform.rotation * WeaponRig.HDir(80f, 0f);
            swordDir.y = 0f;
            var list = gm.enemies;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (i >= list.Count) continue;
                var e = list[i];
                if (e == null || !e.Targetable) continue;
                Vector3 to = e.transform.position - transform.position; to.y = 0f;
                if (to.magnitude > 3.5f + e.Radius) continue;
                spinHits.TryGetValue(e, out int hits);
                if (hits > round || hits >= 2) continue;
                if (Vector3.Angle(swordDir, to) > 38f && to.magnitude > 1f) continue;
                spinHits[e] = round + 1;
                HitEnemy(e, 15f, 6.5f, 0.28f, 0.035f, true);
            }
            ParryProjectiles(3.2f, 180f);

            if (u >= 1f)
            {
                state = State.Move;
                facing = transform.forward;
                rig.SetTrail(false);
            }
            return moveInput * 3.4f;
        }

        // ------------------------------------------------------------------ dash

        void StartDash()
        {
            state = State.Dash;
            stateTime = 0f;
            dashCd = dashCooldown;
            dashBuf = 0f;
            dashDir = moveInput.sqrMagnitude > 0.01f ? moveInput.normalized : transform.forward;
            facing = dashDir;
            dashIframes = 0.3f;
            dashPerfectUsed = false;
            afterImageTimer = 0f;
            rig.SetTrail(false);
            Sfx.Play(Sfx.I.dash, 0.6f);
            FX.Burst(transform.position + Vector3.up * 0.1f, new Color(0.5f, 0.8f, 1f, 0.8f), new Color(0.3f, 0.3f, 0.5f, 0.5f), 12, new Vector2(1f, 3f), new Vector2(0.15f, 0.35f), new Vector2(0.25f, 0.45f), 0f, false, 0.3f);
        }

        Vector3 UpdateDash(float dt)
        {
            afterImageTimer -= dt;
            if (afterImageTimer <= 0f && body)
            {
                afterImageTimer = 0.03f;
                FX.AfterImage(body, new Color(0.3f, 0.9f, 2.2f, 0.55f));
            }
            rig.SetTarget(new Vector3(0.36f, 1.05f, -0.15f), new Vector3(0.25f, -0.25f, -1f), 25f);
            rig.leftHandWeight = 0f;
            if (stateTime >= dashTime)
            {
                state = State.Move;
                moveVel = dashDir * moveSpeed;
            }
            return dashDir * dashSpeed * (1f - 0.35f * stateTime / dashTime);
        }

        // ------------------------------------------------------------------ nova

        void StartNova()
        {
            state = State.Nova;
            stateTime = 0f;
            novaBuf = 0f;
            Rage = 0f;
            impactDone = false;
            invuln = NovaDuration + 0.2f;
            gm.SlowMo(0.4f, 0.5f);
            gm.cameraRig.Punch(-7f);
            gm.hud.Banner("VOID NOVA", "", new Color(0.7f, 0.45f, 1f));
            Sfx.Play(Sfx.I.charge, 0.8f, 0.9f, 0f);
            FX.LightFlash(Chest, new Color(0.6f, 0.3f, 1f), 6f, 10f, 0.5f);
            FX.Burst(transform.position, new Color(0.7f, 0.4f, 1f), new Color(0.3f, 0.8f, 1f), 40, new Vector2(2f, 6f), new Vector2(0.05f, 0.12f), new Vector2(0.3f, 0.7f), -0.5f, true, 1.2f, Vector3.up, 25f);
        }

        Vector3 UpdateNova()
        {
            float t = stateTime;
            float h;
            if (t < 0.42f) h = 2.3f * Mathf.Sin(Mathf.PI * 0.5f * t / 0.42f);
            else if (t < NovaSlamTime) { float k = 1f - (t - 0.42f) / (NovaSlamTime - 0.42f); h = 2.3f * k * k; }
            else h = 0f;
            model.localPosition = new Vector3(0f, h, 0f);
            rig.leftHandWeight = 1f;
            rig.lookWeight = 0.6f;
            rig.lookTargetWorld = Chest + transform.forward * 4f + Vector3.down * 2f;

            if (t < 0.42f)
            {
                rig.SetTarget(WeaponRig.VGrip(-25f) + Vector3.up * h, WeaponRig.VDir(-25f), 18f);
                rig.SetTrail(false);
            }
            else if (t < NovaSlamTime)
            {
                float u = (t - 0.42f) / (NovaSlamTime - 0.42f);
                float phi = Mathf.Lerp(-25f, 140f, u * u);
                rig.Snap(WeaponRig.VGrip(phi) + Vector3.up * h, WeaponRig.VDir(phi));
                rig.SetTrail(true);
            }
            else
            {
                rig.SetTarget(WeaponRig.VGrip(140f), WeaponRig.VDir(140f), 20f);
                if (t > NovaSlamTime + 0.08f) rig.SetTrail(false);
            }

            if (!impactDone && t >= NovaSlamTime) NovaImpact();

            if (t >= NovaDuration)
            {
                model.localPosition = Vector3.zero;
                state = State.Move;
                rig.leftHandWeight = 0f;
            }
            return Vector3.zero;
        }

        void NovaImpact()
        {
            impactDone = true;
            Vector3 c = transform.position; c.y = 0f;
            FX.Shockwave(c, NovaRadius + 1f, new Color(1.6f, 0.6f, 3.6f), 0.6f);
            FX.Shockwave(c, NovaRadius * 0.6f, new Color(0.5f, 1.6f, 3.6f), 0.4f);
            FX.Burst(c + Vector3.up * 0.2f, new Color(0.8f, 0.5f, 1f), new Color(0.4f, 0.9f, 1f), 90, new Vector2(6f, 16f), new Vector2(0.05f, 0.14f), new Vector2(0.3f, 0.8f), 1.5f, true, 1.5f);
            FX.Burst(c + Vector3.up * 0.2f, new Color(0.3f, 0.2f, 0.4f, 0.7f), new Color(0.1f, 0.08f, 0.15f, 0.5f), 24, new Vector2(2f, 6f), new Vector2(1f, 2.2f), new Vector2(0.6f, 1.2f), -0.05f, false, 2f);
            FX.LightFlash(c + Vector3.up * 2f, new Color(0.7f, 0.4f, 1f), 25f, 24f, 0.6f);
            gm.cameraRig.Shake(1f);
            gm.cameraRig.Punch(10f);
            gm.Pulse(1f);
            gm.HitStop(0.1f);
            gm.hud.Flash(0.55f);
            Sfx.Play(Sfx.I.nova, 1f, 1f, 0f);

            var list = gm.enemies;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (i >= list.Count) continue;
                var e = list[i];
                if (e == null || !e.Targetable) continue;
                Vector3 to = e.transform.position - c; to.y = 0f;
                if (to.magnitude > NovaRadius + e.Radius) continue;
                Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : Vector3.forward;
                e.TakeHit(80f * Random.Range(0.95f, 1.1f), dir, 16f, true, true);
            }
            var plist = gm.projectiles;
            for (int i = plist.Count - 1; i >= 0; i--)
                if (i < plist.Count && plist[i] && !plist[i].reflected) plist[i].Pop();
        }

        // ------------------------------------------------------------------ damage

        public bool TakeDamage(float dmg, Vector3 from, float knockback)
        {
            if (IsDead || gm == null || gm.IsGameOver) return false;
            if (dashIframes > 0f)
            {
                if (!dashPerfectUsed) PerfectDodge();
                return false;
            }
            if (invuln > 0f || state == State.Nova) return false;

            Health -= dmg;
            invuln = 0.6f;
            hurtFlash = 1f;
            Vector3 away = transform.position - from; away.y = 0f;
            knock = (away.sqrMagnitude > 0.001f ? away.normalized : -transform.forward) * knockback;
            gm.cameraRig.Shake(0.5f);
            gm.Pulse(0.6f);
            gm.BreakCombo();
            gm.hud.OnPlayerHurt();
            Sfx.Play(Sfx.I.hurt, 0.9f);
            FX.Burst(Chest, new Color(1f, 0.2f, 0.25f), new Color(1f, 0.6f, 0.3f), 22, new Vector2(3f, 8f), new Vector2(0.05f, 0.1f), new Vector2(0.2f, 0.4f), 1.5f, true, 0.2f);
            if (Health <= 0f) Die();
            return true;
        }

        void PerfectDodge()
        {
            dashPerfectUsed = true;
            if (perfectDodgeCd > 0f) return;
            perfectDodgeCd = 1.4f;
            gm.SlowMo(0.3f, 0.9f);
            gm.hud.Popup("PERFECT DODGE", new Color(0.4f, 1f, 0.9f));
            gm.cameraRig.Punch(-4f);
            AddRage(15f);
            Sfx.Play(Sfx.I.ting, 0.7f, 1.4f, 0f);
        }

        void Die()
        {
            Health = 0f;
            state = State.Dead;
            stateTime = 0f;
            rig.SetTrail(false);
            animator.enabled = false;
            FX.Explosion(Chest, new Color(0.3f, 0.8f, 1.6f), 1f);
            Sfx.Play(Sfx.I.death, 1f, 0.7f, 0f);
            gm.OnPlayerDied();
        }

        void UpdateDead()
        {
            float u = Mathf.Clamp01(stateTime / 0.7f);
            float b = 1f - Mathf.Pow(1f - u, 3f);
            model.localRotation = Quaternion.Euler(-82f * b, 0f, 0f);
            model.localPosition = new Vector3(0f, 0.1f * Mathf.Sin(u * Mathf.PI), -0.2f * b);
            rig.SetTarget(new Vector3(0.7f, 0.06f, 0.5f), new Vector3(1f, 0f, 0.3f), 8f);
        }

        // ------------------------------------------------------------------ helpers

        static Vector3 RestGrip(float speed)
        {
            float bob = Mathf.Sin(Time.time * 9.5f) * 0.03f * Mathf.Clamp01(speed / 6f);
            return new Vector3(0.34f, 1.0f + bob, 0.3f);
        }

        static Vector3 RestDir() => new Vector3(0.12f, 0.62f, 1f);

        Vector3 ChooseAttackDir()
        {
            bool hasInput = moveInput.sqrMagnitude > 0.01f;
            Vector3 basis = hasInput ? moveInput.normalized : transform.forward;
            Enemy best = null;
            float bestScore = float.MaxValue;
            foreach (var e in gm.enemies)
            {
                if (e == null || !e.Targetable) continue;
                Vector3 to = e.transform.position - transform.position; to.y = 0f;
                float d = to.magnitude;
                if (d > 5.5f) continue;
                float ang = Vector3.Angle(basis, to);
                if (hasInput && ang > 70f) continue;
                float score = d + ang * 0.035f;
                if (score < bestScore) { bestScore = score; best = e; }
            }
            if (best != null)
            {
                Vector3 to = best.transform.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.01f) return to.normalized;
            }
            return basis;
        }

        void UpdateBodyGlow(float dt)
        {
            if (!bodyMat) return;
            hurtFlash = Mathf.MoveTowards(hurtFlash, 0f, dt * 4f);
            Color c = Color.black;
            if (NovaReady) c += new Color(0.35f, 0.15f, 0.7f) * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f));
            if (state == State.Dash) c += new Color(0.1f, 0.4f, 0.8f);
            c += new Color(2.5f, 0.2f, 0.2f) * hurtFlash;
            if (invuln > 0f && state == State.Move && Mathf.Repeat(Time.unscaledTime * 14f, 1f) > 0.5f) c += new Color(0.4f, 0.4f, 0.4f);
            bodyMat.SetColor(EmissionId, c);
        }

        // ------------------------------------------------------------------ autopilot

        void Autopilot()
        {
            Vector3 pos = transform.position;
            Vector3 mv = Vector3.zero;
            Vector3 threatAway = Vector3.zero;
            bool threatened = false;
            int close = 0, mid = 0;
            foreach (var e in gm.enemies)
            {
                if (e == null || !e.Targetable) continue;
                Vector3 to = e.transform.position - pos; to.y = 0f;
                float d = to.magnitude;
                if (d < 3.6f) close++;
                if (d < 8.5f) mid++;
                if (e.IsThreatening(pos))
                {
                    threatened = true;
                    threatAway -= to.normalized;
                }
            }
            foreach (var p in gm.projectiles)
            {
                if (p == null || p.reflected) continue;
                Vector3 to = p.transform.position - Chest;
                if (to.magnitude < 3.2f)
                {
                    Vector3 flat = new Vector3(to.x, 0f, to.z);
                    if (flat.sqrMagnitude > 0.01f) facing = flat.normalized;
                    attackBuf = 0.2f;
                }
            }

            var target = gm.NearestEnemy(pos, 80f);
            if (NovaReady && mid >= 3) novaBuf = 0.2f;
            else if (threatened && dashCd <= 0f && state != State.Attack)
            {
                Vector3 away = threatAway.sqrMagnitude > 0.01f ? threatAway.normalized : -transform.forward;
                Vector3 side = Vector3.Cross(Vector3.up, away);
                mv = (away + side * 0.6f).normalized;
                dashBuf = 0.2f;
            }
            else if (Health < maxHealth * 0.35f && gm.orbs.Count > 0 && gm.orbs[0] != null)
            {
                Vector3 to = gm.orbs[0].transform.position - pos; to.y = 0f;
                mv = to.normalized;
            }
            else if (target != null)
            {
                Vector3 to = target.transform.position - pos; to.y = 0f;
                float d = to.magnitude;
                if (close >= 3 && spinCd <= 0f) heavyBuf = 0.2f;
                else if (d > 2.4f + target.Radius) mv = to.normalized;
                else { mv = to.normalized * 0.2f; attackBuf = 0.2f; }
            }
            else
            {
                Vector3 home = -pos; home.y = 0f;
                if (home.magnitude > 2f) mv = home.normalized * 0.6f;
            }
            moveInput = mv;
        }
    }
}
