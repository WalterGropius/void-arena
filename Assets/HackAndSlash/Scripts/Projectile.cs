using UnityEngine;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    /// <summary>
    /// Homing hex bolts (Hexers) and rifle rounds (Enforcers, Hornets). Both can be countered back: a bolt is
    /// batted away at the nearest robot, a bullet goes straight back down the barrel it came from.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public bool reflected;
        public bool IsBullet => bullet;
        float damage, speed = 9.5f, life = 5f;
        Vector3 vel;
        Color color;
        Enemy homingTarget, owner;
        bool bullet;
        GameManager gm;
        Light glow;
        TrailRenderer trail;
        MeshRenderer orb;
        CounterIndicator indicator;

        public static Projectile Spawn(Vector3 pos, Vector3 dir, float dmg, GameManager gm, Color color)
        {
            var go = FX.NewObject("HexBolt");
            go.transform.position = pos;
            var p = go.AddComponent<Projectile>();
            p.gm = gm;
            p.damage = dmg;
            p.color = color;
            p.vel = (dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward) * p.speed;

            var core = Weapons.Part(PrimitiveType.Sphere, go.transform, Vector3.zero, Vector3.one * 0.36f, FX.Emissive(color, 5f), default, false);
            p.orb = core.GetComponent<MeshRenderer>();

            p.glow = go.AddComponent<Light>();
            p.glow.type = LightType.Point;
            p.glow.color = color;
            p.glow.range = 4.5f;
            p.glow.intensity = 3.5f;
            p.glow.shadows = LightShadows.None;

            p.trail = go.AddComponent<TrailRenderer>();
            p.trail.time = 0.3f;
            p.trail.startWidth = 0.34f;
            p.trail.endWidth = 0f;
            p.trail.numCapVertices = 4;
            p.trail.sharedMaterial = FX.FlatMat;
            p.trail.shadowCastingMode = ShadowCastingMode.Off;
            p.SetTrailColor(color);

            p.indicator = CounterIndicator.Create(go.transform, 0.55f, 0.42f);
            gm.projectiles.Add(p);
            return p;
        }

        /// <summary>A fast, straight rifle round with a tracer trail.</summary>
        public static Projectile SpawnBullet(Vector3 pos, Vector3 dir, float dmg, GameManager gm, Enemy owner)
        {
            var go = FX.NewObject("Bullet");
            go.transform.position = pos;
            var p = go.AddComponent<Projectile>();
            p.gm = gm;
            p.bullet = true;
            p.owner = owner;
            p.damage = dmg;
            p.speed = 36f * Mathf.Lerp(1f, 0.8f, Settings.difficulty == Difficulty.Easy ? 1f : 0f);
            p.life = 1.4f;
            p.color = new Color(1f, 0.55f, 0.25f);
            p.vel = (dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward) * p.speed;
            go.transform.rotation = Quaternion.LookRotation(p.vel);
            var core = Weapons.Part(PrimitiveType.Capsule, go.transform, Vector3.zero, new Vector3(0.06f, 0.22f, 0.06f), FX.Emissive(new Color(1f, 0.7f, 0.35f), 7f), new Vector3(90f, 0f, 0f), false);
            p.orb = core.GetComponent<MeshRenderer>();
            p.trail = go.AddComponent<TrailRenderer>();
            p.trail.time = 0.06f;
            p.trail.startWidth = 0.07f;
            p.trail.endWidth = 0f;
            p.trail.sharedMaterial = FX.FlatMat;
            p.trail.shadowCastingMode = ShadowCastingMode.Off;
            p.SetTrailColor(p.color);
            gm.projectiles.Add(p);
            return p;
        }

        void SetTrailColor(Color c)
        {
            trail.startColor = new Color(Mathf.Min(1f, c.r * 1.2f), Mathf.Min(1f, c.g * 1.2f), Mathf.Min(1f, c.b * 1.2f), 0.9f);
            trail.endColor = new Color(c.r, c.g, c.b, 0f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            life -= dt;
            if (life <= 0f) { Pop(); return; }

            if (bullet && !reflected)
            {
                var pl = gm.player;
                Vector3 step = vel * dt;
                if (!pl.IsDead && SegmentHits(transform.position, step, pl.Chest, 0.55f) && pl.TakeDamage(damage, transform.position - vel.normalized, 4f)) { Pop(); return; }
                transform.position += step;
                if (transform.position.y < 0.05f) { Pop(); return; }
                return;
            }
            if (!reflected)
            {
                var pl = gm.player;
                if (!pl.IsDead)
                {
                    Vector3 to = pl.Chest - transform.position;
                    vel = Vector3.RotateTowards(vel, to.normalized * speed, 1.0f * dt, 0f);
                    if (indicator) indicator.Show(to.magnitude < 5.5f, false);
                    if (to.magnitude < 0.75f && pl.TakeDamage(damage, transform.position - vel.normalized, 5f)) { Pop(); return; }
                }
            }
            else
            {
                if (bullet && owner != null && owner.Targetable) homingTarget = owner;
                if (homingTarget == null || !homingTarget.Targetable) homingTarget = gm.NearestEnemy(transform.position, 30f);
                if (homingTarget)
                {
                    Vector3 to = homingTarget.Chest - transform.position;
                    vel = Vector3.RotateTowards(vel, to.normalized * vel.magnitude, 5f * dt, 0f);
                }
                var list = gm.enemies;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var e = list[i];
                    if (e == null || !e.Targetable) continue;
                    float r = e.Radius + 0.55f;
                    if ((e.Chest - transform.position).sqrMagnitude < r * r)
                    {
                        Vector3 d = vel; d.y = 0f;
                        e.TakeHit(new HitInfo(damage, d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward, 9f, HitKind.Projectile, true), transform.position - vel.normalized);
                        gm.HitStop(0.06f);
                        gm.cameraRig.Shake(0.35f);
                        Pop();
                        return;
                    }
                }
            }

            transform.position += vel * dt;
            if (bullet && vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(vel);
            if (transform.position.y < 0.2f) vel.y = Mathf.Abs(vel.y);
            if (transform.position.sqrMagnitude > 900f) Destroy(gameObject);
        }

        static bool SegmentHits(Vector3 a, Vector3 d, Vector3 c, float r)
        {
            float len2 = d.sqrMagnitude;
            float t = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(c - a, d) / len2) : 0f;
            return (a + d * t - c).sqrMagnitude < r * r;
        }

        /// <summary>The robot that fired this round (bullets go back to it when countered).</summary>
        public Enemy Owner => owner;

        public void Reflect(Vector3 dir)
        {
            if (reflected) return;
            reflected = true;
            damage = bullet ? 60f : 45f;
            life = 3f;
            color = new Color(0.3f, 0.9f, 1f);
            if (bullet && owner != null && owner.Targetable) dir = owner.Chest - transform.position;
            vel = (dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward) * (bullet ? 40f : speed * 2.1f);
            homingTarget = bullet ? owner : null;
            if (indicator) indicator.Show(false, false);
            if (orb) orb.sharedMaterial = FX.Emissive(color, 5f);
            if (glow) glow.color = color;
            if (trail) SetTrailColor(color);
        }

        public void Pop()
        {
            if (bullet)
            {
                FX.Burst(transform.position, color, Color.white, 6, new Vector2(2f, 5f), new Vector2(0.03f, 0.06f), new Vector2(0.08f, 0.2f), 0.5f, true, 0.05f);
                Destroy(gameObject);
                return;
            }
            FX.Burst(transform.position, color, Color.white, 18, new Vector2(2f, 6f), new Vector2(0.05f, 0.12f), new Vector2(0.2f, 0.45f), 0.5f, true, 0.1f);
            FX.LightFlash(transform.position, color, 4f, 5f, 0.2f);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (gm) gm.projectiles.Remove(this);
        }
    }
}
