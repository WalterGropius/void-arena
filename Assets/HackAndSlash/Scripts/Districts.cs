using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HackAndSlash
{
    /// <summary>Lighting, sky, weather and grade for one district (and one story phase of it).</summary>
    public struct DistrictLook
    {
        public Color fog; public float fogDensity;
        public Color skyTint, horizon, solid; public float skyExposure, solidMix, stars, skyRotation;
        public Color moon; public float moonIntensity; public Vector3 moonEuler;
        public float ambient; public Color ambientTint;
        public float rain; public bool groundRain, storm; public float stormStrength;
        public float bloom, exposure, contrast, saturation, temperature, tint, vignette, grain, chroma;
        public Color filter, splitShadows, splitHighlights; public float splitBalance;

        public static DistrictLook For(District d, int phase = 0)
        {
            var l = new DistrictLook
            {
                fog = new Color(0.12f, 0.08f, 0.125f), fogDensity = 0.0048f,
                skyTint = new Color(0.92f, 0.9f, 1f), horizon = new Color(0.13f, 0.075f, 0.14f), solid = Color.white, skyExposure = 0.55f, skyRotation = 200f,
                moon = new Color(0.62f, 0.66f, 1f), moonIntensity = 0.38f, moonEuler = new Vector3(48f, -32f, 0f),
                ambient = 0.55f, ambientTint = Color.white,
                rain = 1f, groundRain = true, storm = true, stormStrength = 1f,
                bloom = 0.9f, exposure = 0.55f, contrast = 14f, saturation = 6f, temperature = -6f, tint = 4f, vignette = 0.33f, grain = 0.14f, chroma = 0.05f,
                filter = Color.white, splitShadows = new Color(0.36f, 0.46f, 0.62f), splitHighlights = new Color(0.66f, 0.56f, 0.46f), splitBalance = -15f,
            };
            switch (d)
            {
                case District.Streets: // Blade Runner: teal shadows, sodium and neon highlights, thick wet haze
                    l.fog = new Color(0.06f, 0.1f, 0.13f); l.fogDensity = 0.011f;
                    l.horizon = new Color(0.16f, 0.1f, 0.12f); l.skyExposure = 0.35f;
                    l.moonIntensity = 0.2f; l.moon = new Color(0.5f, 0.62f, 0.9f);
                    l.ambient = 0.42f; l.ambientTint = new Color(0.8f, 0.95f, 1.1f);
                    l.rain = 1.25f;
                    l.bloom = 1.25f; l.exposure = 0.7f; l.contrast = 18f; l.saturation = 10f; l.temperature = -10f; l.tint = 2f;
                    l.splitShadows = new Color(0.25f, 0.5f, 0.62f); l.splitHighlights = new Color(0.78f, 0.52f, 0.36f); l.splitBalance = -25f;
                    l.vignette = 0.38f; l.grain = 0.18f;
                    break;
                case District.Hotel: // Kubrick: warm, even, very still; no weather indoors
                    l.fog = new Color(0.16f, 0.1f, 0.06f); l.fogDensity = 0.006f;
                    l.moonIntensity = 0f;
                    l.ambient = 0.7f; l.ambientTint = new Color(1.2f, 0.95f, 0.75f);
                    l.rain = 0f; l.groundRain = false; l.storm = false;
                    l.bloom = 0.7f; l.exposure = 0.35f; l.contrast = 22f; l.saturation = -4f; l.temperature = 12f; l.tint = 0f;
                    l.splitShadows = new Color(0.45f, 0.4f, 0.36f); l.splitHighlights = new Color(0.75f, 0.6f, 0.42f); l.splitBalance = 10f;
                    l.vignette = 0.42f; l.grain = 0.2f; l.chroma = 0.03f;
                    if (phase == 1) { l.filter = new Color(1f, 0.6f, 0.55f); l.saturation = -20f; l.contrast = 30f; } // the recording
                    break;
                case District.Construct: // the white loading program, or its black-and-green glitch
                    if (phase == 0)
                    {
                        l.fog = new Color(0.93f, 0.95f, 0.93f); l.fogDensity = 0.012f;
                        l.solid = new Color(0.95f, 0.97f, 0.95f); l.solidMix = 1f;
                        l.moon = new Color(1f, 1f, 1f); l.moonIntensity = 1.4f; l.moonEuler = new Vector3(62f, 20f, 0f);
                        l.ambient = 1.3f; l.ambientTint = new Color(1f, 1.05f, 1f);
                        l.bloom = 0.5f; l.exposure = 0.45f; l.contrast = 10f; l.saturation = -35f; l.temperature = -4f; l.tint = -8f;
                        l.splitShadows = new Color(0.45f, 0.55f, 0.48f); l.splitHighlights = new Color(0.55f, 0.6f, 0.55f); l.splitBalance = 0f;
                        l.vignette = 0.2f; l.grain = 0.1f;
                    }
                    else
                    {
                        l.fog = new Color(0.0f, 0.04f, 0.01f); l.fogDensity = 0.02f;
                        l.solid = new Color(0f, 0.02f, 0.005f); l.solidMix = 1f;
                        l.moon = new Color(0.3f, 1f, 0.45f); l.moonIntensity = 0.5f;
                        l.ambient = 0.6f; l.ambientTint = new Color(0.4f, 1.3f, 0.6f);
                        l.bloom = 1.4f; l.exposure = 0.6f; l.contrast = 25f; l.saturation = -10f; l.filter = new Color(0.7f, 1f, 0.75f);
                        l.vignette = 0.45f; l.chroma = 0.25f; l.grain = 0.25f;
                    }
                    l.rain = 0f; l.groundRain = false; l.storm = false;
                    break;
                case District.Spire: // above the clouds, under the Monolith; at midnight the storm finally ends
                    l.fog = new Color(0.1f, 0.08f, 0.14f); l.fogDensity = 0.0028f;
                    l.horizon = new Color(0.16f, 0.1f, 0.2f); l.skyExposure = 0.6f;
                    l.moon = new Color(0.62f, 0.62f, 1f); l.moonIntensity = 0.32f;
                    l.ambient = 0.45f; l.ambientTint = new Color(0.9f, 0.85f, 1.15f);
                    l.rain = 1.4f; l.stormStrength = 1.6f;
                    l.bloom = 1.1f; l.exposure = 0.55f; l.contrast = 20f; l.saturation = 0f; l.temperature = -14f; l.tint = 8f;
                    l.splitShadows = new Color(0.3f, 0.32f, 0.62f); l.splitHighlights = new Color(0.7f, 0.45f, 0.42f);
                    l.vignette = 0.4f;
                    if (phase == 1)
                    {
                        // 12:00 — rain stops, stars come out, the light goes cool and clean
                        l.rain = 0f; l.storm = false; l.stars = 1f; l.skyExposure = 0.25f;
                        l.fog = new Color(0.05f, 0.06f, 0.1f); l.fogDensity = 0.0016f;
                        l.moon = new Color(0.8f, 0.86f, 1f); l.moonIntensity = 0.7f;
                        l.saturation = 8f; l.contrast = 12f; l.temperature = -4f; l.tint = 0f; l.vignette = 0.3f;
                    }
                    break;
            }
            return l;
        }
    }

    /// <summary>Loads districts (generation is threaded; instantiation happens on the main thread) and applies their look.</summary>
    public class DistrictManager : MonoBehaviour
    {
        public DistrictInstance Current { get; private set; }
        public District CurrentDistrict => Current != null ? Current.district : District.Rooftop;
        public bool Loading { get; private set; }

        GameManager gm;
        VolumeProfile runtimeProfile;
        float baseSkyRotation = 200f;

        void Awake() => gm = GetComponent<GameManager>();

        public IEnumerator Load(District d, int phase = 0)
        {
            Loading = true;
            if (!gm) gm = GetComponent<GameManager>();
            if (!TexLibrary.Ready) yield return TexLibrary.Generate();
            CityBuilder.ClearCache();
            if (Current != null) { Current.Destroy(); Current = null; }
            yield return null;
            // generation is pure data: run it off the main thread
            int seed = 1977 + (int)d * 101;
            var task = Task.Run(() => CityGen.Build(d, seed));
            while (!task.IsCompleted) yield return null;
            CityPlan plan = null;
            try { plan = task.Result; }
            catch (System.Exception e) { Debug.LogError("[CityGen] " + e); }
            if (plan == null) plan = new CityPlan { district = d };
            Current = CityBuilder.Instantiate(plan, gm);
            if (gm.rooftopRoot) gm.rooftopRoot.SetActive(d == District.Rooftop);
            gm.SetArena(plan.arena, Current.spawnPoints, plan.spawnStyle);
            if (gm.environment)
            {
                gm.environment.extraBeacons.Clear();
                gm.environment.extraBeacons.AddRange(Current.beacons);
            }
            if (gm.weather) gm.weather.SetGroundRadius(plan.arena.Extent + 1f);
            ApplyLook(DistrictLook.For(d, phase));
            if (gm.environment) gm.environment.RefreshProbe();
            Loading = false;
        }

        /// <summary>Story beats re-grade a district (the Construct's glitch, the Spire at midnight, the hotel recording).</summary>
        public void SetPhase(int phase)
        {
            ApplyLook(DistrictLook.For(CurrentDistrict, phase));
            if (Current == null) return;
            if (CurrentDistrict == District.Construct)
                foreach (var r in Current.rainCurtains) if (r) r.SetActive(phase == 1);
        }

        public void ApplyLook(DistrictLook l)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = l.fog;
            RenderSettings.fogDensity = l.fogDensity;
            var env = gm.environment;
            if (env)
            {
                env.ambientIntensity = l.ambient;
                env.ambientTint = l.ambientTint;
                env.ApplyAmbient();
                env.stormEnabled = l.storm;
                env.stormStrength = l.stormStrength;
                var sky = env.skyMaterial;
                if (sky)
                {
                    sky.SetColor("_Tint", l.skyTint);
                    sky.SetFloat("_Exposure", l.skyExposure);
                    sky.SetColor("_HorizonColor", l.horizon);
                    sky.SetFloat("_Stars", l.stars);
                    sky.SetColor("_SolidColor", l.solid);
                    sky.SetFloat("_SolidMix", l.solidMix);
                    sky.SetFloat("_Rotation", baseSkyRotation);
                }
            }
            if (gm.moon)
            {
                gm.moon.color = l.moon;
                gm.moon.intensity = l.moonIntensity;
                gm.moon.transform.rotation = Quaternion.Euler(l.moonEuler);
                gm.moon.enabled = l.moonIntensity > 0.001f;
            }
            if (gm.weather) gm.weather.SetIntensity(l.rain, l.groundRain);
            if (Sfx.I) Sfx.I.SetRain(l.rain > 0.01f ? Mathf.Clamp01(0.6f + 0.4f * l.rain) : (CurrentDistrict == District.Hotel ? 0.18f : 0f), CurrentDistrict == District.Hotel);
            ApplyGrade(l);
        }

        void ApplyGrade(DistrictLook l)
        {
            if (!gm.mainVolume) return;
            if (runtimeProfile == null) runtimeProfile = gm.mainVolume.profile; // instanced copy, the asset stays untouched
            var p = runtimeProfile;
            if (p.TryGet(out Bloom bloom)) bloom.intensity.value = l.bloom;
            if (p.TryGet(out ColorAdjustments ca))
            {
                ca.postExposure.value = l.exposure;
                ca.contrast.value = l.contrast;
                ca.saturation.value = l.saturation;
                ca.colorFilter.overrideState = true;
                ca.colorFilter.value = l.filter;
            }
            if (p.TryGet(out WhiteBalance wb)) { wb.temperature.value = l.temperature; wb.tint.value = l.tint; }
            if (p.TryGet(out SplitToning st)) { st.shadows.value = l.splitShadows; st.highlights.value = l.splitHighlights; st.balance.value = l.splitBalance; }
            if (p.TryGet(out Vignette v)) v.intensity.value = l.vignette;
            if (p.TryGet(out FilmGrain fg)) fg.intensity.value = l.grain;
            if (p.TryGet(out ChromaticAberration ch)) ch.intensity.value = l.chroma;
        }
    }
}
