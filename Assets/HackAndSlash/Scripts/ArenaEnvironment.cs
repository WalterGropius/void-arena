using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    /// <summary>
    /// The rooftop's living lighting: image-based ambient light projected from the HDRI, a one-shot realtime
    /// reflection probe, a thunderstorm (sky flashes, a fork of lightning on the skyline, thunder), and blinking
    /// aircraft beacons on the towers.
    /// </summary>
    public class ArenaEnvironment : MonoBehaviour
    {
        [Header("Image-based ambient")]
        [Tooltip("Lines of 'dx dy dz r g b': HDRI radiance pre-weighted by solid angle / pi (see README).")]
        public TextAsset ambientSamples;
        public float ambientIntensity = 0.55f;
        [Range(0f, 1f)] public float groundBounce = 0.35f;
        public Color ambientTint = Color.white;

        [Header("Reflections")]
        public ReflectionProbe probe;

        [Header("Storm")]
        public Light lightningLight;
        public Material skyMaterial;
        public Vector2 lightningInterval = new Vector2(11f, 24f);

        [Header("Beacons")]
        public Renderer[] beacons;

        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        float nextStrike;
        MaterialPropertyBlock mpb;
        bool striking;

        void Awake()
        {
            ApplyAmbient();
            mpb = new MaterialPropertyBlock();
            nextStrike = Time.time + Random.Range(5f, 9f);
            if (lightningLight) lightningLight.intensity = 0f;
            if (skyMaterial) skyMaterial.SetFloat(FlashId, 0f);
        }

        IEnumerator Start()
        {
            // let the first frame settle (particles, emissive signage) before capturing the reflection probe
            yield return null;
            yield return null;
            if (probe) probe.RenderProbe();
        }

        void OnDestroy()
        {
            if (skyMaterial) skyMaterial.SetFloat(FlashId, 0f);
        }

        /// <summary>Projects the HDRI into the scene's ambient spherical harmonics (no lighting bake needed).</summary>
        public void ApplyAmbient()
        {
            if (!ambientSamples) return;
            var sh = new SphericalHarmonicsL2();
            var lines = ambientSamples.text.Split('\n');
            var inv = CultureInfo.InvariantCulture;
            foreach (var raw in lines)
            {
                var p = raw.Trim().Split(' ');
                if (p.Length < 6) continue;
                if (!float.TryParse(p[0], NumberStyles.Float, inv, out float dx) || !float.TryParse(p[1], NumberStyles.Float, inv, out float dy) ||
                    !float.TryParse(p[2], NumberStyles.Float, inv, out float dz) || !float.TryParse(p[3], NumberStyles.Float, inv, out float r) ||
                    !float.TryParse(p[4], NumberStyles.Float, inv, out float g) || !float.TryParse(p[5], NumberStyles.Float, inv, out float b)) continue;
                var c = new Color(r, g, b) * ambientTint;
                if (dy < 0f) c *= groundBounce;
                float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                if (peak <= 0f) continue;
                sh.AddDirectionalLight(new Vector3(dx, dy, dz).normalized, c / peak, peak * ambientIntensity);
            }
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = sh;
        }

        void Update()
        {
            if (!striking && Time.time >= nextStrike) Lightning(1f);
            if (beacons != null && beacons.Length > 0)
            {
                for (int i = 0; i < beacons.Length; i++)
                {
                    var r = beacons[i];
                    if (!r) continue;
                    float phase = Mathf.Repeat(Time.time * 0.8f + i * 0.37f, 1f);
                    float on = phase < 0.12f ? 1f : 0.05f;
                    mpb.SetColor(EmissionId, new Color(4f, 0.15f, 0.05f) * on);
                    r.SetPropertyBlock(mpb);
                }
            }
        }

        /// <summary>A lightning strike: two or three flashes, a fork on the skyline, thunder after a delay.</summary>
        public void Lightning(float strength)
        {
            if (striking) return;
            StartCoroutine(Strike(strength));
        }

        IEnumerator Strike(float strength)
        {
            striking = true;
            nextStrike = Time.time + Random.Range(lightningInterval.x, lightningInterval.y);
            float yaw = Random.Range(0f, 360f);
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            if (lightningLight) lightningLight.transform.rotation = Quaternion.LookRotation((-dir + Vector3.down * 1.4f).normalized);

            // the bolt itself, far out over the city
            Vector3 top = dir * Random.Range(140f, 220f) + Vector3.up * Random.Range(110f, 160f);
            Vector3 bottom = top + new Vector3(Random.Range(-30f, 30f), -Random.Range(170f, 230f), Random.Range(-30f, 30f));
            FX.Arc(top, bottom, new Color(4f, 4.2f, 5f, 1f), 0.35f, Random.Range(1.2f, 2.2f));
            Vector3 mid = Vector3.Lerp(top, bottom, Random.Range(0.3f, 0.6f));
            FX.Arc(mid, mid + new Vector3(Random.Range(-50f, 50f), -Random.Range(50f, 90f), Random.Range(-50f, 50f)), new Color(3f, 3.2f, 4f, 1f), 0.25f, 0.8f);

            int flashes = Random.Range(2, 4);
            for (int i = 0; i < flashes; i++)
            {
                float peak = strength * Random.Range(0.6f, 1f) * (i == 0 ? 1f : 0.7f);
                float t = 0f, dur = Random.Range(0.06f, 0.12f);
                while (t < dur)
                {
                    t += Time.unscaledDeltaTime;
                    float k = peak * (1f - t / dur);
                    if (lightningLight) lightningLight.intensity = 3.2f * k;
                    if (skyMaterial) skyMaterial.SetFloat(FlashId, 2.5f * k);
                    yield return null;
                }
                if (lightningLight) lightningLight.intensity = 0f;
                if (skyMaterial) skyMaterial.SetFloat(FlashId, 0f);
                yield return new WaitForSecondsRealtime(Random.Range(0.04f, 0.14f));
            }
            yield return new WaitForSecondsRealtime(Random.Range(0.4f, 1.6f));
            if (Sfx.I) Sfx.Play(strength > 1.2f || Random.value < 0.4f ? Sfx.I.thunder : Sfx.I.thunderFar, Mathf.Clamp01(0.55f * strength + 0.25f), Random.Range(0.85f, 1.1f), 0f);
            if (strength > 1.2f && GameManager.I && GameManager.I.cameraRig) GameManager.I.cameraRig.Shake(0.25f);
            striking = false;
        }
    }

    /// <summary>Sweeps a searchlight (or anything) back and forth around its local Y and X axes.</summary>
    public class Sweeper : MonoBehaviour
    {
        public float yawAmplitude = 35f, yawSpeed = 0.25f, pitchAmplitude = 8f, pitchSpeed = 0.17f;
        Quaternion baseRot;
        float phase;

        void Awake()
        {
            baseRot = transform.localRotation;
            phase = Random.value * 10f;
        }

        void Update()
        {
            float t = Time.time + phase;
            transform.localRotation = baseRot * Quaternion.Euler(Mathf.Sin(t * pitchSpeed * Mathf.PI * 2f) * pitchAmplitude, Mathf.Sin(t * yawSpeed * Mathf.PI * 2f) * yawAmplitude, 0f);
        }
    }

    /// <summary>Neon sign buzz: mostly steady, with the occasional stutter.</summary>
    public class NeonFlicker : MonoBehaviour
    {
        public Color emission = Color.white;
        public Light linkedLight;
        Renderer r;
        MaterialPropertyBlock mpb;
        float baseLight, glitch;

        void Awake()
        {
            r = GetComponent<Renderer>();
            mpb = new MaterialPropertyBlock();
            if (linkedLight) baseLight = linkedLight.intensity;
        }

        void Update()
        {
            glitch -= Time.deltaTime;
            if (glitch <= 0f && Random.value < 0.004f) glitch = Random.Range(0.08f, 0.35f);
            float k = glitch > 0f ? (Random.value < 0.5f ? 0.15f : 1f) : 1f;
            if (r)
            {
                mpb.SetColor("_EmissionColor", emission * k);
                r.SetPropertyBlock(mpb);
            }
            if (linkedLight) linkedLight.intensity = baseLight * k;
        }
    }
}
