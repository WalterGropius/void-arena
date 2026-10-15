using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HackAndSlash
{
    /// <summary>
    /// K-7, the Void Knight. Arkham Knight's FreeFlow (directional strikes that warp across the crowd, single and multi
    /// counters, vaults, cape-style arc stun, beatdowns, special-combo, environmental and ground takedowns) meets
    /// Robo Recall's hands (an arc tether that yanks robots in to be held, ripped apart and thrown, or yanks their
    /// guns and shields away) plus a sidearm with Matrix bullet time and the Void Cascade, a time-stopped multi
    /// takedown. All movement beyond locomotion is procedural: humanoid IK drives the hands, feet, hips and spine.
    ///
    /// The class is split across files: this one (state machine, input, movement, damage), PlayerCombat (melee),
    /// PlayerGadgets (blaster, tether, cascade) and PlayerAutopilot (the attract-mode fighter).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public partial class PlayerController : MonoBehaviour
    {
        public enum State { Move, Strike, Counter, Evade, Vault, Stun, Takedown, GroundTakedown, Cascade, Tether, Hold, Zip, Hurt, Dormant, Dead }

        public const float VoidMax = 100f;
        public const int AmmoMax = 6;

        [Header("Rig")]
        public Animator animator;
        public Transform model;
        public WeaponRig rig;
        public SkinnedMeshRenderer body;
        public Material bladeMaterial;

        [Header("Tuning")]
        public float maxHealth = 100f;
        public float moveSpeed = 6.6f;
        public float sprintMultiplier = 1.5f;
        public float dashSpeed = 20f;
        public float dashTime = 0.2f;
        public float dashCooldown = 0.3f;
        public float freeflowRange = 10f;
        public float counterRange = 7.5f;
        public bool autopilot;

        // ---------------------------------------------------------------- public state

        public float Health { get; private set; }
        public float Void { get; private set; }
        public int Ammo { get; private set; } = AmmoMax;
        public bool IsDead => state == State.Dead;
        public State CurrentState => state;
        public Vector3 Velocity => moveVel;
        public Vector3 Chest => transform.position + Vector3.up * 1.25f;
        public float Radius => 0.35f;
        public Enemy CurrentTarget => target;
        public bool InCinematic => state == State.Takedown || state == State.Cascade || (state == State.Counter && counterQueue.Count > 1);
        public ArmorKit Armor => armor;
        /// <summary>Robots a Void Cascade would take down right now (one per full third of the meter).</summary>
        public int CascadeTargets => Mathf.FloorToInt(Void / (VoidMax / 3f) + 0.001f);
        public float TetherReady01 => Mathf.Clamp01(1f - tetherCd / TetherCooldown);
        public bool Holding => state == State.Hold && held != null;
        /// <summary>Where a held robot's chest hangs: at arm's length in front of the Knight.</summary>
        public Vector3 HoldPoint => transform.position + transform.forward * 1.05f + Vector3.up * 1.55f;
        public bool Aiming => aiming;
        public float AimTime => aimTime;
        public Enemy AimTarget => aimTarget;
        /// <summary>Time scale the Knight is asking for (bullet time while aiming, the Cascade's near-stop).</summary>
        public float FocusScale => focusScale;
        /// <summary>Cutscenes and menus: the Knight ignores the controls (the autopilot still works).</summary>
        public bool InputLocked { get; set; }

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        static readonly int FreeFallId = Animator.StringToHash("FreeFall");
        static readonly int JumpId = Animator.StringToHash("Jump");

        CharacterController cc;
        GameManager gm;
        ArmorKit armor;
        Cape cape;
        MeshRenderer capeRenderer;
        bool capeTextured;
        State state;
        float stateTime;
        Vector3 moveInput, moveVel, knock, facing = Vector3.forward, attackDir = Vector3.forward, dashDir;
        float vy;
        bool sprintHeld;
        float strikeBuf, counterBuf, evadeBuf, stunBuf, takedownBuf, cascadeBuf, tetherBuf, fireBuf;
        float dashCd, stunCd, counterLock, invuln, dashIframes, hurtFlash, afterImageTimer, perfectDodgeCd, hintCd;
        bool dashPerfectUsed, impactDone, whooshDone, whiff, kinematic;
        float focusScale = 1f, dormantLevel;

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
            BuildGadgets();
            if (animator)
            {
                var gmi = GameManager.I;
                Material capeMat = gmi && gmi.clothMaterial ? new Material(gmi.clothMaterial) : FX.Lit(new Color(0.05f, 0.05f, 0.06f), 0f, 0.35f);
                cape = Cape.Attach(animator, transform, capeMat);
                if (cape) capeRenderer = cape.GetComponent<MeshRenderer>();
            }
        }

        public void ResetPlayer(Vector3 pos, float yaw = 0f)
        {
            gm = GameManager.I;
            Place(pos);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            facing = transform.forward;
            Health = maxHealth;
            Void = 0f;
            Ammo = AmmoMax;
            state = State.Move;
            stateTime = 0f;
            moveVel = knock = Vector3.zero;
            vy = 0f;
            target = null;
            execTarget = null;
            counterQueue.Clear();
            cascadeQueue.Clear();
            ReleaseHeld(false);
            EndAim();
            tetherCd = 0f;
            tetherLine.enabled = false;
            strikeBuf = counterBuf = evadeBuf = stunBuf = takedownBuf = cascadeBuf = tetherBuf = fireBuf = 0f;
            dashCd = stunCd = counterLock = invuln = dashIframes = hurtFlash = 0f;
            focusScale = 1f;
            dormantLevel = 0f;
            animator.enabled = true;
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            rig.SetTrail(false);
            rig.rightHandWeight = 1f;
            rig.leftHandWeight = 0f;
            rig.leftHandAimWeight = 0f;
            rig.lookWeight = 0f;
            rig.torsoTwist = rig.torsoBend = 0f;
            rig.bodyOffset = Vector3.zero;
            rig.bodyPitch = rig.bodyRoll = 0f;
            rig.plantFeet = 0f;
            rig.rightFootWeight = rig.leftFootWeight = 0f;
            rig.Snap(RestGrip(0f), RestDir());
            if (armor) armor.Restore();
            if (cape) cape.Reset();
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            Health = Mathf.Min(maxHealth, Health + amount);
            FX.Burst(Chest, new Color(0.4f, 1f, 0.5f), new Color(0.8f, 1f, 0.6f), 22, new Vector2(1.5f, 4f), new Vector2(0.08f, 0.18f), new Vector2(0.4f, 0.8f), -0.4f, false, 0.5f);
            if (gm && gm.hud) gm.hud.OnHeal();
        }

        /// <summary>Fills the Void meter (kills, counters, takedowns). Each full third is one more Cascade target.</summary>
        public void AddVoid(float amount)
        {
            if (IsDead || gm == null) return;
            int before = CascadeTargets;
            Void = Mathf.Clamp(Void + amount, 0f, VoidMax);
            int after = CascadeTargets;
            if (after > before)
            {
                gm.hud.Popup(after >= 3 ? "VOID CASCADE x3  [C]" : "VOID CASCADE x" + after + "  [C]", new Color(0.75f, 0.5f, 1f));
                Sfx.Play(Sfx.I.charge, 0.45f, 1.1f + 0.15f * after, 0f);
            }
        }

        /// <summary>Struck a charged robot: the current arcs into the Knight (this cannot be dodged by blocking).</summary>
        public void Shock(float dmg, Vector3 from)
        {
            if (IsDead || invuln > 0f) return;
            Hint("CHARGED - ARC STUN IT [E] FIRST");
            TakeDamage(dmg, from, 6f, true);
        }

        /// <summary>The prologue: K-7 kneels, powered down, until HALCYON finishes reattaching his limbs.</summary>
        public void SetDormant(bool on)
        {
            if (on)
            {
                state = State.Dormant;
                stateTime = 0f;
                dormantLevel = 1f;
                moveVel = Vector3.zero;
                rig.SetTrail(false);
            }
            else if (state == State.Dormant)
            {
                state = State.Move;
                stateTime = 0f;
                // power-up surge
                FX.Burst(Chest, new Color(0.3f, 1.2f, 3f), Color.white, 40, new Vector2(2f, 6f), new Vector2(0.03f, 0.08f), new Vector2(0.3f, 0.7f), -0.3f, true, 0.6f);
                FX.Shockwave(transform.position, 4f, new Color(0.4f, 1.3f, 3.2f), 0.6f);
                FX.LightFlash(Chest, new Color(0.4f, 0.8f, 1f), 8f, 8f, 0.4f);
                Sfx.Play(Sfx.I.charge, 0.8f, 0.8f, 0f);
                if (gm) gm.cameraRig.Shake(0.3f);
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
            if (autopilot && !IsDead && !gm.IsGameOver && state != State.Dormant) Autopilot();

            dashCd -= dt; stunCd -= dt; invuln -= dt; dashIframes -= dt; counterLock -= udt;
            perfectDodgeCd -= udt; hintCd -= udt;
            stateTime += dt;
            kinematic = false;
            UpdateGadgetTimers(dt, udt);

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
                case State.Cascade: UpdateCascade(udt); break;
                case State.Tether: planar = UpdateTether(dt); break;
                case State.Hold: planar = UpdateHold(dt); break;
                case State.Zip: UpdateZip(dt); break;
                case State.Hurt: planar = UpdateHurt(dt); break;
                case State.Dormant: UpdateDormant(dt); break;
                case State.Dead: UpdateDead(); break;
            }
            UpdateAim(dt, udt);

            // integrate movement
            knock = Vector3.MoveTowards(knock, Vector3.zero, 30f * dt);
            if (kinematic) vy = 0f;
            else
            {
                vy = cc.isGrounded ? -3f : vy - 30f * dt;
                if (cc.enabled) cc.Move((state == State.Dead || state == State.Dormant ? Vector3.zero : planar + knock) * dt + Vector3.up * (vy * dt));
            }
            Vector3 clamped = gm.ClampToArena(transform.position, 0.4f);
            if ((clamped - transform.position).sqrMagnitude > 1e-6f) Place(clamped);

            bool selfRotating = state == State.Dead || state == State.Vault || state == State.Takedown || state == State.Cascade || state == State.Dormant || (state == State.Strike && strikeKind == 5);
            if (!selfRotating && facing.sqrMagnitude > 0.001f)
            {
                float sharp = state == State.Move ? (aiming ? 30f : 16f) : 40f;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(facing), 1f - Mathf.Exp(-sharp * dt));
            }

            if (animator.enabled)
            {
                float animSpeed;
                if (state == State.Move || state == State.Hold) animSpeed = new Vector3(moveVel.x, 0f, moveVel.z).magnitude;
                else if (state == State.Evade || (state == State.Strike && stateTime < travelTime)) animSpeed = 6f;
                else if (state == State.Vault || state == State.Counter || state == State.Takedown || state == State.Cascade || state == State.Zip || state == State.Dormant) animSpeed = 0f;
                else animSpeed = Mathf.Min(planar.magnitude * 0.6f, 2.5f);
                animator.SetFloat(SpeedId, animSpeed, 0.05f, Mathf.Max(dt, 0.0001f));
                animator.SetFloat(MotionSpeedId, 1f);
                animator.SetBool(GroundedId, true);
                animator.SetBool(FreeFallId, false);
                animator.SetBool(JumpId, false);
            }

            rig.torsoTwist = Mathf.MoveTowards(rig.torsoTwist, 0f, dt * 160f);
            rig.torsoBend = Mathf.MoveTowards(rig.torsoBend, 0f, dt * 80f);
            if (state == State.Move || state == State.Hold || state == State.Hurt) rig.Relax(dt, 7f);
            rig.Tick(dt);
            if (state != State.Dead) UpdateBodyGlow(dt);

            if (!capeTextured && TexLibrary.Ready)
            {
                capeTextured = true;
                if (capeRenderer) capeRenderer.sharedMaterial = TexLibrary.Mat("Cloth");
                if (armor) armor.RefreshDetail(0.35f);
            }
        }

        void LateUpdate() => UpdateBlasterPose();

        // ------------------------------------------------------------------ input

        float aimHold, gpChordX = -1f, gpChordY = -1f;
        bool aimHeldLast;

        void ReadInput(float udt)
        {
            strikeBuf -= udt; counterBuf -= udt; evadeBuf -= udt; stunBuf -= udt; takedownBuf -= udt; cascadeBuf -= udt; tetherBuf -= udt; fireBuf -= udt;
            Vector2 mv = Vector2.zero;
            const float buffer = 0.22f;
            bool aimHeld = false;
            sprintHeld = false;

            if (!InputLocked && !(gm && gm.Paused))
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed) mv.y += 1f;
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed) mv.y -= 1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) mv.x += 1f;
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) mv.x -= 1f;
                    if (kb.jKey.wasPressedThisFrame) strikeBuf = buffer;
                    if (kb.kKey.wasPressedThisFrame) counterBuf = buffer;
                    if (kb.spaceKey.wasPressedThisFrame) evadeBuf = buffer;
                    if (kb.leftShiftKey.isPressed) sprintHeld = true;
                    if (kb.eKey.wasPressedThisFrame || kb.lKey.wasPressedThisFrame) stunBuf = buffer;
                    if (kb.fKey.wasPressedThisFrame) takedownBuf = buffer;
                    if (kb.cKey.wasPressedThisFrame) cascadeBuf = buffer;
                    if (kb.rKey.wasPressedThisFrame) tetherBuf = buffer;
                    if (kb.qKey.isPressed) aimHeld = true;
                    if (kb.pKey.wasPressedThisFrame)
                    {
                        autopilot = !autopilot;
                        if (gm && gm.hud) gm.hud.Popup(autopilot ? "AUTOPILOT ENGAGED" : "MANUAL CONTROL", new Color(0.5f, 0.9f, 1f));
                    }
                }
                var ms = Mouse.current;
                if (ms != null)
                {
                    if (ms.leftButton.wasPressedThisFrame) { if (aiming) fireBuf = buffer; else strikeBuf = buffer; }
                    if (ms.rightButton.wasPressedThisFrame) counterBuf = buffer;
                    if (ms.middleButton.wasPressedThisFrame) stunBuf = buffer;
                }
                var gp = Gamepad.current;
                if (gp != null)
                {
                    Vector2 stick = gp.leftStick.ReadValue();
                    if (stick.sqrMagnitude > mv.sqrMagnitude) mv = stick;
                    if (gp.leftStickButton.isPressed) sprintHeld = true;
                    float now = Time.unscaledTime;
                    if (gp.buttonWest.wasPressedThisFrame) { strikeBuf = buffer; gpChordX = now; }
                    if (gp.buttonNorth.wasPressedThisFrame) { counterBuf = buffer; gpChordY = now; }
                    // Arkham: X + Y together is the special takedown
                    if (gpChordX > 0f && gpChordY > 0f && Mathf.Abs(gpChordX - gpChordY) < 0.09f) { takedownBuf = buffer; strikeBuf = counterBuf = 0f; gpChordX = gpChordY = -1f; }
                    if (gp.buttonSouth.wasPressedThisFrame) evadeBuf = buffer;
                    if (gp.buttonEast.wasPressedThisFrame) stunBuf = buffer;
                    if (gp.leftTrigger.isPressed) aimHeld = true;
                    if (gp.rightTrigger.wasPressedThisFrame) { if (aiming || gp.leftTrigger.isPressed) fireBuf = buffer; else takedownBuf = buffer; }
                    if (gp.rightShoulder.wasPressedThisFrame) tetherBuf = buffer;
                    if (gp.leftShoulder.wasPressedThisFrame) cascadeBuf = buffer;
                }
            }

            // the blaster: a tap is a quickfire at the current target, holding raises it and slows time
            if (aimHeld) aimHold += udt;
            if (aimHeld && !aimHeldLast) aimHold = 0f;
            if (!aimHeld && aimHeldLast && aimHold < 0.18f && !aiming) QuickfireRequested();
            aimWanted = aimHeld && aimHold >= 0.18f;
            aimHeldLast = aimHeld;

            mv = Vector2.ClampMagnitude(mv, 1f);
            Vector3 camF = Vector3.forward, camR = Vector3.right;
            if (gm && gm.cameraRig) { camF = gm.cameraRig.PlanarForward; camR = gm.cameraRig.PlanarRight; }
            moveInput = camF * mv.y + camR * mv.x;
        }

        void ClearBuffers() => strikeBuf = counterBuf = evadeBuf = stunBuf = takedownBuf = cascadeBuf = tetherBuf = fireBuf = 0f;

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
            if (cascadeBuf > 0f && TryCascade()) return true;
            if (takedownBuf > 0f && TryTakedown()) return true;
            if (tetherBuf > 0f && TryTether()) return true;
            if (stunBuf > 0f && stunCd <= 0f) { StartStun(); return true; }
            if (allowStrike && strikeBuf > 0f && !aiming) { StartStrike(); return true; }
            return false;
        }

        Vector3 UpdateMove(float dt)
        {
            if (TryStartAction()) return Vector3.zero;
            bool sprint = sprintHeld && !aiming && moveInput.sqrMagnitude > 0.3f;
            float speed = moveSpeed * (aiming ? 0.45f : sprint ? sprintMultiplier : 1f);
            Vector3 desired = moveInput * speed;
            moveVel = Vector3.MoveTowards(moveVel, desired, 70f * dt);
            if (aiming) facing = AimFacing();
            else if (moveInput.sqrMagnitude > 0.01f) facing = moveInput.normalized;
            rig.SetTarget(RestGrip(moveVel.magnitude), RestDir(), 14f);
            rig.leftHandWeight = Mathf.MoveTowards(rig.leftHandWeight, 0f, dt * 5f);
            if (!aiming) rig.lookWeight = Mathf.MoveTowards(rig.lookWeight, 0f, dt * 4f);
            // a sprint leans the body into the run; a hard stop plants the feet
            rig.bodyPitch = Mathf.Lerp(rig.bodyPitch, sprint ? 9f : 0f, 1f - Mathf.Exp(-8f * dt));
            float turn = Vector3.SignedAngle(transform.forward, facing, Vector3.up);
            rig.bodyRoll = Mathf.Lerp(rig.bodyRoll, Mathf.Clamp(-turn * 0.06f, -8f, 8f) * Mathf.Clamp01(moveVel.magnitude / moveSpeed), 1f - Mathf.Exp(-8f * dt));
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

        void UpdateDormant(float dt)
        {
            // kneeling, head bowed, lights almost out
            dormantLevel = 1f;
            rig.bodyOffset = Vector3.Lerp(rig.bodyOffset, new Vector3(0f, -0.55f, 0.05f), 1f - Mathf.Exp(-6f * dt));
            rig.bodyPitch = Mathf.Lerp(rig.bodyPitch, 22f, 1f - Mathf.Exp(-6f * dt));
            rig.plantFeet = 1f;
            rig.torsoBend = 25f;
            rig.SetTarget(new Vector3(0.3f, 0.5f, 0.45f), new Vector3(0.1f, -1f, 0.25f), 6f);
            rig.lookWeight = 0f;
            ClearBuffers();
        }

        // ------------------------------------------------------------------ damage

        public bool TakeDamage(float dmg, Vector3 from, float knockback, bool unblockable = false)
        {
            if (IsDead || gm == null || gm.IsGameOver || state == State.Dormant) return false;
            if (dashIframes > 0f)
            {
                if (!dashPerfectUsed) PerfectDodge();
                return false;
            }
            if (invuln > 0f || state == State.Counter || state == State.Takedown || state == State.Vault || state == State.Cascade || state == State.Zip) return false;

            Health -= dmg;
            invuln = 0.6f;
            hurtFlash = 1f;
            Vector3 away = Flat(transform.position - from);
            knock = (away.sqrMagnitude > 0.001f ? away.normalized : -transform.forward) * knockback;
            rig.Jolt(away.sqrMagnitude > 0.001f ? away.normalized : -transform.forward, 1f);
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
            ReleaseHeld(false);
            EndAim();
            tetherLine.enabled = false;
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
            AddVoid(10f);
            Sfx.Play(Sfx.I.ting, 0.7f, 1.4f, 0f);
            if (Sfx.I.focusIn) Sfx.Play(Sfx.I.focusIn, 0.35f, 1.2f, 0f);
        }

        void Die()
        {
            Health = 0f;
            state = State.Dead;
            stateTime = 0f;
            rig.SetTrail(false);
            rig.leftHandAimWeight = 0f;
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
            if (hintCd > 0f || autopilot || gm == null) return;
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
            if (CascadeTargets > 0) c += new Color(0.35f, 0.15f, 0.7f) * (0.25f * CascadeTargets) * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f));
            if (state == State.Evade || state == State.Vault || state == State.Cascade) c += new Color(0.1f, 0.4f, 0.8f);
            c += new Color(2.5f, 0.2f, 0.2f) * hurtFlash;
            if (invuln > 0f && state == State.Move && Mathf.Repeat(Time.unscaledTime * 14f, 1f) > 0.5f) c += new Color(0.4f, 0.4f, 0.4f);
            float glow = 1f + (gm.SpecialReady ? 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 9f) : 0f) + (state == State.Takedown || state == State.Cascade ? 1.5f : 0f);
            if (state == State.Dormant) glow = 0.08f + (Random.value < 0.03f ? 0.6f : 0f);
            armor.SetEmission(c, glow);
        }
    }
}
