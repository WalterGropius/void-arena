using UnityEngine;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    /// <summary>Homing hex bolt. Can be parried by the player's sword and sent back at enemies.</summary>
    public class Projectile : MonoBehaviour
    {
        public bool reflected;
        float damage, speed = 9.5f, life = 5f;
        Vector3 vel;
        Color color;
        Enemy homingTarget;
        GameManager gm;
        Light glow;
        TrailRenderer trail;
        MeshRenderer orb;

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

            if (!reflected)
            {
                var pl = gm.player;
                if (!pl.IsDead)
                {
                    Vector3 to = pl.Chest - transform.position;
                    vel = Vector3.RotateTowards(vel, to.normalized * speed, 1.0f * dt, 0f);
                    if (to.magnitude < 0.75f && pl.TakeDamage(damage, transform.position - vel.normalized, 5f)) { Pop(); return; }
                }
            }
            else
            {
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
                        e.TakeHit(damage, d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward, 9f, true, true);
                        gm.HitStop(0.06f);
                        gm.cameraRig.Shake(0.35f);
                        Pop();
                        return;
                    }
                }
            }

            transform.position += vel * dt;
            if (transform.position.y < 0.2f) vel.y = Mathf.Abs(vel.y);
            if (transform.position.sqrMagnitude > 900f) Destroy(gameObject);
        }

        public void Reflect(Vector3 dir)
        {
            if (reflected) return;
            reflected = true;
            damage = 45f;
            life = 3f;
            color = new Color(0.3f, 0.9f, 1f);
            vel = (dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward) * (speed * 2.1f);
            homingTarget = null;
            orb.sharedMaterial = FX.Emissive(color, 5f);
            glow.color = color;
            SetTrailColor(color);
        }

        public void Pop()
        {
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
