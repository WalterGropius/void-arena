using UnityEngine;

namespace HackAndSlash
{
    /// <summary>Health pickup that pops out of defeated enemies and gets magnetised to the player.</summary>
    public class HealthOrb : MonoBehaviour
    {
        const float Heal = 15f;
        GameManager gm;
        Vector3 vel;
        float age;
        Transform core;

        public static void Spawn(Vector3 pos, GameManager gm)
        {
            var go = FX.NewObject("HealthOrb");
            go.transform.position = pos;
            var o = go.AddComponent<HealthOrb>();
            o.gm = gm;
            o.vel = new Vector3(Random.Range(-3f, 3f), Random.Range(5f, 7.5f), Random.Range(-3f, 3f));
            var green = new Color(0.25f, 1f, 0.45f);
            o.core = Weapons.Part(PrimitiveType.Sphere, go.transform, Vector3.zero, Vector3.one * 0.32f, FX.Emissive(green, 3.5f), default, false).transform;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = green;
            l.range = 3f;
            l.intensity = 2.5f;
            l.shadows = LightShadows.None;
            gm.orbs.Add(o);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            var p = gm.player;
            Vector3 to = p.Chest - transform.position;
            bool magnet = age > 0.6f && !p.IsDead && to.magnitude < 7.5f;
            if (magnet)
            {
                vel = Vector3.MoveTowards(vel, to.normalized * 16f, 45f * dt);
            }
            else
            {
                vel.y -= 18f * dt;
                vel.x *= Mathf.Exp(-1.5f * dt);
                vel.z *= Mathf.Exp(-1.5f * dt);
            }
            var pos = transform.position + vel * dt;
            if (!magnet && pos.y < 0.45f)
            {
                pos.y = 0.45f;
                vel.y = Mathf.Abs(vel.y) * 0.4f;
                vel.x *= 0.7f; vel.z *= 0.7f;
            }
            transform.position = pos;
            core.localPosition = new Vector3(0f, Mathf.Sin(age * 5f) * 0.08f, 0f);
            core.localScale = Vector3.one * (0.32f + Mathf.Sin(age * 9f) * 0.03f);

            if (age > 0.3f && to.magnitude < 0.9f && !p.IsDead)
            {
                p.Heal(Heal);
                Sfx.Play(gm.pickupClip, 0.6f, 1.1f);
                Destroy(gameObject);
                return;
            }
            if (age > 25f) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (gm) gm.orbs.Remove(this);
        }
    }
}
