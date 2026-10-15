using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    /// <summary>
    /// Runs the <see cref="TexRecipes"/> on worker threads once per session and turns the results into textures and
    /// PBR materials (cloned from the scene's URP Lit template so every keyword variant is available).
    /// </summary>
    public static class TexLibrary
    {
        public static bool Ready { get; private set; }
        public static bool Running { get; private set; }
        public static float Progress { get; private set; }

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static readonly Dictionary<string, Texture2D> texs = new Dictionary<string, Texture2D>();
        static readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        public static Texture2D Tex(string key) => texs.TryGetValue(key, out var t) ? t : null;

        /// <summary>Generated material by key (falls back to a plain lit material while generation is pending).</summary>
        public static Material Mat(string key)
        {
            if (mats.TryGetValue(key, out var m) && m) return m;
            return FX.Lit(new Color(0.3f, 0.3f, 0.32f), 0f, 0.4f);
        }

        struct Job
        {
            public string key;
            public Func<TexRecipes.Set> recipe;
            public Func<TexGen.Pixels> single;
            public Task<TexRecipes.Set> task;
            public Task<TexGen.Pixels> singleTask;
        }

        /// <summary>Generates everything (threaded), yielding while the workers run. Safe to call more than once.</summary>
        public static IEnumerator Generate()
        {
            if (Ready || Running) { while (Running) yield return null; yield break; }
            Running = true;
            Progress = 0f;
            const int N = 512;
            var jobs = new List<Job>
            {
                new Job { key = "Concrete", recipe = () => TexRecipes.Concrete(N, 11, new Color(0.62f, 0.6f, 0.58f), 0.6f) },
                new Job { key = "ConcreteDark", recipe = () => TexRecipes.Concrete(N, 12, new Color(0.36f, 0.35f, 0.36f), 0.25f) },
                new Job { key = "Asphalt", recipe = () => TexRecipes.Asphalt(N, 21) },
                new Job { key = "Brick", recipe = () => TexRecipes.Brick(N, 31, new Color(0.5f, 0.22f, 0.15f)) },
                new Job { key = "Rust", recipe = () => TexRecipes.RustMetal(N, 41, new Color(0.2f, 0.32f, 0.36f)) },
                new Job { key = "RustRed", recipe = () => TexRecipes.RustMetal(N, 42, new Color(0.42f, 0.1f, 0.08f)) },
                new Job { key = "Car", recipe = () => TexRecipes.CarPaint(N, 51) },
                new Job { key = "Facade", recipe = () => TexRecipes.Facade(N, 61, 4, 4, 0.05f, 0.35f, new Color(0.42f, 0.4f, 0.38f)) },
                new Job { key = "FacadeLit", recipe = () => TexRecipes.Facade(N, 62, 4, 4, 0.22f, 0.12f, new Color(0.3f, 0.3f, 0.33f)) },
                new Job { key = "FacadeBrick", recipe = () => TexRecipes.Facade(N, 63, 4, 6, 0.08f, 0.25f, new Color(0.45f, 0.24f, 0.18f)) },
                new Job { key = "Marble", recipe = () => TexRecipes.Marble(N, 71) },
                new Job { key = "Carpet", recipe = () => TexRecipes.Carpet(N, 81) },
                new Job { key = "HotelWall", recipe = () => TexRecipes.HotelWall(N, 91) },
                new Job { key = "Wood", recipe = () => TexRecipes.Wood(N, 92) },
                new Job { key = "Construct", recipe = () => TexRecipes.ConstructFloor(N, 101) },
                new Job { key = "Hazard", recipe = () => TexRecipes.Hazard(256, 131) },
                new Job { key = "Cloth", recipe = () => TexRecipes.Cloth(N, 141) },
                new Job { key = "ArmorDetail", recipe = () => TexRecipes.ArmorDetail(N, 151) },
                new Job { key = "Glyphs", single = () => TexRecipes.Glyphs(N, 111) },
                new Job { key = "Holo", single = () => TexRecipes.HoloNoise(256, 121) },
                new Job { key = "Puddles", single = () => TexRecipes.Puddles(N, 161) },
                new Job { key = "Smoke", single = () => TexRecipes.Smoke(128, 171) },
                new Job { key = "SoftDot", single = () => TexRecipes.SoftDot(64) },
            };
            // a few at a time, so the main thread keeps rendering the title screen smoothly
            int workers = Mathf.Clamp(SystemInfo.processorCount - 1, 1, 6);
            int next = 0, done = 0;
            var running = new List<int>();
            while (done < jobs.Count)
            {
                while (running.Count < workers && next < jobs.Count)
                {
                    var j = jobs[next];
                    if (j.recipe != null) { var r = j.recipe; j.task = Task.Run(r); }
                    else { var s = j.single; j.singleTask = Task.Run(s); }
                    jobs[next] = j;
                    running.Add(next++);
                }
                for (int k = running.Count - 1; k >= 0; k--)
                {
                    var j = jobs[running[k]];
                    bool finished = j.task != null ? j.task.IsCompleted : j.singleTask.IsCompleted;
                    if (!finished) continue;
                    running.RemoveAt(k);
                    done++;
                    try
                    {
                        if (j.task != null) Build(j.key, j.task.Result);
                        else texs[j.key] = Upload(j.key, j.singleTask.Result, j.key == "Glyphs" || j.key == "Smoke" || j.key == "SoftDot" ? TextureWrapMode.Repeat : TextureWrapMode.Repeat);
                    }
                    catch (Exception e) { Debug.LogWarning("[TexGen] " + j.key + " failed: " + e.Message); }
                    Progress = done / (float)jobs.Count;
                }
                yield return null;
            }
            Running = false;
            Ready = true;
            Progress = 1f;
        }

        static Texture2D Upload(string name, TexGen.Pixels p, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            var t = new Texture2D(p.n, p.n, TextureFormat.RGBA32, true, p.linear) { name = "Gen_" + name, wrapMode = wrap, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels(p.c);
            t.Apply(true, true);
            owned.Add(t);
            return t;
        }

        static void Build(string key, TexRecipes.Set s)
        {
            var al = Upload(key + "_A", s.albedo);
            var nm = s.normal != null ? Upload(key + "_N", s.normal) : null;
            var mk = s.mask != null ? Upload(key + "_M", s.mask) : null;
            var em = s.emission != null ? Upload(key + "_E", s.emission) : null;
            texs[key + "_A"] = al;
            if (nm) texs[key + "_N"] = nm;
            if (mk) texs[key + "_M"] = mk;
            if (em) texs[key + "_E"] = em;
            if (s.extra != null) texs[key + "_X"] = Upload(key + "_X", s.extra);
            if (key == "ArmorDetail") return; // used as a detail layer on the robots, not a standalone material

            var m = NewLit(key);
            m.SetTexture("_BaseMap", al);
            m.SetColor("_BaseColor", Color.white);
            if (nm) { m.SetTexture("_BumpMap", nm); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            if (mk)
            {
                m.SetTexture("_MetallicGlossMap", mk);
                m.SetFloat("_Smoothness", 1f);
                m.SetFloat("_SmoothnessTextureChannel", 0f);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetTexture("_OcclusionMap", mk);
                m.SetFloat("_OcclusionStrength", 1f);
                m.EnableKeyword("_OCCLUSIONMAP");
            }
            if (em)
            {
                m.SetTexture("_EmissionMap", em);
                m.SetColor("_EmissionColor", key == "Construct" ? new Color(0.6f, 0.6f, 0.6f) : new Color(2.2f, 2.1f, 2f));
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            if (key == "Cloth")
            {
                m.SetFloat("_Cull", 0f);
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            mats[key] = m;
        }

        static Material NewLit(string key)
        {
            var gm = GameManager.I;
            var tpl = gm && gm.cityMaterial ? gm.cityMaterial : gm && gm.metalMaterial ? gm.metalMaterial : FX.FallbackLit;
            var m = new Material(tpl) { name = "Gen_" + key };
            m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            owned.Add(m);
            return m;
        }

        /// <summary>A tinted copy of a generated material (cars, painted props). Cached per key + colour.</summary>
        public static Material Tinted(string key, Color tint, float smoothScale = 1f)
        {
            string k = key + "#" + ColorUtility.ToHtmlStringRGB(tint) + smoothScale.ToString("F2");
            if (mats.TryGetValue(k, out var m) && m) return m;
            var src = Mat(key);
            m = new Material(src) { name = src.name + "_Tint" };
            m.SetColor("_BaseColor", tint);
            if (smoothScale != 1f) m.SetFloat("_Smoothness", smoothScale);
            owned.Add(m);
            mats[k] = m;
            return m;
        }

        /// <summary>Applies the generated grime/scratch/panel detail layer to a robot paint material.</summary>
        public static void ApplyArmorDetail(Material paint, float strength)
        {
            var a = Tex("ArmorDetail_A"); var n = Tex("ArmorDetail_N");
            if (!paint || !a) return;
            paint.SetTexture("_DetailAlbedoMap", a);
            paint.SetFloat("_DetailAlbedoMapScale", strength);
            if (n) { paint.SetTexture("_DetailNormalMap", n); paint.SetFloat("_DetailNormalMapScale", 0.6f * strength); }
            paint.SetTextureScale("_DetailAlbedoMap", new Vector2(2f, 2f));
            paint.EnableKeyword("_DETAIL_MULX2");
        }
    }
}
