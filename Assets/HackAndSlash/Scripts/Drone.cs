using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    /// <summary>
    /// Hornet: a quad-rotor gunship that orbits above the fight, paints the Knight with a laser and fires a two-round
    /// burst. Counter the shot just before the trigger and the round goes back up the barrel; tether it out of the sky
    /// and rip its rotors off, or throw it into a crowd, where it goes off like a grenade. An arc stun drops it to
    /// head height, sparking, where a sword can reach it.
    ///
    /// The same class is the Tyrant's ejected personality core (Portal): a hovering eye with a label, immune to
    /// anything but being tethered in and torn apart.
    /// </summary>
    public class Drone : Enemy
    {
        public bool core;
        public string coreLabel;

        readonly Transform[] rotors = new Transform[4];
        readonly Renderer[] navLights = new Renderer[2];
        Transform hull, muzzle;
        Material eyeMat;
        Renderer eyeRenderer;
        Light eyeLight;
        LineRenderer laser;
        TextMeshPro label;
        Vector3 vel, fallVel, fallSpin, pullFrom, throwVel;
        float orbitAngle, orbitDir = 1f, hoverHeight, orbitRadius, nextFire, wreckAge, pullTime, bob, impactAt = -1f;
        int shotsLeft;
        float nextShotAt;
        bool reservedShot;
        PlayerController holder;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public override bool Targetable => state != State.Dead && state != State.Spawning && state != State.Thrown;
        public override Vector3 Chest => transform.position;
        public override Vector3 Head => transform.position + Vector3.up * (core ? 0.75f : 0.35f);
        public override float Radius => core ? 0.6f : 0.5f;
        public bool IsCore => core;

        /// <summary>Only the instant before the trigger can be countered (the round goes back).</summary>
        public override bool CanBeCountered => !core && state == State.Aim && stateTime >= stateDur - 0.38f - Settings.CounterLeniency;

        public override TetherAction Tether => !Targetable || state == State.Pulled || state == State.Grabbed ? TetherAction.None : TetherAction.Pull;

        // ------------------------------------------------------------------ construction

        protected override void Setup(EnemyType t, GameManager g, Vector3 landing)
        {
            gm = g;
            type = EnemyType.Drone;
            displayName = core ? coreLabel : "Hornet";
            maxHealth = core ? 80f : 30f;
            if (!core) maxHealth *= (1f + Mathf.Max(0, gm.Wave - 1) * (gm.Mode == GameMode.Story ? 0.03f : 0.06f)) * Settings.EnemyHealth;
            health = maxHealth;
            speed = 7.5f;
            damage = 6f * Settings.EnemyDamage;
            scale = 1f;
            scoreValue = core ? 1000 : 200;
            glowColor = core ? new Color(1f, 0.32f, 0.12f) : new Color(1f, 0.55f, 0.12f);
            facing = transform.forward;

            if (core) BuildCore(); else BuildHornet();
            if (!core) bar = EnemyHealthBar.Create(this, gm);
            indicator = CounterIndicator.Create(transform, core ? 1.2f : 0.75f, 0.45f);

            Vector3 flat = new Vector3(landing.x, 0f, landing.z);
            orbitAngle = Mathf.Atan2(flat.x, flat.z);
            orbitDir = Random.value < 0.5f ? -1f : 1f;
            hoverHeight = core ? 3.2f : Random.Range(3.4f, 4.8f);
            orbitRadius = core ? 3.5f : Random.Range(6.5f, 9.5f);
            nextFire = Time.time + Random.Range(2.2f, 3.6f);
            bob = Random.value * 10f;

            spawnTo = new Vector3(landing.x, hoverHeight, landing.z);
            Vector3 outward = flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward;
            spawnFrom = core ? landing : outward * (gm.arenaRadius + 16f) + Vector3.up * 14f;
            transform.position = spawnFrom;
            state = State.Spawning;
            stateTime = 0f;
            stateDur = core ? 0.7f : Random.Range(1.4f, 1.8f);
            Sfx.Play(Sfx.I.servo ? Sfx.I.servo : Sfx.I.dash, 0.25f, 1.6f);
        }

        void BuildHornet()
        {
            var metal = FX.Lit(new Color(0.09f, 0.09f, 0.1f), 0.85f, 0.7f);
            var paint = FX.Lit(new Color(0.75f, 0.42f, 0.08f), 0.3f, 0.75f);
            var dark = FX.Lit(new Color(0.02f, 0.02f, 0.025f), 0.6f, 0.5f);
            eyeMat = new Material(FX.Emissive(glowColor, 4f));
            hull = new GameObject("Hull").transform;
            hull.SetParent(transform, false);
            Weapons.Part(PrimitiveType.Sphere, hull, Vector3.zero, new Vector3(0.46f, 0.24f, 0.66f), paint);
            Weapons.Part(PrimitiveType.Sphere, hull, new Vector3(0f, -0.06f, -0.12f), new Vector3(0.36f, 0.2f, 0.5f), metal);
            Weapons.Part(PrimitiveType.Cube, hull, new Vector3(0f, 0.1f, -0.05f), new Vector3(0.08f, 0.06f, 0.42f), dark);
            eyeRenderer = Weapons.Part(PrimitiveType.Sphere, hull, new Vector3(0f, 0.01f, 0.3f), new Vector3(0.18f, 0.12f, 0.12f), eyeMat, default, false).GetComponent<Renderer>();
            for (int i = 0; i < 4; i++)
            {
                float a = 45f + i * 90f;
                var r = Quaternion.Euler(0f, a, 0f);
                Weapons.Part(PrimitiveType.Cube, hull, r * new Vector3(0f, 0.04f, 0.32f), new Vector3(0.06f, 0.05f, 0.5f), metal, new Vector3(0f, a, 0f));
                Vector3 hub = r * new Vector3(0f, 0.07f, 0.58f);
                Weapons.Part(PrimitiveType.Cylinder, hull, hub, new Vector3(0.09f, 0.05f, 0.09f), dark);
                var rotor = new GameObject("Rotor").transform;
                rotor.SetParent(hull, false);
                rotor.localPosition = hub + Vector3.up * 0.06f;
                Weapons.Part(PrimitiveType.Cube, rotor, Vector3.zero, new Vector3(0.5f, 0.008f, 0.05f), dark);
                Weapons.Part(PrimitiveType.Cube, rotor, Vector3.zero, new Vector3(0.05f, 0.008f, 0.5f), dark);
                var blur = Weapons.Part(PrimitiveType.Cylinder, rotor, Vector3.zero, new Vector3(0.5f, 0.002f, 0.5f), FX.FlatMat, default, false);
                FX.SetColor(blur.GetComponent<Renderer>(), new Color(0.35f, 0.35f, 0.4f, 0.18f));
                rotors[i] = rotor;
            }
            // nav lights: port red, starboard green (they blink, because aircraft never stopped doing that)
            navLights[0] = Weapons.Part(PrimitiveType.Sphere, hull, new Vector3(-0.25f, 0.02f, 0f), Vector3.one * 0.05f, FX.Emissive(new Color(1f, 0.05f, 0.03f), 5f), default, false).GetComponent<Renderer>();
            navLights[1] = Weapons.Part(PrimitiveType.Sphere, hull, new Vector3(0.25f, 0.02f, 0f), Vector3.one * 0.05f, FX.Emissive(new Color(0.05f, 1f, 0.2f), 5f), default, false).GetComponent<Renderer>();
            // chin gun
            Weapons.Part(PrimitiveType.Cylinder, hull, new Vector3(0f, -0.15f, 0.22f), new Vector3(0.05f, 0.14f, 0.05f), metal, new Vector3(90f, 0f, 0f));
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(hull, false);
            muzzle.localPosition = new Vector3(0f, -0.15f, 0.38f);
            laser = Weapons.Laser(muzzle);

            eyeLight = new GameObject("EyeLight").AddComponent<Light>();
            eyeLight.transform.SetParent(hull, false);
            eyeLight.transform.localPosition = new Vector3(0f, 0f, 0.45f);
            eyeLight.type = LightType.Point;
            eyeLight.color = glowColor;
            eyeLight.range = 3f;
            eyeLight.intensity = 2f;
            eyeLight.shadows = LightShadows.None;
        }

        void BuildCore()
        {
            var shell = FX.Lit(new Color(0.78f, 0.78f, 0.76f), 0.2f, 0.8f);
            var dark = FX.Lit(new Color(0.06f, 0.06f, 0.07f), 0.7f, 0.6f);
            eyeMat = new Material(FX.Emissive(glowColor, 5f));
            hull = new GameObject("Core").transform;
            hull.SetParent(transform, false);
            Weapons.Part(PrimitiveType.Sphere, hull, Vector3.zero, Vector3.one * 0.95f, shell);
            Weapons.Part(PrimitiveType.Cylinder, hull, Vector3.zero, new Vector3(1.02f, 0.06f, 1.02f), dark);
            Weapons.Part(PrimitiveType.Cube, hull, new Vector3(0f, 0.55f, 0f), new Vector3(0.6f, 0.08f, 0.12f), dark);
            Weapons.Part(PrimitiveType.Cube, hull, new Vector3(0f, -0.55f, 0f), new Vector3(0.6f, 0.08f, 0.12f), dark);
            Weapons.Part(PrimitiveType.Cylinder, hull, new Vector3(0f, 0f, 0.4f), new Vector3(0.46f, 0.04f, 0.46f), dark, new Vector3(90f, 0f, 0f));
            eyeRenderer = Weapons.Part(PrimitiveType.Sphere, hull, new Vector3(0f, 0f, 0.42f), new Vector3(0.3f, 0.3f, 0.12f), eyeMat, default, false).GetComponent<Renderer>();
            eyeLight = new GameObject("EyeLight").AddComponent<Light>();
            eyeLight.transform.SetParent(hull, false);
            eyeLight.transform.localPosition = new Vector3(0f, 0f, 0.7f);
            eyeLight.type = LightType.Point;
            eyeLight.color = glowColor;
            eyeLight.range = 5f;
            eyeLight.intensity = 4f;
            eyeLight.shadows = LightShadows.None;
            label = Ambient.NeonText(transform, transform.position + Vector3.up * 1.1f, 0f, 0.3f, coreLabel ?? "CORE", new Color(1f, 0.45f, 0.25f));
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (eyeMat) Destroy(eyeMat);
            if (reservedShot && gm) gm.ReleaseShot(this);
        }

        // ------------------------------------------------------------------ update

        protected override void Update()
        {
            if (gm == null) return;
            float dt = Time.deltaTime;
            stateTime += dt;
            float spin = state == State.Dead ? 200f : state == State.Stunned ? 600f : 2400f;
            foreach (var r in rotors) if (r) r.Rotate(0f, spin * dt, 0f, Space.Self);
            if (navLights[0])
            {
                bool on = Mathf.Repeat(Time.time + bob, 1.2f) < 0.12f && state != State.Dead;
                navLights[0].enabled = on;
                navLights[1].enabled = on;
            }
            if (state == State.Dead) { UpdateWreck(dt); return; }

            var player = gm.player;
            Vector3 target = transform.position;
            bool hold = false;
            float glow = 1f;
            switch (state)
            {
                case State.Spawning:
                {
                    float u = Mathf.Clamp01(stateTime / stateDur);
                    float k = 1f - (1f - u) * (1f - u);
                    Vector3 p = Vector3.Lerp(spawnFrom, spawnTo, k);
                    if (core) p.y += 2.5f * Mathf.Sin(u * Mathf.PI);
                    transform.position = p;
                    vel = Vector3.zero;
                    if (u >= 1f) { state = State.Circle; stateTime = 0f; }
                    hold = true;
                    break;
                }
                case State.Circle:
                    target = OrbitPoint(dt);
                    if (!core && gm.CombatOn && !player.IsDead && Time.time >= nextFire && gm.TryReserveShot(this))
                    {
                        reservedShot = true;
                        state = State.Aim;
                        stateTime = 0f;
                        stateDur = Random.Range(1.0f, 1.25f) * Settings.WindupScale;
                        Sfx.Play(Sfx.I.charge, 0.22f, 2.2f);
                    }
                    break;
                case State.Aim:
                {
                    target = OrbitPoint(dt * 0.25f);
                    float u = Mathf.Clamp01(stateTime / stateDur);
                    bool late = stateTime >= stateDur - 0.38f;
                    glow = 1.4f + 2f * u;
                    if (laser && muzzle)
                    {
                        Vector3 aimAt = player.Chest + Random.insideUnitSphere * (0.3f * (1f - u));
                        laser.enabled = true;
                        laser.SetPosition(0, muzzle.position);
                        laser.SetPosition(1, muzzle.position + (aimAt - muzzle.position).normalized * Mathf.Min(40f, (aimAt - muzzle.position).magnitude + 0.5f));
                        laser.widthMultiplier = late ? 0.028f : 0.01f;
                        FX.SetColor(laser, late ? new Color(4f, 0.6f, 0.15f, 1f) : new Color(2.5f, 0.35f, 0.08f, 1f));
                    }
                    if (u >= 1f)
                    {
                        if (laser) laser.enabled = false;
                        state = State.Attack;
                        stateTime = 0f;
                        stateDur = 0.5f;
                        shotsLeft = 2;
                        nextShotAt = 0f;
                        LastAttackTime = Time.time;
                    }
                    break;
                }
                case State.Attack:
                    target = transform.position;
                    if (shotsLeft > 0 && stateTime >= nextShotAt && !player.IsDead)
                    {
                        Fire(player);
                        shotsLeft--;
                        nextShotAt = stateTime + 0.13f;
                    }
                    if (stateTime >= stateDur) EndAttack();
                    break;
                case State.Countered:
                {
                    // knocked back and spinning, its own round coming home
                    hull.localRotation = Quaternion.Euler(0f, stateTime * 900f, 25f * Mathf.Sin(stateTime * 14f));
                    target = transform.position + knock * dt;
                    knock = Vector3.MoveTowards(knock, Vector3.zero, 10f * dt);
                    glow = Random.value < 0.4f ? 0.2f : 1.6f;
                    if (stateTime >= stateDur) { hull.localRotation = Quaternion.identity; Recover(); }
                    break;
                }
                case State.Stunned:
                {
                    // drops out of the sky to head height, sputtering
                    Vector3 p = transform.position;
                    target = new Vector3(p.x, 1.5f + 0.15f * Mathf.Sin(stateTime * 9f), p.z);
                    hull.localRotation = Quaternion.Euler(18f * Mathf.Sin(stateTime * 7f), stateTime * 120f, 22f * Mathf.Sin(stateTime * 5f));
                    glow = Random.value < 0.35f ? 0.1f : 1.3f;
                    if (Random.value < 0.25f) FX.Arc(transform.position + Random.insideUnitSphere * 0.4f, transform.position + Random.insideUnitSphere * 0.5f, new Color(0.6f, 1.4f, 3.5f, 1f), 0.1f, 0.02f);
                    if (stateTime >= stateDur) { hull.localRotation = Quaternion.identity; Recover(); }
                    break;
                }
                case State.Pulled:
                {
                    if (holder == null || holder.IsDead) { Drop(); break; }
                    pullTime += dt;
                    float u = Mathf.Clamp01(pullTime / 0.28f);
                    transform.position = Vector3.Lerp(pullFrom, holder.HoldPoint, u * u);
                    hull.localRotation = Quaternion.Euler(0f, u * 360f, 0f);
                    hold = true;
                    if (u >= 1f) { state = State.Grabbed; stateTime = 0f; }
                    break;
                }
                case State.Grabbed:
                    if (holder == null || holder.IsDead || !holder.Holding) { Drop(); break; }
                    transform.position = Vector3.Lerp(transform.position, holder.HoldPoint, 1f - Mathf.Exp(-30f * dt));
                    hull.localRotation = Quaternion.Euler(12f * Mathf.Sin(stateTime * 31f), 0f, 15f * Mathf.Sin(stateTime * 23f));
                    glow = 0.6f + Random.value;
                    hold = true;
                    break;
                case State.Thrown:
                    UpdateThrown(dt);
                    return;
            }

            if (!hold)
            {
                // critically damped flight towards the target, with a gentle hover bob
                bob += dt;
                target.y += 0.12f * Mathf.Sin(bob * 2.3f);
                Vector3 want = (target - transform.position) * 3.2f;
                want = Vector3.ClampMagnitude(want, speed);
                vel = Vector3.Lerp(vel, want + DroneSeparation(), 1f - Mathf.Exp(-4f * dt));
                Vector3 p = transform.position + vel * dt;
                Vector3 c = gm.ClampToArena(p, 0.4f);
                p.x = c.x; p.z = c.z;
                p.y = Mathf.Max(core ? 1.2f : 0.9f, p.y);
                transform.position = p;
            }

            // face the Knight (pitched into the direction of travel, like a real quad)
            Vector3 toP = player.Chest - transform.position;
            Vector3 flat = new Vector3(toP.x, 0f, toP.z);
            if (flat.sqrMagnitude > 0.01f) facing = Vector3.RotateTowards(facing, flat.normalized, 6f * dt, 0f);
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f) facing = Vector3.forward;
            Vector3 localVel = Quaternion.Inverse(Quaternion.LookRotation(facing)) * vel;
            transform.rotation = Quaternion.LookRotation(facing) * Quaternion.Euler(Mathf.Clamp(localVel.z * 4f, -25f, 25f), 0f, Mathf.Clamp(-localVel.x * 4f, -25f, 25f));
            if (core && hull) hull.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.3f) * 8f, 0f, Mathf.Sin(Time.time * 0.9f) * 6f);
            if (label)
            {
                var cam = Camera.main;
                label.transform.position = transform.position + Vector3.up * 1.15f;
                if (cam) label.transform.rotation = cam.transform.rotation;
            }

            flash = Mathf.MoveTowards(flash, 0f, dt * 7f);
            if (eyeMat) eyeMat.SetColor(EmissionId, glowColor * ((core ? 5f : 4f) * glow) + Color.white * (flash * 3f));
            if (eyeLight) eyeLight.intensity = (core ? 4f : 2f) * glow;
            if (indicator) indicator.Show(Settings.CounterIndicators && CanBeCountered, false);
        }

        Vector3 OrbitPoint(float dt)
        {
            Vector3 centre = gm.player.transform.position;
            if (core && gm.Boss != null && !gm.Boss.IsDead) centre = gm.Boss.transform.position;
            orbitAngle += orbitDir * (core ? 0.5f : 0.32f) * dt;
            return centre + new Vector3(Mathf.Sin(orbitAngle), 0f, Mathf.Cos(orbitAngle)) * orbitRadius + Vector3.up * hoverHeight;
        }

        Vector3 DroneSeparation()
        {
            Vector3 s = Vector3.zero;
            foreach (var o in gm.enemies)
            {
                if (o == null || o == this || o.type != EnemyType.Drone || o.IsDead) continue;
                Vector3 d = transform.position - o.transform.position;
                float m = d.magnitude;
                if (m < 2.2f && m > 0.001f) s += d / m * ((2.2f - m) * 3f);
            }
            return s;
        }

        void Fire(PlayerController player)
        {
            if (!muzzle) return;
            Vector3 from = muzzle.position;
            Vector3 to = player.Chest + player.Velocity * 0.1f + Random.insideUnitSphere * 0.25f;
            Projectile.SpawnBullet(from, to - from, damage, gm, this);
            FX.Burst(from, new Color(1f, 0.85f, 0.5f), new Color(1f, 0.5f, 0.2f), 5, new Vector2(2f, 5f), new Vector2(0.04f, 0.08f), new Vector2(0.05f, 0.1f), 0f, true, 0.02f, to - from, 15f);
            FX.LightFlash(from, new Color(1f, 0.7f, 0.35f), 4f, 5f, 0.05f);
            Sfx.Play(Sfx.I.rifle ? Sfx.I.rifle : Sfx.I.cast, 0.4f, Random.Range(1.25f, 1.4f));
            vel -= (to - from).normalized * 1.2f; // recoil
        }

        void EndAttack()
        {
            if (laser) laser.enabled = false;
            if (reservedShot) { reservedShot = false; gm.ReleaseShot(this); }
            Recover();
        }

        void Recover()
        {
            state = State.Circle;
            stateTime = 0f;
            nextFire = Time.time + Random.Range(3f, 4.6f);
            if (Random.value < 0.5f) orbitDir = -orbitDir;
        }

        void CancelShot()
        {
            if (laser) laser.enabled = false;
            shotsLeft = 0;
            if (reservedShot) { reservedShot = false; gm.ReleaseShot(this); }
        }

        // ------------------------------------------------------------------ combat API

        public override void OnCountered(float hold = 1.1f)
        {
            if (!Targetable || core) return;
            CancelShot();
            state = State.Countered;
            stateTime = 0f;
            stateDur = 0.9f;
            Vector3 away = transform.position - gm.player.transform.position; away.y = 0f;
            knock = (away.sqrMagnitude > 0.01f ? away.normalized : -facing) * 5f;
            Sfx.Play(Sfx.I.clang, 0.45f, 1.5f);
        }

        public override bool Stun(float duration, bool force = false)
        {
            if (!Targetable || core || state == State.Grabbed || state == State.Pulled) return false;
            CancelShot();
            state = State.Stunned;
            stateTime = 0f;
            stateDur = duration + 0.6f;
            Sfx.Play(Sfx.I.stun, 0.45f, 1.3f);
            return true;
        }

        public override void BeginPull(PlayerController by)
        {
            if (!Targetable) return;
            CancelShot();
            holder = by;
            pullFrom = transform.position;
            pullTime = 0f;
            state = State.Pulled;
            stateTime = 0f;
            if (indicator) indicator.Show(false, false);
            Sfx.Play(Sfx.I.servo ? Sfx.I.servo : Sfx.I.dash, 0.4f, 2f);
        }

        public override void Drop()
        {
            if (state != State.Grabbed && state != State.Pulled) return;
            holder = null;
            hull.localRotation = Quaternion.identity;
            Stun(1.2f);
        }

        /// <summary>Tear the rotors off a held Hornet (or the casing off a core). Always lethal.</summary>
        public override bool Rip(Vector3 pullDir)
        {
            if (state != State.Grabbed) return false;
            holder = null;
            FX.Sparks(Chest, pullDir, true, true);
            Sfx.Play(Sfx.I.rip ? Sfx.I.rip : Sfx.I.clang, 0.85f, core ? 0.8f : 1.2f, 0f);
            // the rotor arms come away as real debris
            int n = 0;
            foreach (var r in rotors)
            {
                if (!r || n++ > 1) continue;
                r.SetParent(FX.Root, true);
                var bc = r.gameObject.AddComponent<BoxCollider>();
                bc.size = new Vector3(0.5f, 0.04f, 0.5f);
                Debris.MakePhysical(r.gameObject, pullDir * 6f + Vector3.up * 4f + Random.insideUnitSphere * 2f, Random.insideUnitSphere * 20f, 0.4f, false);
            }
            gm.RegisterHit(health);
            Die(new HitInfo(health, pullDir, 4f, HitKind.Rip, true));
            return true;
        }

        public override void Throw(Vector3 velocity)
        {
            if (state != State.Grabbed && state != State.Pulled) return;
            holder = null;
            throwVel = velocity;
            state = State.Thrown;
            stateTime = 0f;
            Sfx.Play(Sfx.I.swingHeavy, 0.6f, 1.1f);
        }

        void UpdateThrown(float dt)
        {
            throwVel += Vector3.down * (14f * dt);
            Vector3 p = transform.position + throwVel * dt;
            hull.localRotation *= Quaternion.Euler(720f * dt, 300f * dt, 0f);
            foreach (var o in gm.enemies)
            {
                if (o == null || o == this || !o.Targetable) continue;
                float r = Radius + o.Radius + 0.35f;
                if ((o.Chest - p).sqrMagnitude < r * r || (o.transform.position + Vector3.up - p).sqrMagnitude < r * r) { Detonate(p); return; }
            }
            if (p.y <= 0.3f || !gm.Arena.Contains(p, -1.5f) || stateTime > 3f) { Detonate(p); return; }
            transform.position = p;
        }

        /// <summary>A thrown Hornet is a grenade.</summary>
        void Detonate(Vector3 at)
        {
            transform.position = at;
            const float radius = 3.4f;
            FX.Explosion(at, glowColor, 1.1f);
            FX.Shockwave(new Vector3(at.x, 0.05f, at.z), radius, new Color(3f, 1.2f, 0.3f), 0.45f);
            FX.LightFlash(at, new Color(1f, 0.6f, 0.25f), 10f, 9f, 0.25f);
            gm.cameraRig.Shake(0.55f);
            gm.HitStop(0.07f);
            gm.Rumble(0.8f, 0.6f, 0.25f);
            Sfx.Play(Sfx.I.slam, 0.9f, 1.25f, 0f);
            var list = gm.enemies.ToArray();
            foreach (var o in list)
            {
                if (o == null || o == this || !o.Targetable) continue;
                Vector3 d = o.Chest - at;
                if (d.magnitude > radius + o.Radius) continue;
                Vector3 dir = new Vector3(d.x, 0f, d.z);
                dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
                o.TakeHit(new HitInfo(48f * (1f - 0.5f * d.magnitude / radius), dir, 12f, HitKind.Throw, true), at);
            }
            Die(new HitInfo(health + 1f, throwVel.sqrMagnitude > 0.01f ? throwVel.normalized : Vector3.down, 4f, HitKind.Environment, true));
        }

        public override HitResult TakeHit(HitInfo h, Vector3 attackerPos)
        {
            if (state == State.Dead || state == State.Spawning) return HitResult.Immune;
            if (core && h.kind != HitKind.Rip)
            {
                // a core is armoured glass and polite refusal: it must be tethered in and torn apart
                FX.Clash(Chest + Random.insideUnitSphere * 0.3f, -h.dir, new Color(3f, 0.6f, 0.3f));
                Sfx.Play(Sfx.I.clang, 0.45f, 1.4f);
                flash = 0.6f;
                gm.hud.Hint("TETHER THE CORE [R / RB], THEN RIP IT OUT [LMB / X]");
                return HitResult.Blocked;
            }
            float dmg = h.damage;
            health -= dmg;
            flash = 1f;
            FX.Sparks(Chest - h.dir * 0.3f, -h.dir, h.crit, h.kind != HitKind.Strike);
            FX.DamageNumber(Head + Random.insideUnitSphere * 0.3f, dmg, h.crit);
            Sfx.Hit(h.crit, h.kind != HitKind.Strike, false);
            if (bar) bar.Show();
            gm.RegisterHit(dmg);
            if (health <= 0f) { Die(h); return HitResult.Landed; }
            if (state == State.Grabbed || state == State.Pulled) return HitResult.Landed;
            vel += h.dir * (h.knockback * 0.5f) + Vector3.up * 1.5f;
            if (h.kind == HitKind.Heavy || h.kind == HitKind.Counter || h.kind == HitKind.Nova || h.kind == HitKind.Takedown || h.kind == HitKind.Throw || h.kind == HitKind.Projectile)
                Stun(1.4f);
            else if (state == State.Aim && (h.kind == HitKind.Shot || h.kind == HitKind.Strike)) { CancelShot(); Recover(); }
            return HitResult.Landed;
        }

        protected override void Die(HitInfo h)
        {
            if (state == State.Dead) return;
            CancelShot();
            state = State.Dead;
            stateTime = 0f;
            wreckAge = 0f;
            holder = null;
            if (indicator) { Destroy(indicator.gameObject); indicator = null; }
            if (bar) { Destroy(bar.gameObject); bar = null; }
            if (label) label.gameObject.SetActive(false);
            gm.RegisterKill(this);
            Vector3 dir = h.dir.sqrMagnitude > 1e-4f ? h.dir.normalized : -facing;
            fallVel = dir * 3f + Vector3.up * 2f + vel * 0.5f;
            fallSpin = new Vector3(Random.Range(-300f, 300f), Random.Range(400f, 900f), Random.Range(-300f, 300f));
            FX.Sparks(Chest, dir, true, true);
            FX.Explosion(Chest, glowColor, core ? 1.4f : 0.6f);
            FX.LightFlash(Chest, glowColor, 6f, 7f, 0.3f);
            Sfx.Play(Sfx.I.death, 0.75f, core ? 0.7f : 1.3f);
            gm.cameraRig.Shake(core ? 0.6f : 0.25f);
            if (eyeLight) eyeLight.enabled = false;
            if (!core)
            {
                gm.player.AddVoid(8f);
                if (Random.value < Settings.OrbChance * 0.6f) HealthOrb.Spawn(Chest, gm);
            }
            else
            {
                // a core dies like a lightbulb with opinions
                FX.Shockwave(transform.position, 6f, new Color(3f, 0.6f, 0.3f), 0.6f);
                Sfx.Play(Sfx.I.glitch ? Sfx.I.glitch : Sfx.I.stun, 0.8f, 0.7f, 0f);
            }
        }

        void UpdateWreck(float dt)
        {
            wreckAge += dt;
            if (eyeMat) eyeMat.SetColor(EmissionId, glowColor * (Random.value < 0.2f && wreckAge < 0.8f ? 2f : 0f));
            if (impactAt < 0f)
            {
                fallVel += Vector3.down * (22f * dt);
                transform.position += fallVel * dt;
                if (hull) hull.localRotation *= Quaternion.Euler(fallSpin * dt);
                if (Random.value < 0.5f) FX.Burst(transform.position, new Color(0.15f, 0.13f, 0.14f, 0.6f), new Color(0.05f, 0.05f, 0.06f, 0.4f), 1, new Vector2(0.2f, 0.6f), new Vector2(0.25f, 0.5f), new Vector2(0.5f, 0.9f), -0.1f, false, 0.1f);
                if (transform.position.y <= 0.2f)
                {
                    Vector3 p = transform.position; p.y = 0.2f;
                    transform.position = p;
                    impactAt = wreckAge;
                    FX.Dust(p, 0.7f);
                    FX.Sparks(p, Vector3.up, true, false);
                    Sfx.Play(Sfx.I.thud, 0.5f, 1.3f);
                }
                if (wreckAge > 4f) Destroy(gameObject);
                return;
            }
            if (wreckAge - impactAt > 1.6f) Destroy(gameObject);
        }
    }
}
