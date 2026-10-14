using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HackAndSlash
{
    /// <summary>
    /// The Void Knight, driven by an Arkham-style FreeFlow combat model: directional strikes that warp across the
    /// crowd, counters (single and multi), evades that vault over robots, an arc stun with beatdowns, special-combo
    /// and ground takedowns, and the Void Nova ultimate. Swings are procedural (no attack clips): the hands follow
    /// the weapon through humanoid IK while the torso twists into each blow.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public enum State { Move, Strike, Counter, Evade, Vault, Stun, Takedown, GroundTakedown, Nova, Hurt, Dead }

        struct StrikeDef
        {
            public float anticipation, impact, recover, damage, knockback, shake, hitstop;
            public HitKind kind;
        }

        // kind: 0 slash R->L, 1 backhand L->R, 2 overhead, 3 thrust, 4 rising launcher, 5 spin
        static readonly StrikeDef[] Strikes =
        {
            new StrikeDef { anticipation = 0.05f, impact = 0.1f, recover = 0.2f, damage = 13f, knockback = 3.5f, shake = 0.2f, hitstop = 0.05f, kind = HitKind.Strike },
            new StrikeDef { anticipation = 0.05f, impact = 0.1f, recover = 0.2f, damage = 14f, knockback = 3.5f, shake = 0.22f, hitstop = 0.055f, kind = HitKind.Strike },
            new StrikeDef { anticipation = 0.08f, impact = 0.15f, recover = 0.26f, damage = 21f, knockback = 6f, shake = 0.4f, hitstop = 0.08f, kind = HitKind.Strike },
            new StrikeDef { anticipation = 0.05f, impact = 0.09f, recover = 0.2f, damage = 15f, knockback = 5f, shake = 0.24f, hitstop = 0.06f, kind = HitKind.Strike },
            new StrikeDef { anticipation = 0.07f, impact = 0.13f, recover = 0.28f, damage = 19f, knockback = 7f, shake = 0.45f, hitstop = 0.09f, kind = HitKind.Heavy },
            new StrikeDef { anticipation = 0.06f, impact = 0.15f, recover = 0.3f, damage = 16f, knockback = 6.5f, shake = 0.4f, hitstop = 0.06f, kind = HitKind.Strike },
        };
        static readonly int[] StrikeCycle = { 0, 1, 3, 0, 2, 1, 4, 0, 1, 2 };

        const float NovaSlamTime = 0.52f;
        const float NovaDuration = 0.95f;
        const float NovaRadius = 10.5f;
        const float VaultTime = 0.46f;

        [Header("Rig")]
        public Animator animator;
        public Transform model;
        public WeaponRig rig;
        public SkinnedMeshRenderer body;
        public Material bladeMaterial;

        [Header("Tuning")]
        public float maxHealth = 100f;
        public float moveSpeed = 6.8f;
        public float dashSpeed = 20f;
        public float dashTime = 0.2f;
        public float dashCooldown = 0.32f;
        public float freeflowRange = 10f;
        public float counterRange = 7.5f;
        public bool autopilot;

        public float Health { get; private set; }
        public float Rage { get; private set; }
        public bool IsDead => state == State.Dead;
        public State CurrentState => state;
        public bool NovaReady => Rage >= 100f;
        public Vector3 Velocity => moveVel;
        public Vector3 Chest => transform.position + Vector3.up * 1.25f;
        public float Radius => 0.35f;
        public Enemy CurrentTarget => target;
        public bool InCinematic => state == State.Takedown || (state == State.Counter && counterQueue.Count > 1);
        public ArmorKit Armor => armor;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        static readonly int FreeFallId = Animator.StringToHash("FreeFall");
        static readonly int JumpId = Animator.StringToHash("Jump");

        CharacterController cc;
        GameManager gm;
        ArmorKit armor;
        State state;
        float stateTime;
        Vector3 moveInput, moveVel, knock, facing = Vector3.forward, attackDir = Vector3.forward, dashDir;
        float vy;
        float strikeBuf, counterBuf, evadeBuf, stunBuf, takedownBuf, novaBuf;
        float dashCd, stunCd, counterLock, invuln, dashIframes, hurtFlash, afterImageTimer, perfectDodgeCd, hintCd;
        bool dashPerfectUsed, impactDone, whooshDone, whiff, kinematic;

        // strike
        Enemy target;
        int strikeKind, strikeCount;
        Vector3 strikeStart;
        float travelTime, impactTime, totalTime, speedMul = 1f, spinStartYaw, travelHeight;

        // counter
        readonly List<Enemy> counterQueue = new List<Enemy>();
        int counterIndex;
        float counterStepStart, counterTravel;
        bool counterParried, counterHit;

        // vault / takedowns
        Vector3 vaultFrom, vaultTo;
        float vaultHeight;
        Enemy execTarget;
        int execStep;

        // autopilot
        float apCounterAt = -1f, apThink;

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            gm = GameManager.I;
            if (rig && rig.weapon == null)
            {
                var metal = FX.Lit(new Color(0.55f, 0.58f, 0.64f), 1f, 0.9f);
                var blade = bladeMaterial ? bladeMaterial : FX.Emissive(new Color(0.3f, 0.8f, 1f), 4f);
                rig.weapon = Weapons.Sword(model ? model : transform, metal, blade);
                rig.bladeLength = 1.25f;
            }
            if (animator && body) armor = ArmorKit.Build(animator, body, transform, ArmorStyle.Hero);
            if (rig) rig.InitTrail(new Color(0.35f, 1.6f, 3.6f, 1f), "PlayerTrail", 0.15f);
        }

        public void ResetPlayer(Vector3 pos)
        {
            gm = GameManager.I;
            Place(pos);
            transform.rotation = Quaternion.identity;
            Health = maxHealth;
            Rage = 0f;
            state = State.Move;
            stateTime = 0f;
            moveVel = knock = Vector3.zero;
            vy = 0f;
            facing = Vector3.forward;
            target = null;
            execTarget = null;
            counterQueue.Clear();
            strikeBuf = counterBuf = evadeBuf = stunBuf = takedownBuf = novaBuf = 0f;
            dashCd = stunCd = counterLock = invuln = dashIframes = hurtFlash = 0f;
            animator.enabled = true;
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            rig.SetTrail(false);
            rig.rightHandWeight = 1f;
            rig.leftHandWeight = 0f;
            rig.lookWeight = 0f;
            rig.torsoTwist = rig.torsoBend = 0f;
            rig.Snap(RestGrip(0f), RestDir());
            if (armor) armor.Restore();
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

            dashCd -= dt; stunCd -= dt; invuln -= dt; dashIframes -= dt; counterLock -= udt;
            perfectDodgeCd -= udt; hintCd -= udt;
            stateTime += dt;
            kinematic = false;

            Vector3 planar = Vector3.zero;
            switch (state)
            {
                case State.Move: planar = UpdateMove(dt); break;
                case State.Strike: planar = UpdateStrike(dt); break;
                case State.Counter: UpdateCounter(dt); break;
                case State.Evade: planar = UpdateEvade(dt); break;
                case State.Vault: UpdateVault(); break;
                case State.Stun: planar = UpdateStun(dt); break;
                case State.Takedown: UpdateTakedown(dt); break;
                case State.GroundTakedown: UpdateGroundTakedown(dt); break;
                case State.Nova: UpdateNova(); break;
                case State.Hurt: planar = UpdateHurt(dt); break;
                case State.Dead: UpdateDead(); break;
            }

            // integrate movement
            knock = Vector3.MoveTowards(knock, Vector3.zero, 30f * dt);
            if (kinematic) vy = 0f;
            else
            {
                vy = cc.isGrounded ? -3f : vy - 30f * dt;
                if (cc.enabled) cc.Move((state == State.Dead ? Vector3.zero : planar + knock) * dt + Vector3.up * (vy * dt));
            }
            Vector3 clamped = gm.ClampToArena(transform.position, 0.4f);
            if ((clamped - transform.position).sqrMagnitude > 1e-6f) Place(clamped);

            if (state != State.Dead && state != State.Vault && state != State.Takedown && !(state == State.Strike && strikeKind == 5) && facing.sqrMagnitude > 0.001f)
            {
                float sharp = state == State.Move ? 16f : 40f;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(facing), 1f - Mathf.Exp(-sharp * dt));
            }

            if (animator.enabled)
            {
                float animSpeed;
                if (state == State.Move) animSpeed = new Vector3(moveVel.x, 0f, moveVel.z).magnitude;
                else if (state == State.Evade || (state == State.Strike && stateTime < travelTime)) animSpeed = 6f;
                else if (state == State.Vault || state == State.Counter || state == State.Takedown) animSpeed = 0f;
                else animSpeed = Mathf.Min(planar.magnitude * 0.6f, 2.5f);
                animator.SetFloat(SpeedId, animSpeed, 0.05f, Mathf.Max(dt, 0.0001f));
                animator.SetFloat(MotionSpeedId, 1f);
                animator.SetBool(GroundedId, true);
                animator.SetBool(FreeFallId, false);
                animator.SetBool(JumpId, false);
            }

            rig.torsoTwist = Mathf.MoveTowards(rig.torsoTwist, 0f, dt * 160f);
            rig.torsoBend = Mathf.MoveTowards(rig.torsoBend, 0f, dt * 80f);
            rig.Tick(dt);
            if (state != State.Dead) UpdateBodyGlow(dt);
        }

        // ------------------------------------------------------------------ input

        void ReadInput(float udt)
        {
            strikeBuf -= udt; counterBuf -= udt; evadeBuf -= udt; stunBuf -= udt; takedownBuf -= udt; novaBuf -= udt;
            Vector2 mv = Vector2.zero;
            const float buffer = 0.22f;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) mv.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) mv.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) mv.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) mv.x -= 1f;
                if (kb.jKey.wasPressedThisFrame) strikeBuf = buffer;
                if (kb.kKey.wasPressedThisFrame) counterBuf = buffer;
                if (kb.spaceKey.wasPressedThisFrame || kb.leftShiftKey.wasPressedThisFrame) evadeBuf = buffer;
                if (kb.eKey.wasPressedThisFrame || kb.lKey.wasPressedThisFrame) stunBuf = buffer;
                if (kb.fKey.wasPressedThisFrame) takedownBuf = buffer;
                if (kb.qKey.wasPressedThisFrame) novaBuf = buffer;
                if (kb.pKey.wasPressedThisFrame)
                {
                    autopilot = !autopilot;
                    if (gm && gm.hud) gm.hud.Popup(autopilot ? "AUTOPILOT ENGAGED" : "MANUAL CONTROL", new Color(0.5f, 0.9f, 1f));
                }
            }
            var ms = Mouse.current;
            if (ms != null)
            {
                if (ms.leftButton.wasPressedThisFrame) strikeBuf = buffer;
                if (ms.rightButton.wasPressedThisFrame) counterBuf = buffer;
                if (ms.middleButton.wasPressedThisFrame) stunBuf = buffer;
            }
            var gp = Gamepad.current;
            if (gp != null)
            {
                Vector2 stick = gp.leftStick.ReadValue();
                if (stick.sqrMagnitude > mv.sqrMagnitude) mv = stick;
                if (gp.buttonWest.wasPressedThisFrame) strikeBuf = buffer;
                if (gp.buttonNorth.wasPressedThisFrame) counterBuf = buffer;
                if (gp.buttonSouth.wasPressedThisFrame) evadeBuf = buffer;
                if (gp.buttonEast.wasPressedThisFrame) stunBuf = buffer;
                if (gp.rightShoulder.wasPressedThisFrame) takedownBuf = buffer;
                if (gp.rightTrigger.wasPressedThisFrame || gp.leftShoulder.wasPressedThisFrame) novaBuf = buffer;
            }

            mv = Vector2.ClampMagnitude(mv, 1f);
            Vector3 camF = Vector3.forward, camR = Vector3.right;
            if (gm && gm.cameraRig) { camF = gm.cameraRig.PlanarForward; camR = gm.cameraRig.PlanarRight; }
            moveInput = camF * mv.y + camR * mv.x;
        }

        // ------------------------------------------------------------------ move

        bool TryStartAction(bool allowStrike = true)
        {
            if (counterBuf > 0f && counterLock <= 0f)
            {
                counterBuf = 0f;
                if (StartCounter()) return true;
                counterLock = 0.35f; // Arkham-style lockout: no counter spam
            }
            if (evadeBuf > 0f && dashCd <= 0f) { StartEvade(); return true; }
            if (novaBuf > 0f && NovaReady) { StartNova(); return true; }
            if (takedownBuf > 0f && TryTakedown()) return true;
            if (stunBuf > 0f && stunCd <= 0f) { StartStun(); return true; }
            if (allowStrike && strikeBuf > 0f) { StartStrike(); return true; }
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

        Vector3 UpdateHurt(float dt)
        {
            float u = Mathf.Clamp01(stateTime / 0.3f);
            model.localRotation = Quaternion.Euler(-12f * Mathf.Sin(u * Mathf.PI), 0f, 0f);
            rig.SetTarget(RestGrip(0f) + new Vector3(0f, 0.1f, -0.15f), RestDir(), 10f);
            if (u >= 1f)
            {
                model.localRotation = Quaternion.identity;
                state = State.Move;
            }
            else if (u > 0.55f && (counterBuf > 0f || evadeBuf > 0f))
            {
                model.localRotation = Quaternion.identity;
                if (TryStartAction(false)) return Vector3.zero;
            }
            return Vector3.zero;
        }

        // ------------------------------------------------------------------ freeflow strike

        /// <summary>Arkham targeting: the robot best aligned with the stick wins; with no input the nearest one ahead.</summary>
        Enemy PickTarget(Vector3 input, float range, bool downedOnly = false)
        {
            bool hasInput = input.sqrMagnitude > 0.04f;
            Vector3 basis = hasInput ? input.normalized : facing;
            Vector3 pos = transform.position;
            Enemy best = null;
            float bestScore = float.MaxValue;
            foreach (var e in gm.enemies)
            {
                if (e == null || !e.Targetable) continue;
                if (downedOnly != e.IsDowned) continue;
                Vector3 to = e.transform.position - pos; to.y = 0f;
                if (downedOnly) { to = e.BodyCenter - pos; to.y = 0f; }
                float d = to.magnitude;
                if (d > range + e.Radius) continue;
                float ang = d > 0.3f ? Vector3.Angle(basis, to) : 0f;
                float score;
                if (hasInput)
                {
                    if (ang > 70f) continue;
                    score = ang * 0.06f + d * 0.2f;
                }
                else
                {
                    score = d * 0.3f + ang * 0.02f;
                    if (e == target && d < 6f) score -= 0.8f;
                }
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        float Reach(Enemy e) => 1.15f + e.Radius + Radius;

        void StartStrike()
        {
            strikeBuf = 0f;
            var t = PickTarget(moveInput, freeflowRange);
            target = t;
            whiff = t == null;
            state = State.Strike;
            stateTime = 0f;
            impactDone = false;
            whooshDone = false;
            strikeCount++;
            speedMul = 1f + Mathf.Min(gm.Combo, 40) * 0.016f;

            bool beatdown = t != null && t.IsStunned;
            if (beatdown) strikeKind = strikeCount % 2;
            else if (gm.Combo >= 5 && gm.Combo % 7 == 6) strikeKind = 5;
            else strikeKind = StrikeCycle[strikeCount % StrikeCycle.Length];
            if (beatdown) speedMul *= 1.6f;

            strikeStart = transform.position;
            Vector3 to = t ? Flat(t.transform.position - transform.position) : Vector3.zero;
            float dist = to.magnitude;
            attackDir = t && dist > 0.05f ? to / dist : (moveInput.sqrMagnitude > 0.04f ? moveInput.normalized : facing);
            facing = attackDir;
            transform.rotation = Quaternion.LookRotation(attackDir);
            spinStartYaw = transform.eulerAngles.y;

            bool travel = t && dist > Reach(t);
            travelTime = travel ? Mathf.Clamp(dist / 24f, 0.09f, 0.3f) / Mathf.Sqrt(speedMul) : 0f;
            travelHeight = travel ? Mathf.Clamp(dist * 0.045f, 0f, 0.45f) : 0f;
            var def = Strikes[strikeKind];
            impactTime = travelTime + def.impact / speedMul;
            totalTime = impactTime + def.recover / speedMul;
            moveVel = Vector3.zero;
            if (travel)
            {
                Sfx.Play(Sfx.I.dash, 0.4f, 1.15f);
                gm.cameraRig.Punch(-1.2f);
                afterImageTimer = 0f;
            }
            gm.RegisterMove(beatdown ? "beatdown" : strikeKind == 5 ? "spin" : "strike");
        }

        Vector3 UpdateStrike(float dt)
        {
            var def = Strikes[strikeKind];
            float t = stateTime;

            // counter beats everything (Arkham): it cancels a strike at any time
            if (counterBuf > 0f && counterLock <= 0f && AnyCounterable())
            {
                counterBuf = 0f;
                if (StartCounter()) return Vector3.zero;
            }
            // land the blow before anything can cancel or chain out of the swing
            if (!impactDone && t >= impactTime)
            {
                impactDone = true;
                ResolveStrike(def);
            }
            if (t >= impactTime)
            {
                if (TryStartAction(false)) return Vector3.zero;
                if (strikeBuf > 0f && t >= impactTime + 0.015f) { StartStrike(); return Vector3.zero; }
            }
            if (t >= totalTime)
            {
                state = State.Move;
                rig.SetTrail(false);
                return Vector3.zero;
            }

            bool alive = target != null && target.Targetable;
            Vector3 vel = Vector3.zero;
            if (alive)
            {
                Vector3 to = Flat(target.transform.position - transform.position);
                if (to.sqrMagnitude > 0.01f) { attackDir = to.normalized; facing = attackDir; }
            }

            if (t < travelTime && alive)
            {
                // freeflow lunge: home in on the target and arrive just inside striking range
                float u = Mathf.Clamp01(t / travelTime);
                float e = u * u * (3f - 2f * u);
                Vector3 goal = target.transform.position - attackDir * (Reach(target) - 0.35f);
                goal.y = 0f;
                Vector3 p = Vector3.Lerp(strikeStart, goal, e);
                p.y = travelHeight * 4f * u * (1f - u);
                Place(p);
                kinematic = true;
                rig.torsoBend = 16f;
                afterImageTimer -= Time.deltaTime;
                if (afterImageTimer <= 0f && body)
                {
                    afterImageTimer = 0.03f;
                    FX.AfterImage(body, new Color(0.25f, 0.8f, 2.2f, 0.4f), 0.22f, armor ? armor.Renderers : null);
                }
            }
            else if (alive && !impactDone)
            {
                float d = Flat(target.transform.position - transform.position).magnitude;
                if (d > Reach(target) * 0.85f) vel = attackDir * Mathf.Min(8f, (d - Reach(target) * 0.7f) * 18f);
            }

            float phase = (t - (impactTime - def.impact / speedMul)) / (def.impact / speedMul);
            PoseStrike(strikeKind, phase, dt);

            if (!whooshDone && phase >= 0.3f)
            {
                whooshDone = true;
                Sfx.Play(strikeKind == 2 || strikeKind == 4 || strikeKind == 5 ? Sfx.I.swingHeavy : Sfx.I.swing, 0.65f, strikeKind == 1 ? 1.12f : 1f * Mathf.Min(1.3f, speedMul));
            }
            return vel;
        }

        void PoseStrike(int kind, float phase, float dt)
        {
            rig.lookWeight = Mathf.MoveTowards(rig.lookWeight, 0.85f, dt * 8f);
            rig.lookTargetWorld = (target ? target.Chest : Chest + attackDir * 4f);
            float p = Mathf.Clamp(phase, -0.4f, 1.8f);
            bool active = phase > 0f && phase < 1.45f;
            rig.SetTrail(active);
            float k = Mathf.Clamp01(phase);
            float ease = 1f - (1f - k) * (1f - k) * (1f - k);
            float follow = Mathf.Clamp01((phase - 1f) / 0.8f);
            switch (kind)
            {
                case 0:
                case 1:
                {
                    float a0 = kind == 0 ? 118f : -112f, aMid = kind == 0 ? -35f : 35f, a1 = kind == 0 ? -112f : 120f;
                    float h0 = kind == 0 ? 1.36f : 1.42f, h1 = kind == 0 ? 1.1f : 1.18f;
                    float ang = phase <= 1f ? Mathf.Lerp(a0, aMid, ease) : Mathf.Lerp(aMid, a1, follow);
                    float h = Mathf.Lerp(h0, h1, phase <= 1f ? ease * 0.6f : 0.6f + 0.4f * follow);
                    if (phase < 0f) rig.SetTarget(WeaponRig.HGrip(a0 + 10f * (kind == 0 ? 1f : -1f), h0), WeaponRig.HDir(a0, 0.2f), 40f);
                    else rig.Snap(WeaponRig.HGrip(ang, h), WeaponRig.HDir(ang, Mathf.Lerp(0.15f, -0.12f, Mathf.Clamp01(p))));
                    rig.leftHandWeight = Mathf.MoveTowards(rig.leftHandWeight, 0f, dt * 6f);
                    rig.torsoTwist = Mathf.Lerp(kind == 0 ? 30f : -30f, kind == 0 ? -34f : 34f, Mathf.Clamp01(p * 0.8f));
                    break;
                }
                case 2:
                {
                    const float p0 = -52f, pMid = 95f, p1 = 140f;
                    float phi = phase <= 1f ? Mathf.Lerp(p0, pMid, k * k) : Mathf.Lerp(pMid, p1, follow);
                    if (phase < 0f) rig.SetTarget(WeaponRig.VGrip(p0), WeaponRig.VDir(p0), 24f);
                    else rig.Snap(WeaponRig.VGrip(phi), WeaponRig.VDir(phi));
                    rig.leftHandWeight = Mathf.MoveTowards(rig.leftHandWeight, 1f, dt * 10f);
                    rig.torsoBend = Mathf.Lerp(-8f, 22f, Mathf.Clamp01(p));
                    break;
                }
                case 3:
                {
                    Vector3 back = new Vector3(0.32f, 1.28f, -0.05f), front = new Vector3(0.12f, 1.3f, 0.95f);
                    Vector3 g = phase <= 1f ? Vector3.Lerp(back, front, ease) : Vector3.Lerp(front, new Vector3(0.2f, 1.25f, 0.7f), follow);
                    if (phase < 0f) rig.SetTarget(back, new Vector3(0.05f, 0.05f, 1f), 30f);
                    else rig.Snap(g, new Vector3(0.02f, 0.02f, 1f));
                    rig.torsoTwist = Mathf.Lerp(20f, -12f, Mathf.Clamp01(p));
                    rig.torsoBend = 10f * Mathf.Clamp01(p);
                    break;
                }
                case 4:
                {
                    float ang = phase <= 1f ? Mathf.Lerp(75f, -10f, ease) : Mathf.Lerp(-10f, -60f, follow);
                    float h = phase <= 1f ? Mathf.Lerp(0.78f, 1.5f, ease) : Mathf.Lerp(1.5f, 1.85f, follow);
                    if (phase < 0f) rig.SetTarget(WeaponRig.HGrip(80f, 0.8f), WeaponRig.HDir(80f, -0.55f), 30f);
                    else rig.Snap(WeaponRig.HGrip(ang, h), WeaponRig.HDir(ang, Mathf.Lerp(-0.4f, 0.9f, Mathf.Clamp01(p))));
                    rig.torsoBend = Mathf.Lerp(18f, -12f, Mathf.Clamp01(p));
                    rig.torsoTwist = Mathf.Lerp(20f, -15f, Mathf.Clamp01(p));
                    break;
                }
                default:
                {
                    // 540 degree spin: the body whirls, the blade sweeps everything around it
                    float u = Mathf.Clamp01((phase + 0.4f) / 2.2f);
                    float total = 540f * (1f - (1f - u) * (1f - u));
                    transform.rotation = Quaternion.Euler(0f, spinStartYaw + total, 0f);
                    rig.Snap(WeaponRig.HGrip(80f, 1.16f, 0.56f), WeaponRig.HDir(80f, 0.03f));
                    rig.SetTrail(phase > -0.3f && phase < 1.6f);
                    if (phase > 1.6f) facing = transform.forward;
                    break;
                }
            }
        }

        void ResolveStrike(StrikeDef def)
        {
            ParryProjectiles(2.9f, 110f);
            if (whiff && strikeKind != 5)
            {
                gm.BreakCombo();
                return;
            }
            float dmgScale = 1f;
            if (strikeKind == 5)
            {
                var list = gm.enemies;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (i >= list.Count) continue;
                    var e = list[i];
                    if (e == null || !e.Targetable || e.IsDowned) continue;
                    Vector3 to = Flat(e.transform.position - transform.position);
                    if (to.magnitude > 3.1f + e.Radius) continue;
                    HitEnemy(e, def.damage, def.kind, def.knockback, def.shake, def.hitstop);
                }
                FX.Shockwave(transform.position, 3.6f, new Color(0.4f, 1.3f, 3.2f), 0.35f);
                return;
            }
            if (target == null || !target.Targetable) return;
            bool landed = HitEnemy(target, def.damage * dmgScale, def.kind, def.knockback, def.shake, def.hitstop);
            if (!landed) return;
            if (strikeKind == 2)
            {
                Vector3 impact = transform.position + attackDir * 1.6f;
                FX.Shockwave(impact, 2.6f, new Color(0.4f, 1.4f, 3.2f), 0.35f);
                FX.Dust(impact, 0.6f);
            }
            // cleave: the sweep clips neighbours in its arc
            if (strikeKind <= 2)
            {
                var list = gm.enemies;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (i >= list.Count) continue;
                    var e = list[i];
                    if (e == null || e == target || !e.Targetable || e.IsDowned) continue;
                    Vector3 to = Flat(e.transform.position - transform.position);
                    if (to.magnitude > 2.4f + e.Radius || Vector3.Angle(attackDir, to) > 70f) continue;
                    HitEnemy(e, def.damage * 0.45f, HitKind.Strike, def.knockback * 0.6f, def.shake * 0.5f, 0f);
                }
            }
        }

        bool HitEnemy(Enemy e, float baseDamage, HitKind kind, float knockback, float shake, float hitstop)
        {
            bool crit = Random.value < 0.12f;
            float dmg = baseDamage * (crit ? 1.7f : 1f) * Random.Range(0.92f, 1.08f);
            Vector3 dir = Flat(e.transform.position - transform.position);
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward;
            bool landed = e.TakeHit(new HitInfo(dmg, dir, knockback, kind, crit), transform.position);
            if (!landed)
            {
                knock = -dir * 6f;
                gm.HitStop(0.05f);
                gm.cameraRig.Shake(0.22f);
                Hint("SHIELD UP - STUN IT [E] OR VAULT OVER IT [SPACE]");
                return false;
            }
            AddRage(crit ? 6f : 4f);
            gm.HitStop(crit ? hitstop * 1.5f : hitstop);
            gm.cameraRig.Shake(crit ? shake * 1.4f : shake);
            gm.Rumble(0.15f + shake * 0.4f, 0.25f + shake * 0.6f, 0.08f);
            if (crit) gm.Pulse(0.25f);
            if (e.IsArmored && kind == HitKind.Strike) Hint("ARMORED - STUN IT [E] THEN BEAT IT DOWN");
            return true;
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
                Vector3 flat = Flat(to);
                if (to.magnitude > range) continue;
                if (Vector3.Angle(transform.forward, flat) > arc) continue;
                ReflectBolt(p);
            }
        }

        void ReflectBolt(Projectile p)
        {
            var target2 = gm.NearestEnemy(transform.position, 25f);
            Vector3 dir = target2 ? (target2.Chest - p.transform.position) : transform.forward;
            Vector3 flat = Flat(p.transform.position - Chest);
            p.Reflect(dir);
            AddRage(10f);
            gm.HitStop(0.07f);
            gm.cameraRig.Shake(0.3f);
            gm.hud.Popup("DEFLECTED!", new Color(0.4f, 0.95f, 1f));
            gm.RegisterMove("deflect");
            gm.KeepComboAlive();
            Sfx.Play(Sfx.I.ting, 0.8f, 1.1f);
            FX.Clash(p.transform.position, -flat.normalized, new Color(0.4f, 1.2f, 3f));
        }

        // ------------------------------------------------------------------ counter

        bool AnyCounterable()
        {
            foreach (var e in gm.enemies)
                if (e && e.CanBeCountered && Flat(e.transform.position - transform.position).magnitude <= counterRange + e.Radius) return true;
            foreach (var p in gm.projectiles)
                if (p && !p.reflected && (p.transform.position - Chest).magnitude < 4.2f) return true;
            return false;
        }

        bool StartCounter()
        {
            counterQueue.Clear();
            foreach (var e in gm.enemies)
            {
                if (e == null || !e.CanBeCountered) continue;
                if (Flat(e.transform.position - transform.position).magnitude > counterRange + e.Radius) continue;
                counterQueue.Add(e);
            }
            Projectile bolt = null;
            float bestBolt = 4.2f;
            foreach (var p in gm.projectiles)
            {
                if (p == null || p.reflected) continue;
                float d = (p.transform.position - Chest).magnitude;
                if (d < bestBolt) { bestBolt = d; bolt = p; }
            }

            if (counterQueue.Count == 0 && bolt == null)
            {
                foreach (var e in gm.enemies)
                    if (e && e.UnblockableIncoming && Flat(e.transform.position - transform.position).magnitude < counterRange)
                    {
                        Hint("RED ATTACKS CAN'T BE COUNTERED - EVADE [SPACE]");
                        break;
                    }
                return false;
            }
            if (bolt != null) ReflectBolt(bolt);
            if (counterQueue.Count == 0)
            {
                state = State.Counter;
                stateTime = 0f;
                counterIndex = 0;
                counterStepStart = 0f;
                counterParried = counterHit = true;
                return true;
            }

            counterQueue.Sort((a, b) => a.TimeToImpact.CompareTo(b.TimeToImpact));
            if (counterQueue.Count > 4) counterQueue.RemoveRange(4, counterQueue.Count - 4);
            // later robots in a multi-counter stay frozen until their blow arrives
            for (int i = 0; i < counterQueue.Count; i++) counterQueue[i].OnCountered(1.1f + 0.45f * i);
            state = State.Counter;
            stateTime = 0f;
            counterIndex = 0;
            int n = counterQueue.Count;
            BeginCounterStep();
            invuln = 0.35f + n * 0.3f;
            if (n >= 2)
            {
                gm.SlowMo(0.42f, 0.25f + 0.22f * n);
                Vector3 side = Vector3.Cross(Vector3.up, gm.cameraRig.PlanarForward) * (Random.value < 0.5f ? -1f : 1f);
                gm.cameraRig.Cinematic(Chest, (-gm.cameraRig.PlanarForward + side * 0.8f + Vector3.up * 0.55f).normalized, 5.2f, -6f, 0.35f + 0.3f * n);
            }
            else gm.SlowMo(0.6f, 0.16f);
            string[] names = { "COUNTER", "DOUBLE COUNTER", "TRIPLE COUNTER", "QUAD COUNTER" };
            gm.hud.Popup(names[Mathf.Clamp(n - 1, 0, 3)], new Color(0.45f, 0.9f, 1f));
            gm.RegisterMove(n >= 2 ? "multicounter" : "counter");
            Sfx.Play(Sfx.I.whoom, 0.35f, 1.6f, 0f);
            return true;
        }

        /// <summary>Advances past robots that died mid-sequence (e.g. to a reflected bolt). Returns false when none are left.</summary>
        bool SkipDeadCounterTargets()
        {
            while (counterIndex < counterQueue.Count && (counterQueue[counterIndex] == null || counterQueue[counterIndex].IsDead)) counterIndex++;
            return counterIndex < counterQueue.Count;
        }

        void EndCounter()
        {
            rig.SetTrail(false);
            counterQueue.Clear();
            state = State.Move;
            invuln = Mathf.Max(invuln, 0.2f);
        }

        void BeginCounterStep()
        {
            if (!SkipDeadCounterTargets()) { EndCounter(); return; }
            counterStepStart = stateTime;
            counterParried = counterHit = false;
            strikeStart = transform.position;
            var e = counterQueue[counterIndex];
            float d = e ? Flat(e.transform.position - transform.position).magnitude : 0f;
            counterTravel = e && d > Reach(e) + 0.2f ? Mathf.Clamp(d / 26f, 0.06f, 0.16f) : 0f;
            if (counterTravel > 0f) Sfx.Play(Sfx.I.dash, 0.35f, 1.25f);
        }

        void UpdateCounter(float dt)
        {
            if (counterQueue.Count == 0)
            {
                // bolt-only counter: a quick flick of the blade
                rig.SetTarget(WeaponRig.HGrip(40f, 1.5f), WeaponRig.HDir(40f, 0.6f), 30f);
                if (stateTime > 0.22f) state = State.Move;
                return;
            }
            if (counterIndex >= counterQueue.Count) { EndCounter(); return; }
            var e = counterQueue[counterIndex];
            float t = stateTime - counterStepStart;
            bool valid = e != null && !e.IsDead;
            if (valid)
            {
                Vector3 to = Flat(e.transform.position - transform.position);
                if (to.sqrMagnitude > 0.01f) { attackDir = to.normalized; facing = attackDir; transform.rotation = Quaternion.LookRotation(attackDir); }
            }
            int kindSwing = counterIndex % 2;

            if (valid && t < counterTravel)
            {
                float u = Mathf.Clamp01(t / counterTravel);
                Vector3 goal = e.transform.position - attackDir * (Reach(e) - 0.3f);
                goal.y = 0f;
                Place(Vector3.Lerp(strikeStart, goal, u * u * (3f - 2f * u)));
                kinematic = true;
                afterImageTimer -= Time.deltaTime;
                if (body && afterImageTimer <= 0f)
                {
                    afterImageTimer = 0.03f;
                    FX.AfterImage(body, new Color(0.25f, 0.8f, 2.2f, 0.45f), 0.2f, armor ? armor.Renderers : null);
                }
                rig.SetTarget(WeaponRig.HGrip(30f, 1.5f), WeaponRig.HDir(30f, 0.8f), 30f);
                return;
            }
            float tp = t - counterTravel;
            if (!counterParried)
            {
                // the parry: blade meets their weapon
                counterParried = true;
                rig.Snap(WeaponRig.HGrip(25f, 1.55f, 0.55f), WeaponRig.HDir(25f, 0.9f));
                if (valid)
                {
                    Vector3 clash = (Chest + e.Chest) * 0.5f + Vector3.up * 0.25f;
                    FX.Clash(clash, -attackDir, new Color(0.4f, 1.2f, 3f));
                    gm.HitStop(0.06f);
                    gm.cameraRig.Shake(0.3f);
                    gm.Rumble(0.4f, 0.8f, 0.1f);
                    Sfx.Play(Sfx.I.parry, 0.75f, Random.Range(0.95f, 1.08f), 0f);
                    Sfx.Play(Sfx.I.clang, 0.5f, 1.3f);
                }
            }
            if (tp < 0.09f)
            {
                rig.SetTarget(WeaponRig.HGrip(kindSwing == 0 ? 110f : -100f, 1.4f), WeaponRig.HDir(kindSwing == 0 ? 110f : -100f, 0.25f), 30f);
                rig.torsoTwist = kindSwing == 0 ? 32f : -32f;
                return;
            }
            float sw = Mathf.Clamp01((tp - 0.09f) / 0.08f);
            float a0 = kindSwing == 0 ? 110f : -100f, a1 = kindSwing == 0 ? -110f : 115f;
            float ang = Mathf.Lerp(a0, a1, 1f - (1f - sw) * (1f - sw));
            rig.Snap(WeaponRig.HGrip(ang, Mathf.Lerp(1.4f, 1.12f, sw)), WeaponRig.HDir(ang, Mathf.Lerp(0.2f, -0.1f, sw)));
            rig.SetTrail(sw > 0f && sw < 1f);
            rig.torsoTwist = Mathf.Lerp(kindSwing == 0 ? 32f : -32f, kindSwing == 0 ? -36f : 36f, sw);
            if (!counterHit && sw >= 0.55f)
            {
                counterHit = true;
                Sfx.Play(Sfx.I.swingHeavy, 0.6f, 1.15f);
                if (valid)
                {
                    HitEnemy(e, 22f, HitKind.Counter, 9f, 0.45f, 0.08f);
                    AddRage(6f);
                }
            }
            if (tp >= 0.26f)
            {
                rig.SetTrail(false);
                counterIndex++;
                if (counterIndex < counterQueue.Count) BeginCounterStep();
                else EndCounter();
            }
        }

        // ------------------------------------------------------------------ evade & vault

        void StartEvade()
        {
            evadeBuf = 0f;
            Vector3 dir = moveInput.sqrMagnitude > 0.04f ? moveInput.normalized : -facing;
            // evading into a robot vaults over it
            Enemy over = null;
            float best = 99f;
            foreach (var e in gm.enemies)
            {
                if (e == null || !e.Targetable || e.IsDowned || e.IsBoss) continue;
                Vector3 to = Flat(e.transform.position - transform.position);
                float d = to.magnitude;
                if (d > 2.9f + e.Radius || d < 0.2f) continue;
                if (Vector3.Angle(dir, to) > 38f) continue;
                if (d < best) { best = d; over = e; }
            }
            if (over != null && moveInput.sqrMagnitude > 0.04f) { StartVault(over); return; }

            state = State.Evade;
            stateTime = 0f;
            dashCd = dashCooldown;
            dashDir = dir;
            facing = moveInput.sqrMagnitude > 0.04f ? dir : facing;
            dashIframes = 0.32f;
            dashPerfectUsed = false;
            afterImageTimer = 0f;
            rig.SetTrail(false);
            Sfx.Play(Sfx.I.dash, 0.6f);
            gm.RegisterMove("evade");
            FX.Burst(transform.position + Vector3.up * 0.1f, new Color(0.5f, 0.8f, 1f, 0.8f), new Color(0.3f, 0.3f, 0.5f, 0.5f), 12, new Vector2(1f, 3f), new Vector2(0.15f, 0.35f), new Vector2(0.25f, 0.45f), 0f, false, 0.3f);
        }

        Vector3 UpdateEvade(float dt)
        {
            afterImageTimer -= dt;
            if (afterImageTimer <= 0f && body)
            {
                afterImageTimer = 0.03f;
                FX.AfterImage(body, new Color(0.3f, 0.9f, 2.2f, 0.5f), 0.3f, armor ? armor.Renderers : null);
            }
            rig.SetTarget(new Vector3(0.36f, 1.05f, -0.15f), new Vector3(0.25f, -0.25f, -1f), 25f);
            rig.leftHandWeight = 0f;
            rig.torsoBend = 14f;
            if (stateTime >= dashTime)
            {
                state = State.Move;
                moveVel = dashDir * moveSpeed;
                if (TryStartAction()) return Vector3.zero;
            }
            return dashDir * dashSpeed * (1f - 0.35f * stateTime / dashTime);
        }

        void StartVault(Enemy e)
        {
            state = State.Vault;
            stateTime = 0f;
            dashCd = dashCooldown;
            vaultFrom = transform.position;
            Vector3 d = Flat(e.transform.position - transform.position).normalized;
            vaultTo = gm.ClampToArena(e.transform.position + d * (e.Radius + 1.35f), 0.6f);
            vaultTo.y = 0f;
            vaultHeight = 1.85f * e.scale + 0.15f;
            dashIframes = VaultTime + 0.05f;
            dashPerfectUsed = false;
            facing = -d;
            e.OnVaulted();
            gm.RegisterMove("vault");
            gm.KeepComboAlive();
            Sfx.Play(Sfx.I.dash, 0.7f, 0.85f);
            Sfx.Play(Sfx.I.whoom, 0.25f, 1.8f, 0f);
            rig.SetTrail(true);
        }

        void UpdateVault()
        {
            float u = Mathf.Clamp01(stateTime / VaultTime);
            float e = u * u * (3f - 2f * u);
            Vector3 p = Vector3.Lerp(vaultFrom, vaultTo, e);
            p.y = vaultHeight * 4f * u * (1f - u);
            Place(p);
            kinematic = true;
            // front flip around the centre of mass
            Vector3 travel = Flat(vaultTo - vaultFrom);
            if (travel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(travel.normalized);
            float flip = 360f * e;
            var rot = Quaternion.Euler(flip, 0f, 0f);
            model.localRotation = rot;
            model.localPosition = Vector3.up * 1.0f - rot * (Vector3.up * 1.0f);
            rig.SetTarget(new Vector3(0.3f, 1.15f, 0.1f), new Vector3(0.2f, -0.3f, -1f), 20f);
            if (u >= 1f)
            {
                model.localRotation = Quaternion.identity;
                model.localPosition = Vector3.zero;
                transform.rotation = Quaternion.LookRotation(facing);
                rig.SetTrail(false);
                state = State.Move;
                FX.Dust(transform.position, 0.5f);
                TryStartAction();
            }
        }

        // ------------------------------------------------------------------ arc stun

        void StartStun()
        {
            stunBuf = 0f;
            stunCd = 0.65f;
            state = State.Stun;
            stateTime = 0f;
            impactDone = false;
            var t = PickTarget(moveInput, 5.5f);
            if (t) { attackDir = Flat(t.transform.position - transform.position).normalized; facing = attackDir; }
            else attackDir = moveInput.sqrMagnitude > 0.04f ? moveInput.normalized : facing;
            Sfx.Play(Sfx.I.swing, 0.55f, 0.8f);
        }

        Vector3 UpdateStun(float dt)
        {
            float t = stateTime;
            float u = Mathf.Clamp01((t - 0.04f) / 0.14f);
            float ang = Mathf.Lerp(-70f, 85f, 1f - (1f - u) * (1f - u));
            rig.Snap(WeaponRig.HGrip(ang, 1.32f, 0.6f), WeaponRig.HDir(ang, 0.1f));
            rig.SetTrail(u > 0f && u < 1f);
            rig.torsoTwist = Mathf.Lerp(-28f, 30f, u);
            if (!impactDone && t >= 0.12f)
            {
                impactDone = true;
                int n = 0;
                Vector3 tip = rig.TipWorld;
                foreach (var e in gm.enemies)
                {
                    if (e == null || !e.Targetable || e.IsDowned) continue;
                    Vector3 to = Flat(e.transform.position - transform.position);
                    if (to.magnitude > 4.6f + e.Radius || Vector3.Angle(attackDir, to) > 68f) continue;
                    if (e.Stun(e.type == EnemyType.Brute ? 2.3f : 1.8f))
                    {
                        n++;
                        FX.Arc(tip, e.Chest, new Color(0.6f, 1.5f, 3.8f, 1f), 0.22f, 0.05f);
                        FX.Arc(tip, e.Head, new Color(0.6f, 1.5f, 3.8f, 1f), 0.16f, 0.03f);
                    }
                }
                FX.Burst(tip, new Color(0.6f, 1.4f, 3.5f), Color.white, 26, new Vector2(3f, 9f), new Vector2(0.02f, 0.05f), new Vector2(0.12f, 0.3f), 0.5f, true, 0.1f, attackDir, 45f);
                FX.LightFlash(tip, new Color(0.5f, 0.8f, 1f), 7f, 7f, 0.18f);
                Sfx.Play(Sfx.I.stun, 0.75f, 1.05f);
                gm.cameraRig.Shake(0.15f);
                if (n > 0)
                {
                    gm.KeepComboAlive();
                    gm.RegisterMove("stun");
                    gm.hud.Popup(n > 1 ? "MULTI STUN" : "STUNNED", new Color(0.5f, 0.85f, 1f));
                }
            }
            if (t >= 0.2f && TryStartAction()) return Vector3.zero;
            if (t >= 0.32f) { state = State.Move; rig.SetTrail(false); }
            return Vector3.zero;
        }

        // ------------------------------------------------------------------ takedowns

        bool TryTakedown()
        {
            takedownBuf = 0f;
            if (gm.SpecialReady)
            {
                var t = PickTarget(moveInput, 9.5f);
                if (t) { StartSpecialTakedown(t); return true; }
            }
            var d = PickTarget(moveInput, 4.8f, true);
            if (d) { StartGroundTakedown(d); return true; }
            if (!gm.SpecialReady) Hint("BUILD A x" + GameManager.SpecialCost + " COMBO TO UNLOCK A TAKEDOWN");
            return false;
        }

        void StartSpecialTakedown(Enemy e)
        {
            gm.ConsumeSpecial();
            execTarget = e;
            e.BeginExecution();
            state = State.Takedown;
            stateTime = 0f;
            execStep = 0;
            strikeStart = transform.position;
            float d = Flat(e.transform.position - transform.position).magnitude;
            travelTime = d > Reach(e) + 0.2f ? Mathf.Clamp(d / 22f, 0.08f, 0.24f) : 0f;
            invuln = 3f;
            gm.SlowMo(0.3f, 1.15f);
            Vector3 line = Flat(e.transform.position - transform.position).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, line);
            if (Vector3.Dot(side, gm.cameraRig.transform.position - transform.position) < 0f) side = -side;
            gm.cameraRig.Cinematic((transform.position + e.transform.position) * 0.5f + Vector3.up * 1.1f, (side + Vector3.up * 0.25f - line * 0.2f).normalized, 4.3f, -9f, 1.1f);
            gm.hud.Letterbox(1.1f);
            gm.hud.Popup("TAKEDOWN", new Color(1f, 0.85f, 0.35f));
            gm.RegisterMove("takedown");
            Sfx.Play(Sfx.I.whoom, 0.8f, 0.8f, 0f);
        }

        void UpdateTakedown(float dt)
        {
            var e = execTarget;
            bool valid = e != null && !e.IsDead;
            float t = stateTime;
            if (valid)
            {
                Vector3 to = Flat(e.transform.position - transform.position);
                if (to.sqrMagnitude > 0.01f) { attackDir = to.normalized; facing = attackDir; }
            }
            if (t - travelTime < 0.16f) transform.rotation = Quaternion.LookRotation(attackDir); // the spin phase drives rotation itself
            if (valid && t < travelTime)
            {
                float u = t / travelTime;
                Vector3 goal = e.transform.position - attackDir * (Reach(e) - 0.4f);
                goal.y = 0f;
                Place(Vector3.Lerp(strikeStart, goal, u * u * (3f - 2f * u)));
                kinematic = true;
                afterImageTimer -= Time.deltaTime;
                if (body && afterImageTimer <= 0f)
                {
                    afterImageTimer = 0.03f;
                    FX.AfterImage(body, new Color(1f, 0.85f, 0.35f, 0.4f), 0.25f, armor ? armor.Renderers : null);
                }
                rig.SetTarget(WeaponRig.HGrip(115f, 1.35f), WeaponRig.HDir(115f, 0.2f), 30f);
                return;
            }
            float tt = t - travelTime;
            if (tt < 0.16f)
            {
                // hit 1: rising slash
                float u = Mathf.Clamp01(tt / 0.1f);
                float ang = Mathf.Lerp(115f, -60f, u);
                rig.Snap(WeaponRig.HGrip(ang, Mathf.Lerp(0.9f, 1.6f, u)), WeaponRig.HDir(ang, Mathf.Lerp(-0.3f, 0.8f, u)));
                rig.SetTrail(true);
                rig.torsoTwist = Mathf.Lerp(30f, -30f, u);
            }
            else if (tt < 0.42f)
            {
                // hit 2: spinning decapitation
                float u = Mathf.Clamp01((tt - 0.16f) / 0.2f);
                float total = 360f * (1f - (1f - u) * (1f - u));
                transform.rotation = Quaternion.LookRotation(attackDir) * Quaternion.Euler(0f, total, 0f);
                rig.Snap(WeaponRig.HGrip(80f, 1.55f, 0.6f), WeaponRig.HDir(80f, 0.05f));
                rig.SetTrail(true);
            }
            else
            {
                transform.rotation = Quaternion.LookRotation(attackDir);
                rig.SetTrail(false);
                rig.SetTarget(WeaponRig.HGrip(-60f, 1.1f), WeaponRig.HDir(-60f, -0.4f), 10f);
            }
            // hit triggers live outside the pose windows so a long frame can't skip them
            if (execStep == 0 && tt >= 0.07f)
            {
                execStep = 1;
                if (valid)
                {
                    FX.Sparks(e.Chest, -attackDir, true, true);
                    if (e.armor) e.armor.Chip(attackDir, 6f);
                    Sfx.Hit(true, true, false);
                    gm.HitStop(0.05f);
                    gm.cameraRig.Shake(0.35f);
                }
            }
            if (execStep == 1 && tt >= 0.33f)
            {
                execStep = 2;
                if (valid)
                {
                    e.Execute(new HitInfo(60f, attackDir, 8f, HitKind.Takedown, true));
                    FX.Clash(e.Head, attackDir, new Color(1f, 0.8f, 0.3f));
                    gm.cameraRig.Shake(0.7f);
                    gm.Pulse(0.7f);
                    gm.Rumble(0.8f, 1f, 0.2f);
                    Sfx.Play(Sfx.I.takedown, 0.95f, 1f, 0.05f);
                    AddRage(14f);
                }
            }
            if (tt >= 0.62f)
            {
                if (valid && execStep < 2) e.ReleaseExecution();
                execTarget = null;
                state = State.Move;
                invuln = 0.3f;
            }
        }

        void StartGroundTakedown(Enemy e)
        {
            execTarget = e;
            e.HoldDown(1.4f);
            state = State.GroundTakedown;
            stateTime = 0f;
            execStep = 0;
            strikeStart = transform.position;
            travelTime = 0.24f;
            gm.RegisterMove("groundtakedown");
            gm.hud.Popup("GROUND TAKEDOWN", new Color(1f, 0.75f, 0.35f));
            Sfx.Play(Sfx.I.dash, 0.5f, 0.9f);
        }

        void UpdateGroundTakedown(float dt)
        {
            var e = execTarget;
            bool valid = e != null && !e.IsDead && e.IsDowned;
            float t = stateTime;
            Vector3 aim = valid ? e.BodyCenter : transform.position + facing;
            Vector3 to = Flat(aim - transform.position);
            if (to.sqrMagnitude > 0.01f && t < travelTime) { attackDir = to.normalized; facing = attackDir; }
            if (t < travelTime)
            {
                float u = t / travelTime;
                Vector3 goal = aim - attackDir * 0.55f;
                goal.y = 0f;
                Vector3 p = Vector3.Lerp(strikeStart, goal, u * u * (3f - 2f * u));
                p.y = 0.9f * 4f * u * (1f - u);
                Place(p);
                kinematic = true;
                rig.SetTarget(WeaponRig.VGrip(-40f), WeaponRig.VDir(-40f), 22f);
                rig.leftHandWeight = 1f;
                return;
            }
            float tt = t - travelTime;
            float crouch = Mathf.Clamp01(tt / 0.12f) * (1f - Mathf.Clamp01((tt - 0.45f) / 0.25f));
            model.localPosition = Vector3.down * (0.38f * crouch);
            rig.torsoBend = 30f * crouch;
            float plunge = Mathf.Clamp01((tt - 0.06f) / 0.1f);
            float phi = Mathf.Lerp(-40f, 165f, plunge * plunge);
            rig.Snap(WeaponRig.VGrip(phi) + Vector3.down * (0.3f * crouch), WeaponRig.VDir(phi));
            rig.leftHandWeight = 1f;
            rig.SetTrail(plunge > 0f && plunge < 1f);
            if (execStep == 0 && plunge >= 1f)
            {
                execStep = 1;
                if (valid)
                {
                    e.Execute(new HitInfo(80f, Vector3.down, 0f, HitKind.Stab, true));
                    FX.Sparks(e.BodyCenter, Vector3.up, true, true);
                    FX.Shockwave(e.BodyCenter, 2.2f, new Color(0.4f, 1.3f, 3.2f), 0.35f);
                    gm.HitStop(0.1f);
                    gm.cameraRig.Shake(0.55f);
                    gm.cameraRig.Punch(-4f);
                    gm.Rumble(0.7f, 0.9f, 0.18f);
                    Sfx.Play(Sfx.I.takedown, 0.9f, 1.1f, 0.05f);
                    AddRage(10f);
                }
            }
            if (tt >= 0.75f)
            {
                model.localPosition = Vector3.zero;
                rig.leftHandWeight = 0f;
                execTarget = null;
                state = State.Move;
            }
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
            gm.RegisterMove("nova");
            Sfx.Play(Sfx.I.charge, 0.8f, 0.9f, 0f);
            FX.LightFlash(Chest, new Color(0.6f, 0.3f, 1f), 6f, 10f, 0.5f);
            FX.Burst(transform.position, new Color(0.7f, 0.4f, 1f), new Color(0.3f, 0.8f, 1f), 40, new Vector2(2f, 6f), new Vector2(0.05f, 0.12f), new Vector2(0.3f, 0.7f), -0.5f, true, 1.2f, Vector3.up, 25f);
        }

        void UpdateNova()
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
                rig.SetTarget(WeaponRig.VGrip(-25f), WeaponRig.VDir(-25f), 18f);
                rig.SetTrail(false);
            }
            else if (t < NovaSlamTime)
            {
                float u = (t - 0.42f) / (NovaSlamTime - 0.42f);
                float phi = Mathf.Lerp(-25f, 140f, u * u);
                rig.Snap(WeaponRig.VGrip(phi), WeaponRig.VDir(phi));
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
            FX.Dust(c, 3f);
            gm.cameraRig.Shake(1f);
            gm.cameraRig.Punch(10f);
            gm.Pulse(1f);
            gm.HitStop(0.1f);
            gm.hud.Flash(0.55f);
            gm.Rumble(1f, 1f, 0.45f);
            Sfx.Play(Sfx.I.nova, 1f, 1f, 0f);

            var list = gm.enemies;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (i >= list.Count) continue;
                var e = list[i];
                if (e == null || !e.Targetable) continue;
                Vector3 to = Flat(e.transform.position - c);
                if (to.magnitude > NovaRadius + e.Radius) continue;
                Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : Vector3.forward;
                e.TakeHit(new HitInfo(80f * Random.Range(0.95f, 1.1f), dir, 16f, HitKind.Nova, true), transform.position);
            }
            var plist = gm.projectiles;
            for (int i = plist.Count - 1; i >= 0; i--)
                if (i < plist.Count && plist[i] && !plist[i].reflected) plist[i].Pop();
        }

        // ------------------------------------------------------------------ damage

        public bool TakeDamage(float dmg, Vector3 from, float knockback, bool unblockable = false)
        {
            if (IsDead || gm == null || gm.IsGameOver) return false;
            if (dashIframes > 0f)
            {
                if (!dashPerfectUsed) PerfectDodge();
                return false;
            }
            if (invuln > 0f || state == State.Nova || state == State.Counter || state == State.Takedown || state == State.Vault) return false;

            Health -= dmg;
            invuln = 0.6f;
            hurtFlash = 1f;
            Vector3 away = Flat(transform.position - from);
            knock = (away.sqrMagnitude > 0.001f ? away.normalized : -transform.forward) * knockback;
            gm.cameraRig.Shake(0.5f);
            gm.Pulse(0.6f);
            gm.BreakCombo();
            gm.NotifyPlayerHit();
            gm.hud.OnPlayerHurt();
            gm.Rumble(0.8f, 0.5f, 0.2f);
            Sfx.Play(Sfx.I.hurt, 0.9f);
            FX.Burst(Chest, new Color(1f, 0.2f, 0.25f), new Color(1f, 0.6f, 0.3f), 22, new Vector2(3f, 8f), new Vector2(0.05f, 0.1f), new Vector2(0.2f, 0.4f), 1.5f, true, 0.2f);
            if (!unblockable) Hint("COUNTER [RMB / K] WHEN BLUE LIGHTNING APPEARS");
            else Hint("RED = UNBLOCKABLE - EVADE [SPACE]");

            if (execTarget != null && state == State.GroundTakedown) execTarget = null;
            rig.SetTrail(false);
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            if (Health <= 0f) { Die(); return true; }
            state = State.Hurt;
            stateTime = 0f;
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
            gm.RegisterMove("perfectdodge");
            gm.KeepComboAlive();
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
            if (armor) armor.Decapitate(Vector3.up * 5f + Random.insideUnitSphere * 2f);
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
            if (armor) armor.PowerDown(u);
        }

        // ------------------------------------------------------------------ helpers

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        void Place(Vector3 p)
        {
            bool was = cc.enabled;
            cc.enabled = false;
            transform.position = p;
            cc.enabled = was;
        }

        void Hint(string text)
        {
            if (hintCd > 0f || autopilot) return;
            hintCd = 4f;
            gm.hud.Hint(text);
        }

        static Vector3 RestGrip(float speed)
        {
            float bob = Mathf.Sin(Time.time * 9.5f) * 0.03f * Mathf.Clamp01(speed / 6f);
            return new Vector3(0.34f, 1.0f + bob, 0.3f);
        }

        static Vector3 RestDir() => new Vector3(0.12f, 0.62f, 1f);

        void UpdateBodyGlow(float dt)
        {
            if (!armor) return;
            hurtFlash = Mathf.MoveTowards(hurtFlash, 0f, dt * 4f);
            Color c = Color.black;
            if (NovaReady) c += new Color(0.35f, 0.15f, 0.7f) * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f));
            if (state == State.Evade || state == State.Vault) c += new Color(0.1f, 0.4f, 0.8f);
            c += new Color(2.5f, 0.2f, 0.2f) * hurtFlash;
            if (invuln > 0f && state == State.Move && Mathf.Repeat(Time.unscaledTime * 14f, 1f) > 0.5f) c += new Color(0.4f, 0.4f, 0.4f);
            float glow = 1f + (gm.SpecialReady ? 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 9f) : 0f) + (state == State.Takedown ? 1.5f : 0f);
            armor.SetEmission(c, glow);
        }

        // ------------------------------------------------------------------ autopilot

        void Autopilot()
        {
            Vector3 pos = transform.position;
            Vector3 mv = Vector3.zero;
            Vector3 threatAway = Vector3.zero;
            bool threatened = false, counterable = false;
            int close = 0, mid = 0;
            Enemy shieldInFront = null, armoredNear = null, downedNear = null;
            foreach (var e in gm.enemies)
            {
                if (e == null || !e.Targetable) continue;
                Vector3 to = Flat(e.transform.position - pos);
                float d = to.magnitude;
                if (d < 3.6f) close++;
                if (d < 8.5f) mid++;
                if (e.CanBeCountered && d < counterRange) counterable = true;
                if (e.UnblockableIncoming && e.IsThreatening(pos)) { threatened = true; threatAway -= to.normalized; }
                if (e.ShieldUp && d < 4f && !e.IsDowned) shieldInFront = e;
                if (e.IsArmored && d < 4.5f) armoredNear = e;
                if (e.IsDowned && d < 4f) downedNear = e;
            }
            foreach (var p in gm.projectiles)
                if (p && !p.reflected && (p.transform.position - Chest).magnitude < 3.6f) counterable = true;

            // react to counter windows like a (very good) human: a short, random reaction delay
            if (counterable)
            {
                if (apCounterAt < 0f) apCounterAt = Time.unscaledTime + Random.Range(0.08f, 0.24f);
                if (Time.unscaledTime >= apCounterAt) { counterBuf = 0.2f; apCounterAt = -1f; }
            }
            else apCounterAt = -1f;

            apThink -= Time.unscaledDeltaTime;
            var target2 = PickTarget(Vector3.zero, 30f);
            if (threatened && dashCd <= 0f)
            {
                Vector3 away = threatAway.sqrMagnitude > 0.01f ? threatAway.normalized : -transform.forward;
                Vector3 side = Vector3.Cross(Vector3.up, away);
                mv = (away + side * 0.6f).normalized;
                evadeBuf = 0.2f;
            }
            else if (!counterable)
            {
                if (NovaReady && mid >= 4) novaBuf = 0.2f;
                else if (gm.SpecialReady && target2 != null) takedownBuf = 0.2f;
                else if (downedNear != null && close <= 1) { takedownBuf = 0.2f; mv = Flat(downedNear.BodyCenter - pos).normalized * 0.3f; }
                else if ((armoredNear != null || shieldInFront != null) && stunCd <= 0f) { mv = Flat((armoredNear ? armoredNear : shieldInFront).transform.position - pos).normalized * 0.3f; stunBuf = 0.2f; }
                else if (Health < maxHealth * 0.35f && gm.orbs.Count > 0 && gm.orbs[0] != null) mv = Flat(gm.orbs[0].transform.position - pos).normalized;
                else if (target2 != null)
                {
                    Vector3 to = Flat(target2.transform.position - pos);
                    if (to.magnitude > freeflowRange) mv = to.normalized;
                    else if (apThink <= 0f)
                    {
                        mv = to.normalized * 0.6f;
                        strikeBuf = 0.2f;
                        apThink = Random.Range(0.02f, 0.12f);
                    }
                }
                else
                {
                    Vector3 home = Flat(-pos);
                    if (home.magnitude > 2f) mv = home.normalized * 0.6f;
                }
            }
            moveInput = mv;
        }
    }
}
