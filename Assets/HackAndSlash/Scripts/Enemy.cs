using UnityEngine;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    public enum HitKind { Strike, Heavy, Counter, Takedown, Nova, Projectile, Stab }

    public struct HitInfo
    {
        public float damage, knockback;
        public Vector3 dir;
        public HitKind kind;
        public bool crit;

        public HitInfo(float damage, Vector3 dir, float knockback, HitKind kind, bool crit)
        {
            this.damage = damage; this.dir = dir; this.knockback = knockback; this.kind = kind; this.crit = crit;
        }
    }

    /// <summary>
    /// Armoured combat robots with Arkham-style crowd behaviour: they ring the player, a director hands out attack
    /// tokens, every attack telegraphs a counter window (blue spider-sense) or must be dodged (red), and they can be
    /// stunned, knocked down, vaulted over, executed and torn apart.
    /// Scrapper (lunging blade), Hexer (homing bolts), Juggernaut (unblockable slam, needs a stun to hurt),
    /// Bulwark (riot shield: stun it or vault over it), Iron Tyrant (boss).
    /// </summary>
    public class Enemy : MonoBehaviour
    {
        public enum State { Spawning, Circle, Windup, Attack, Recover, Stagger, Stunned, Countered, Downed, GettingUp, Vaulted, Executed, Dead }

        public EnemyType type;
        public string displayName;
        public float maxHealth, health, speed, damage, attackRange, windupTime, slamRadius, scale = 1f;
        public long scoreValue;
        public bool superArmor;

        public Animator animator;
        public Transform model;
        public WeaponRig rig;
        public SkinnedMeshRenderer body;
        public ArmorKit armor;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");

        GameManager gm;
        CharacterController cc;
        Color glowColor;
        Transform staffOrb, shield;
        Light staffLight;
        Material shieldGlow;
        State state;
        float stateTime, stateDur;
        Vector3 knock, facing = Vector3.forward, slamPoint, lungeDir, spawnFrom, spawnTo, fallDir;
        float vy, flash, nextCast, strafeTimer, strafeSign = 1f, ringRadius, poise, poiseMax, tokenDelay, stunCooldown, attackCooldown, arcTimer, glowLevel = 1f, shieldLower;
        bool hitDone, hasToken, unblockable, sweep, enraged, landed;
        int slamCount;
        TelegraphFX telegraph;
        EnemyHealthBar bar;
        CounterIndicator indicator;

        public State CurrentState => state;
        public bool IsDead => state == State.Dead;
        public bool Targetable => state != State.Dead && state != State.Spawning && state != State.Executed;
        public bool IsDowned => state == State.Downed || state == State.GettingUp;
        public bool IsStunned => state == State.Stunned;
        public bool IsArmored => type == EnemyType.Brute && state != State.Stunned;
        public bool IsBoss => type == EnemyType.Boss;
        public bool ShieldUp => type == EnemyType.Shield && shieldLower < 0.5f && state != State.Stunned && !IsDowned;
        public Vector3 Chest => transform.position + Vector3.up * (1.2f * scale);
        public Vector3 Head => transform.position + Vector3.up * (2.0f * scale);
        public Vector3 BodyCenter => IsDowned || IsDead ? transform.position - facing * (0.85f * scale) + Vector3.up * 0.25f : Chest;
        public float Radius => 0.42f * scale;
        public float Health01 => Mathf.Clamp01(health / maxHealth);
        public Color GlowColor => glowColor;
        public float LastAttackTime { get; private set; } = -99f;

        /// <summary>The attack can be countered right now (blue indicator).</summary>
        public bool CanBeCountered =>
            !unblockable && type != EnemyType.Caster &&
            ((state == State.Windup && stateTime >= stateDur * 0.18f) || (state == State.Attack && !hitDone));

        /// <summary>A red, unblockable attack is coming: dodge it.</summary>
        public bool UnblockableIncoming => unblockable && (state == State.Windup || (state == State.Attack && !hitDone));

        /// <summary>Seconds until this attack lands (for multi-counter ordering and the autopilot).</summary>
        public float TimeToImpact => state == State.Windup ? (stateDur - stateTime) + 0.1f : state == State.Attack ? Mathf.Max(0f, stateDur * 0.45f - stateTime) : 99f;

        public bool WantsToken => !hasToken && state == State.Circle && type != EnemyType.Caster && type != EnemyType.Boss && Time.time - LastAttackTime > 1.2f;
        public bool HoldsToken => hasToken;

        // ------------------------------------------------------------------ factory

        public static Enemy Create(EnemyType type, Vector3 landing, GameManager gm)
        {
            var root = FX.NewObject(type.ToString());
            Vector3 look = gm.player.transform.position - landing; look.y = 0f;
            root.transform.SetPositionAndRotation(landing, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);
            var e = root.AddComponent<Enemy>();
            e.Setup(type, gm, landing);
            return e;
        }

        void Setup(EnemyType t, GameManager g, Vector3 landing)
        {
            gm = g;
            type = t;
            ArmorStyle style;
            switch (t)
            {
                case EnemyType.Grunt:
                    displayName = "Scrapper"; maxHealth = 44f; speed = 4.4f; damage = 10f; attackRange = 1.9f; windupTime = 0.62f; scale = 0.95f; scoreValue = 100;
                    poiseMax = 3f; ringRadius = Random.Range(3.3f, 5.2f); style = ArmorStyle.Scrapper;
                    break;
                case EnemyType.Caster:
                    displayName = "Hexer"; maxHealth = 34f; speed = 3.6f; damage = 12f; attackRange = 9f; windupTime = 0.75f; scale = 0.95f; scoreValue = 150;
                    poiseMax = 2f; ringRadius = Random.Range(8f, 10.5f); style = ArmorStyle.Hexer;
                    break;
                case EnemyType.Shield:
                    displayName = "Bulwark"; maxHealth = 72f; speed = 3.5f; damage = 12f; attackRange = 2.0f; windupTime = 0.7f; scale = 1.02f; scoreValue = 250;
                    poiseMax = 3f; ringRadius = Random.Range(3.1f, 4.4f); style = ArmorStyle.Bulwark;
                    break;
                case EnemyType.Brute:
                    displayName = "Juggernaut"; maxHealth = 190f; speed = 2.8f; damage = 24f; attackRange = 2.7f; windupTime = 1.0f; scale = 1.38f; scoreValue = 400;
                    superArmor = true; slamRadius = 3.2f; poiseMax = 99f; ringRadius = Random.Range(3.4f, 4.4f); style = ArmorStyle.Juggernaut;
                    break;
                default:
                    displayName = "Iron Tyrant"; maxHealth = 820f; speed = 3.0f; damage = 30f; attackRange = 3.8f; windupTime = 0.85f; scale = 2.1f; scoreValue = 5000;
                    superArmor = true; slamRadius = 4.6f; poiseMax = 999f; ringRadius = 4f; style = ArmorStyle.Tyrant;
                    break;
            }
            maxHealth *= 1f + Mathf.Max(0, gm.Wave - 1) * 0.08f;
            health = maxHealth;
            poise = poiseMax;
            windupTime *= Mathf.Lerp(1f, 0.78f, Mathf.Clamp01((gm.Wave - 1) / 10f));
            transform.localScale = Vector3.one * scale;
            facing = transform.forward;

            // body
            var m = Instantiate(gm.robotModel, transform);
            m.name = "Model";
            model = m.transform;
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            animator = m.GetComponent<Animator>();
            animator.runtimeAnimatorController = gm.characterController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            body = m.GetComponentInChildren<SkinnedMeshRenderer>();
            armor = ArmorKit.Build(animator, body, transform, style);
            Color pg = armor.Palette.glow;
            glowColor = pg / Mathf.Max(pg.r, Mathf.Max(pg.g, pg.b));

            cc = gameObject.AddComponent<CharacterController>();
            cc.center = new Vector3(0f, 0.97f, 0f);
            cc.height = 1.9f;
            cc.radius = 0.36f;
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.04f;
            cc.minMoveDistance = 0f;
            cc.enabled = false;

            // weapon (parented to the model so it falls with the body)
            rig = gameObject.AddComponent<WeaponRig>();
            var metal = FX.Lit(new Color(0.16f, 0.15f, 0.17f), 0.85f, 0.6f);
            var hot = FX.Emissive(glowColor, 3.2f);
            switch (t)
            {
                case EnemyType.Grunt: rig.weapon = Weapons.Dagger(model, metal, hot); rig.bladeLength = 0.68f; break;
                case EnemyType.Caster: rig.weapon = Weapons.Staff(model, metal, hot, out staffOrb, out staffLight, glowColor); rig.bladeLength = 1.24f; break;
                case EnemyType.Shield:
                    rig.weapon = Weapons.Baton(model, metal, hot); rig.bladeLength = 0.62f;
                    shieldGlow = new Material(FX.Emissive(glowColor, 2.4f));
                    shield = Weapons.RiotShield(model, FX.Lit(new Color(0.05f, 0.07f, 0.12f), 0.6f, 0.92f), shieldGlow);
                    shield.localPosition = new Vector3(-0.12f, 1.05f, 0.52f);
                    rig.leftHandTarget = shield.Find("Handle");
                    rig.leftHandWeight = 1f;
                    break;
                default: rig.weapon = Weapons.Hammer(model, metal, hot); rig.bladeLength = 1.25f; break;
            }
            if (t != EnemyType.Caster) rig.InitTrail(glowColor * 2.4f, displayName + "_Trail", t == EnemyType.Grunt ? 0.12f : 0.18f);
            rig.Snap(RestGrip(), RestDir());
            var ik = m.AddComponent<IKRelay>();
            ik.rig = rig;

            if (t != EnemyType.Boss) bar = EnemyHealthBar.Create(this, gm);
            indicator = CounterIndicator.Create(transform, 2.35f * scale, 0.55f + 0.15f * scale);

            // leap in over the parapet (the boss drops out of the sky)
            spawnTo = landing;
            if (t == EnemyType.Boss) spawnFrom = landing + Vector3.up * 26f;
            else
            {
                Vector3 outward = new Vector3(landing.x, 0f, landing.z);
                outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.forward;
                spawnFrom = outward * (gm.arenaRadius + 4.5f) + Vector3.down * 2f;
            }
            transform.position = spawnFrom;
            state = State.Spawning;
            stateTime = 0f;
            stateDur = t == EnemyType.Boss ? 1.25f : Random.Range(0.85f, 1.05f);
            nextCast = Time.time + Random.Range(1.8f, 3.2f);
            strafeSign = Random.value < 0.5f ? -1f : 1f;
            attackCooldown = Random.Range(1.2f, 2.2f);
            Sfx.Play(Sfx.I.servo ? Sfx.I.servo : Sfx.I.dash, t == EnemyType.Boss ? 0.9f : 0.3f, t == EnemyType.Boss ? 0.5f : 0.8f);
            if (t == EnemyType.Boss)
            {
                gm.cameraRig.Shake(0.4f);
                Sfx.Play(Sfx.I.whoom, 1f, 0.6f, 0f);
            }
        }

        void OnDestroy()
        {
            if (bar) Destroy(bar.gameObject);
            if (telegraph) Destroy(telegraph.gameObject);
            if (indicator) Destroy(indicator.gameObject);
            if (shieldGlow) Destroy(shieldGlow, 10f); // the dropped shield keeps using it while it lies on the roof
            if (gm)
            {
                gm.enemies.Remove(this);
                gm.ReleaseToken(this);
            }
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            if (gm == null) return;
            float dt = Time.deltaTime;
            stateTime += dt;
            if (state == State.Dead) { UpdateWreck(dt); return; }
            glowLevel = 1f;
            stunCooldown -= dt;

            var player = gm.player;
            Vector3 toP = player.transform.position - transform.position; toP.y = 0f;
            float dist = toP.magnitude;
            Vector3 dirP = dist > 0.001f ? toP / dist : facing;
            Vector3 move = Vector3.zero;
            bool face = false, physical = true;
            float turnRate = 540f;

            switch (state)
            {
                case State.Spawning:
                    UpdateSpawning();
                    physical = false;
                    break;
                case State.Circle:
                    move = UpdateCircle(dist, dirP, dt, ref face);
                    break;
                case State.Windup:
                    UpdateWindup(ref face, ref turnRate);
                    break;
                case State.Attack:
                    move = UpdateAttack(dist, dirP);
                    break;
                case State.Recover:
                    rig.SetTarget(RestGrip(), RestDir(), 7f);
                    model.localRotation = Quaternion.Slerp(model.localRotation, Quaternion.identity, 1f - Mathf.Exp(-12f * dt));
                    if (stateTime >= stateDur) Enter(State.Circle);
                    break;
                case State.Stagger:
                {
                    float s = Mathf.Clamp01(stateTime / stateDur);
                    model.localRotation = Quaternion.Euler(-18f * Mathf.Sin(s * Mathf.PI), 0f, 0f);
                    rig.SetTarget(RestGrip() + new Vector3(0f, 0.12f, -0.2f), RestDir(), 10f);
                    if (s >= 1f) Enter(State.Circle);
                    break;
                }
                case State.Stunned:
                    UpdateStunned(dt);
                    break;
                case State.Countered:
                {
                    face = true;
                    float s = Mathf.Clamp01(stateTime / 0.25f);
                    model.localRotation = Quaternion.Euler(-14f * s, 0f, 0f);
                    rig.SetTarget(WeaponRig.HGrip(70f, 1.62f, 0.45f), WeaponRig.HDir(70f, 0.7f), 14f);
                    glowLevel = 1.6f;
                    if (stateTime >= stateDur) Enter(State.Recover);
                    break;
                }
                case State.Downed:
                    UpdateDowned();
                    break;
                case State.GettingUp:
                {
                    float s = Mathf.Clamp01(stateTime / stateDur);
                    float k = 1f - s * s * (3f - 2f * s);
                    model.localRotation = Quaternion.Euler(-88f * k, 0f, 0f);
                    rig.SetTarget(RestGrip(), RestDir(), 8f);
                    if (s >= 1f) { model.localRotation = Quaternion.identity; Enter(State.Circle); }
                    break;
                }
                case State.Vaulted:
                    face = true;
                    turnRate = 260f;
                    glowLevel = 0.6f;
                    if (stateTime >= stateDur) Enter(State.Circle);
                    break;
                case State.Executed:
                    face = true;
                    physical = false;
                    glowLevel = 2f;
                    model.localRotation = Quaternion.Euler(-10f * Mathf.Clamp01(stateTime / 0.2f), 0f, 0f);
                    rig.SetTarget(RestGrip() + new Vector3(0.1f, 0.2f, -0.25f), new Vector3(0.6f, 0.3f, 0.4f), 6f);
                    if (stateTime > 3f) Enter(State.Circle); // safety: the player was interrupted
                    break;
            }

            if (physical && state != State.Dead)
            {
                bool grounded = state == State.Downed || state == State.GettingUp;
                Vector3 vel = (grounded ? Vector3.zero : move + Separation()) + knock;
                knock = Vector3.MoveTowards(knock, Vector3.zero, (grounded ? 9f : 16f) * dt);
                vy = cc.isGrounded ? -2f : vy - 25f * dt;
                if (cc.enabled) cc.Move((vel + Vector3.up * vy) * dt);
                Vector3 c = gm.ClampToArena(transform.position, 0.3f);
                if ((c - transform.position).sqrMagnitude > 1e-6f) Teleport(c);
            }

            if (state != State.Downed && state != State.GettingUp)
            {
                Vector3 want = face ? dirP : (move.sqrMagnitude > 0.05f ? move.normalized : facing);
                facing = Vector3.RotateTowards(facing, want, turnRate * Mathf.Deg2Rad * dt, 0f);
                facing.y = 0f;
                if (facing.sqrMagnitude < 1e-4f) facing = transform.forward;
                facing.Normalize();
            }
            transform.rotation = Quaternion.LookRotation(facing);

            float planar = new Vector3(move.x, 0f, move.z).magnitude / scale;
            animator.SetFloat(SpeedId, planar, 0.08f, Mathf.Max(dt, 0.0001f));
            animator.SetFloat(MotionSpeedId, 1f);
            animator.SetBool(GroundedId, true);

            UpdateShield(dt);
            rig.Tick(dt);
            flash = Mathf.MoveTowards(flash, 0f, dt * 7f);
            if (state == State.Windup || (state == State.Attack && !hitDone)) glowLevel = 1.2f + 1.6f * Mathf.Clamp01(stateTime / Mathf.Max(0.01f, stateDur));
            if (unblockable && (state == State.Windup || state == State.Attack)) armor.SetGlowColor(new Color(4f, 0.25f, 0.1f));
            else armor.SetGlowColor(enraged ? new Color(4f, 0.3f, 0.05f) : armor.Palette.glow);
            armor.SetEmission(Color.white * (flash * 2.2f), glowLevel);
            if (indicator) indicator.Show(CanBeCountered || UnblockableIncoming, unblockable);
        }

        void UpdateSpawning()
        {
            float u = Mathf.Clamp01(stateTime / stateDur);
            Vector3 p;
            if (type == EnemyType.Boss)
            {
                float k = u * u;
                p = Vector3.Lerp(spawnFrom, spawnTo, k);
                model.localRotation = Quaternion.identity;
            }
            else
            {
                p = Vector3.Lerp(spawnFrom, spawnTo, u);
                p.y += 5.2f * 4f * u * (1f - u);
                model.localRotation = Quaternion.Euler(28f * Mathf.Sin(u * Mathf.PI), 0f, 0f);
            }
            transform.position = p;
            rig.SetTarget(RestGrip() + new Vector3(0f, 0.25f, -0.1f), RestDir(), 10f);
            if (u >= 1f && !landed)
            {
                landed = true;
                transform.position = spawnTo;
                model.localRotation = Quaternion.identity;
                cc.enabled = true;
                FX.Dust(transform.position, type == EnemyType.Boss ? 3f : scale);
                Sfx.Play(Sfx.I.thud, type == EnemyType.Boss ? 1f : 0.5f, type == EnemyType.Boss ? 0.55f : 1f);
                if (type == EnemyType.Boss)
                {
                    gm.cameraRig.Shake(1f);
                    gm.Rumble(1f, 1f, 0.5f);
                    Sfx.Play(Sfx.I.slam, 1f, 0.55f, 0f);
                    FX.Shockwave(transform.position, 9f, glowColor * 2.4f, 0.8f);
                    FX.LightFlash(transform.position + Vector3.up * 2f, glowColor, 14f, 20f, 0.6f);
                    var pl = gm.player;
                    Vector3 away = pl.transform.position - transform.position; away.y = 0f;
                    if (away.magnitude < 5f) pl.TakeDamage(12f, transform.position, 14f);
                }
                else gm.cameraRig.Shake(0.12f);
                Enter(State.Circle);
            }
        }

        Vector3 UpdateCircle(float dist, Vector3 dirP, float dt, ref bool face)
        {
            rig.SetTarget(RestGrip(), RestDir(), 8f);
            face = true;
            if (gm.player.IsDead) return Vector3.zero;

            strafeTimer -= dt;
            if (strafeTimer <= 0f)
            {
                strafeTimer = Random.Range(1.4f, 3.2f);
                strafeSign = -strafeSign;
            }
            Vector3 perp = new Vector3(-dirP.z, 0f, dirP.x) * strafeSign;
            float reach = attackRange + 0.25f + gm.player.Radius;

            if (type == EnemyType.Caster)
            {
                if (Time.time >= nextCast && dist < 16f && gm.projectiles.Count < 3)
                {
                    Enter(State.Windup);
                    return Vector3.zero;
                }
                return Ring(dist, dirP, perp, 0.55f);
            }

            if (type == EnemyType.Boss)
            {
                attackCooldown -= dt;
                if (dist > reach) return dirP * (dist > 8f ? speed * 1.5f : speed) * (enraged ? 1.2f : 1f);
                if (attackCooldown <= 0f) { sweep = Random.value < 0.45f; Enter(State.Windup); return Vector3.zero; }
                return perp * (speed * 0.3f);
            }

            if (hasToken)
            {
                tokenDelay -= dt;
                if (tokenDelay > 0f) return Ring(dist, dirP, perp, 0.25f);
                if (dist > reach * 0.92f) return dirP * (speed * 1.3f);
                Enter(State.Windup);
                return Vector3.zero;
            }
            return Ring(dist, dirP, perp, 0.32f);
        }

        Vector3 Ring(float dist, Vector3 dirP, Vector3 perp, float strafe)
        {
            float err = dist - ringRadius;
            return dirP * (Mathf.Clamp(err * 1.4f, -1f, 1f) * speed * 0.8f) + perp * (speed * strafe);
        }

        void UpdateWindup(ref bool face, ref float turnRate)
        {
            float u = Mathf.Clamp01(stateTime / stateDur);
            switch (type)
            {
                case EnemyType.Grunt:
                    face = u < 0.8f;
                    rig.SetTarget(WeaponRig.HGrip(108f, 1.38f, 0.42f), WeaponRig.HDir(108f, 0.35f), 16f);
                    rig.torsoTwist = Mathf.Lerp(rig.torsoTwist, 28f, 0.2f);
                    break;
                case EnemyType.Shield:
                    face = u < 0.8f;
                    rig.SetTarget(WeaponRig.HGrip(95f, 1.62f, 0.32f), new Vector3(0.2f, 0.9f, 0.2f), 14f);
                    break;
                case EnemyType.Caster:
                    face = true;
                    rig.SetTarget(new Vector3(0.22f, 1.62f, 0.38f), new Vector3(0f, 1f, 0.3f), 10f);
                    if (staffOrb) staffOrb.localScale = Vector3.one * Mathf.Lerp(0.26f, 0.6f, u);
                    if (staffLight) staffLight.intensity = Mathf.Lerp(1.2f, 7f, u);
                    break;
                default:
                    if (sweep)
                    {
                        face = u < 0.7f;
                        turnRate = 300f;
                        rig.SetTarget(WeaponRig.HGrip(118f, 1.32f, 0.62f), WeaponRig.HDir(118f, 0.15f), 10f);
                        rig.leftHandWeight = 1f;
                        rig.torsoTwist = Mathf.Lerp(rig.torsoTwist, 35f, 0.15f);
                    }
                    else
                    {
                        face = u < 0.55f;
                        turnRate = 220f;
                        if (u < 0.55f)
                        {
                            slamPoint = ComputeSlamPoint();
                            if (telegraph) telegraph.transform.position = slamPoint;
                        }
                        if (telegraph) telegraph.SetProgress(u);
                        rig.SetTarget(WeaponRig.VGrip(-48f), WeaponRig.VDir(-48f), 9f);
                        rig.leftHandWeight = 1f;
                    }
                    break;
            }
            if (stateTime >= stateDur) Enter(State.Attack);
        }

        Vector3 UpdateAttack(float dist, Vector3 dirP)
        {
            float u = Mathf.Clamp01(stateTime / stateDur);
            Vector3 move = Vector3.zero;
            var player = gm.player;
            switch (type)
            {
                case EnemyType.Grunt:
                {
                    float k = 1f - (1f - u) * (1f - u);
                    float ang = Mathf.Lerp(108f, -100f, k);
                    rig.Snap(WeaponRig.HGrip(ang, Mathf.Lerp(1.38f, 1.15f, k), 0.42f), WeaponRig.HDir(ang, Mathf.Lerp(0.3f, -0.05f, k)));
                    rig.torsoTwist = Mathf.Lerp(28f, -30f, k);
                    if (!hitDone && u >= 0.45f)
                    {
                        hitDone = true;
                        if (dist <= attackRange + 0.6f && Vector3.Angle(facing, dirP) < 80f) player.TakeDamage(damage, transform.position, 7f, unblockable);
                    }
                    if (u < 0.6f && dist > 1.1f) move = lungeDir * (8.5f * (1f - u / 0.6f));
                    break;
                }
                case EnemyType.Shield:
                {
                    float k = Mathf.Sin(Mathf.Clamp01(u / 0.5f) * Mathf.PI * 0.5f);
                    rig.Snap(Vector3.Lerp(WeaponRig.HGrip(60f, 1.5f, 0.35f), new Vector3(0.12f, 1.3f, 0.78f), k), Vector3.Lerp(new Vector3(0.2f, 0.9f, 0.2f), new Vector3(0f, 0.05f, 1f), k));
                    if (!hitDone && u >= 0.5f)
                    {
                        hitDone = true;
                        if (dist <= attackRange + 0.6f && Vector3.Angle(facing, dirP) < 70f) player.TakeDamage(damage, transform.position, 8f, false);
                    }
                    if (u < 0.5f && dist > 1.2f) move = lungeDir * 5f;
                    break;
                }
                case EnemyType.Caster:
                    if (!hitDone)
                    {
                        hitDone = true;
                        Vector3 from = staffOrb ? staffOrb.position : Chest;
                        Vector3 target = player.Chest + player.Velocity * 0.3f;
                        Projectile.Spawn(from, target - from, damage, gm, glowColor);
                        if (staffOrb) staffOrb.localScale = Vector3.one * 0.26f;
                        if (staffLight) staffLight.intensity = 1.2f;
                        Sfx.Play(Sfx.I.cast, 0.55f);
                        FX.Burst(from, glowColor, Color.white, 14, new Vector2(1f, 4f), new Vector2(0.05f, 0.12f), new Vector2(0.2f, 0.4f), 0f, true, 0.1f);
                    }
                    rig.SetTarget(new Vector3(0.18f, 1.4f, 0.65f), new Vector3(0f, 0.55f, 1f), 18f);
                    break;
                default:
                    if (sweep)
                    {
                        float k = u * u * (3f - 2f * u);
                        float ang = Mathf.Lerp(118f, -115f, k);
                        rig.Snap(WeaponRig.HGrip(ang, 1.25f, 0.62f), WeaponRig.HDir(ang, -0.05f));
                        rig.SetTrail(true);
                        rig.torsoTwist = Mathf.Lerp(35f, -35f, k);
                        if (!hitDone && u >= 0.5f)
                        {
                            hitDone = true;
                            if (dist <= attackRange + 1.5f + player.Radius && Vector3.Angle(facing, dirP) < 110f) player.TakeDamage(damage * 0.8f, transform.position, 15f, false);
                            gm.cameraRig.Shake(0.35f);
                        }
                    }
                    else
                    {
                        float phi = Mathf.Lerp(-48f, 140f, u * u);
                        rig.Snap(WeaponRig.VGrip(phi), WeaponRig.VDir(phi));
                        rig.SetTrail(true);
                        if (!hitDone && u >= 1f) { hitDone = true; Slam(); }
                    }
                    break;
            }

            if (stateTime >= stateDur)
            {
                if (type == EnemyType.Boss && !sweep && slamCount < 3 && !player.IsDead) Enter(State.Windup);
                else Enter(State.Recover);
            }
            return move;
        }

        Vector3 ComputeSlamPoint()
        {
            float reach = Mathf.Clamp((gm.player.transform.position - transform.position).magnitude, 1.2f * scale, 1.9f * scale);
            Vector3 p = transform.position + facing * reach;
            p.y = 0f;
            return p;
        }

        void Slam()
        {
            slamCount++;
            if (telegraph) { telegraph.Kill(); telegraph = null; }
            rig.SetTrail(false);
            FX.Shockwave(slamPoint, slamRadius * 1.15f, glowColor * 2.2f, 0.5f);
            FX.Burst(slamPoint + Vector3.up * 0.2f, glowColor, new Color(1f, 0.85f, 0.5f), 30, new Vector2(3f, 9f), new Vector2(0.05f, 0.12f), new Vector2(0.3f, 0.7f), 2f, true, slamRadius * 0.4f, Vector3.up, 50f);
            FX.Dust(slamPoint, slamRadius * 0.45f);
            FX.LightFlash(slamPoint + Vector3.up, glowColor, 8f, slamRadius * 3f, 0.3f);
            var player = gm.player;
            Vector3 d = player.transform.position - slamPoint; d.y = 0f;
            if (d.magnitude <= slamRadius + 0.3f) player.TakeDamage(damage, slamPoint, 13f, true);
            Vector3 toMe = player.transform.position - transform.position; toMe.y = 0f;
            float k = Mathf.Lerp(type == EnemyType.Boss ? 0.9f : 0.6f, 0.15f, Mathf.Clamp01(toMe.magnitude / 16f));
            gm.cameraRig.Shake(k);
            gm.Rumble(k, k * 0.6f, 0.25f);
            Sfx.Play(Sfx.I.slam, 0.85f, type == EnemyType.Boss ? 0.7f : 0.95f);
        }

        void UpdateStunned(float dt)
        {
            float s = stateTime;
            model.localRotation = Quaternion.Euler(-8f + 6f * Mathf.Sin(s * 7f), 9f * Mathf.Sin(s * 4.3f), 7f * Mathf.Sin(s * 5.1f));
            rig.SetTarget(RestGrip() + new Vector3(0.05f, -0.15f, -0.1f), new Vector3(0.3f, -0.4f, 0.8f), 5f);
            if (type != EnemyType.Shield) rig.leftHandWeight = Mathf.MoveTowards(rig.leftHandWeight, 0f, dt * 4f);
            glowLevel = Random.value < 0.35f ? 0.15f : 1.4f;
            arcTimer -= dt;
            if (arcTimer <= 0f)
            {
                arcTimer = Random.Range(0.06f, 0.16f);
                Vector3 a = Chest + Random.insideUnitSphere * (0.45f * scale);
                Vector3 b = Chest + Random.insideUnitSphere * (0.55f * scale) + Vector3.up * (0.4f * scale);
                FX.Arc(a, b, new Color(0.6f, 1.4f, 3.5f, 1f), 0.12f, 0.025f * scale);
            }
            if (stateTime >= stateDur) { model.localRotation = Quaternion.identity; Enter(State.Circle); }
        }

        void UpdateDowned()
        {
            float s = stateTime;
            float fall = Mathf.Clamp01(s / 0.32f);
            float ang = 88f * fall * fall;
            if (s > 0.32f && s < 0.55f) ang = 88f - 7f * Mathf.Sin((s - 0.32f) / 0.23f * Mathf.PI);
            model.localRotation = Quaternion.Euler(-ang, 0f, 0f);
            rig.SetTarget(new Vector3(0.45f, 0.5f, -0.35f), new Vector3(1f, 0.2f, -0.3f), 6f);
            glowLevel = 0.35f + 0.25f * Mathf.Sin(s * 6f);
            if (stateTime >= stateDur) Enter(State.GettingUp);
        }

        void UpdateShield(float dt)
        {
            if (!shield) return;
            float target = (state == State.Stunned || IsDowned || state == State.Countered) ? 1f : 0f;
            shieldLower = Mathf.MoveTowards(shieldLower, target, dt * 5f);
            shield.localPosition = Vector3.Lerp(new Vector3(-0.12f, 1.05f, 0.52f), new Vector3(-0.42f, 0.7f, 0.2f), shieldLower);
            shield.localRotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(25f, -60f, -20f), shieldLower);
            if (shieldGlow) shieldGlow.SetColor("_EmissionColor", glowColor * Mathf.Lerp(2.4f, 0.3f, shieldLower) * (1f + flash));
            rig.leftHandWeight = IsDowned ? 0f : 1f;
        }

        void Enter(State s)
        {
            var prev = state;
            state = s;
            stateTime = 0f;
            if (prev == State.Windup || prev == State.Attack) rig.torsoTwist = 0f;
            switch (s)
            {
                case State.Circle:
                    model.localRotation = Quaternion.identity;
                    rig.SetTrail(false);
                    rig.torsoTwist = 0f;
                    if (type != EnemyType.Shield) rig.leftHandWeight = 0f;
                    break;
                case State.Windup:
                    hitDone = false;
                    if (type == EnemyType.Boss) stateDur = (slamCount > 0 && !sweep ? 0.5f : windupTime * (sweep ? 1.1f : 1f)) * (enraged ? 0.8f : 1f);
                    else stateDur = windupTime * Random.Range(0.92f, 1.08f);
                    unblockable = (type == EnemyType.Brute) || (type == EnemyType.Boss && !sweep) ||
                                  (type == EnemyType.Grunt && gm.Wave >= 4 && Random.value < 0.22f);
                    if ((type == EnemyType.Brute || type == EnemyType.Boss) && !sweep)
                    {
                        slamPoint = ComputeSlamPoint();
                        if (telegraph) telegraph.Kill();
                        telegraph = FX.Telegraph(slamPoint, slamRadius, new Color(1.6f, 0.2f, 0.08f));
                        Sfx.Play(Sfx.I.warn, 0.4f, 0.7f, 0f);
                    }
                    else if (type == EnemyType.Caster) Sfx.Play(Sfx.I.charge, 0.35f, 1.6f);
                    else Sfx.Play(Sfx.I.warn, unblockable ? 0.4f : 0.2f, unblockable ? 0.9f : 1.5f);
                    break;
                case State.Attack:
                    hitDone = false;
                    lungeDir = facing;
                    stateDur = type == EnemyType.Grunt ? 0.3f : type == EnemyType.Caster ? 0.22f : type == EnemyType.Shield ? 0.32f : sweep ? 0.32f : 0.17f;
                    if (type == EnemyType.Grunt || type == EnemyType.Shield) { rig.SetTrail(true); Sfx.Play(Sfx.I.swing, 0.45f, 1.3f); }
                    else if (type != EnemyType.Caster) Sfx.Play(Sfx.I.swingHeavy, 0.6f, 0.75f);
                    LastAttackTime = Time.time;
                    break;
                case State.Recover:
                    rig.SetTrail(false);
                    slamCount = 0;
                    unblockable = false;
                    stateDur = type == EnemyType.Grunt ? 0.6f : type == EnemyType.Caster ? 0.45f : type == EnemyType.Shield ? 0.65f : type == EnemyType.Brute ? 1.0f : 1.1f;
                    if (type == EnemyType.Caster) nextCast = Time.time + Random.Range(2.6f, 3.8f);
                    if (type == EnemyType.Boss) attackCooldown = Random.Range(0.9f, 1.8f) * (enraged ? 0.7f : 1f);
                    DropToken();
                    break;
                case State.Stagger:
                    stateDur = 0.32f;
                    CancelAttack();
                    break;
                case State.Stunned:
                    CancelAttack();
                    rig.SetTrail(false);
                    Sfx.Play(Sfx.I.stun, 0.5f, Random.Range(0.9f, 1.15f));
                    break;
                case State.Countered:
                    CancelAttack();
                    stateDur = 1.1f;
                    break;
                case State.Downed:
                    CancelAttack();
                    rig.leftHandWeight = 0f;
                    stateDur = (type == EnemyType.Caster ? 2.4f : 2.9f) * Random.Range(0.9f, 1.15f);
                    Sfx.Play(Sfx.I.thud, 0.55f, Random.Range(0.85f, 1.05f));
                    break;
                case State.GettingUp:
                    stateDur = 0.55f;
                    poise = poiseMax;
                    break;
                case State.Vaulted:
                    CancelAttack();
                    stateDur = 0.75f;
                    break;
                case State.Executed:
                    CancelAttack();
                    break;
            }
        }

        void CancelAttack()
        {
            rig.SetTrail(false);
            slamCount = 0;
            unblockable = false;
            sweep = false;
            if (telegraph) { telegraph.Kill(); telegraph = null; }
            if (staffOrb) staffOrb.localScale = Vector3.one * 0.26f;
            if (staffLight) staffLight.intensity = 1.2f;
            if (type == EnemyType.Caster) nextCast = Time.time + Random.Range(1.5f, 2.5f);
            if (type == EnemyType.Boss) attackCooldown = Random.Range(1.2f, 2f);
            DropToken();
        }

        void DropToken()
        {
            hasToken = false;
            gm.ReleaseToken(this);
        }

        public void GrantToken(float delay)
        {
            hasToken = true;
            tokenDelay = delay;
        }

        Vector3 Separation()
        {
            Vector3 s = Vector3.zero;
            var list = gm.enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o == null || o == this || o.IsDowned) continue;
                Vector3 d = transform.position - o.transform.position; d.y = 0f;
                float r = Radius + o.Radius + 0.45f;
                float m = d.magnitude;
                if (m < r && m > 0.001f) s += d / m * ((r - m) * 6f);
            }
            return Vector3.ClampMagnitude(s, 4f);
        }

        void Teleport(Vector3 p)
        {
            bool was = cc.enabled;
            cc.enabled = false;
            transform.position = p;
            cc.enabled = was;
        }

        Vector3 RestGrip()
        {
            switch (type)
            {
                case EnemyType.Grunt: return new Vector3(0.3f, 1.0f, 0.28f);
                case EnemyType.Caster: return new Vector3(0.32f, 1.0f, 0.18f);
                case EnemyType.Shield: return new Vector3(0.32f, 1.02f, 0.3f);
                default: return new Vector3(0.34f, 1.05f, 0.3f);
            }
        }

        Vector3 RestDir()
        {
            switch (type)
            {
                case EnemyType.Grunt: return new Vector3(0.1f, 0.25f, 1f);
                case EnemyType.Caster: return new Vector3(0.05f, 1f, 0.25f);
                case EnemyType.Shield: return new Vector3(0.15f, 0.7f, 0.6f);
                default: return new Vector3(0.12f, 0.85f, 0.45f);
            }
        }

        // ------------------------------------------------------------------ combat API

        public bool IsThreatening(Vector3 p)
        {
            if (state != State.Windup && !(state == State.Attack && !hitDone)) return false;
            if (state == State.Windup && stateTime / stateDur < 0.45f) return false;
            Vector3 d;
            switch (type)
            {
                case EnemyType.Grunt:
                case EnemyType.Shield:
                    d = p - transform.position; d.y = 0f;
                    return d.magnitude < attackRange + 1.4f;
                case EnemyType.Brute:
                case EnemyType.Boss:
                    if (sweep) { d = p - transform.position; d.y = 0f; return d.magnitude < attackRange + 2f; }
                    d = p - slamPoint; d.y = 0f;
                    return d.magnitude < slamRadius + 0.7f;
                default:
                    return false;
            }
        }

        /// <summary>Riot shield stops frontal strikes while it is raised.</summary>
        public bool BlocksFrom(Vector3 attackerPos, HitKind kind)
        {
            if (!ShieldUp || kind == HitKind.Nova || kind == HitKind.Takedown || kind == HitKind.Counter || kind == HitKind.Stab) return false;
            Vector3 to = attackerPos - transform.position; to.y = 0f;
            return to.sqrMagnitude > 0.01f && Vector3.Angle(facing, to) < 75f;
        }

        public void OnCountered(float hold = 1.1f)
        {
            if (!Targetable) return;
            Enter(State.Countered);
            stateDur = hold;
        }

        /// <summary>Arc stun (Arkham's cape stun). Returns false if the robot shrugged it off.</summary>
        public bool Stun(float duration, bool force = false)
        {
            if (!Targetable || IsDowned) return false;
            if (type == EnemyType.Boss && stunCooldown > 0f && !force) return false;
            if (type == EnemyType.Boss) { duration *= 0.8f; stunCooldown = 5f; }
            Enter(State.Stunned);
            stateDur = duration;
            return true;
        }

        public void OnVaulted()
        {
            if (!Targetable || IsDowned || type == EnemyType.Boss) return;
            if (state == State.Windup || state == State.Attack || state == State.Circle || state == State.Recover) Enter(State.Vaulted);
        }

        public void BeginExecution()
        {
            if (!Targetable) return;
            Enter(State.Executed);
        }

        /// <summary>Keeps a downed robot on the floor while a ground takedown lands.</summary>
        public void HoldDown(float seconds)
        {
            if (state == State.Downed) stateDur = Mathf.Max(stateDur, stateTime + seconds);
            else if (state == State.GettingUp) { Enter(State.Downed); stateTime = 0.6f; stateDur = 0.6f + seconds; }
        }

        public void ReleaseExecution()
        {
            if (state == State.Executed) Enter(State.Circle);
        }

        public void Knockdown(Vector3 dir)
        {
            if (state == State.Dead) return;
            if (state == State.Downed) { stateTime = Mathf.Min(stateTime, 0.6f); return; } // keep the body where it lies
            Vector3 d = new Vector3(dir.x, 0f, dir.z);
            if (d.sqrMagnitude > 1e-4f) facing = -d.normalized;
            transform.rotation = Quaternion.LookRotation(facing);
            Enter(State.Downed);
        }

        /// <summary>Returns true when the hit landed (false when the riot shield blocked it).</summary>
        public bool TakeHit(HitInfo h, Vector3 attackerPos)
        {
            if (!Targetable && state != State.Executed) return false;
            if (BlocksFrom(attackerPos, h.kind))
            {
                Vector3 sp = shield ? shield.position + Vector3.up * 0.1f : Chest;
                FX.Clash(sp, -h.dir, glowColor);
                Sfx.Play(Sfx.I.clang, 0.6f, 1.2f);
                Sfx.Play(Sfx.I.parry, 0.25f, 1.5f);
                flash = 0.5f;
                knock = h.dir * 2.5f;
                return false;
            }

            float dmg = h.damage;
            if (IsArmored && (h.kind == HitKind.Strike || h.kind == HitKind.Heavy)) dmg *= 0.3f;
            if (state == State.Stunned && h.kind == HitKind.Strike) dmg *= 1.35f;
            health -= dmg;
            flash = 1f;
            float kbScale = superArmor ? (type == EnemyType.Boss ? 0.08f : 0.25f) : 1f;
            knock = h.dir * (h.knockback * kbScale);

            Vector3 hitPos = (IsDowned ? BodyCenter : Chest) - h.dir * (Radius * 0.8f);
            FX.Sparks(hitPos, -h.dir, h.crit, h.kind != HitKind.Strike);
            FX.DamageNumber(Head + Random.insideUnitSphere * 0.35f, dmg, h.crit);
            Sfx.Hit(h.crit, h.kind != HitKind.Strike, IsArmored);
            if (bar) bar.Show();
            gm.RegisterHit(dmg);

            if ((h.crit || h.kind == HitKind.Heavy || h.kind == HitKind.Counter) && Random.value < 0.5f) armor.Chip(h.dir, 5f);

            if (health <= 0f) { Die(h); return true; }

            if (type == EnemyType.Boss && !enraged && Health01 < 0.5f)
            {
                enraged = true;
                gm.hud.Popup("THE TYRANT IS ENRAGED", new Color(1f, 0.35f, 0.15f));
                armor.SetGlowColor(new Color(4f, 0.3f, 0.05f));
                FX.Shockwave(transform.position, 7f, glowColor * 2.5f, 0.7f);
                Sfx.Play(Sfx.I.charge, 0.9f, 0.6f, 0f);
            }

            switch (h.kind)
            {
                case HitKind.Counter:
                    if (!superArmor) Knockdown(h.dir);
                    else Stun(type == EnemyType.Boss ? 1.6f : 1.4f, true);
                    break;
                case HitKind.Heavy:
                case HitKind.Nova:
                    if (!superArmor) Knockdown(h.dir);
                    else if (state != State.Stunned && type == EnemyType.Brute && h.kind == HitKind.Nova) Stun(1.2f, true);
                    break;
                case HitKind.Takedown:
                    Stun(1.8f, true);
                    break;
                case HitKind.Strike:
                case HitKind.Projectile:
                    if (IsDowned) break;
                    if (!superArmor || (type == EnemyType.Brute && state == State.Stunned))
                    {
                        poise -= 1f;
                        if (poise <= 0f && !superArmor) Knockdown(h.dir);
                        else if (state != State.Stunned) Enter(State.Stagger);
                    }
                    break;
            }
            return true;
        }

        /// <summary>Finisher from a special-combo or ground takedown.</summary>
        public void Execute(HitInfo h)
        {
            if (state == State.Dead) return;
            if (type == EnemyType.Boss)
            {
                if (state == State.Executed) Enter(State.Circle); // leave the lock so the takedown stun can apply
                h.kind = HitKind.Takedown;
                h.damage = maxHealth * 0.2f;
                TakeHit(h, transform.position - h.dir);
                return;
            }
            health = 0f;
            flash = 1f;
            FX.Sparks(Chest, -h.dir, true, true);
            Sfx.Hit(true, true, false);
            gm.RegisterHit(h.damage);
            Die(h);
        }

        void Die(HitInfo h)
        {
            state = State.Dead;
            stateTime = 0f;
            cc.enabled = false;
            hasToken = false;
            if (telegraph) { telegraph.Kill(); telegraph = null; }
            if (indicator) { Destroy(indicator.gameObject); indicator = null; }
            if (bar) { Destroy(bar.gameObject); bar = null; }
            gm.RegisterKill(this);

            Vector3 dir = h.dir.sqrMagnitude > 1e-4f ? h.dir.normalized : -facing;
            fallDir = new Vector3(dir.x, 0f, dir.z).normalized;
            if (fallDir.sqrMagnitude < 0.01f) fallDir = -facing;
            knock = fallDir * Mathf.Min(h.knockback * 0.6f, 7f);
            animator.enabled = false;
            rig.SetTrail(false);
            rig.rightHandWeight = 0f;
            rig.leftHandWeight = 0f;

            // Robo Recall-style destruction
            Vector3 up = Vector3.up;
            switch (h.kind)
            {
                case HitKind.Takedown:
                    armor.Decapitate(dir * 4f + up * 7f);
                    break;
                case HitKind.Stab:
                    armor.Decapitate(up * 4f + Random.insideUnitSphere * 2f);
                    break;
                case HitKind.Nova:
                    armor.BlowApart(Chest - dir, 7f);
                    armor.Decapitate(dir * 5f + up * 9f);
                    break;
                default:
                {
                    float r = Random.value;
                    if (h.crit || h.kind != HitKind.Strike)
                    {
                        if (r < 0.5f) armor.Decapitate(dir * 5f + up * 6f);
                        else armor.SeverArm(Random.value < 0.5f, dir * 4f + up * 4f);
                        if (Random.value < 0.4f) armor.SeverArm(Random.value < 0.5f, dir * 3f + up * 3f);
                    }
                    else if (r < 0.35f) armor.Decapitate(dir * 4f + up * 5f);
                    else if (r < 0.6f) armor.SeverArm(Random.value < 0.5f, dir * 3f + up * 3f);
                    break;
                }
            }
            if (type == EnemyType.Boss || type == EnemyType.Brute) armor.BlowApart(Chest, type == EnemyType.Boss ? 9f : 6f);
            DropWeapon(dir);

            FX.Sparks(Chest, dir, true, true);
            FX.LightFlash(Chest, glowColor, 6f * scale, 7f * scale, 0.35f);
            if (type == EnemyType.Boss || type == EnemyType.Brute || h.kind == HitKind.Nova) FX.Explosion(Chest, glowColor, type == EnemyType.Boss ? 2f : scale * 0.8f);
            else FX.Burst(Chest, new Color(0.12f, 0.1f, 0.14f, 0.6f), new Color(0.05f, 0.04f, 0.06f, 0.4f), 8, new Vector2(0.5f, 1.5f), new Vector2(0.4f, 0.9f), new Vector2(0.6f, 1.2f), -0.08f, false, 0.25f);
            Sfx.Play(Sfx.I.death, 0.85f, type == EnemyType.Boss ? 0.55f : type == EnemyType.Brute ? 0.8f : 1.05f);
            gm.cameraRig.Shake(type == EnemyType.Boss ? 1f : type == EnemyType.Brute ? 0.5f : 0.25f);
            gm.Rumble(0.35f, 0.6f, 0.12f);

            int drops = type == EnemyType.Boss ? 6 : type == EnemyType.Brute ? 1 : (Random.value < 0.2f ? 1 : 0);
            for (int i = 0; i < drops; i++) HealthOrb.Spawn(Chest, gm);
            gm.player.AddRage(type == EnemyType.Boss ? 100f : type == EnemyType.Brute ? 18f : 8f);
        }

        void DropWeapon(Vector3 dir)
        {
            foreach (var w in new[] { rig.weapon, shield })
            {
                if (!w) continue;
                var rends = w.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) continue;
                w.SetParent(FX.Root, true);
                var bounds = rends[0].bounds;
                foreach (var r in rends) bounds.Encapsulate(r.bounds);
                var bc = w.gameObject.AddComponent<BoxCollider>();
                bc.center = w.InverseTransformPoint(bounds.center);
                Vector3 ls = w.InverseTransformVector(bounds.size);
                bc.size = new Vector3(Mathf.Max(0.04f, Mathf.Abs(ls.x)), Mathf.Max(0.04f, Mathf.Abs(ls.y)), Mathf.Max(0.04f, Mathf.Abs(ls.z)));
                Debris.MakePhysical(w.gameObject, dir * 3f + Vector3.up * 3.5f + Random.insideUnitSphere, Random.insideUnitSphere * 10f, 2f, false);
            }
            rig.weapon = null;
            shield = null;
        }

        void UpdateWreck(float dt)
        {
            float t = stateTime;
            float u = Mathf.Clamp01(t / 0.42f);
            float ang = 90f * u * u;
            if (t > 0.42f && t < 0.7f) ang = 90f - 8f * Mathf.Sin((t - 0.42f) / 0.28f * Mathf.PI);
            Vector3 axis = Vector3.Cross(Vector3.up, fallDir);
            if (axis.sqrMagnitude < 1e-4f) axis = transform.right;
            model.rotation = Quaternion.AngleAxis(ang, axis.normalized) * transform.rotation;
            transform.position += knock * dt;
            knock = Vector3.MoveTowards(knock, Vector3.zero, 14f * dt);
            transform.position = gm.ClampToArena(transform.position, 0.3f);
            armor.PowerDown(t / 2.4f);
            if (t > 3.3f) model.localPosition += Vector3.down * (dt * 0.6f);
            if (t > 4.8f) Destroy(gameObject);
        }
    }

    /// <summary>Billboarded world-space health bar with a delayed "chip" segment.</summary>
    public class EnemyHealthBar : MonoBehaviour
    {
        const float W = 0.9f, H = 0.07f;
        Enemy owner;
        Transform fill, chip;
        Renderer[] renderers;
        float chipValue = 1f, chipDelay, visibleTimer;
        bool visible;

        public static EnemyHealthBar Create(Enemy e, GameManager gm)
        {
            var go = FX.NewObject("HealthBar_" + e.displayName);
            var bar = go.AddComponent<EnemyHealthBar>();
            bar.owner = e;
            var mpb = new MaterialPropertyBlock();
            bar.MakeQuad("BG", new Color(0f, 0f, 0f, 0.6f), W + 0.04f, H + 0.04f, 0f, 1, mpb);
            bar.chip = bar.MakeQuad("Chip", new Color(1f, 0.92f, 0.75f, 1f), W, H, -0.002f, 2, mpb);
            bar.fill = bar.MakeQuad("Fill", e.superArmor ? new Color(1.4f, 0.55f, 0.1f, 1f) : new Color(1.3f, 0.15f, 0.2f, 1f), W, H, -0.004f, 3, mpb);
            bar.renderers = go.GetComponentsInChildren<Renderer>();
            bar.SetVisible(false);
            return bar;
        }

        Transform MakeQuad(string n, Color c, float w, float h, float z, int order, MaterialPropertyBlock mpb)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = n;
            var col = q.GetComponent<Collider>();
            if (col) DestroyImmediate(col);
            q.transform.SetParent(transform, false);
            q.transform.localPosition = new Vector3(0f, 0f, z);
            q.transform.localScale = new Vector3(w, h, 1f);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = FX.FlatMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingOrder = order;
            mpb.SetColor("_Color", c);
            r.SetPropertyBlock(mpb);
            return q.transform;
        }

        public void Show()
        {
            chipDelay = 0.35f;
            visibleTimer = 4f;
            if (!visible) SetVisible(true);
        }

        void SetVisible(bool v)
        {
            visible = v;
            foreach (var r in renderers) if (r) r.enabled = v;
        }

        void LateUpdate()
        {
            if (owner == null || owner.IsDead) { Destroy(gameObject); return; }
            var cam = Camera.main;
            transform.position = owner.Head + Vector3.up * 0.32f;
            if (cam) transform.rotation = cam.transform.rotation;
            visibleTimer -= Time.unscaledDeltaTime;
            if (visible && visibleTimer <= 0f) SetVisible(false);
            float v = owner.Health01;
            chipDelay -= Time.deltaTime;
            if (chipDelay <= 0f) chipValue = Mathf.MoveTowards(chipValue, v, Time.deltaTime * 1.2f);
            if (chipValue < v) chipValue = v;
            SetFill(fill, v, -0.004f);
            SetFill(chip, chipValue, -0.002f);
        }

        static void SetFill(Transform t, float v, float z)
        {
            v = Mathf.Clamp01(v);
            t.localScale = new Vector3(Mathf.Max(0.0001f, W * v), H, 1f);
            t.localPosition = new Vector3(-W * 0.5f + W * v * 0.5f, 0f, z);
        }
    }
}
