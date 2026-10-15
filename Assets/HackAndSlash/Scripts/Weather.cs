using UnityEngine;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    /// <summary>
    /// Rain that follows the camera (wind-slanted streaks), plus splashes and expanding ripple rings across the
    /// whole roof. Everything is built in code from two small materials.
    /// </summary>
    public class Weather : MonoBehaviour
    {
        public Material streakMaterial;
        public Material rippleMaterial;
        public float roofRadius = 17.6f;
        [Range(0f, 2f)] public float intensity = 1f;
        public Vector3 wind = new Vector3(-2.2f, 0f, 1.1f);

        ParticleSystem rain, splashes, ripples;
        Transform follow;

        void Awake()
        {
            rain = Rain();
            splashes = Splashes();
            ripples = Ripples();
        }

        /// <summary>Scales the storm at runtime (0 = dry). Splashes and ripples only fall on wet open ground.</summary>
        public void SetIntensity(float k, bool groundEffects)
        {
            intensity = k;
            if (!rain) return;
            var em = rain.emission; em.rateOverTime = 4200f * k;
            var es = splashes.emission; es.rateOverTime = groundEffects ? 900f * k : 0f;
            var er = ripples.emission; er.rateOverTime = groundEffects ? 1100f * k : 0f;
            if (k <= 0.001f) { rain.Clear(); splashes.Clear(); ripples.Clear(); }
        }

        public void SetGroundRadius(float r)
        {
            roofRadius = r;
            if (!splashes) return;
            var a = splashes.shape; a.radius = r;
            var b = ripples.shape; b.radius = r;
        }

        void LateUpdate()
        {
            if (!follow) { var cam = Camera.main; if (cam) follow = cam.transform; }
            if (!follow || !rain) return;
            Vector3 f = follow.forward; f.y = 0f;
            Vector3 p = follow.position + (f.sqrMagnitude > 0.01f ? f.normalized * 7f : Vector3.zero);
            rain.transform.position = new Vector3(p.x, Mathf.Max(follow.position.y + 9f, 12f), p.z);
        }

        ParticleSystem Make(string name, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = true;
            main.duration = 2f;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        ParticleSystem Rain()
        {
            var ps = Make("Rain", streakMaterial, 6000);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.85f, 1.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.026f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.65f, 0.85f, 0.16f), new Color(0.8f, 0.85f, 1f, 0.28f));
            var em = ps.emission;
            em.rateOverTime = 4200f * intensity;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(42f, 0.5f, 42f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(wind.x - 0.4f, wind.x + 0.4f);
            vel.y = new ParticleSystem.MinMaxCurve(-25f, -21f);
            vel.z = new ParticleSystem.MinMaxCurve(wind.z - 0.4f, wind.z + 0.4f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.032f;
            r.lengthScale = 1f;
            ps.Play();
            return ps;
        }

        ParticleSystem Splashes()
        {
            var ps = Make("RainSplashes", streakMaterial, 1500);
            ps.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.022f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.7f, 0.78f, 0.95f, 0.35f), new Color(0.9f, 0.95f, 1f, 0.5f));
            main.gravityModifier = 1.4f;
            var em = ps.emission;
            em.rateOverTime = 900f * intensity;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = roofRadius;
            sh.radiusThickness = 1f;
            sh.rotation = new Vector3(90f, 0f, 0f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            vel.y = new ParticleSystem.MinMaxCurve(0.9f, 2.4f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.03f;
            r.lengthScale = 1.2f;
            ps.Play();
            return ps;
        }

        ParticleSystem Ripples()
        {
            var ps = Make("RainRipples", rippleMaterial, 1800);
            ps.transform.localPosition = new Vector3(0f, 0.015f, 0f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.82f, 1f, 0.22f), new Color(0.9f, 0.95f, 1f, 0.38f));
            var em = ps.emission;
            em.rateOverTime = 1100f * intensity;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = roofRadius;
            sh.radiusThickness = 1f;
            sh.rotation = new Vector3(90f, 0f, 0f);
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(1f, 1f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ps.Play();
            return ps;
        }
    }
}
