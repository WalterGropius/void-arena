using UnityEngine;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    /// <summary>
    /// Robot enemies built at runtime from the humanoid robot model.
    /// Scrapper (dagger lunge), Hexer (ranged homing bolts), Juggernaut (telegraphed AoE slam), Iron Tyrant (boss triple-slam).
    /// </summary>
    public class Enemy : MonoBehaviour
    {
        public enum State { Spawning, Chase, Windup, Attack, Recover, Stagger, Dead }

        public EnemyType type;
        public string displayName;
        public float maxHealth, health, speed, damage, attackRange, windupTime, slamRadius, scale = 1f;
        public long scoreValue;
        public bool superArmor;

        public Animator animator;
        public Transform model;
        public WeaponRig rig;
        public SkinnedMeshRenderer body;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        GameManager gm;
        CharacterController cc;
        Material bodyMat;
        Color tint, glowColor, glow;
        Transform staffOrb;
        Light staffLight;
        State state;
        float stateTime, stateDur;
        Vector3 knock, facing = Vector3.forward, slamPoint, lungeDir;
        float vy, flash, nextCast, strafeTimer, strafeSign = 1f;
        bool hitDone;
        int slamCount;
        TelegraphFX telegraph;
        EnemyHealthBar bar;

        public bool IsDead => state == State.Dead;
        public bool Targetable => state != State.Dead && state != State.Spawning;
        public Vector3 Chest => transform.position + Vector3.up * (1.2f * scale);
        public Vector3 Head => transform.position + Vector3.up * (2.0f * scale);
        public float Radius => 0.42f * scale;
        public float Health01 => Mathf.Clamp01(health / maxHealth);
        public Color GlowColor => glowColor;

        // ------------------------------------------------------------------ factory

        public static Enemy Create(EnemyType type, Vector3 pos, GameManager gm)
        {
            var root = FX.NewObject(type.ToString());
            Vector3 look = gm.player.transform.position - pos; look.y = 0f;
            root.transform.SetPositionAndRotation(pos, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);
            var e = root.AddComponent<Enemy>();
            e.Setup(type, gm);
            return e;
        }

        void Setup(EnemyType t, GameManager g)
        {
            gm = g;
            type = t;
            switch (t)
            {
                case EnemyType.Grunt:
                    displayName = "Scrapper"; maxHealth = 42f; speed = 4.2f; damage = 10f; attackRange = 1.9f; windupTime = 0.5f; scale = 0.92f; scoreValue = 100;
                    tint = new Color(0.95f, 0.4f, 0.32f); glowColor = new Color(1f, 0.18f, 0.06f);
                    break;
                case EnemyType.Caster:
                    displayName = "Hexer"; maxHealth = 32f; speed = 3.4f; damage = 12f; attackRange = 9f; windupTime = 0.75f; scale = 0.95f; scoreValue = 150;
                    tint = new Color(0.6f, 0.45f, 1f); glowColor = new Color(0.75f, 0.25f, 1f);
                    break;
                case EnemyType.Brute:
                    displayName = "Juggernaut"; maxHealth = 170f; speed = 2.7f; damage = 24f; attackRange = 2.7f; windupTime = 0.95f; scale = 1.45f; scoreValue = 400;
                    superArmor = true; slamRadius = 3.2f;
                    tint = new Color(0.42f, 0.42f, 0.46f); glowColor = new Color(1f, 0.45f, 0.06f);
                    break;
                default:
                    displayName = "Iron Tyrant"; maxHealth = 750f; speed = 3.0f; damage = 30f; attackRange = 3.8f; windupTime = 0.85f; scale = 2.15f; scoreValue = 5000;
                    superArmor = true; slamRadius = 4.6f;
                    tint = new Color(0.3f, 0.27f, 0.27f); glowColor = new Color(1f, 0.32f, 0.03f);
                    break;
            }
            maxHealth *= 1f + Mathf.Max(0, gm.Wave - 1) * 0.08f;
            health = maxHealth;
            transform.localScale = Vector3.one * scale;
            facing = transform.forward;

            // body
            var m = Instantiate(gm.robotModel, transform);
            m.name = "Model";
            model = m.transform;
            model.localPosition = new Vector3(0f, -2.1f, 0f);
            model.localRotation = Quaternion.identity;
            animator = m.GetComponent<Animator>();
            animator.runtimeAnimatorController = gm.characterController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            body = m.GetComponentInChildren<SkinnedMeshRenderer>();
            bodyMat = new Material(gm.robotMaterial);
            bodyMat.SetColor(BaseColorId, tint);
            bodyMat.EnableKeyword("_EMISSION");
            bodyMat.SetColor(EmissionId, Color.black);
            body.sharedMaterial = bodyMat;

            cc = gameObject.AddComponent<CharacterController>();
            cc.center = new Vector3(0f, 0.97f, 0f);
            cc.height = 1.9f;
            cc.radius = 0.36f;
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.04f;
            cc.minMoveDistance = 0f;

            // weapon
            rig = gameObject.AddComponent<WeaponRig>();
            var metal = FX.Lit(new Color(0.16f, 0.15f, 0.17f), 0.85f, 0.6f);
            var hot = FX.Emissive(glowColor, 3.2f);
            switch (t)
            {
                case EnemyType.Grunt: rig.weapon = Weapons.Dagger(transform, metal, hot); rig.bladeLength = 0.68f; break;
                case EnemyType.Caster: rig.weapon = Weapons.Staff(transform, metal, hot, out staffOrb, out staffLight, glowColor); rig.bladeLength = 1.24f; break;
                default: rig.weapon = Weapons.Hammer(transform, metal, hot); rig.bladeLength = 1.25f; break;
            }
            if (t != EnemyType.Caster) rig.InitTrail(glowColor * 2.4f, displayName + "_Trail", t == EnemyType.Grunt ? 0.12f : 0.18f);
            rig.Snap(RestGrip(), RestDir());
            var ik = m.AddComponent<IKRelay>();
            ik.rig = rig;

            if (t != EnemyType.Boss) bar = EnemyHealthBar.Create(this, gm);

            state = State.Spawning;
            stateTime = 0f;
            stateDur = t == EnemyType.Boss ? 1.8f : 0.85f;
            nextCast = Time.time + Random.Range(1.4f, 2.6f);
            strafeSign = Random.value < 0.5f ? -1f : 1f;
            FX.SpawnPortal(pos: transform.position, color: glowColor, size: scale);
            Sfx.Play(Sfx.I.spawn, t == EnemyType.Boss ? 1f : 0.45f, t == EnemyType.Boss ? 0.6f : 1f);
            if (t == EnemyType.Boss) gm.cameraRig.Shake(0.8f);
        }

        void OnDestroy()
        {
            if (bodyMat) Destroy(bodyMat);
            if (bar) Destroy(bar.gameObject);
            if (telegraph) Destroy(telegraph.gameObject);
            if (gm)
            {
                gm.enemies.Remove(this);
                gm.ReleaseToken(this);
            }
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            if (gm == null || state == State.Dead) return;
            float dt = Time.deltaTime;
            stateTime += dt;
            glow = Color.black;

            var player = gm.player;
            Vector3 toP = player.transform.position - transform.position; toP.y = 0f;
            float dist = toP.magnitude;
            Vector3 dirP = dist > 0.001f ? toP / dist : facing;
            Vector3 move = Vector3.zero;
            bool face = false;
            float turnRate = 540f;

            switch (state)
            {
                case State.Spawning:
                {
                    float u = Mathf.Clamp01(stateTime / stateDur);
                    model.localPosition = new Vector3(0f, Mathf.Lerp(-2.1f, 0f, 1f - Mathf.Pow(1f - u, 3f)), 0f);
                    glow = glowColor * (1.5f * (1f - u));
                    face = true;
                    if (u >= 1f)
                    {
                        model.localPosition = Vector3.zero;
                        if (type == EnemyType.Boss)
                        {
                            gm.cameraRig.Shake(0.6f);
                            Sfx.Play(Sfx.I.slam, 1f, 0.6f, 0f);
                            FX.Shockwave(transform.position, 6f, glowColor * 2f, 0.6f);
                        }
                        Enter(State.Chase);
                    }
                    break;
                }
                case State.Chase:
                    move = UpdateChase(dist, dirP, dt, ref face);
                    break;
                case State.Windup:
                    UpdateWindup(dist, dirP, ref face, ref turnRate);
                    break;
                case State.Attack:
                    move = UpdateAttack(dist, dirP);
                    break;
                case State.Recover:
                    rig.SetTarget(RestGrip(), RestDir(), 7f);
                    rig.leftHandWeight = Mathf.MoveTowards(rig.leftHandWeight, 0f, dt * 3f);
                    if (stateTime >= stateDur) Enter(State.Chase);
                    break;
                case State.Stagger:
                {
                    float s = Mathf.Clamp01(stateTime / stateDur);
                    model.localRotation = Quaternion.Euler(-20f * Mathf.Sin(s * Mathf.PI), 0f, 0f);
                    rig.SetTarget(RestGrip() + new Vector3(0f, 0.12f, -0.2f), RestDir(), 10f);
                    if (s >= 1f) Enter(State.Chase);
                    break;
                }
            }

            // movement
            Vector3 vel = move + (state == State.Spawning ? Vector3.zero : Separation()) + knock;
            knock = Vector3.MoveTowards(knock, Vector3.zero, 16f * dt);
            vy = cc.isGrounded ? -2f : vy - 25f * dt;
            cc.Move((vel + Vector3.up * vy) * dt);
            Vector3 c = gm.ClampToArena(transform.position, 0.3f);
            if ((c - transform.position).sqrMagnitude > 1e-6f)
            {
                cc.enabled = false;
                transform.position = c;
                cc.enabled = true;
            }

            Vector3 want = face ? dirP : (move.sqrMagnitude > 0.05f ? move.normalized : facing);
            facing = Vector3.RotateTowards(facing, want, turnRate * Mathf.Deg2Rad * dt, 0f);
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f) facing = transform.forward;
            facing.Normalize();
            transform.rotation = Quaternion.LookRotation(facing);

            float planar = new Vector3(move.x, 0f, move.z).magnitude / scale;
            animator.SetFloat(SpeedId, planar, 0.08f, Mathf.Max(dt, 0.0001f));
            animator.SetFloat(MotionSpeedId, 1f);
            animator.SetBool(GroundedId, true);

            rig.Tick(dt);
            flash = Mathf.MoveTowards(flash, 0f, dt * 7f);
            bodyMat.SetColor(EmissionId, glow + Color.white * (flash * 2.4f));
        }

        Vector3 UpdateChase(float dist, Vector3 dirP, float dt, ref bool face)
        {
            rig.SetTarget(RestGrip(), RestDir(), 8f);
            rig.leftHandWeight = Mathf.MoveTowards(rig.leftHandWeight, 0f, dt * 3f);
            if (gm.player.IsDead) return Vector3.zero;

            strafeTimer -= dt;
            if (strafeTimer <= 0f)
            {
                strafeTimer = Random.Range(1.4f, 3f);
                strafeSign = -strafeSign;
            }
            Vector3 perp = new Vector3(-dirP.z, 0f, dirP.x) * strafeSign;

            if (type == EnemyType.Caster)
            {
                face = true;
                if (Time.time >= nextCast && dist < 15f)
                {
                    Enter(State.Windup);
                    return Vector3.zero;
                }
                if (dist < 6f) return (-dirP + perp * 0.3f).normalized * speed;
                if (dist > 10.5f) return dirP * speed;
                return perp * (speed * 0.55f);
            }

            float reach = attackRange + 0.35f;
            if (dist > reach * 0.9f) return dirP * (type == EnemyType.Boss && dist > 8f ? speed * 1.5f : speed);
            if (type == EnemyType.Boss || gm.RequestToken(this))
            {
                Enter(State.Windup);
                return Vector3.zero;
            }
            face = true;
            return perp * (speed * 0.35f) + (dist < reach * 0.6f ? -dirP * (speed * 0.4f) : Vector3.zero);
        }

        void UpdateWindup(float dist, Vector3 dirP, ref bool face, ref float turnRate)
        {
            float u = Mathf.Clamp01(stateTime / stateDur);
            glow = glowColor * (0.3f + 2.4f * u * u);
            switch (type)
            {
                case EnemyType.Grunt:
                    face = u < 0.75f;
                    rig.SetTarget(WeaponRig.HGrip(108f, 1.38f, 0.42f), WeaponRig.HDir(108f, 0.35f), 16f);
                    break;
                case EnemyType.Caster:
                    face = true;
                    rig.SetTarget(new Vector3(0.22f, 1.62f, 0.38f), new Vector3(0f, 1f, 0.3f), 10f);
                    if (staffOrb) staffOrb.localScale = Vector3.one * Mathf.Lerp(0.26f, 0.6f, u);
                    if (staffLight) staffLight.intensity = Mathf.Lerp(1.2f, 7f, u);
                    break;
                default:
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
                    if (!hitDone && u >= 0.45f)
                    {
                        hitDone = true;
                        if (dist <= attackRange + 0.5f && Vector3.Angle(facing, dirP) < 80f) player.TakeDamage(damage, transform.position, 7f);
                    }
                    if (u < 0.6f && dist > 1.1f) move = lungeDir * (8.5f * (1f - u / 0.6f));
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
                {
                    float phi = Mathf.Lerp(-48f, 140f, u * u);
                    rig.Snap(WeaponRig.VGrip(phi), WeaponRig.VDir(phi));
                    rig.SetTrail(true);
                    if (!hitDone && u >= 1f) { hitDone = true; Slam(); }
                    break;
                }
            }

            if (stateTime >= stateDur)
            {
                if (type == EnemyType.Boss && slamCount < 3 && !player.IsDead) Enter(State.Windup);
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
            FX.Burst(slamPoint, new Color(0.3f, 0.25f, 0.25f, 0.7f), new Color(0.15f, 0.12f, 0.12f, 0.5f), 16, new Vector2(1f, 3f), new Vector2(0.7f, 1.4f), new Vector2(0.6f, 1.1f), -0.05f, false, slamRadius * 0.5f);
            FX.LightFlash(slamPoint + Vector3.up, glowColor, 8f, slamRadius * 3f, 0.3f);
            var player = gm.player;
            Vector3 d = player.transform.position - slamPoint; d.y = 0f;
            if (d.magnitude <= slamRadius + 0.3f) player.TakeDamage(damage, slamPoint, 13f);
            Vector3 toMe = player.transform.position - transform.position; toMe.y = 0f;
            gm.cameraRig.Shake(Mathf.Lerp(type == EnemyType.Boss ? 0.9f : 0.6f, 0.15f, Mathf.Clamp01(toMe.magnitude / 16f)));
            Sfx.Play(Sfx.I.slam, 0.85f, type == EnemyType.Boss ? 0.7f : 0.95f);
        }

        void Enter(State s)
        {
            state = s;
            stateTime = 0f;
            switch (s)
            {
                case State.Chase:
                    model.localRotation = Quaternion.identity;
                    rig.SetTrail(false);
                    break;
                case State.Windup:
                    hitDone = false;
                    stateDur = (type == EnemyType.Boss && slamCount > 0 ? 0.5f : windupTime) * Random.Range(0.92f, 1.08f);
                    if (type == EnemyType.Brute || type == EnemyType.Boss)
                    {
                        slamPoint = ComputeSlamPoint();
                        if (telegraph) telegraph.Kill();
                        telegraph = FX.Telegraph(slamPoint, slamRadius, new Color(1.6f, 0.2f, 0.08f));
                        Sfx.Play(Sfx.I.warn, 0.4f, 0.7f, 0f);
                    }
                    else if (type == EnemyType.Grunt) Sfx.Play(Sfx.I.warn, 0.22f, 1.5f);
                    else Sfx.Play(Sfx.I.charge, 0.35f, 1.6f);
                    break;
                case State.Attack:
                    hitDone = false;
                    lungeDir = facing;
                    stateDur = type == EnemyType.Grunt ? 0.3f : type == EnemyType.Caster ? 0.22f : 0.17f;
                    if (type == EnemyType.Grunt) { rig.SetTrail(true); Sfx.Play(Sfx.I.swing, 0.45f, 1.3f); }
                    else if (type != EnemyType.Caster) Sfx.Play(Sfx.I.swingHeavy, 0.6f, 0.75f);
                    break;
                case State.Recover:
                    rig.SetTrail(false);
                    slamCount = 0;
                    stateDur = type == EnemyType.Grunt ? 0.6f : type == EnemyType.Caster ? 0.45f : type == EnemyType.Brute ? 1.0f : 1.1f;
                    if (type == EnemyType.Caster) nextCast = Time.time + Random.Range(2.4f, 3.6f);
                    gm.ReleaseToken(this);
                    break;
                case State.Stagger:
                    rig.SetTrail(false);
                    stateDur = 0.32f;
                    slamCount = 0;
                    if (telegraph) { telegraph.Kill(); telegraph = null; }
                    if (staffOrb) staffOrb.localScale = Vector3.one * 0.26f;
                    if (staffLight) staffLight.intensity = 1.2f;
                    if (type == EnemyType.Caster) nextCast = Time.time + Random.Range(1.5f, 2.5f);
                    gm.ReleaseToken(this);
                    break;
            }
        }

        Vector3 Separation()
        {
            Vector3 s = Vector3.zero;
            var list = gm.enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o == null || o == this) continue;
                Vector3 d = transform.position - o.transform.position; d.y = 0f;
                float r = Radius + o.Radius + 0.35f;
                float m = d.magnitude;
                if (m < r && m > 0.001f) s += d / m * ((r - m) * 6f);
            }
            return Vector3.ClampMagnitude(s, 4f);
        }

        Vector3 RestGrip()
        {
            switch (type)
            {
                case EnemyType.Grunt: return new Vector3(0.3f, 1.0f, 0.28f);
                case EnemyType.Caster: return new Vector3(0.32f, 1.0f, 0.18f);
                default: return new Vector3(0.34f, 1.05f, 0.3f);
            }
        }

        Vector3 RestDir()
        {
            switch (type)
            {
                case EnemyType.Grunt: return new Vector3(0.1f, 0.25f, 1f);
                case EnemyType.Caster: return new Vector3(0.05f, 1f, 0.25f);
                default: return new Vector3(0.12f, 0.85f, 0.45f);
            }
        }

        // ------------------------------------------------------------------ combat

        public bool IsThreatening(Vector3 p)
        {
            if (state != State.Windup) return false;
            if (stateTime / stateDur < 0.5f) return false;
            Vector3 d;
            switch (type)
            {
                case EnemyType.Grunt:
                    d = p - transform.position; d.y = 0f;
                    return d.magnitude < attackRange + 1.4f;
                case EnemyType.Brute:
                case EnemyType.Boss:
                    d = p - slamPoint; d.y = 0f;
                    return d.magnitude < slamRadius + 0.7f;
                default:
                    return false;
            }
        }

        public void TakeHit(float dmg, Vector3 dir, float knockback, bool heavy, bool crit)
        {
            if (!Targetable) return;
            health -= dmg;
            flash = 1f;
            float kbScale = superArmor ? (type == EnemyType.Boss ? 0.08f : 0.25f) : 1f;
            knock = dir * (knockback * kbScale);
            bool stagger = !superArmor || (heavy && type == EnemyType.Brute);
            if (stagger && health > 0f)
            {
                Enter(State.Stagger);
                if (heavy) stateDur = 0.55f;
            }

            Vector3 hitPos = Chest - dir * (Radius * 0.8f);
            FX.Sparks(hitPos, -dir, crit, heavy);
            FX.DamageNumber(Head + Random.insideUnitSphere * 0.35f, dmg, crit);
            Sfx.Hit(crit, heavy, superArmor);
            if (bar) bar.Show();
            gm.RegisterHit(dmg);
            if (health <= 0f) Die(dir);
        }

        void Die(Vector3 dir)
        {
            state = State.Dead;
            if (telegraph) { telegraph.Kill(); telegraph = null; }
            gm.RegisterKill(this);

            FX.Explosion(Chest, glowColor, scale);
            FX.Debris(Chest, FX.Lit(tint * 0.7f, 0.7f, 0.5f), FX.Emissive(glowColor, 2.5f), Mathf.RoundToInt(10 * scale), scale, dir);
            FX.LightFlash(Chest, glowColor, 6f * scale, 7f * scale, 0.35f);
            Sfx.Play(Sfx.I.death, 0.85f, type == EnemyType.Boss ? 0.55f : type == EnemyType.Brute ? 0.8f : 1.05f);
            gm.cameraRig.Shake(type == EnemyType.Boss ? 1f : type == EnemyType.Brute ? 0.5f : 0.25f);

            int drops = type == EnemyType.Boss ? 6 : type == EnemyType.Brute ? 1 : (Random.value < 0.2f ? 1 : 0);
            for (int i = 0; i < drops; i++) HealthOrb.Spawn(Chest, gm);
            gm.player.AddRage(type == EnemyType.Boss ? 100f : type == EnemyType.Brute ? 18f : 8f);

            Destroy(gameObject);
        }
    }

    /// <summary>Billboarded world-space health bar with a delayed "chip" segment.</summary>
    public class EnemyHealthBar : MonoBehaviour
    {
        const float W = 1.1f, H = 0.11f;
        Enemy owner;
        Transform fill, chip;
        Renderer[] renderers;
        float chipValue = 1f, chipDelay;
        bool visible;

        public static EnemyHealthBar Create(Enemy e, GameManager gm)
        {
            var go = FX.NewObject("HealthBar_" + e.displayName);
            var bar = go.AddComponent<EnemyHealthBar>();
            bar.owner = e;
            var mpb = new MaterialPropertyBlock();
            bar.MakeQuad("BG", new Color(0f, 0f, 0f, 0.65f), W + 0.06f, H + 0.06f, 0f, 1, mpb);
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
            if (!visible) SetVisible(true);
        }

        void SetVisible(bool v)
        {
            visible = v;
            foreach (var r in renderers) if (r) r.enabled = v;
        }

        void LateUpdate()
        {
            if (owner == null) { Destroy(gameObject); return; }
            var cam = Camera.main;
            transform.position = owner.Head + Vector3.up * 0.35f;
            if (cam) transform.rotation = cam.transform.rotation;
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
