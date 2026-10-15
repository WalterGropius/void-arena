using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    /// <summary>
    /// The Knight's gadgets. The blaster (Officer Dallow's, once): tap for a quickfire at the current target, hold to
    /// raise it and slow time (Matrix bullet time), fire with the attack button. The arc tether (Robo Recall's hands,
    /// Arkham's batclaw): yank a robot into your grip to rip apart or throw, yank guns and shields away, or zip into a
    /// Juggernaut feet first. The Void Cascade: time all but stops while the Knight blinks through the crowd.
    /// </summary>
    public partial class PlayerController
    {
        const float TetherCooldown = 2.2f;
        const float TetherRange = 18f;
        const float FocusMax = 2.6f;

        // blaster
        Transform blaster, blasterMuzzle;
        Light muzzleLight;
        float blasterShown, ammoRegen, lastShot = -9f, focus = FocusMax, aimTime, fireCooldown;
        bool aiming, aimWanted;
        Enemy aimTarget;

        // tether
        LineRenderer tetherLine;
        float tetherCd, tetherTime, tetherTravel, holdTime;
        Enemy tetherTarget, held;
        TetherAction tetherAction;
        Vector3 tetherEnd, zipFrom;

        // cascade
        readonly List<Enemy> cascadeQueue = new List<Enemy>();
        int cascadeIndex;
        float cascadeStep, cascadeClock;
        Vector3 cascadeFrom;

        void BuildGadgets()
        {
            var metal = FX.Lit(new Color(0.12f, 0.12f, 0.14f), 0.9f, 0.7f);
            var glow = FX.Emissive(new Color(1f, 0.55f, 0.15f), 3f);
            blaster = Weapons.Blaster(transform, metal, glow, out blasterMuzzle);
            muzzleLight = new GameObject("MuzzleFlash").AddComponent<Light>();
            muzzleLight.transform.SetParent(blasterMuzzle, false);
            muzzleLight.type = LightType.Point;
            muzzleLight.color = new Color(1f, 0.7f, 0.35f);
            muzzleLight.range = 6f;
            muzzleLight.intensity = 0f;
            muzzleLight.shadows = LightShadows.None;

            var tl = new GameObject("TetherLine");
            tl.transform.SetParent(transform, false);
            tetherLine = tl.AddComponent<LineRenderer>();
            tetherLine.sharedMaterial = FX.FlatMat;
            tetherLine.shadowCastingMode = ShadowCastingMode.Off;
            tetherLine.receiveShadows = false;
            tetherLine.positionCount = 12;
            tetherLine.widthMultiplier = 0.035f;
            tetherLine.numCapVertices = 2;
            tetherLine.enabled = false;
            FX.SetColor(tetherLine, new Color(0.5f, 1.6f, 3.6f, 1f));
        }

        void UpdateGadgetTimers(float dt, float udt)
        {
            tetherCd -= dt;
            blasterShown -= udt;
            fireCooldown -= udt;
            if (muzzleLight) muzzleLight.intensity = Mathf.MoveTowards(muzzleLight.intensity, 0f, udt * 120f);
            // rounds come back one at a time once the Knight stops firing
            if (Ammo < AmmoMax && Time.unscaledTime - lastShot > 0.8f)
            {
                ammoRegen += dt;
                if (ammoRegen >= 1.1f) { ammoRegen = 0f; Ammo++; }
            }
            if (!aiming) focus = Mathf.Min(FocusMax, focus + udt * 0.5f);
        }

        Transform Bone(HumanBodyBones b) => animator ? animator.GetBoneTransform(b) : null;

        Vector3 LeftShoulder
        {
            get
            {
                var t = Bone(HumanBodyBones.LeftUpperArm);
                return t ? t.position : transform.position + transform.up * 1.45f - transform.right * 0.2f;
            }
        }

        // ------------------------------------------------------------------ blaster

        Vector3 AimFacing()
        {
            Vector3 f = gm && gm.cameraRig ? gm.cameraRig.PlanarForward : facing;
            return f.sqrMagnitude > 0.01f ? f.normalized : facing;
        }

        Ray AimRay
        {
            get
            {
                if (gm && gm.cameraRig)
                {
                    var r = gm.cameraRig.AimRay;
                    // start the ray past the Knight so it never hits him
                    float skip = Vector3.Dot(Chest - r.origin, r.direction);
                    return new Ray(r.origin + r.direction * Mathf.Max(0f, skip), r.direction);
                }
                return new Ray(Chest, facing);
            }
        }

        void UpdateAim(float dt, float udt)
        {
            bool can = state == State.Move && !IsDead && !InputLocked && gm.CombatOn;
            if (aimWanted && can && !aiming)
            {
                aiming = true;
                aimTime = 0f;
                if (Sfx.I.focusIn && focus > 0.3f) Sfx.Play(Sfx.I.focusIn, 0.5f, 1f, 0f);
            }
            else if (aiming && (!aimWanted || !can)) EndAim();

            if (!aiming)
            {
                if (state != State.Cascade) focusScale = Mathf.MoveTowards(focusScale, 1f, udt * 4f);
                if (blasterShown <= 0f && state != State.Hold && state != State.Tether && state != State.Strike && state != State.Stun && state != State.Takedown)
                    rig.leftHandAimWeight = Mathf.MoveTowards(rig.leftHandAimWeight, 0f, udt * 6f);
                return;
            }
            aimTime += udt;
            focus -= udt;
            // Matrix bullet time while there is focus left; then real time, still aiming
            focusScale = Mathf.MoveTowards(focusScale, focus > 0f ? 0.3f : 1f, udt * 5f);
            facing = AimFacing();

            // soft lock: the robot closest to the crosshair
            var ray = AimRay;
            aimTarget = null;
            float best = 999f;
            foreach (var e in gm.enemies)
            {
                if (e == null || !e.Targetable) continue;
                Vector3 to = e.Chest - ray.origin;
                float along = Vector3.Dot(to, ray.direction);
                if (along < 0.5f || along > 45f) continue;
                float off = (to - ray.direction * along).magnitude;
                float allow = e.Radius + 0.35f + along * 0.02f;
                if (off > allow) continue;
                float score = off / allow + along * 0.01f;
                if (score < best) { best = score; aimTarget = e; }
            }

            Vector3 aimPoint = aimTarget ? aimTarget.Chest : ray.origin + ray.direction * 30f;
            rig.leftHandAimWeight = Mathf.MoveTowards(rig.leftHandAimWeight, 1f, udt * 10f);
            rig.leftHandAim = LeftShoulder + (aimPoint - LeftShoulder).normalized * 0.62f;
            rig.lookWeight = 0.9f;
            rig.lookTargetWorld = aimPoint;
            rig.torsoTwist = Mathf.Lerp(rig.torsoTwist, -12f, 0.3f);

            if (fireBuf > 0f && fireCooldown <= 0f)
            {
                fireBuf = 0f;
                FireShot(aimTarget, aimTarget ? aimTarget.Chest : ray.origin + ray.direction * 45f);
            }
        }

        void EndAim()
        {
            if (!aiming) return;
            aiming = false;
            aimTarget = null;
            blasterShown = 0.25f;
            if (Sfx.I && Sfx.I.focusOut) Sfx.Play(Sfx.I.focusOut, 0.35f, 1f, 0f);
        }

        /// <summary>A tap of the blaster button: one round at the best target ahead, without breaking stride.</summary>
        void QuickfireRequested()
        {
            if (gm == null || IsDead || !gm.CombatOn) return;
            if (state != State.Move && state != State.Strike && state != State.Evade) return;
            Enemy best = null;
            float bestScore = float.MaxValue;
            Vector3 basis = moveInput.sqrMagnitude > 0.04f ? moveInput.normalized : facing;
            foreach (var e in gm.enemies)
            {
                if (e == null || !e.Targetable || e.IsHeld) continue;
                if (e is Drone d && d.IsCore) continue;
                Vector3 to = Flat(e.transform.position - transform.position);
                float dist = to.magnitude;
                if (dist > 30f) continue;
                float ang = Vector3.Angle(basis, to);
                if (ang > 75f) continue;
                // shooters first: the Arkham quickfire is for interrupting guns
                float score = ang * 0.03f + dist * 0.05f - (e.IsRangedThreat ? 1.5f : 0f) - (e.CurrentState == Enemy.State.Aim ? 2f : 0f);
                if (score < bestScore) { bestScore = score; best = e; }
            }
            FireShot(best, best ? best.Chest : Chest + basis * 25f);
        }

        void FireShot(Enemy assist, Vector3 aimPoint)
        {
            if (Ammo <= 0)
            {
                Sfx.Play(Sfx.I.uiMove ? Sfx.I.uiMove : Sfx.I.ting, 0.4f, 0.6f, 0f);
                Hint("OUT OF ROUNDS - THEY RELOAD THEMSELVES");
                return;
            }
            Ammo--;
            lastShot = Time.unscaledTime;
            ammoRegen = 0f;
            fireCooldown = 0.16f;
            blasterShown = 0.45f;
            Vector3 from = blasterMuzzle ? blasterMuzzle.position : Chest + facing * 0.5f;
            Vector3 dir = (aimPoint - from).normalized;
            if (!aiming)
            {
                // quickfire: the off arm snaps out at the target
                rig.leftHandAimWeight = 1f;
                rig.leftHandAim = LeftShoulder + dir * 0.62f;
            }

            // hitscan with generous assist; the first robot along the line takes it
            Enemy hit = assist;
            float hitDist = assist ? (assist.Chest - from).magnitude : 60f;
            foreach (var e in gm.enemies)
            {
                if (e == null || !e.Targetable || e == assist) continue;
                Vector3 to = e.Chest - from;
                float along = Vector3.Dot(to, dir);
                if (along < 0f || along > hitDist) continue;
                if ((to - dir * along).magnitude < e.Radius + 0.15f) { hit = e; hitDist = along; }
            }
            Vector3 end = hit ? hit.Chest : from + dir * 60f;
            FX.Tracer(from, end, new Color(3.5f, 1.8f, 0.6f, 1f), 0.05f, 0.09f);
            FX.Burst(from, new Color(1f, 0.85f, 0.5f), new Color(1f, 0.5f, 0.2f), 8, new Vector2(3f, 7f), new Vector2(0.04f, 0.08f), new Vector2(0.05f, 0.12f), 0f, true, 0.02f, dir, 18f);
            if (muzzleLight) muzzleLight.intensity = 9f;
            Sfx.Play(Sfx.I.blaster ? Sfx.I.blaster : Sfx.I.cast, 0.7f, Random.Range(0.96f, 1.04f), 0f);
            gm.cameraRig.Shake(0.12f);
            gm.cameraRig.Punch(aiming ? 1.5f : 0.6f);
            gm.Rumble(0.2f, 0.5f, 0.06f);
            rig.Jolt(-dir, 0.25f);
            gm.RegisterMove("shot");

            if (hit == null) return;
            bool drone = hit.type == EnemyType.Drone;
            bool head = !drone && Random.value < (aiming ? 0.35f : 0.1f);
            float dmg = (drone ? 16f : 14f) * (head ? 1.8f : 1f);
            var result = hit.TakeHit(new HitInfo(dmg, Flat(dir).sqrMagnitude > 0.01f ? Flat(dir).normalized : transform.forward, 3.5f, HitKind.Shot, head), transform.position);
            if (result == HitResult.Landed)
            {
                gm.hud.HitMarker();
                FX.Sparks(hit.Chest - dir * 0.3f, -dir, head, false);
                if (head) gm.hud.Popup("HEADSHOT", new Color(1f, 0.75f, 0.35f));
            }
            else if (result == HitResult.Dodged)
            {
                gm.hud.Popup("DODGED", new Color(0.4f, 1f, 0.6f));
            }
        }

        void UpdateBlasterPose()
        {
            if (!blaster) return;
            bool drawn = aiming || blasterShown > 0f;
            var hand = Bone(HumanBodyBones.LeftHand);
            if (drawn && hand)
            {
                Vector3 aimPoint = aiming ? (aimTarget ? aimTarget.Chest : AimRay.origin + AimRay.direction * 30f) : blaster.position + blaster.forward * 10f;
                if (!aiming && rig.leftHandAimWeight > 0.5f) aimPoint = rig.leftHandAim + (rig.leftHandAim - LeftShoulder) * 20f;
                Vector3 dir = (aimPoint - hand.position).normalized;
                blaster.SetPositionAndRotation(hand.position + dir * 0.06f + transform.up * 0.02f, Quaternion.LookRotation(dir, transform.up));
            }
            else
            {
                // holstered on the left thigh
                var hips = Bone(HumanBodyBones.Hips);
                Vector3 p = hips ? hips.position : transform.position + Vector3.up;
                blaster.SetPositionAndRotation(p - transform.right * 0.19f - transform.up * 0.12f, Quaternion.LookRotation(-transform.up, transform.forward));
            }
        }

        // ------------------------------------------------------------------ tether

        Enemy PickTetherTarget(Vector3 input, float range)
        {
            Vector3 basis = input.sqrMagnitude > 0.04f ? input.normalized : aiming ? AimFacing() : facing;
            Enemy best = null;
            float bestScore = float.MaxValue;
            foreach (var e in gm.enemies)
            {
                if (e == null || e.Tether == TetherAction.None) continue;
                Vector3 to = e.Chest - Chest;
                float dist = to.magnitude;
                if (dist > range) continue;
                float ang = Vector3.Angle(basis, Flat(to));
                if (ang > 65f) continue;
                float score = ang * 0.05f + dist * 0.12f;
                if (e.type == EnemyType.Drone) score -= 1.2f;              // hornets and cores first
                if (e.CurrentState == Enemy.State.Aim) score -= 1f;        // about to shoot
                if (e.IsRangedThreat) score -= 0.5f;
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        bool TryTether()
        {
            tetherBuf = 0f;
            if (tetherCd > 0f)
            {
                Hint("TETHER RECHARGING");
                return false;
            }
            EndAim();
            var e = PickTetherTarget(moveInput, TetherRange);
            state = State.Tether;
            stateTime = 0f;
            tetherTime = 0f;
            tetherTarget = e;
            tetherAction = e ? e.Tether : TetherAction.None;
            Vector3 aimAt = e ? e.Chest : Chest + facing * TetherRange * 0.6f;
            tetherTravel = Mathf.Clamp((aimAt - Chest).magnitude / 70f, 0.06f, 0.24f);
            tetherEnd = aimAt;
            Vector3 flat = Flat(aimAt - transform.position);
            if (flat.sqrMagnitude > 0.01f) facing = flat.normalized;
            tetherLine.enabled = true;
            Sfx.Play(Sfx.I.tether ? Sfx.I.tether : Sfx.I.dash, 0.7f, 1f);
            gm.RegisterMove("tether");
            return true;
        }

        void DrawTether(Vector3 from, Vector3 to, float slack)
        {
            int n = tetherLine.positionCount;
            Vector3 side = Vector3.Cross(to - from, Vector3.up).normalized;
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1);
                float wave = Mathf.Sin(u * Mathf.PI * 3f + Time.unscaledTime * 40f) * slack * Mathf.Sin(u * Mathf.PI);
                tetherLine.SetPosition(i, Vector3.Lerp(from, to, u) + side * wave + Vector3.up * (wave * 0.5f));
            }
        }

        Vector3 UpdateTether(float dt)
        {
            tetherTime += dt;
            var hand = Bone(HumanBodyBones.LeftHand);
            Vector3 from = hand ? hand.position : Chest;
            bool valid = tetherTarget != null && tetherTarget.Targetable;
            if (valid) tetherEnd = tetherTarget.Chest;
            rig.leftHandAimWeight = 1f;
            rig.leftHandAim = LeftShoulder + (tetherEnd - LeftShoulder).normalized * 0.65f;
            rig.torsoTwist = -15f;
            float u = Mathf.Clamp01(tetherTime / tetherTravel);
            if (u < 1f)
            {
                DrawTether(from, Vector3.Lerp(from, tetherEnd, u), 0.15f * (1f - u));
                return Vector3.zero;
            }
            if (tetherTarget == null || tetherAction == TetherAction.None || !valid || tetherTarget.Tether == TetherAction.None)
            {
                // a miss: the line snaps back
                float back = Mathf.Clamp01((tetherTime - tetherTravel) / 0.15f);
                DrawTether(from, Vector3.Lerp(tetherEnd, from, back), 0.2f);
                if (back >= 1f) { tetherLine.enabled = false; tetherCd = 0.5f; state = State.Move; rig.leftHandAimWeight = 0f; }
                return Vector3.zero;
            }

            // connected
            var e = tetherTarget;
            FX.Arc(from, e.Chest, new Color(0.6f, 1.6f, 3.8f, 1f), 0.12f, 0.04f);
            Sfx.Play(Sfx.I.tetherHit ? Sfx.I.tetherHit : Sfx.I.clang, 0.6f, 1f);
            gm.HitStop(0.03f);
            switch (tetherAction)
            {
                case TetherAction.Pull:
                    e.BeginPull(this);
                    held = e;
                    holdTime = 0f;
                    state = State.Hold;
                    stateTime = 0f;
                    tetherCd = TetherCooldown;
                    gm.KeepComboAlive();
                    gm.RegisterHit(4f);
                    AddVoid(3f);
                    if (e.type == EnemyType.Drone && ((Drone)e).IsCore) Hint("RIP IT OUT [LMB / X]");
                    else Hint("HOLDING: RIP [LMB / X]   THROW [R / RB]");
                    break;
                case TetherAction.Zip:
                    StartZip(e);
                    break;
                default: // disarm or shield yank
                    e.Disarm(transform.position);
                    gm.hud.Popup(tetherAction == TetherAction.ShieldYank ? "SHIELD YANKED" : "DISARMED", new Color(0.5f, 0.9f, 1f));
                    gm.RegisterMove("disarm");
                    gm.KeepComboAlive();
                    gm.RegisterHit(4f);
                    AddVoid(5f);
                    tetherCd = TetherCooldown * 0.6f;
                    tetherLine.enabled = false;
                    rig.leftHandAimWeight = 0f;
                    state = State.Move;
                    break;
            }
            return Vector3.zero;
        }

        // ---- holding a robot: rip it apart or throw it

        Vector3 UpdateHold(float dt)
        {
            holdTime += dt;
            var hand = Bone(HumanBodyBones.LeftHand);
            if (held == null || held.IsDead || (!held.IsHeld && !held.IsPulled))
            {
                ReleaseHeld(false);
                return Vector3.zero;
            }
            tetherLine.enabled = held.IsPulled;
            if (held.IsPulled && hand) DrawTether(hand.position, held.Chest, 0.05f);
            rig.leftHandAimWeight = 1f;
            rig.leftHandAim = held.IsHeld ? held.Chest + (Chest - held.Chest).normalized * 0.25f : LeftShoulder + (held.Chest - LeftShoulder).normalized * 0.6f;
            rig.lookWeight = 0.6f;
            rig.lookTargetWorld = held.Head;
            rig.SetTarget(new Vector3(0.38f, 1.25f, -0.05f), new Vector3(0.2f, 0.7f, 0.3f), 12f);

            // counter / evade drop the robot and go
            if ((counterBuf > 0f && AnyCounterable()) || evadeBuf > 0f)
            {
                ReleaseHeld(true);
                TryStartAction(false);
                return Vector3.zero;
            }
            if (held.IsHeld && strikeBuf > 0f)
            {
                strikeBuf = 0f;
                RipHeld();
                if (state != State.Hold) return Vector3.zero;
            }
            if (held != null && held.IsHeld && tetherBuf > 0f)
            {
                tetherBuf = 0f;
                ThrowHeld();
                return Vector3.zero;
            }
            if (holdTime > 5f) { ReleaseHeld(true); return Vector3.zero; }

            // walk with it
            float speed = moveSpeed * 0.6f;
            moveVel = Vector3.MoveTowards(moveVel, moveInput * speed, 50f * dt);
            Vector3 look = gm.cameraRig ? gm.cameraRig.PlanarForward : facing;
            facing = moveInput.sqrMagnitude > 0.04f ? Vector3.Slerp(look, moveInput.normalized, 0.3f).normalized : look;
            return moveVel;
        }

        void RipHeld()
        {
            Vector3 pull = (Chest - held.Chest);
            pull = (pull.sqrMagnitude > 0.01f ? pull.normalized : -transform.forward) + Vector3.up * 0.3f;
            // the sword hand tears at it
            rig.Snap(new Vector3(-0.05f, 1.45f, 0.65f), new Vector3(-0.4f, 0.6f, 0.6f));
            rig.torsoTwist = 35f;
            rig.Jolt(-transform.forward, 0.5f);
            gm.cameraRig.Shake(0.35f);
            gm.HitStop(0.06f);
            gm.Rumble(0.6f, 0.9f, 0.15f);
            var e = held;
            bool killed = e.Rip(pull.normalized);
            gm.RegisterMove("rip");
            AddVoid(killed ? 6f : 3f);
            if (killed)
            {
                held = null;
                tetherLine.enabled = false;
                rig.leftHandAimWeight = 0f;
                state = State.Move;
                gm.hud.Popup("TORN APART", new Color(1f, 0.6f, 0.3f));
            }
        }

        void ThrowHeld()
        {
            Vector3 dir = gm.cameraRig ? gm.cameraRig.PlanarForward : facing;
            if (moveInput.sqrMagnitude > 0.04f) dir = moveInput.normalized;
            // aim assist: lob it at the robot nearest the throw line
            Enemy best = null;
            float bestAng = 30f;
            foreach (var o in gm.enemies)
            {
                if (o == null || o == held || !o.Targetable) continue;
                Vector3 to = Flat(o.transform.position - transform.position);
                float a = Vector3.Angle(dir, to);
                if (a < bestAng && to.magnitude < 22f) { bestAng = a; best = o; }
            }
            Vector3 vel;
            if (best != null)
            {
                Vector3 to = best.Chest - held.Chest;
                float t = Mathf.Clamp(to.magnitude / 22f, 0.15f, 0.8f);
                vel = to / t + Vector3.up * (0.5f * 20f * t); // compensate gravity (thrown robots fall at 20 m/s²)
                if (best.type == EnemyType.Drone) vel = to / t + Vector3.up * (0.5f * 14f * t);
            }
            else vel = dir * 20f + Vector3.up * 4f;
            var e = held;
            held = null;
            tetherLine.enabled = false;
            e.Throw(vel);
            rig.leftHandAimWeight = 0f;
            rig.Snap(new Vector3(0.1f, 1.5f, 0.8f), new Vector3(0f, 0.3f, 1f));
            rig.torsoTwist = -40f;
            gm.cameraRig.Punch(-2f);
            gm.RegisterMove("throw");
            gm.KeepComboAlive();
            state = State.Move;
            Sfx.Play(Sfx.I.swingHeavy, 0.7f, 0.85f);
        }

        void ReleaseHeld(bool drop)
        {
            if (held != null)
            {
                if (held.IsHeld || held.IsPulled) held.Drop();
                held = null;
            }
            if (tetherLine) tetherLine.enabled = false;
            if (rig) rig.leftHandAimWeight = 0f;
            if (state == State.Hold || state == State.Tether) state = State.Move;
        }

        // ---- zip: the tether pulls the Knight to a Juggernaut, feet first

        void StartZip(Enemy e)
        {
            tetherTarget = e;
            zipFrom = transform.position;
            state = State.Zip;
            stateTime = 0f;
            invuln = 0.6f;
            tetherCd = TetherCooldown;
            Sfx.Play(Sfx.I.whoom, 0.5f, 1.4f, 0f);
            gm.RegisterMove("zip");
        }

        void UpdateZip(float dt)
        {
            var e = tetherTarget;
            bool valid = e != null && e.Targetable;
            Vector3 goal = valid ? e.transform.position - Flat(e.transform.position - zipFrom).normalized * (Reach(e) - 0.3f) : zipFrom;
            goal.y = 0f;
            float u = Mathf.Clamp01(stateTime / 0.28f);
            Vector3 p = Vector3.Lerp(zipFrom, goal, u * u);
            p.y = 1.1f * Mathf.Sin(u * Mathf.PI);
            Place(p);
            kinematic = true;
            var hand = Bone(HumanBodyBones.LeftHand);
            if (valid && hand) DrawTether(hand.position, e.Chest, 0.03f);
            Vector3 to = valid ? Flat(e.transform.position - transform.position) : facing;
            if (to.sqrMagnitude > 0.01f) { facing = to.normalized; transform.rotation = Quaternion.LookRotation(facing); }
            // flying kick: both feet out front
            rig.rightFootWeight = rig.leftFootWeight = u;
            rig.rightFootTarget = rig.Local(0.12f, 1.2f, 0.9f);
            rig.leftFootTarget = rig.Local(-0.12f, 1.0f, 0.75f);
            rig.rightKneeHint = rig.Local(0.2f, 1.4f, 0.5f); rig.leftKneeHint = rig.Local(-0.2f, 1.3f, 0.4f);
            rig.bodyPitch = -25f * u;
            afterImageTimer -= dt;
            if (body && afterImageTimer <= 0f) { afterImageTimer = 0.03f; FX.AfterImage(body, new Color(0.3f, 0.9f, 2.2f, 0.45f), 0.22f, armor ? armor.Renderers : null); }
            if (u >= 1f)
            {
                tetherLine.enabled = false;
                rig.rightFootWeight = rig.leftFootWeight = 0f;
                rig.bodyPitch = 0f;
                if (valid)
                {
                    HitEnemy(e, 24f, HitKind.Heavy, 6f, 0.6f, 0.1f);
                    if (!e.IsDead) e.Stun(1.8f, true);
                    FX.Shockwave(e.Chest, 2.5f, new Color(0.4f, 1.3f, 3.2f), 0.3f);
                    gm.hud.Popup("ZIP KICK", new Color(0.5f, 0.9f, 1f));
                }
                Place(new Vector3(transform.position.x, 0f, transform.position.z));
                knock = -facing * 5f;
                state = State.Move;
            }
        }

        // ------------------------------------------------------------------ void cascade

        bool TryCascade()
        {
            cascadeBuf = 0f;
            int n = CascadeTargets;
            if (n <= 0)
            {
                Hint("FILL THE VOID METER: COUNTERS, TAKEDOWNS, KILLS");
                return false;
            }
            var pool = new List<Enemy>();
            foreach (var e in gm.enemies)
            {
                if (e == null || !e.Targetable || e.IsHeld) continue;
                if (e is Drone d && d.IsCore) continue;
                if (Flat(e.transform.position - transform.position).magnitude > 18f) continue;
                pool.Add(e);
            }
            if (pool.Count == 0) { Hint("NO TARGETS IN RANGE"); return false; }
            // nearest first, then hop to whoever is closest to the last victim
            cascadeQueue.Clear();
            Vector3 from = transform.position;
            for (int k = 0; k < n && pool.Count > 0; k++)
            {
                Enemy best = null; float bd = float.MaxValue;
                foreach (var e in pool) { float d = (e.transform.position - from).sqrMagnitude; if (d < bd) { bd = d; best = e; } }
                pool.Remove(best);
                cascadeQueue.Add(best);
                from = best.transform.position;
            }
            Void = Mathf.Max(0f, Void - cascadeQueue.Count * (VoidMax / 3f));
            EndAim();
            ReleaseHeld(true);
            foreach (var e in cascadeQueue) e.BeginExecution();
            state = State.Cascade;
            stateTime = 0f;
            cascadeIndex = -1;
            cascadeClock = 0f;
            cascadeStep = 0f;
            cascadeFrom = transform.position;
            invuln = 5f;
            focusScale = 0.04f;
            gm.hud.Banner("VOID CASCADE", "x" + cascadeQueue.Count, new Color(0.7f, 0.45f, 1f));
            gm.hud.Letterbox(0.6f + 0.35f * cascadeQueue.Count);
            gm.RegisterMove("cascade");
            gm.Pulse(0.6f);
            Sfx.Play(Sfx.I.focusIn ? Sfx.I.focusIn : Sfx.I.whoom, 0.8f, 0.8f, 0f);
            FX.Shockwave(transform.position, 6f, new Color(1.2f, 0.5f, 3f), 0.5f);
            return true;
        }

        void UpdateCascade(float udt)
        {
            cascadeClock += udt;
            focusScale = 0.04f;
            kinematic = true;
            const float StepTime = 0.34f;
            int idx = Mathf.FloorToInt(cascadeClock / StepTime);
            float local = cascadeClock - idx * StepTime;
            if (idx != cascadeIndex)
            {
                cascadeIndex = idx;
                if (idx < cascadeQueue.Count)
                {
                    // blink: leave an afterimage, reappear at the next robot's back
                    var e = cascadeQueue[idx];
                    if (body) FX.AfterImage(body, new Color(0.7f, 0.4f, 2.5f, 0.6f), 0.6f, armor ? armor.Renderers : null);
                    if (e != null && !e.IsDead)
                    {
                        Vector3 back = -Flat(e.transform.forward);
                        if (back.sqrMagnitude < 0.01f) back = Flat(transform.position - e.transform.position).normalized;
                        Vector3 p = e.transform.position + back.normalized * (Reach(e) - 0.2f);
                        p.y = 0f;
                        Place(gm.ClampToArena(p, 0.4f));
                        attackDir = Flat(e.transform.position - transform.position).normalized;
                        if (attackDir.sqrMagnitude < 0.01f) attackDir = facing;
                        facing = attackDir;
                        transform.rotation = Quaternion.LookRotation(attackDir);
                        gm.cameraRig.Cinematic(e.Chest, (Quaternion.Euler(0f, 60f + 40f * idx, 0f) * -attackDir + Vector3.up * 0.35f).normalized, 4.2f, -8f, 0.45f);
                    }
                    Sfx.Play(Sfx.I.whoom, 0.45f, 1.5f + 0.1f * idx, 0f);
                    cascadeStep = 0f;
                }
            }
            if (cascadeIndex < cascadeQueue.Count)
            {
                var e = cascadeQueue[cascadeIndex];
                float u = Mathf.Clamp01(local / 0.16f);
                float ang = Mathf.Lerp(120f, -110f, 1f - (1f - u) * (1f - u));
                rig.Snap(WeaponRig.HGrip(ang, 1.35f), WeaponRig.HDir(ang, 0.1f));
                rig.SetTrail(u > 0f && u < 1f);
                rig.torsoTwist = Mathf.Lerp(30f, -35f, u);
                rig.bodyOffset = new Vector3(0f, -0.2f, 0.1f);
                if (cascadeStep == 0f && u >= 0.6f)
                {
                    cascadeStep = 1f;
                    if (e != null && !e.IsDead)
                    {
                        if (e.IsBoss) e.TakeHit(new HitInfo(e.maxHealth * 0.12f, attackDir, 4f, HitKind.Takedown, true), transform.position);
                        else e.Execute(new HitInfo(70f, attackDir, 7f, HitKind.Takedown, true));
                        FX.Clash(e.Chest, attackDir, new Color(1.2f, 0.6f, 3f));
                        FX.LightFlash(e.Chest, new Color(0.7f, 0.4f, 1f), 8f, 6f, 0.2f);
                        Sfx.Play(Sfx.I.takedown, 0.8f, 1.1f + 0.08f * cascadeIndex, 0f);
                        gm.cameraRig.Shake(0.4f);
                        gm.Rumble(0.6f, 0.9f, 0.15f);
                    }
                }
                return;
            }
            // finale: time snaps back in with a shockwave
            if (local >= 0f && stateTime >= 0f)
            {
                foreach (var e in cascadeQueue) if (e != null && !e.IsDead) e.ReleaseExecution();
                cascadeQueue.Clear();
                rig.SetTrail(false);
                rig.bodyOffset = Vector3.zero;
                focusScale = 1f;
                state = State.Move;
                invuln = 0.6f;
                Vector3 c = transform.position;
                FX.Shockwave(c, 7f, new Color(1.4f, 0.6f, 3.4f), 0.5f);
                FX.Burst(c + Vector3.up * 0.3f, new Color(0.8f, 0.5f, 1f), new Color(0.4f, 0.9f, 1f), 50, new Vector2(4f, 12f), new Vector2(0.04f, 0.1f), new Vector2(0.3f, 0.7f), 1.2f, true, 1f);
                gm.cameraRig.Punch(8f);
                gm.hud.Flash(0.35f);
                gm.SlowMo(0.35f, 0.5f);
                Sfx.Play(Sfx.I.nova, 0.8f, 1.2f, 0f);
                if (Sfx.I.focusOut) Sfx.Play(Sfx.I.focusOut, 0.5f, 0.9f, 0f);
            }
        }
    }
}
