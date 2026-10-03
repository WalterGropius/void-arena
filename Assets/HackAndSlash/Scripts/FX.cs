using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    /// <summary>
    /// Code-built VFX: particle bursts, shockwave rings, ground telegraphs, light flashes,
    /// floating damage numbers, dash afterimages and physics-free debris.
    /// </summary>
    public static class FX
    {
        static GameManager gm;
        static Material glow, flat, dmgTextMat;
        static Mesh ringMesh, thinRingMesh, discMesh;
        static MaterialPropertyBlock mpb;
        static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();
        static readonly int ColorId = Shader.PropertyToID("_Color");

        static Transform root;

        /// <summary>Container for runtime-spawned objects so the Hierarchy stays readable.</summary>
        public static Transform Root
        {
            get
            {
                if (root == null && Application.isPlaying) root = new GameObject("_Runtime").transform;
                return root;
            }
        }

        public static GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            if (Root) go.transform.SetParent(root, false);
            return go;
        }

        public static void Adopt(GameObject go)
        {
            if (Root) go.transform.SetParent(root, true);
        }

        public static Material FlatMat => flat;
        public static Material GlowMat => glow;
        public static Mesh RingMesh => ringMesh;
        public static Mesh ThinRingMesh => thinRingMesh;
        public static Mesh DiscMesh => discMesh;

        public static void Init(GameManager g)
        {
            Cleanup();
            gm = g;
            var sprite = Shader.Find("Sprites/Default");
            glow = g.glowMaterial ? g.glowMaterial : new Material(sprite);
            flat = g.trailMaterial ? g.trailMaterial : new Material(sprite);
            ringMesh = BuildRing(0.78f, 1f, 72);
            thinRingMesh = BuildRing(0.93f, 1f, 72);
            discMesh = BuildDisc(64);
            mpb = new MaterialPropertyBlock();
        }

        public static void Cleanup()
        {
            foreach (var m in matCache.Values) if (m) Object.Destroy(m);
            matCache.Clear();
            if (ringMesh) Object.Destroy(ringMesh);
            if (thinRingMesh) Object.Destroy(thinRingMesh);
            if (discMesh) Object.Destroy(discMesh);
            if (dmgTextMat) Object.Destroy(dmgTextMat);
            dmgTextMat = null;
        }

        // ---------------------------------------------------------------- materials

        public static Material Lit(Color baseColor, float metallic, float smoothness)
        {
            string key = "L" + baseColor + metallic + smoothness;
            if (matCache.TryGetValue(key, out var m) && m) return m;
            m = new Material(gm.metalMaterial);
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.SetColor("_EmissionColor", Color.black);
            matCache[key] = m;
            return m;
        }

        public static Material Emissive(Color c, float intensity)
        {
            string key = "E" + c + intensity;
            if (matCache.TryGetValue(key, out var m) && m) return m;
            m = new Material(gm.metalMaterial);
            m.SetColor("_BaseColor", c * 0.25f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.8f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * intensity);
            matCache[key] = m;
            return m;
        }

        // ---------------------------------------------------------------- particles

        public static ParticleSystem Burst(Vector3 pos, Color a, Color b, int count, Vector2 speed, Vector2 size, Vector2 life,
            float gravity = 0f, bool stretch = false, float radius = 0.1f, Vector3? direction = null, float cone = 30f)
        {
            var go = NewObject("FX_Burst");
            go.transform.position = pos;
            if (direction.HasValue && direction.Value.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.LookRotation(direction.Value);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.maxParticles = Mathf.Max(1, count);

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(1, count)) });

            var sh = ps.shape;
            sh.enabled = true;
            if (direction.HasValue)
            {
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = Mathf.Clamp(cone, 0f, 89f);
                sh.radius = Mathf.Max(0.01f, radius);
            }
            else
            {
                sh.shapeType = ParticleSystemShapeType.Sphere;
                sh.radius = Mathf.Max(0.01f, radius);
            }

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeOut());

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f)));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = glow;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (stretch)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.045f;
                r.lengthScale = 1.6f;
            }
            ps.Play();
            return ps;
        }

        static Gradient FadeOut()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        public static void Sparks(Vector3 pos, Vector3 dir, bool crit, bool heavy)
        {
            int n = crit ? 34 : heavy ? 26 : 16;
            Burst(pos, new Color(1f, 0.95f, 0.75f), new Color(1f, 0.55f, 0.15f), n, new Vector2(5f, 13f), new Vector2(0.035f, 0.08f), new Vector2(0.15f, 0.4f), 1.6f, true, 0.05f, dir, 55f);
            Burst(pos, new Color(1f, 1f, 1f, 1f), new Color(1f, 0.9f, 0.7f, 1f), 1, Vector2.zero, crit ? new Vector2(2.4f, 2.4f) : new Vector2(1.3f, 1.5f), new Vector2(0.07f, 0.09f));
            if (crit) Burst(pos, new Color(1f, 0.85f, 0.3f), new Color(1f, 0.4f, 0.1f), 10, new Vector2(1f, 3f), new Vector2(0.2f, 0.35f), new Vector2(0.15f, 0.3f));
            LightFlash(pos, new Color(1f, 0.75f, 0.45f), crit ? 6f : 3f, 4.5f, 0.1f);
        }

        public static void Explosion(Vector3 pos, Color c, float scale)
        {
            Burst(pos, Color.white, c, Mathf.RoundToInt(40 * scale), new Vector2(4f, 14f) * Mathf.Sqrt(scale), new Vector2(0.05f, 0.14f), new Vector2(0.3f, 0.8f), 1.2f, true, 0.3f * scale);
            Burst(pos, c, new Color(1f, 0.9f, 0.7f), 14, new Vector2(1f, 4f), new Vector2(0.5f, 1.1f) * scale, new Vector2(0.18f, 0.4f), 0f, false, 0.3f * scale);
            Burst(pos, new Color(0.12f, 0.1f, 0.14f, 0.65f), new Color(0.05f, 0.04f, 0.06f, 0.45f), 12, new Vector2(0.5f, 2f), new Vector2(0.9f, 1.8f) * scale, new Vector2(0.8f, 1.4f), -0.08f, false, 0.4f * scale);
            Vector3 ground = new Vector3(pos.x, 0f, pos.z);
            Shockwave(ground, 2.6f * scale, c * 2f, 0.4f);
        }

        public static void SpawnPortal(Vector3 pos, Color color, float size)
        {
            Vector3 g = new Vector3(pos.x, 0f, pos.z);
            Shockwave(g, 2.2f * size, color * 2.5f, 0.85f);
            var tele = Telegraph(g, 1.2f * size, color * 1.6f);
            tele.SetProgress(1f);
            tele.Kill(0.9f);
            Burst(g + Vector3.up * 0.1f, color, Color.white, 30, new Vector2(3f, 8f), new Vector2(0.05f, 0.12f), new Vector2(0.4f, 0.9f), -0.2f, true, 0.6f * size, Vector3.up, 12f);
            LightFlash(g + Vector3.up, color, 5f, 8f, 0.8f);

            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var col = beam.GetComponent<Collider>();
            if (col) Object.DestroyImmediate(col);
            beam.name = "FX_Beam";
            Adopt(beam);
            beam.transform.position = g + Vector3.up * (3f * size);
            beam.transform.localScale = new Vector3(1.1f * size, 3f * size, 1.1f * size);
            var mr = beam.GetComponent<MeshRenderer>();
            mr.sharedMaterial = flat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            beam.AddComponent<BeamFX>().Init(color * 1.8f, 0.8f);
        }

        // ---------------------------------------------------------------- shapes

        public static RingFX Shockwave(Vector3 pos, float radius, Color color, float duration)
        {
            var go = NewObject("FX_Ring");
            go.transform.position = new Vector3(pos.x, 0.05f, pos.z);
            go.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = flat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var r = go.AddComponent<RingFX>();
            r.Init(radius, color, duration);
            return r;
        }

        public static TelegraphFX Telegraph(Vector3 pos, float radius, Color color)
        {
            var go = NewObject("FX_Telegraph");
            go.transform.position = new Vector3(pos.x, 0.03f, pos.z);
            var t = go.AddComponent<TelegraphFX>();
            t.Init(radius, color);
            return t;
        }

        public static void LightFlash(Vector3 pos, Color c, float intensity, float range, float duration)
        {
            var go = NewObject("FX_Light");
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            go.AddComponent<LightFlashFX>().Init(l, intensity, duration);
        }

        public static void DamageNumber(Vector3 pos, float dmg, bool crit)
        {
            var go = NewObject("FX_Damage");
            go.transform.position = pos;
            var t = go.AddComponent<TextMeshPro>();
            if (dmgTextMat == null && t.font != null)
            {
                dmgTextMat = new Material(t.font.material);
                dmgTextMat.EnableKeyword(ShaderUtilities.Keyword_Outline);
                dmgTextMat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.28f);
                dmgTextMat.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
                dmgTextMat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.15f);
            }
            if (dmgTextMat) t.fontSharedMaterial = dmgTextMat;
            int v = Mathf.Max(1, Mathf.RoundToInt(dmg));
            t.text = crit ? v + "!" : v.ToString();
            t.fontSize = crit ? 7.5f : 5f;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.color = crit ? new Color(1f, 0.82f, 0.2f) : new Color(1f, 1f, 1f);
            t.rectTransform.sizeDelta = new Vector2(4f, 1.5f);
            t.sortingOrder = 100;
            go.AddComponent<FloatingTextFX>().Init(crit);
        }

        public static void AfterImage(SkinnedMeshRenderer smr, Color c, float life = 0.3f)
        {
            var mesh = new Mesh { name = "Ghost" };
            smr.BakeMesh(mesh, true);
            var go = NewObject("FX_Ghost");
            go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = flat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.AddComponent<GhostFX>().Init(mesh, c, life);
        }

        public static void Debris(Vector3 pos, Material a, Material b, int count, float scale, Vector3 bias)
        {
            for (int i = 0; i < count; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = c.GetComponent<Collider>();
                if (col) Object.DestroyImmediate(col);
                c.name = "FX_Debris";
                Adopt(c);
                float s = Random.Range(0.07f, 0.2f) * scale;
                c.transform.position = pos + Random.insideUnitSphere * (0.3f * scale);
                c.transform.rotation = Random.rotation;
                c.transform.localScale = new Vector3(s * Random.Range(0.6f, 1.6f), s * Random.Range(0.5f, 1.2f), s * Random.Range(0.6f, 1.6f));
                var mr = c.GetComponent<MeshRenderer>();
                mr.sharedMaterial = i % 3 == 0 ? b : a;
                Vector3 v = (Random.insideUnitSphere + Vector3.up * 1.3f + bias * 0.7f).normalized * (Random.Range(4f, 10f) * Mathf.Sqrt(scale));
                c.AddComponent<DebrisFX>().Init(v, Random.insideUnitSphere * 900f, Random.Range(1.3f, 2.3f), s);
            }
        }

        // ---------------------------------------------------------------- ambient (also used by the editor scene builder)

        public static ParticleSystem Embers(Transform parent, Material mat, float radius)
        {
            var go = new GameObject("Embers");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 5f;
            main.playOnAwake = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.15f), new Color(0.7f, 0.3f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 700;
            var em = ps.emission;
            em.rateOverTime = 70f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(radius * 2f, 0.2f, radius * 2f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            vel.y = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.35f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        public static ParticleSystem Fire(Transform parent, Material mat, Vector3 localPos, float size)
        {
            var go = new GameObject("Fire");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.playOnAwake = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f * size, 2.4f * size);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f * size, 0.65f * size);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f), new Color(1f, 0.4f, 0.1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            var em = ps.emission;
            em.rateOverTime = 45f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 10f;
            sh.radius = 0.15f * size;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.45f), new GradientColorKey(new Color(0.6f, 0.1f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.15f)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // ---------------------------------------------------------------- meshes

        public static Mesh BuildRing(float inner, float outer, int seg)
        {
            // three vertex rings: transparent inner edge, bright middle, transparent outer edge -> soft glowing band
            var mesh = new Mesh { name = "FX_Ring" };
            var v = new Vector3[seg * 3];
            var c = new Color[seg * 3];
            var uv = new Vector2[seg * 3];
            float mid = (inner + outer) * 0.5f;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 3] = d * inner; v[i * 3 + 1] = d * mid; v[i * 3 + 2] = d * outer;
                c[i * 3] = new Color(1f, 1f, 1f, 0f); c[i * 3 + 1] = Color.white; c[i * 3 + 2] = new Color(1f, 1f, 1f, 0f);
                uv[i * 3] = new Vector2(i / (float)seg, 0f); uv[i * 3 + 1] = new Vector2(i / (float)seg, 0.5f); uv[i * 3 + 2] = new Vector2(i / (float)seg, 1f);
            }
            var t = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                for (int k = 0; k < 2; k++)
                {
                    int a0 = i * 3 + k, a1 = i * 3 + k + 1, b0 = j * 3 + k, b1 = j * 3 + k + 1;
                    t.Add(a0); t.Add(b0); t.Add(a1);
                    t.Add(a1); t.Add(b0); t.Add(b1);
                }
            }
            mesh.vertices = v;
            mesh.colors = c;
            mesh.uv = uv;
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        public static Mesh BuildDisc(int seg)
        {
            var mesh = new Mesh { name = "FX_Disc" };
            var v = new Vector3[seg + 1];
            var c = new Color[seg + 1];
            v[0] = Vector3.zero;
            c[0] = new Color(1f, 1f, 1f, 0.35f);
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                v[i + 1] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                c[i + 1] = new Color(1f, 1f, 1f, 0.85f);
            }
            var t = new int[seg * 3];
            for (int i = 0; i < seg; i++)
            {
                t[i * 3] = 0;
                t[i * 3 + 1] = (i + 1) % seg + 1;
                t[i * 3 + 2] = i + 1;
            }
            mesh.vertices = v;
            mesh.colors = c;
            mesh.triangles = t;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        internal static void SetColor(Renderer r, Color c)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(mpb);
        }
    }

    // -------------------------------------------------------------------- small FX components

    public class RingFX : MonoBehaviour
    {
        float radius, duration, age;
        Color color;
        MeshRenderer mr;

        public void Init(float r, Color c, float d)
        {
            radius = r; color = c; duration = d;
            mr = GetComponent<MeshRenderer>();
            transform.localScale = Vector3.one * 0.01f;
            FX.SetColor(mr, c);
        }

        void Update()
        {
            age += Time.deltaTime;
            float u = Mathf.Clamp01(age / duration);
            float s = radius * (1f - Mathf.Pow(1f - u, 3f));
            transform.localScale = new Vector3(s, 1f, s);
            var c = color;
            c.a = (1f - u) * (1f - u);
            FX.SetColor(mr, c);
            if (u >= 1f) Destroy(gameObject);
        }
    }

    public class TelegraphFX : MonoBehaviour
    {
        MeshRenderer fill, edge;
        Color color;
        float radius, progress, killTimer = -1f, killDur = 0.15f, fade = 1f;

        public void Init(float r, Color c)
        {
            radius = r; color = c;
            fill = MakePart("Fill", FX.DiscMesh, 0f);
            edge = MakePart("Edge", FX.ThinRingMesh, 0.01f);
            edge.transform.localScale = new Vector3(r, 1f, r);
            SetProgress(0f);
        }

        MeshRenderer MakePart(string n, Mesh m, float y)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = FX.FlatMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        public void SetProgress(float p)
        {
            progress = Mathf.Clamp01(p);
            float s = Mathf.Max(0.01f, radius * progress);
            fill.transform.localScale = new Vector3(s, 1f, s);
        }

        public void Kill(float duration = 0.15f)
        {
            if (killTimer >= 0f) return;
            killTimer = 0f;
            killDur = duration;
        }

        void Update()
        {
            if (killTimer >= 0f)
            {
                killTimer += Time.deltaTime;
                fade = 1f - Mathf.Clamp01(killTimer / killDur);
                if (fade <= 0f) { Destroy(gameObject); return; }
            }
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 18f);
            var f = color; f.a = 0.32f * fade;
            var e = color * (1.2f * pulse); e.a = 0.95f * fade;
            FX.SetColor(fill, f);
            FX.SetColor(edge, e);
        }
    }

    public class LightFlashFX : MonoBehaviour
    {
        Light l;
        float peak, duration, age;

        public void Init(Light light, float intensity, float d) { l = light; peak = intensity; duration = d; }

        void Update()
        {
            age += Time.deltaTime;
            float u = Mathf.Clamp01(age / duration);
            l.intensity = peak * (1f - u) * (1f - u);
            if (u >= 1f) Destroy(gameObject);
        }
    }

    public class FloatingTextFX : MonoBehaviour
    {
        TextMeshPro text;
        float age, life;
        Vector3 vel;
        Color baseColor;
        bool crit;

        public void Init(bool isCrit)
        {
            crit = isCrit;
            text = GetComponent<TextMeshPro>();
            baseColor = text.color;
            life = crit ? 1.1f : 0.8f;
            vel = new Vector3(Random.Range(-0.6f, 0.6f), crit ? 2.6f : 2.1f, 0f);
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            age += dt;
            var cam = Camera.main;
            if (cam) transform.rotation = cam.transform.rotation;
            transform.position += (cam ? cam.transform.rotation * vel : vel) * dt;
            vel *= Mathf.Exp(-3.5f * dt);
            float pop = age < 0.1f ? Mathf.Lerp(crit ? 2.2f : 1.7f, 1f, age / 0.1f) : 1f;
            transform.localScale = Vector3.one * pop;
            float u = age / life;
            var c = baseColor;
            c.a = u < 0.6f ? 1f : 1f - (u - 0.6f) / 0.4f;
            text.color = c;
            if (u >= 1f) Destroy(gameObject);
        }
    }

    public class GhostFX : MonoBehaviour
    {
        Mesh mesh;
        Color color;
        float life, age;
        MeshRenderer mr;

        public void Init(Mesh m, Color c, float l)
        {
            mesh = m; color = c; life = l;
            mr = GetComponent<MeshRenderer>();
            FX.SetColor(mr, c);
        }

        void Update()
        {
            age += Time.deltaTime;
            float u = Mathf.Clamp01(age / life);
            var c = color;
            c.a = color.a * (1f - u) * (1f - u);
            FX.SetColor(mr, c);
            if (u >= 1f) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (mesh) Destroy(mesh);
        }
    }

    public class DebrisFX : MonoBehaviour
    {
        Vector3 vel, angVel, baseScale;
        float life, age, half;

        public void Init(Vector3 v, Vector3 av, float l, float size)
        {
            vel = v; angVel = av; life = l; half = size * 0.5f;
            baseScale = transform.localScale;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            vel.y -= 24f * dt;
            var p = transform.position + vel * dt;
            if (p.y < half)
            {
                p.y = half;
                if (vel.y < 0f) vel.y = -vel.y * 0.35f;
                vel.x *= 0.6f; vel.z *= 0.6f;
                angVel *= 0.6f;
            }
            transform.position = p;
            transform.Rotate(angVel * dt, Space.World);
            float shrink = Mathf.Clamp01((life - age) / 0.4f);
            transform.localScale = baseScale * shrink;
            if (age >= life) Destroy(gameObject);
        }
    }

    public class BeamFX : MonoBehaviour
    {
        Color color;
        float life, age;
        Vector3 baseScale;
        MeshRenderer mr;

        public void Init(Color c, float l)
        {
            color = c; life = l;
            mr = GetComponent<MeshRenderer>();
            baseScale = transform.localScale;
        }

        void Update()
        {
            age += Time.deltaTime;
            float u = Mathf.Clamp01(age / life);
            float w = Mathf.Lerp(1f, 0.05f, u * u);
            transform.localScale = new Vector3(baseScale.x * w, baseScale.y * (1f + u * 0.3f), baseScale.z * w);
            var c = color;
            c.a = 0.55f * (1f - u);
            FX.SetColor(mr, c);
            if (u >= 1f) Destroy(gameObject);
        }
    }
}
