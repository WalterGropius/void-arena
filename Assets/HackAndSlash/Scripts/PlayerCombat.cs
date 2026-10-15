using System.Collections.Generic;
using UnityEngine;

namespace HackAndSlash
{
    /// <summary>The Knight's melee: FreeFlow strikes, beatdowns, counters (and bullet deflects), vaults, the arc stun and takedowns.</summary>
    public partial class PlayerController
    {
        struct StrikeDef
        {
            public float impact, recover, damage, knockback, shake, hitstop;
            public HitKind kind;
        }

        // kind: 0 slash R->L, 1 backhand L->R, 2 overhead, 3 thrust, 4 rising launcher, 5 spin, 6 jab (beatdown), 7 beatdown finisher
        static readonly StrikeDef[] Strikes =
        {
            new StrikeDef { impact = 0.1f, recover = 0.2f, damage = 13f, knockback = 3.5f, shake = 0.2f, hitstop = 0.05f, kind = HitKind.Strike },
            new StrikeDef { impact = 0.1f, recover = 0.2f, damage = 14f, knockback = 3.5f, shake = 0.22f, hitstop = 0.055f, kind = HitKind.Strike },
            new StrikeDef { impact = 0.15f, recover = 0.26f, damage = 21f, knockback = 6f, shake = 0.4f, hitstop = 0.08f, kind = HitKind.Strike },
            new StrikeDef { impact = 0.09f, recover = 0.2f, damage = 15f, knockback = 5f, shake = 0.24f, hitstop = 0.06f, kind = HitKind.Strike },
            new StrikeDef { impact = 0.13f, recover = 0.28f, damage = 19f, knockback = 7f, shake = 0.45f, hitstop = 0.09f, kind = HitKind.Heavy },
            new StrikeDef { impact = 0.15f, recover = 0.3f, damage = 16f, knockback = 6.5f, shake = 0.4f, hitstop = 0.06f, kind = HitKind.Strike },
            new StrikeDef { impact = 0.07f, recover = 0.12f, damage = 9f, knockback = 1f, shake = 0.15f, hitstop = 0.035f, kind = HitKind.Strike },
            new StrikeDef { impact = 0.14f, recover = 0.32f, damage = 30f, knockback = 9f, shake = 0.6f, hitstop = 0.11f, kind = HitKind.Heavy },
        };
        static readonly int[] StrikeCycle = { 0, 1, 3, 0, 2, 1, 4, 0, 1, 2 };
        const float VaultTime = 0.46f;
        const int BeatdownLength = 6;

        // strike
        Enemy target, beatdownTarget;
        int strikeKind, strikeCount, beatdownHits;
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
        bool envTakedown;
        AnchorSpec envAnchor;
        Vector3 envFrom;

        // ------------------------------------------------------------------ targeting

        /// <summary>A sword can reach this robot (Hornets have to be stunned down, tethered or shot first).</summary>
        static bool InSwordReach(Enemy e) => e.type != EnemyType.Drone || e.transform.position.y < 2.4f;

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
                if (e == null || !e.Targetable || !InSwordReach(e)) continue;
                if (e.IsHeld || e.IsPulled) continue;
                if (downedOnly != e.IsDowned) continue;
                if (e is Drone d && d.IsCore) continue;
                Vector3 to = e.transform.position - pos; to.y = 0f;
                if (downedOnly) { to = e.BodyCenter - pos; to.y = 0f; }
                float dist = to.magnitude;
                if (dist > range + e.Radius) continue;
                float ang = dist > 0.3f ? Vector3.Angle(basis, to) : 0f;
                float score;
                if (hasInput)
                {
                    if (ang > 70f) continue;
                    score = ang * 0.06f + dist * 0.2f;
                }
                else
                {
                    score = dist * 0.3f + ang * 0.02f;
                    if (e == target && dist < 6f) score -= 0.8f;
                }
                if (e.IsStunned) score -= 0.4f; // Arkham: a stunned robot invites the beatdown
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        float Reach(Enemy e) => 1.15f + e.Radius + Radius;

        // ------------------------------------------------------------------ freeflow strike

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

            // beatdown: a flurry of jabs on a stunned robot, then a launcher
            bool beatdown = t != null && t.IsStunned && !t.IsBoss;
            if (beatdown)
            {
                if (beatdownTarget != t) { beatdownTarget = t; beatdownHits = 0; }
                beatdownHits++;
                strikeKind = beatdownHits >= BeatdownLength ? 7 : 6;
                speedMul *= 1.8f;
                if (beatdownHits == 2) Hint("KEEP STRIKING - BEATDOWN");
            }
            else
            {
                beatdownTarget = null;
                beatdownHits = 0;
                if (gm.Combo >= 5 && gm.Combo % 7 == 6) strikeKind = 5;
                else strikeKind = StrikeCycle[strikeCount % StrikeCycle.Length];
            }

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
                rig.rightFootWeight = rig.leftFootWeight = 0f;
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
                rig.bodyPitch = 12f * Mathf.Sin(u * Mathf.PI);
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
                bool heavy = strikeKind == 2 || strikeKind == 4 || strikeKind == 5 || strikeKind == 7;
                Sfx.Play(heavy ? Sfx.I.swingHeavy : Sfx.I.swing, strikeKind == 6 ? 0.4f : 0.65f, (strikeKind == 1 ? 1.12f : 1f) * Mathf.Min(1.3f, speedMul));
            }
            return vel;
        }

        void PoseStrike(int kind, float phase, float dt)
        {
            rig.lookWeight = Mathf.MoveTowards(rig.lookWeight, 0.85f, dt * 8f);
            rig.lookTargetWorld = target ? target.Chest : Chest + attackDir * 4f;
            float p = Mathf.Clamp(phase, -0.4f, 1.8f);
            bool active = phase > 0f && phase < 1.45f;
            rig.SetTrail(active && kind != 6);
            float k = Mathf.Clamp01(phase);
            float ease = 1f - (1f - k) * (1f - k) * (1f - k);
            float follow = Mathf.Clamp01((phase - 1f) / 0.8f);
            // weight shifts: step into the blow, then settle
            rig.bodyOffset = Vector3.Lerp(rig.bodyOffset, new Vector3(0f, -0.08f * Mathf.Sin(k * Mathf.PI), 0.12f * ease), 0.5f);
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
                    rig.bodyOffset = new Vector3(0f, -0.2f * Mathf.Clamp01(p), 0.15f * Mathf.Clamp01(p)); // drop into the chop
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
                    rig.bodyOffset = new Vector3(0f, -0.12f, 0.3f * ease); // lunge
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
                    rig.bodyOffset = new Vector3(0f, Mathf.Lerp(-0.25f, 0.05f, ease), 0.1f);
                    break;
                }
                case 6:
                {
                    // beatdown: the off hand jabs (gauntlet), the blade stays chambered; feet stay planted
                    bool left = beatdownHits % 2 == 0;
                    float jab = Mathf.Sin(Mathf.Clamp01(phase) * Mathf.PI);
                    rig.SetTarget(WeaponRig.HGrip(70f, 1.35f, 0.35f), WeaponRig.HDir(70f, 0.6f), 30f);
                    rig.leftHandAimWeight = left ? jab : 0f;
                    rig.leftHandAim = Chest + attackDir * (0.25f + 0.55f * jab) + Vector3.up * 0.1f;
                    if (!left) rig.Snap(Vector3.Lerp(new Vector3(0.25f, 1.35f, 0.1f), new Vector3(0.1f, 1.35f, 0.75f), jab), new Vector3(0.1f, 0.8f, 0.6f));
                    rig.torsoTwist = (left ? -18f : 18f) * jab;
                    rig.plantFeet = 1f;
                    break;
                }
                case 7:
                {
                    // the launcher kick: right foot snaps up into the robot's chest
                    float kick = Mathf.Sin(Mathf.Clamp01(phase) * Mathf.PI * 0.5f) * (1f - Mathf.Clamp01((phase - 1f) / 0.8f));
                    rig.rightFootWeight = kick;
                    rig.rightFootTarget = rig.Local(0.12f, 1.25f, 0.95f);
                    rig.rightKneeHint = rig.Local(0.25f, 1.2f, 0.6f);
                    rig.bodyPitch = -14f * kick;
                    rig.bodyOffset = new Vector3(0f, 0.05f, -0.15f * kick);
                    rig.SetTarget(WeaponRig.HGrip(110f, 1.2f, 0.45f), WeaponRig.HDir(110f, 0.1f), 20f);
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
                    rig.bodyOffset = new Vector3(0f, -0.22f, 0f);
                    if (phase > 1.6f) facing = transform.forward;
                    break;
                }
            }
            if (phase > 1.3f) { rig.leftHandAimWeight = Mathf.MoveTowards(rig.leftHandAimWeight, 0f, dt * 8f); rig.plantFeet = Mathf.MoveTowards(rig.plantFeet, 0f, dt * 6f); }
        }

        void ResolveStrike(StrikeDef def)
        {
            ParryProjectiles(2.9f, 110f);
            if (whiff && strikeKind != 5)
            {
                gm.BreakCombo();
                return;
            }
            if (strikeKind == 5)
            {
                var list = gm.enemies;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (i >= list.Count) continue;
                    var e = list[i];
                    if (e == null || !e.Targetable || e.IsDowned || !InSwordReach(e)) continue;
                    Vector3 to = Flat(e.transform.position - transform.position);
                    if (to.magnitude > 3.1f + e.Radius) continue;
                    HitEnemy(e, def.damage, def.kind, def.knockback, def.shake, def.hitstop);
                }
                FX.Shockwave(transform.position, 3.6f, new Color(0.4f, 1.3f, 3.2f), 0.35f);
                return;
            }
            if (target == null || !target.Targetable) return;
            bool landed = HitEnemy(target, def.damage, def.kind, def.knockback, def.shake, def.hitstop);
            if (!landed) return;
            if (strikeKind == 7)
            {
                // the beatdown ends with the robot sent flying
                beatdownHits = 0;
                beatdownTarget = null;
                if (!target.IsDead && !target.superArmor) target.Knockdown(attackDir);
                FX.Shockwave(target.Chest, 2.2f, new Color(0.4f, 1.4f, 3.2f), 0.3f);
                gm.cameraRig.Punch(-3f);
                gm.RegisterMove("beatdownfinisher");
                AddVoid(6f);
            }
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
                    if (e == null || e == target || !e.Targetable || e.IsDowned || !InSwordReach(e)) continue;
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
            var result = e.TakeHit(new HitInfo(dmg, dir, knockback, kind, crit), transform.position);
            switch (result)
            {
                case HitResult.Blocked:
                    knock = -dir * 6f;
                    rig.Jolt(-dir, 0.6f);
                    gm.HitStop(0.05f);
                    gm.cameraRig.Shake(0.22f);
                    if (e.ShieldUp) Hint("SHIELD UP - STUN IT [E], VAULT IT [SPACE] OR YANK IT AWAY [R]");
                    return false;
                case HitResult.Dodged:
                    gm.KeepComboAlive();
                    Hint("AUDITORS DODGE WHAT THEY SEE - COUNTER, STUN OR HIT THEM FROM BEHIND");
                    return false;
                case HitResult.Shocked:
                case HitResult.Immune:
                    return false;
            }
            AddVoid(crit ? 2.5f : 1.5f);
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
            Vector3 dir;
            if (p.IsBullet && p.Owner != null && p.Owner.Targetable) dir = p.Owner.Chest - p.transform.position;
            else
            {
                var target2 = gm.NearestEnemy(transform.position, 25f);
                dir = target2 ? target2.Chest - p.transform.position : transform.forward;
            }
            Vector3 flat = Flat(p.transform.position - Chest);
            p.Reflect(dir);
            AddVoid(6f);
            gm.HitStop(0.07f);
            gm.cameraRig.Shake(0.3f);
            gm.hud.Popup(p.IsBullet ? "RETURNED TO SENDER" : "DEFLECTED!", new Color(0.4f, 0.95f, 1f));
            gm.RegisterMove("deflect");
            gm.KeepComboAlive();
            Sfx.Play(Sfx.I.ting, 0.8f, p.IsBullet ? 1.4f : 1.1f);
            FX.Clash(p.transform.position, flat.sqrMagnitude > 0.001f ? -flat.normalized : -transform.forward, new Color(0.4f, 1.2f, 3f));
        }

        // ------------------------------------------------------------------ counter

        /// <summary>The Enforcer or Hornet is about to fire: countering sends the round straight back down the barrel.</summary>
        static bool IsShotCounter(Enemy e) => e.CurrentState == Enemy.State.Aim;

        bool AnyCounterable()
        {
            foreach (var e in gm.enemies)
            {
                if (e == null || !e.CanBeCountered) continue;
                float d = Flat(e.transform.position - transform.position).magnitude;
                if (d <= counterRange + e.Radius || (IsShotCounter(e) && d < 30f)) return true;
            }
            foreach (var p in gm.projectiles)
                if (p && !p.reflected && (p.transform.position - Chest).magnitude < (p.IsBullet ? 6f : 4.2f)) return true;
            return false;
        }

        bool StartCounter()
        {
            counterQueue.Clear();
            var shots = new List<Enemy>();
            foreach (var e in gm.enemies)
            {
                if (e == null || !e.CanBeCountered) continue;
                float d = Flat(e.transform.position - transform.position).magnitude;
                if (IsShotCounter(e)) { if (d < 30f) shots.Add(e); continue; }
                if (d > counterRange + e.Radius) continue;
                counterQueue.Add(e);
            }
            Projectile bolt = null;
            float bestBolt = 6f;
            foreach (var p in gm.projectiles)
            {
                if (p == null || p.reflected) continue;
                float d = (p.transform.position - Chest).magnitude;
                if (d < (p.IsBullet ? 6f : 4.2f) && d < bestBolt) { bestBolt = d; bolt = p; }
            }

            if (counterQueue.Count == 0 && bolt == null && shots.Count == 0)
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
            // bullet counters: the blade meets the round the instant it leaves the barrel
            foreach (var s in shots)
            {
                Vector3 tip = rig.TipWorld;
                var b = Projectile.SpawnBullet(tip, s.Chest - tip, 0f, gm, s);
                b.Reflect(s.Chest - tip);
                s.OnCountered(0.9f);
                FX.Clash(tip, (s.Chest - tip).normalized, new Color(1f, 0.8f, 0.4f));
                Sfx.Play(Sfx.I.parry, 0.6f, 1.3f, 0f);
                AddVoid(5f);
            }
            if (shots.Count > 0)
            {
                gm.hud.Popup(shots.Count > 1 ? "RETURNED x" + shots.Count : "RETURNED TO SENDER", new Color(1f, 0.8f, 0.4f));
                gm.RegisterMove("deflect");
                gm.KeepComboAlive();
                gm.SlowMo(0.45f, 0.25f);
            }
            if (counterQueue.Count == 0)
            {
                state = State.Counter;
                stateTime = 0f;
                counterIndex = 0;
                counterStepStart = 0f;
                counterParried = counterHit = true;
                invuln = Mathf.Max(invuln, 0.25f);
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
            rig.rightFootWeight = rig.leftFootWeight = 0f;
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
                // bolt / bullet counter: a quick flick of the blade
                rig.SetTarget(WeaponRig.HGrip(40f, 1.5f), WeaponRig.HDir(40f, 0.6f), 30f);
                rig.torsoTwist = 20f;
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
            // alternate a blade counter with a kick counter (Arkham's variety)
            bool kickCounter = counterIndex % 3 == 2;
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
                if (kickCounter) rig.SetTarget(WeaponRig.HGrip(40f, 1.5f), WeaponRig.HDir(40f, 0.7f), 30f);
                else rig.SetTarget(WeaponRig.HGrip(kindSwing == 0 ? 110f : -100f, 1.4f), WeaponRig.HDir(kindSwing == 0 ? 110f : -100f, 0.25f), 30f);
                rig.torsoTwist = kindSwing == 0 ? 32f : -32f;
                return;
            }
            float sw = Mathf.Clamp01((tp - 0.09f) / 0.08f);
            if (kickCounter)
            {
                // spinning back kick: hips turn, the right leg snaps out at chest height
                float kick = Mathf.Sin(sw * Mathf.PI * 0.5f) * (1f - Mathf.Clamp01((tp - 0.2f) / 0.08f));
                rig.rightFootWeight = kick;
                rig.rightFootTarget = rig.Local(0.05f, 1.15f, 1.0f);
                rig.rightKneeHint = rig.Local(0.35f, 1.1f, 0.5f);
                rig.bodyPitch = -16f * kick;
                rig.bodyRoll = 10f * kick;
                rig.torsoTwist = -25f * kick;
            }
            else
            {
                float a0 = kindSwing == 0 ? 110f : -100f, a1 = kindSwing == 0 ? -110f : 115f;
                float ang = Mathf.Lerp(a0, a1, 1f - (1f - sw) * (1f - sw));
                rig.Snap(WeaponRig.HGrip(ang, Mathf.Lerp(1.4f, 1.12f, sw)), WeaponRig.HDir(ang, Mathf.Lerp(0.2f, -0.1f, sw)));
                rig.SetTrail(sw > 0f && sw < 1f);
                rig.torsoTwist = Mathf.Lerp(kindSwing == 0 ? 32f : -32f, kindSwing == 0 ? -36f : 36f, sw);
            }
            if (!counterHit && sw >= 0.55f)
            {
                counterHit = true;
                Sfx.Play(kickCounter ? Sfx.I.swing : Sfx.I.swingHeavy, 0.6f, kickCounter ? 0.9f : 1.15f);
                if (valid)
                {
                    HitEnemy(e, kickCounter ? 20f : 22f, HitKind.Counter, kickCounter ? 11f : 9f, 0.45f, 0.08f);
                    AddVoid(4f);
                }
            }
            if (tp >= 0.26f)
            {
                rig.SetTrail(false);
                rig.rightFootWeight = 0f;
                counterIndex++;
                if (counterIndex < counterQueue.Count) BeginCounterStep();
                else EndCounter();
            }
        }

        // ------------------------------------------------------------------ evade & vault

        void StartEvade()
        {
            evadeBuf = 0f;
            EndAim();
            Vector3 dir = moveInput.sqrMagnitude > 0.04f ? moveInput.normalized : -facing;
            // evading into a robot vaults over it
            Enemy over = null;
            float best = 99f;
            foreach (var e in gm.enemies)
            {
                if (e == null || !e.Targetable || e.IsDowned || e.IsBoss || e.type == EnemyType.Drone || e.IsHeld) continue;
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
            float u = Mathf.Clamp01(stateTime / dashTime);
            // a low, sliding dodge: hips drop and the body leans away from the threat
            Vector3 local = Quaternion.Inverse(transform.rotation) * dashDir;
            rig.bodyOffset = new Vector3(0f, -0.3f * Mathf.Sin(u * Mathf.PI), 0f);
            rig.bodyRoll = -local.x * 18f * Mathf.Sin(u * Mathf.PI);
            rig.bodyPitch = local.z * 10f * Mathf.Sin(u * Mathf.PI);
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
            // front flip around the centre of mass, legs tucked
            Vector3 travel = Flat(vaultTo - vaultFrom);
            if (travel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(travel.normalized);
            float flip = 360f * e;
            var rot = Quaternion.Euler(flip, 0f, 0f);
            model.localRotation = rot;
            model.localPosition = Vector3.up * 1.0f - rot * (Vector3.up * 1.0f);
            float tuck = Mathf.Sin(u * Mathf.PI);
            rig.rightFootWeight = rig.leftFootWeight = 0.7f * tuck;
            rig.rightFootTarget = rig.Local(0.15f, 0.75f, 0.25f);
            rig.leftFootTarget = rig.Local(-0.15f, 0.7f, 0.2f);
            rig.rightKneeHint = rig.Local(0.2f, 1.1f, 0.8f); rig.leftKneeHint = rig.Local(-0.2f, 1.1f, 0.8f);
            rig.SetTarget(new Vector3(0.3f, 1.15f, 0.1f), new Vector3(0.2f, -0.3f, -1f), 20f);
            if (u >= 1f)
            {
                model.localRotation = Quaternion.identity;
                model.localPosition = Vector3.zero;
                rig.rightFootWeight = rig.leftFootWeight = 0f;
                transform.rotation = Quaternion.LookRotation(facing);
                rig.SetTrail(false);
                rig.bodyOffset = new Vector3(0f, -0.25f, 0f); // land in a crouch
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
            // the off hand sweeps the arc out like a cape
            rig.leftHandAimWeight = Mathf.Sin(u * Mathf.PI);
            rig.leftHandAim = Chest + Quaternion.AngleAxis(Mathf.Lerp(70f, -70f, u), Vector3.up) * attackDir * 0.7f;
            if (!impactDone && t >= 0.12f)
            {
                impactDone = true;
                int n = 0;
                Vector3 tip = rig.TipWorld;
                foreach (var e in gm.enemies)
                {
                    if (e == null || !e.Targetable || e.IsDowned || e.IsHeld) continue;
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
            if (t >= 0.2f && TryStartAction()) { rig.leftHandAimWeight = 0f; return Vector3.zero; }
            if (t >= 0.32f) { state = State.Move; rig.SetTrail(false); rig.leftHandAimWeight = 0f; }
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
            // Arkham's environmental takedowns: next to a car, column or barrier, the robot meets it face first
            envTakedown = !e.IsBoss && gm.NearestAnchor(e.transform.position, 3.2f, out envAnchor);
            gm.SlowMo(0.3f, envTakedown ? 1.3f : 1.15f);
            Vector3 line = Flat(e.transform.position - transform.position).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, line);
            if (Vector3.Dot(side, gm.cameraRig.transform.position - transform.position) < 0f) side = -side;
            Vector3 focus = envTakedown ? (e.transform.position + envAnchor.pos) * 0.5f + Vector3.up * 1.1f : (transform.position + e.transform.position) * 0.5f + Vector3.up * 1.1f;
            gm.cameraRig.Cinematic(focus, (side + Vector3.up * 0.25f - line * 0.2f).normalized, envTakedown ? 5.2f : 4.3f, -9f, envTakedown ? 1.3f : 1.1f);
            gm.hud.Letterbox(1.1f);
            gm.hud.Popup(envTakedown ? "ENVIRONMENTAL TAKEDOWN" : "TAKEDOWN", new Color(1f, 0.85f, 0.35f));
            gm.RegisterMove(envTakedown ? "envtakedown" : "takedown");
            Sfx.Play(Sfx.I.whoom, 0.8f, 0.8f, 0f);
        }

        void UpdateTakedown(float dt)
        {
            var e = execTarget;
            bool valid = e != null && !e.IsDead;
            float t = stateTime;
            if (valid && !(envTakedown && t - travelTime > 0.1f))
            {
                Vector3 to = Flat(e.transform.position - transform.position);
                if (to.sqrMagnitude > 0.01f) { attackDir = to.normalized; facing = attackDir; }
            }
            if (t - travelTime < 0.16f || envTakedown) transform.rotation = Quaternion.LookRotation(attackDir);
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
            if (envTakedown) { UpdateEnvTakedown(e, valid, tt); return; }
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
                rig.bodyOffset = new Vector3(0f, -0.2f, 0f);
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
                    AddVoid(10f);
                }
            }
            if (tt >= 0.62f) EndTakedown(valid);
        }

        /// <summary>Grab, drive and slam the robot into the nearest hard thing.</summary>
        void UpdateEnvTakedown(Enemy e, bool valid, float tt)
        {
            Vector3 slamAt = envAnchor.pos;
            Vector3 drive = Flat(slamAt - (valid ? e.transform.position : transform.position));
            if (execStep == 0)
            {
                execStep = 1;
                envFrom = valid ? e.transform.position : transform.position;
                attackDir = drive.sqrMagnitude > 0.01f ? drive.normalized : attackDir;
                facing = attackDir;
                Sfx.Play(Sfx.I.swingHeavy, 0.7f, 0.8f);
            }
            float u = Mathf.Clamp01(tt / 0.26f);
            float k = u * u;
            // the off hand has it by the head; both of them travel to the anchor
            Vector3 robotPos = Vector3.Lerp(envFrom, new Vector3(slamAt.x, 0f, slamAt.z) - attackDir * 0.35f, k);
            if (valid && execStep == 1) e.transform.position = robotPos;
            Place(robotPos - attackDir * ((valid ? Reach(e) : 1.6f) - 0.2f));
            kinematic = true;
            rig.leftHandAimWeight = 1f;
            rig.leftHandAim = valid ? e.Head : robotPos + Vector3.up * 1.6f;
            rig.SetTarget(WeaponRig.HGrip(100f, 1.2f), WeaponRig.HDir(100f, 0f), 20f);
            rig.bodyPitch = 15f * k;
            rig.bodyOffset = new Vector3(0f, -0.15f * k, 0.2f * k);
            if (execStep == 1 && u >= 1f)
            {
                execStep = 2;
                if (valid) e.Execute(new HitInfo(90f, attackDir, 4f, HitKind.Environment, true));
                Vector3 c = slamAt + Vector3.up * 0.2f;
                FX.Sparks(c, -attackDir, true, true);
                FX.Explosion(c, new Color(1f, 0.7f, 0.3f), 0.5f);
                FX.Shockwave(new Vector3(c.x, 0.05f, c.z), 3f, new Color(1f, 0.75f, 0.35f), 0.4f);
                FX.Dust(c, 1.2f);
                gm.HitStop(0.12f);
                gm.cameraRig.Shake(0.9f);
                gm.cameraRig.Punch(-5f);
                gm.Pulse(0.8f);
                gm.Rumble(1f, 1f, 0.3f);
                Sfx.Play(Sfx.I.slam, 1f, 0.9f, 0f);
                Sfx.Play(Sfx.I.takedown, 0.9f, 0.85f, 0f);
                Sfx.Play(Sfx.I.clang, 0.7f, 0.7f);
                AddVoid(14f);
            }
            if (tt >= 0.75f) { rig.leftHandAimWeight = 0f; EndTakedown(valid); }
        }

        void EndTakedown(bool valid)
        {
            if (valid && execStep < 2 && execTarget != null) execTarget.ReleaseExecution();
            execTarget = null;
            envTakedown = false;
            rig.leftHandAimWeight = 0f;
            state = State.Move;
            invuln = 0.3f;
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
            // drop onto one knee over the body
            rig.bodyOffset = new Vector3(0f, -0.42f * crouch, 0.1f * crouch);
            rig.plantFeet = crouch;
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
                    AddVoid(8f);
                }
            }
            if (tt >= 0.75f)
            {
                rig.leftHandWeight = 0f;
                rig.plantFeet = 0f;
                execTarget = null;
                state = State.Move;
            }
        }
    }
}
