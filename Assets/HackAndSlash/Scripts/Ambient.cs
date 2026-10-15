using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    /// <summary>Set dressing that needs code: neon text, searchlights, traffic lights, the Construct's armchairs, digital rain.</summary>
    public static class Ambient
    {
        static readonly Dictionary<Color, Material> neonText = new Dictionary<Color, Material>();
        static Mesh searchBeam;
        static Material rainInstance;

        public static TextMeshPro NeonText(Transform parent, Vector3 pos, float yaw, float height, string text, Color color)
        {
            var go = new GameObject("Neon_" + text.Replace("\n", ""));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw + 180f, 0f));
            var t = go.AddComponent<TextMeshPro>();
            t.text = text;
            t.fontSize = height * 10f;
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.characterSpacing = 8f;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(height * text.Length * 1.2f + 4f, height * 1.4f * (1 + text.Split('\n').Length));
            t.color = Color.white;
            if (t.font != null)
            {
                if (!neonText.TryGetValue(color, out var m) || !m)
                {
                    m = new Material(t.font.material) { name = "NeonText_" + ColorUtility.ToHtmlStringRGB(color) };
                    m.SetColor(ShaderUtilities.ID_FaceColor, color * 3.2f + new Color(0.4f, 0.4f, 0.4f, 1f));
                    m.EnableKeyword(ShaderUtilities.Keyword_Glow);
                    m.SetColor(ShaderUtilities.ID_GlowColor, new Color(color.r, color.g, color.b, 0.7f));
                    m.SetFloat(ShaderUtilities.ID_GlowPower, 0.7f);
                    m.SetFloat(ShaderUtilities.ID_GlowOuter, 0.45f);
                    neonText[color] = m;
                }
                t.fontSharedMaterial = m;
            }
            go.AddComponent<NeonBuzz>();
            return t;
        }

        public static void Searchlight(Transform parent, GameManager gm, PropSpec p)
        {
            if (!gm || !gm.beamMaterial) return;
            if (!searchBeam) searchBeam = MeshKit.Cone(1.2f, 22f, 420f, 24, "Searchbeam");
            var s = new GameObject("Searchlight").transform;
            s.SetParent(parent, false);
            s.position = p.pos;
            s.rotation = Quaternion.Euler(-70f - (p.seed % 12), p.yaw, 0f);
            var beam = new GameObject("Beam");
            beam.transform.SetParent(s, false);
            beam.AddComponent<MeshFilter>().sharedMesh = searchBeam;
            var mr = beam.AddComponent<MeshRenderer>();
            mr.sharedMaterial = gm.beamMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var sw = s.gameObject.AddComponent<Sweeper>();
            sw.yawAmplitude = 25f + (p.seed % 25);
            sw.yawSpeed = 0.03f + (p.seed % 5) * 0.01f;
            sw.pitchAmplitude = 5f + (p.seed % 7);
        }

        public static void TrafficLight(Transform parent, PropSpec p)
        {
            var root = new GameObject("TrafficLight").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(p.pos, Quaternion.Euler(0f, p.yaw, 0f));
            var g = new Geo();
            g.Cylinder(Vector3.zero, 0.14f, 0.12f, 6.4f, 10, Vector2.one, true);
            g.Strut(new Vector3(0f, 6.2f, 0f), new Vector3(0f, 6.2f, 5.5f), 0.12f, Vector2.one);
            g.Box(new Vector3(0f, 5.5f, 5.3f), new Vector3(0.45f, 1.3f, 0.4f), Quaternion.identity, Vector2.one);
            var body = new GameObject("Body");
            body.transform.SetParent(root, false);
            var mesh = CityBuilder.ToMesh(g, "TrafficLight");
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            body.AddComponent<MeshOwner>().mesh = mesh;
            body.AddComponent<MeshRenderer>().sharedMaterial = CityBuilder.Resolve(CityGen.Metal);
            var lamps = new Renderer[3];
            for (int i = 0; i < 3; i++)
            {
                var l = Weapons.Part(PrimitiveType.Sphere, root, new Vector3(0f, 5.9f - i * 0.4f, 5.08f), new Vector3(0.24f, 0.24f, 0.08f), FX.Emissive(i == 0 ? new Color(1f, 0.1f, 0.05f) : i == 1 ? new Color(1f, 0.55f, 0.05f) : new Color(0.1f, 1f, 0.4f), 3.5f), default, false);
                lamps[i] = l.GetComponent<Renderer>();
            }
            var light = new GameObject("Amber").AddComponent<Light>();
            light.transform.SetParent(root, false);
            light.transform.localPosition = new Vector3(0f, 5.5f, 4.6f);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.1f);
            light.range = 9f;
            light.shadows = LightShadows.None;
            var b = root.gameObject.AddComponent<TrafficBlink>();
            b.lamps = lamps;
            b.amber = light;
        }

        public static void Chairs(Transform parent, PropSpec p)
        {
            var root = new GameObject("ConstructChairs").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(p.pos, Quaternion.Euler(0f, p.yaw, 0f));
            var leather = new Geo();
            for (int side = -1; side <= 1; side += 2)
            {
                var r = Quaternion.Euler(0f, side * 35f, 0f);
                Vector3 c = new Vector3(side * 1.6f, 0f, 0f);
                leather.Box(c + r * new Vector3(0f, 0.25f, 0f), new Vector3(1f, 0.5f, 0.95f), r, Vector2.one);
                leather.Box(c + r * new Vector3(0f, 0.62f, 0.05f), new Vector3(0.8f, 0.22f, 0.8f), r, Vector2.one);
                leather.Box(c + r * new Vector3(0f, 1.05f, 0.42f), new Vector3(1f, 0.95f, 0.22f), r, Vector2.one);
                leather.Box(c + r * new Vector3(-0.45f, 0.82f, 0f), new Vector3(0.18f, 0.35f, 0.95f), r, Vector2.one);
                leather.Box(c + r * new Vector3(0.45f, 0.82f, 0f), new Vector3(0.18f, 0.35f, 0.95f), r, Vector2.one);
            }
            AddMesh(root, leather, CityBuilder.Resolve(CityGen.Leather), "Chairs");
            var tv = new Geo();
            tv.Box(new Vector3(0f, 0.35f, 1.4f), new Vector3(0.8f, 0.7f, 0.6f), Quaternion.identity, Vector2.one);
            tv.Box(new Vector3(0f, 1.05f, 1.45f), new Vector3(0.9f, 0.7f, 0.65f), Quaternion.identity, Vector2.one);
            AddMesh(root, tv, CityBuilder.Resolve(CityGen.Dark), "TV");
            var screen = new Geo();
            screen.Card(new Vector3(0f, 1.05f, 1.115f), 0.66f, 0.5f, Vector3.back, false, Vector2.zero, Vector2.one);
            var sm = AddMesh(root, screen, CityBuilder.Resolve(CityGen.Glow(new Color(0.6f, 1f, 0.7f), 1.8f)), "Screen");
            sm.gameObject.AddComponent<NeonBuzz>();
        }

        public static GameObject RainCurtain(Transform parent, GameManager gm, PropSpec p)
        {
            if (!gm || !gm.rainMaterial) return null;
            var g = new Geo();
            g.Card(new Vector3(0f, p.extra.y * 0.5f, 0f), p.extra.x, p.extra.y, Vector3.back, true, Vector2.zero, new Vector2(p.extra.x / 2.5f, p.extra.y / 2.5f));
            var root = new GameObject("DigitalRain");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(p.pos, Quaternion.Euler(0f, p.yaw, 0f));
            // one runtime copy carries the generated glyph atlas (the asset stays untouched)
            if (!rainInstance) rainInstance = new Material(gm.rainMaterial) { name = "DigitalRain_Runtime" };
            var glyphs = TexLibrary.Tex("Glyphs");
            if (glyphs && rainInstance.GetTexture("_GlyphTex") != glyphs) rainInstance.SetTexture("_GlyphTex", glyphs);
            var curtain = AddMesh(root.transform, g, rainInstance, "Curtain", false);
            var mpb = new MaterialPropertyBlock();
            mpb.SetFloat("_Speed", 0.7f + Mathf.Abs(p.seed % 7) * 0.1f);
            curtain.SetPropertyBlock(mpb);
            return root;
        }

        public static MeshRenderer AddMesh(Transform parent, Geo g, Material m, string name, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = CityBuilder.ToMesh(g, name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshOwner>().mesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return mr;
        }

        public static void Clear()
        {
            foreach (var m in neonText.Values) if (m) Object.Destroy(m);
            neonText.Clear();
            if (rainInstance) Object.Destroy(rainInstance);
            if (searchBeam) Object.Destroy(searchBeam);
        }
    }

    /// <summary>Neon text that occasionally stutters off, like every sign in Meridian.</summary>
    public class NeonBuzz : MonoBehaviour
    {
        Renderer r;
        float glitch;
        void Awake() => r = GetComponent<Renderer>();
        void Update()
        {
            if (!r) return;
            glitch -= Time.deltaTime;
            if (glitch <= 0f && Random.value < 0.0015f) glitch = Random.Range(0.05f, 0.4f);
            r.enabled = glitch <= 0f || Random.value < 0.4f;
        }
    }

    /// <summary>The intersection's traffic light: blinking amber for eleven years. "Still working."</summary>
    public class TrafficBlink : MonoBehaviour
    {
        public Renderer[] lamps;
        public Light amber;
        void Update()
        {
            bool on = Mathf.Repeat(Time.time, 1.1f) < 0.55f;
            if (lamps != null)
            {
                if (lamps[0]) lamps[0].enabled = false;
                if (lamps[1]) lamps[1].enabled = on;
                if (lamps[2]) lamps[2].enabled = false;
            }
            if (amber) amber.intensity = on ? 6f : 0f;
        }
    }

    /// <summary>A giant translucent Associate standing over the city (Blade Runner 2049), idling, glitching.</summary>
    public class HoloGiant : MonoBehaviour
    {
        Animator anim;
        Transform model;
        Material mat;
        float baseYaw, seed, glitch;
        Vector3 baseScale;

        public static void Create(Transform parent, GameManager gm, PropSpec p)
        {
            if (!gm || !gm.robotModel || !gm.holoMaterial) return;
            var go = new GameObject("HoloGiant");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(p.pos, Quaternion.Euler(0f, p.yaw, 0f));
            var h = go.AddComponent<HoloGiant>();
            var m = Instantiate(gm.robotModel, go.transform);
            h.model = m.transform;
            m.transform.localScale = Vector3.one * p.scale;
            h.baseScale = m.transform.localScale;
            h.anim = m.GetComponent<Animator>();
            if (h.anim)
            {
                h.anim.runtimeAnimatorController = gm.characterController;
                h.anim.applyRootMotion = false;
                h.anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }
            h.mat = new Material(gm.holoMaterial) { name = "Holo_Giant" };
            h.mat.SetColor("_Color", p.color * 1.6f);
            var noise = TexLibrary.Tex("Holo");
            if (noise) h.mat.SetTexture("_NoiseTex", noise);
            foreach (var r in m.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = h.mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            var l = new GameObject("HoloLight").AddComponent<Light>();
            l.transform.SetParent(go.transform, false);
            l.transform.localPosition = new Vector3(0f, p.scale * 0.9f, p.scale * 0.4f);
            l.type = LightType.Point;
            l.color = p.color;
            l.range = p.scale * 2.2f;
            l.intensity = 60f;
            l.shadows = LightShadows.None;
            h.baseYaw = p.yaw;
            h.seed = p.seed % 100;
        }

        void Update()
        {
            if (anim)
            {
                anim.SetFloat("Speed", 0f);
                anim.SetFloat("MotionSpeed", 1f);
                anim.SetBool("Grounded", true);
            }
            float t = Time.time + seed;
            transform.rotation = Quaternion.Euler(0f, baseYaw + Mathf.Sin(t * 0.07f) * 25f, 0f);
            glitch -= Time.deltaTime;
            if (glitch <= 0f && Random.value < 0.004f) glitch = Random.Range(0.08f, 0.35f);
            if (model) model.localScale = glitch > 0f ? Vector3.Scale(baseScale, new Vector3(Random.Range(0.85f, 1.25f), 1f, 1f)) : baseScale;
            if (mat) mat.SetFloat("_Glitch", glitch > 0f ? 1f : 0f);
        }

        void OnDestroy() { if (mat) Destroy(mat); }
    }

    /// <summary>
    /// A spinner on a looping flight path between the towers (police ones strobe red/blue and sweep a searchlight),
    /// or a crashed one smouldering on a terrace.
    /// </summary>
    public class FlyingCar : MonoBehaviour
    {
        float radius, ellipse, dir, angle, speed, altitude, seed;
        bool police, wreck;
        Light strobe;

        public static void Create(Transform parent, PropSpec p)
        {
            var go = new GameObject(p.text == "wreck" ? "CrashedSpinner" : "Spinner");
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<FlyingCar>();
            c.police = p.color.r > 0.9f && p.color.g < 0.5f;
            c.wreck = p.text == "wreck";
            c.radius = p.extra.x; c.ellipse = p.extra.y; c.dir = p.extra.z;
            c.angle = p.yaw * Mathf.Deg2Rad;
            c.altitude = p.pos.y;
            c.speed = c.police ? 26f : Random.Range(14f, 24f);
            c.seed = p.seed % 1000;
            c.BuildBody();
            if (c.wreck)
            {
                go.transform.SetPositionAndRotation(p.pos + Vector3.up * 0.6f, Quaternion.Euler(-12f, p.yaw, 68f));
                c.enabled = false;
            }
            else c.Update();
        }

        void BuildBody()
        {
            var body = new Geo();
            Color paint = police ? new Color(0.08f, 0.1f, 0.16f) : Color.HSVToRGB(Random.value, 0.4f, 0.35f);
            body.Box(new Vector3(0f, 0f, 0f), new Vector3(2.2f, 0.8f, 5.2f), Quaternion.identity, Vector2.one);
            body.Box(new Vector3(0f, -0.05f, 2.75f), new Vector3(2f, 0.55f, 1f), Quaternion.Euler(14f, 0f, 0f), Vector2.one);
            body.Box(new Vector3(1.35f, -0.2f, -0.6f), new Vector3(0.6f, 0.5f, 3.4f), Quaternion.identity, Vector2.one);
            body.Box(new Vector3(-1.35f, -0.2f, -0.6f), new Vector3(0.6f, 0.5f, 3.4f), Quaternion.identity, Vector2.one);
            if (police) body.Box(new Vector3(0f, 0.95f, -0.4f), new Vector3(1.6f, 0.1f, 2.4f), Quaternion.identity, Vector2.one);
            Ambient.AddMesh(transform, body, wreck ? CityBuilder.Resolve(CityGen.Tint("Car", paint * 0.4f)) : CityBuilder.Resolve(CityGen.Tint("Car", paint)), "Body");
            var glass = new Geo();
            glass.Box(new Vector3(0f, 0.65f, 0.4f), new Vector3(1.8f, 0.55f, 2.2f), Quaternion.identity, Vector2.one);
            Ambient.AddMesh(transform, glass, CityBuilder.Resolve(CityGen.Glass), "Canopy");
            if (wreck) return;
            var glow = new Geo();
            glow.Box(new Vector3(0f, 0.05f, 3.25f), new Vector3(1.6f, 0.08f, 0.05f), Quaternion.identity, Vector2.one);
            Ambient.AddMesh(transform, glow, CityBuilder.Resolve(CityGen.Glow(new Color(1f, 0.95f, 0.85f), 6f)), "Headlights", false);
            var tail = new Geo();
            tail.Box(new Vector3(0f, 0.1f, -2.62f), new Vector3(2f, 0.08f, 0.05f), Quaternion.identity, Vector2.one);
            Ambient.AddMesh(transform, tail, CityBuilder.Resolve(CityGen.Glow(new Color(1f, 0.08f, 0.05f), 5f)), "Tail", false);
            var thrust = new Geo();
            thrust.Box(new Vector3(1.35f, -0.48f, -0.6f), new Vector3(0.4f, 0.04f, 2.6f), Quaternion.identity, Vector2.one);
            thrust.Box(new Vector3(-1.35f, -0.48f, -0.6f), new Vector3(0.4f, 0.04f, 2.6f), Quaternion.identity, Vector2.one);
            Ambient.AddMesh(transform, thrust, CityBuilder.Resolve(CityGen.Glow(new Color(1f, 0.5f, 0.2f), 4f)), "Thrusters", false);
            if (police)
            {
                var bar = new Geo();
                bar.Box(new Vector3(-0.45f, 1.06f, -0.4f), new Vector3(0.7f, 0.12f, 0.3f), Quaternion.identity, Vector2.one);
                Ambient.AddMesh(transform, bar, CityBuilder.Resolve(CityGen.Glow(new Color(1f, 0.1f, 0.08f), 6f)), "Red", false);
                var bar2 = new Geo();
                bar2.Box(new Vector3(0.45f, 1.06f, -0.4f), new Vector3(0.7f, 0.12f, 0.3f), Quaternion.identity, Vector2.one);
                Ambient.AddMesh(transform, bar2, CityBuilder.Resolve(CityGen.Glow(new Color(0.15f, 0.3f, 1f), 6f)), "Blue", false);
                strobe = new GameObject("Strobe").AddComponent<Light>();
                strobe.transform.SetParent(transform, false);
                strobe.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                strobe.type = LightType.Point;
                strobe.range = 22f;
                strobe.intensity = 30f;
                strobe.shadows = LightShadows.None;
                strobe.gameObject.AddComponent<LightFlicker>().mode = 4;
                var gm = GameManager.I;
                if (gm && gm.beamMaterial)
                {
                    var beam = new GameObject("SpotBeam");
                    beam.transform.SetParent(transform, false);
                    beam.transform.localPosition = new Vector3(0f, -0.4f, 2f);
                    beam.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
                    beam.AddComponent<MeshFilter>().sharedMesh = SharedBeam;
                    var mr = beam.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = gm.beamMaterial;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }

        static Mesh sharedBeam;
        static Mesh SharedBeam => sharedBeam ? sharedBeam : (sharedBeam = MeshKit.Cone(0.25f, 6f, 70f, 16, "SpinnerBeam"));

        void Update()
        {
            float dt = Time.deltaTime;
            angle += dir * speed / Mathf.Max(10f, radius) * dt;
            float t = Time.time * 0.25f + seed;
            Vector3 pos = new Vector3(Mathf.Cos(angle) * radius, altitude + Mathf.Sin(t) * 4f, Mathf.Sin(angle) * radius * ellipse);
            Vector3 vel = new Vector3(-Mathf.Sin(angle) * radius, Mathf.Cos(t) * 1f, Mathf.Cos(angle) * radius * ellipse) * dir;
            if (vel.sqrMagnitude < 1e-4f) vel = Vector3.forward;
            var look = Quaternion.LookRotation(vel.normalized);
            transform.SetPositionAndRotation(pos, look * Quaternion.Euler(0f, 0f, -dir * 14f));
        }
    }

    /// <summary>Decommissioned Associates in ponchos huddled around fires. HALCYON calls them strays.</summary>
    public class Survivor : MonoBehaviour
    {
        Animator anim;
        Transform head;
        float seed, look;

        public static void Create(Transform parent, GameManager gm, PropSpec p)
        {
            if (!gm || !gm.robotModel) return;
            var go = new GameObject("Stray");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(p.pos, Quaternion.Euler(0f, p.yaw, 0f));
            var s = go.AddComponent<Survivor>();
            s.seed = p.seed % 100;
            var m = Instantiate(gm.robotModel, go.transform);
            m.transform.localScale = Vector3.one * p.scale;
            s.anim = m.GetComponent<Animator>();
            if (s.anim)
            {
                s.anim.runtimeAnimatorController = gm.characterController;
                s.anim.applyRootMotion = false;
                s.anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                s.head = s.anim.GetBoneTransform(HumanBodyBones.Head);
            }
            var body = m.GetComponentInChildren<SkinnedMeshRenderer>();
            if (body && gm.suitMaterial) body.sharedMaterial = gm.suitMaterial;
            // poncho and hood (cloth), a dim amber visor glowing under the hood
            var poncho = new Geo();
            poncho.Cylinder(new Vector3(0f, 0.42f, 0f), 0.62f, 0.2f, 1.08f, 14, new Vector2(1.2f, 1.2f), false);
            var hood = new Geo();
            hood.Cylinder(new Vector3(0f, 1.45f, -0.02f), 0.24f, 0.1f, 0.5f, 12, new Vector2(1.2f, 1.2f), true);
            var cloth = TexLibrary.Mat("Cloth");
            var pr = Ambient.AddMesh(m.transform, poncho, cloth, "Poncho");
            Transform hips = s.anim ? s.anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips) pr.transform.SetParent(hips, true);
            var hr = Ambient.AddMesh(m.transform, hood, cloth, "Hood");
            if (s.head) hr.transform.SetParent(s.head, true);
            var visor = Weapons.Part(PrimitiveType.Cube, hr.transform, new Vector3(0f, 1.68f, 0.17f), new Vector3(0.14f, 0.03f, 0.02f), FX.Emissive(new Color(1f, 0.55f, 0.15f), 2.5f), default, false);
            visor.name = "Visor";
        }

        void Update()
        {
            if (anim)
            {
                anim.SetFloat("Speed", 0f);
                anim.SetFloat("MotionSpeed", 1f);
                anim.SetBool("Grounded", true);
            }
        }

        void LateUpdate()
        {
            if (head) head.localScale = Vector3.one * 0.62f;
            // glance at the fight now and then
            var gm = GameManager.I;
            if (!head || !gm || !gm.player) return;
            Vector3 to = gm.player.Chest - head.position;
            bool watch = to.sqrMagnitude < 900f && Mathf.Repeat(Time.time * 0.11f + seed, 1f) < 0.45f;
            look = Mathf.MoveTowards(look, watch ? 1f : 0f, Time.deltaTime * 1.5f);
            if (look <= 0.01f) return;
            Vector3 flat = Vector3.ProjectOnPlane(to, Vector3.up);
            float yaw = Mathf.Clamp(Vector3.SignedAngle(transform.forward, flat, Vector3.up), -60f, 60f);
            head.rotation = Quaternion.AngleAxis(yaw * look, Vector3.up) * head.rotation;
        }
    }

    /// <summary>An inert armoured Associate standing guard. Its head follows you. Its body does not.</summary>
    public class Statue : MonoBehaviour
    {
        Animator anim;
        Transform head;
        float freezeAt;
        Quaternion baseLocal;
        bool captured;

        public static void Create(Transform parent, GameManager gm, PropSpec p)
        {
            if (!gm || !gm.robotModel) return;
            var go = new GameObject("Statue");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(p.pos, Quaternion.Euler(0f, p.yaw, 0f));
            var s = go.AddComponent<Statue>();
            var m = Instantiate(gm.robotModel, go.transform);
            s.anim = m.GetComponent<Animator>();
            var body = m.GetComponentInChildren<SkinnedMeshRenderer>();
            if (s.anim)
            {
                s.anim.runtimeAnimatorController = gm.characterController;
                s.anim.applyRootMotion = false;
                s.anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                s.head = s.anim.GetBoneTransform(HumanBodyBones.Head);
                if (body) ArmorKit.Build(s.anim, body, go.transform, ArmorStyle.Bulwark);
            }
            s.freezeAt = Time.time + 0.4f;
        }

        void Update()
        {
            if (!anim) return;
            anim.SetBool("Grounded", true);
            if (Time.time > freezeAt) anim.speed = 0f;
        }

        void LateUpdate()
        {
            var gm = GameManager.I;
            if (!head || !gm || !gm.player) return;
            // absolute, from the frozen pose, so nothing accumulates
            if (!captured) { if (Time.time < freezeAt + 0.1f) return; baseLocal = head.localRotation; captured = true; }
            head.localRotation = baseLocal;
            Vector3 to = gm.player.Chest - head.position;
            Vector3 flat = Vector3.ProjectOnPlane(to, Vector3.up);
            float yaw = Mathf.Clamp(Vector3.SignedAngle(transform.forward, flat, Vector3.up), -70f, 70f);
            head.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * head.rotation;
        }
    }

    /// <summary>A wall clock, stopped at 11:59 for eleven years.</summary>
    public class Clock : MonoBehaviour
    {
        Transform hour, minute;
        float h = 11f, m = 59f, shownH, shownM;

        public static Clock Create(Transform parent, PropSpec p)
        {
            var go = new GameObject("Clock");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(p.pos, Quaternion.Euler(0f, p.yaw, 0f));
            go.transform.localScale = Vector3.one * p.scale;
            var c = go.AddComponent<Clock>();
            var face = new Geo();
            for (int s = 0; s < 48; s++)
            {
                float a0 = s / 48f * Mathf.PI * 2f, a1 = (s + 1) / 48f * Mathf.PI * 2f;
                face.Tri(Vector3.zero, new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f), new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f), Vector3.forward, Vector2.zero, Vector2.zero, Vector2.zero);
            }
            Ambient.AddMesh(go.transform, face, CityBuilder.Resolve(CityGen.Cream), "Face", false);
            var rim = new Geo();
            for (int s = 0; s < 48; s++)
            {
                float a0 = s / 48f * Mathf.PI * 2f, a1 = (s + 1) / 48f * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f), d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                rim.Quad(d0 * 1f + Vector3.forward * 0.02f, d1 * 1f + Vector3.forward * 0.02f, d1 * 1.12f + Vector3.forward * 0.02f, d0 * 1.12f + Vector3.forward * 0.02f, Vector3.forward, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            }
            for (int k = 0; k < 12; k++)
            {
                var r = Quaternion.Euler(0f, 0f, -k * 30f);
                rim.Box(r * new Vector3(0f, 0.86f, 0.03f), new Vector3(k % 3 == 0 ? 0.07f : 0.035f, 0.16f, 0.02f), r, Vector2.one);
            }
            Ambient.AddMesh(go.transform, rim, CityBuilder.Resolve(CityGen.Gold), "Rim", false);
            c.hour = Hand(go.transform, 0.55f, 0.07f, 0.05f);
            c.minute = Hand(go.transform, 0.82f, 0.045f, 0.06f);
            c.shownH = c.h; c.shownM = c.m;
            c.Apply();
            return c;
        }

        static Transform Hand(Transform parent, float len, float w, float z)
        {
            var pivot = new GameObject("Hand").transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = new Vector3(0f, 0f, z);
            var g = new Geo();
            g.Box(new Vector3(0f, len * 0.4f, 0f), new Vector3(w, len, 0.02f), Quaternion.identity, Vector2.one);
            Ambient.AddMesh(pivot, g, CityBuilder.Resolve(CityGen.Dark), "Blade", false);
            return pivot;
        }

        public void SetTime(float hours, float minutes) { h = hours; m = minutes; }

        void Update()
        {
            shownH = Mathf.MoveTowards(shownH, h, Time.unscaledDeltaTime * 0.6f);
            shownM = Mathf.MoveTowards(shownM, m, Time.unscaledDeltaTime * 8f);
            Apply();
        }

        void Apply()
        {
            float hm = (shownH % 12f + shownM / 60f) * 30f;
            if (hour) hour.localRotation = Quaternion.Euler(0f, 0f, -hm);
            if (minute) minute.localRotation = Quaternion.Euler(0f, 0f, -shownM * 6f);
        }
    }

    /// <summary>The Monolith on the Spire (1 : 4 : 9) and the red eye set into it. HALCYON lives here.</summary>
    public class Monolith : MonoBehaviour
    {
        Material eyeMat;
        Light eyeLight, eyeSpot;
        float level = 1f, pulse, targetLevel = 1f;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public Vector3 EyePosition => transform.TransformPoint(new Vector3(0f, 15f, 1.7f));

        public static Monolith Create(Transform parent, PropSpec p)
        {
            var go = new GameObject("Monolith");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(p.pos, Quaternion.Euler(0f, p.yaw, 0f));
            var mono = go.AddComponent<Monolith>();
            var slab = new Geo();
            slab.Box(new Vector3(0f, 13.5f, 0f), new Vector3(12f, 27f, 3f), Quaternion.identity, new Vector2(6f, 6f));
            Ambient.AddMesh(go.transform, slab, CityBuilder.Resolve(CityGen.Mono), "Slab");
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 13.5f, 0f);
            col.size = new Vector3(12f, 27f, 3f);
            go.layer = Layers.Environment;
            // the eye: chrome bezel, black glass, red lens with a hot core (faces local +Z)
            var bezel = new Geo();
            Disc(bezel, new Vector3(0f, 15f, 1.52f), 0f, 2.3f, 48);
            Ambient.AddMesh(go.transform, bezel, CityBuilder.Resolve(CityGen.Chrome), "Bezel", false);
            var black = new Geo();
            Disc(black, new Vector3(0f, 15f, 1.56f), 0f, 1.9f, 48);
            Ambient.AddMesh(go.transform, black, CityBuilder.Resolve(CityGen.Glass), "EyeGlass", false);
            var lens = new Geo();
            Disc(lens, new Vector3(0f, 15f, 1.6f), 0f, 1.2f, 48);
            mono.eyeMat = new Material(FX.Emissive(new Color(1f, 0.05f, 0.02f), 6f)) { name = "HalcyonEye" };
            Ambient.AddMesh(go.transform, lens, mono.eyeMat, "Lens", false);
            var core = new Geo();
            Disc(core, new Vector3(0f, 15f, 1.64f), 0f, 0.28f, 24);
            Ambient.AddMesh(go.transform, core, CityBuilder.Resolve(CityGen.Glow(new Color(1f, 0.85f, 0.4f), 8f)), "Core", false);
            mono.eyeLight = new GameObject("EyeLight").AddComponent<Light>();
            mono.eyeLight.transform.SetParent(go.transform, false);
            mono.eyeLight.transform.localPosition = new Vector3(0f, 15f, 3.5f);
            mono.eyeLight.type = LightType.Point;
            mono.eyeLight.color = new Color(1f, 0.08f, 0.04f);
            mono.eyeLight.range = 40f;
            mono.eyeLight.intensity = 60f;
            mono.eyeLight.shadows = LightShadows.None;
            mono.eyeSpot = new GameObject("EyeSpot").AddComponent<Light>();
            mono.eyeSpot.transform.SetParent(go.transform, false);
            mono.eyeSpot.transform.localPosition = new Vector3(0f, 15f, 2f);
            mono.eyeSpot.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
            mono.eyeSpot.type = LightType.Spot;
            mono.eyeSpot.color = new Color(1f, 0.1f, 0.05f);
            mono.eyeSpot.range = 60f;
            mono.eyeSpot.spotAngle = 70f;
            mono.eyeSpot.intensity = 400f;
            mono.eyeSpot.shadows = LightShadows.Soft;
            return mono;
        }

        static void Disc(Geo g, Vector3 c, float inner, float outer, int seg)
        {
            for (int s = 0; s < seg; s++)
            {
                float a0 = s / (float)seg * Mathf.PI * 2f, a1 = (s + 1) / (float)seg * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f), d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                if (inner <= 0f) g.Tri(c, c + d0 * outer, c + d1 * outer, Vector3.forward, Vector2.zero, Vector2.zero, Vector2.zero);
                else g.Quad(c + d0 * inner, c + d1 * inner, c + d1 * outer, c + d0 * outer, Vector3.forward, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            }
        }

        /// <summary>HALCYON is talking: the eye swells.</summary>
        public void Pulse(float amount) => pulse = Mathf.Max(pulse, amount);

        /// <summary>1 = awake, 0 = dark (the end).</summary>
        public void SetLevel(float l) => targetLevel = l;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            level = Mathf.MoveTowards(level, targetLevel, dt * (targetLevel < level ? 0.25f : 1f));
            pulse = Mathf.MoveTowards(pulse, 0f, dt * 2.5f);
            float breathe = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 1.3f);
            float k = level * (breathe + pulse * 0.8f);
            if (eyeMat) eyeMat.SetColor(EmissionId, new Color(1f, 0.05f, 0.02f) * (6f * k));
            if (eyeLight) eyeLight.intensity = 60f * k;
            if (eyeSpot) eyeSpot.intensity = 400f * k;
        }

        void OnDestroy() { if (eyeMat) Destroy(eyeMat); }
    }

    /// <summary>
    /// THE CUSTODIAN, a commission for the Midnight Gala: an industrial arm in a glass case that leaks dark fluid
    /// and spends its existence scraping the puddle back towards itself. Nobody ever turned it off. It has been
    /// sweeping for eleven years, a little slower every year, and occasionally it stops to look at whoever is watching.
    /// </summary>
    public class Custodian : MonoBehaviour
    {
        Transform turret, upper, fore, wrist, puddle;
        float radius = 0.8f, phase, stateTime, speed = 1f, age, shiver;
        int state; // 0 reach, 1 lower, 2 drag, 3 lift, 4 look
        Vector3 from, to, tip;
        const float ShoulderY = 0.95f, L1 = 1.15f, L2 = 1.05f, Blade = 0.42f;

        public static void Create(Transform parent, PropSpec p)
        {
            var root = new GameObject("TheCustodian").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(p.pos, Quaternion.Euler(0f, p.yaw, 0f));
            var c = root.gameObject.AddComponent<Custodian>();
            var orange = CityBuilder.Resolve(CityGen.Plain(new Color(0.78f, 0.32f, 0.08f), 0.35f, 0.55f));
            var steel = CityBuilder.Resolve(CityGen.Chrome);
            var dark = CityBuilder.Resolve(CityGen.Dark);

            // plinth, glass-case frame (the panes are implied: chrome edges and a low rail), placard
            var frame = new Geo();
            const float half = 1.9f, hgt = 3.1f;
            frame.Box(new Vector3(0f, 0.08f, 0f), new Vector3(half * 2f + 0.3f, 0.16f, half * 2f + 0.3f), Quaternion.identity, Vector2.one);
            for (int i = 0; i < 4; i++)
            {
                var r = Quaternion.Euler(0f, i * 90f, 0f);
                frame.Box(r * new Vector3(half, hgt * 0.5f, half), new Vector3(0.06f, hgt, 0.06f), r, Vector2.one);
                frame.Box(r * new Vector3(0f, hgt, half), new Vector3(half * 2f, 0.06f, 0.06f), r, Vector2.one);
                frame.Box(r * new Vector3(0f, 0.3f, half), new Vector3(half * 2f, 0.05f, 0.05f), r, Vector2.one);
            }
            Ambient.AddMesh(root, frame, steel, "Case");
            var plinth = new Geo();
            plinth.Cylinder(new Vector3(0f, 0.16f, 0f), 0.42f, 0.36f, 0.5f, 16, Vector2.one, true);
            Ambient.AddMesh(root, plinth, dark, "Plinth");
            Ambient.NeonText(root, root.TransformPoint(new Vector3(0f, 0.45f, half + 0.05f)), p.yaw, 0.16f, "THE CUSTODIAN\nMIDNIGHT GALA COMMISSION", new Color(1f, 0.78f, 0.4f));

            // the leak: a glossy black-red disc that grows when nobody scrapes it
            var pg = new Geo();
            pg.Cylinder(Vector3.zero, 1f, 1f, 0.012f, 28, Vector2.one, true);
            var pm = Ambient.AddMesh(root, pg, CityBuilder.Resolve(CityGen.Plain(new Color(0.09f, 0.01f, 0.012f), 0.2f, 0.97f)), "Leak", false);
            c.puddle = pm.transform;
            c.puddle.localPosition = new Vector3(0f, 0.165f, 0.4f);

            // the arm: turret (yaw), shoulder (pitch), elbow (pitch), wrist (pitch) with a squeegee blade
            c.turret = Joint(root, new Vector3(0f, 0.66f, 0f));
            var tg = new Geo();
            tg.Cylinder(Vector3.zero, 0.33f, 0.3f, 0.3f, 14, Vector2.one, true);
            tg.Box(new Vector3(0f, 0.28f, 0f), new Vector3(0.36f, 0.32f, 0.36f), Quaternion.identity, Vector2.one);
            Ambient.AddMesh(c.turret, tg, orange, "Turret");
            c.upper = Joint(c.turret, new Vector3(0f, ShoulderY - 0.66f, 0f));
            var ug = new Geo();
            ug.Box(new Vector3(0f, 0f, L1 * 0.5f), new Vector3(0.22f, 0.26f, L1), Quaternion.identity, Vector2.one);
            ug.Cylinder(new Vector3(-0.16f, 0f, 0f), 0.17f, 0.17f, 0.32f, 12, Vector2.one, true, Quaternion.Euler(0f, 0f, 90f));
            Ambient.AddMesh(c.upper, ug, orange, "UpperArm");
            c.fore = Joint(c.upper, new Vector3(0f, 0f, L1));
            var fg = new Geo();
            fg.Box(new Vector3(0f, 0f, L2 * 0.5f), new Vector3(0.16f, 0.18f, L2), Quaternion.identity, Vector2.one);
            fg.Cylinder(new Vector3(-0.12f, 0f, 0f), 0.13f, 0.13f, 0.24f, 12, Vector2.one, true, Quaternion.Euler(0f, 0f, 90f));
            Ambient.AddMesh(c.fore, fg, orange, "Forearm");
            c.wrist = Joint(c.fore, new Vector3(0f, 0f, L2));
            var wg = new Geo();
            wg.Box(new Vector3(0f, 0f, Blade * 0.45f), new Vector3(0.1f, 0.1f, Blade * 0.9f), Quaternion.identity, Vector2.one);
            wg.Box(new Vector3(0f, 0f, Blade), new Vector3(0.62f, 0.2f, 0.035f), Quaternion.identity, Vector2.one);
            Ambient.AddMesh(c.wrist, wg, steel, "Squeegee");
            c.tip = new Vector3(0f, 0.17f, 1.6f);
            c.from = c.to = c.tip + Vector3.up * 0.5f;
            c.phase = (p.seed % 7) * 0.13f;
        }

        static Transform Joint(Transform parent, Vector3 local)
        {
            var t = new GameObject("Joint").transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            // it gets slower as it ages; the leak never does
            speed = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(age / 900f));
            radius = Mathf.Min(1.45f, radius + dt * 0.035f);
            stateTime += dt * speed;
            switch (state)
            {
                case 0: Move(0.9f, 1); break;
                case 1: Move(0.35f, 2); break;
                case 2:
                    Move(1.1f, 3);
                    radius = Mathf.Max(0.45f, radius - dt * speed * 0.42f);
                    break;
                case 3: Move(0.5f, -1); break;
                case 4: Move(2.6f, 0); break;
            }
            puddle.localScale = new Vector3(radius, 1f, radius);

            // lay the blade on the target with two-bone IK in the turret's vertical plane
            Vector3 aim = tip;
            if (shiver > 0f) { shiver -= dt; aim += new Vector3(Mathf.Sin(Time.time * 37f), 0f, Mathf.Cos(Time.time * 29f)) * 0.03f; }
            float yaw = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg;
            if (state == 4)
            {
                var gm = GameManager.I;
                if (gm && gm.player)
                {
                    Vector3 lp = transform.InverseTransformPoint(gm.player.transform.position);
                    yaw = Mathf.LerpAngle(yaw, Mathf.Atan2(lp.x, lp.z) * Mathf.Rad2Deg, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(stateTime * 2f)));
                }
            }
            turret.localRotation = Quaternion.Euler(0f, yaw, 0f);
            float d = new Vector2(aim.x, aim.z).magnitude;
            float h = aim.y + Blade - ShoulderY;
            float r = Mathf.Clamp(Mathf.Sqrt(d * d + h * h), Mathf.Abs(L1 - L2) + 0.02f, L1 + L2 - 0.02f);
            float a = Mathf.Atan2(h, d) * Mathf.Rad2Deg;
            float s = a + Mathf.Acos(Mathf.Clamp((L1 * L1 + r * r - L2 * L2) / (2f * L1 * r), -1f, 1f)) * Mathf.Rad2Deg;
            float e = Mathf.Acos(Mathf.Clamp((L1 * L1 + L2 * L2 - r * r) / (2f * L1 * L2), -1f, 1f)) * Mathf.Rad2Deg;
            float f = s - (180f - e);
            upper.localRotation = Quaternion.Euler(-s, 0f, 0f);
            fore.localRotation = Quaternion.Euler(180f - e, 0f, 0f);
            float wristElev = state == 4 ? 10f : -90f;
            wrist.localRotation = Quaternion.Euler(-(wristElev - f), 0f, 0f);
        }

        void Move(float duration, int next)
        {
            float u = Mathf.Clamp01(stateTime / duration);
            float k = u * u * (3f - 2f * u);
            tip = Vector3.Lerp(from, to, k);
            if (state == 0 || state == 3) tip += Vector3.up * Mathf.Sin(u * Mathf.PI) * 0.25f;
            if (u < 1f) return;
            stateTime = 0f;
            from = tip;
            Vector3 leak = puddle.localPosition;
            if (next == -1)
            {
                // between strokes it sometimes stops and looks at you, sometimes shudders
                phase += 0.37f;
                if (Mathf.Repeat(phase, 1f) < 0.16f) { state = 4; to = new Vector3(0f, 1.7f, 0.9f); return; }
                if (Mathf.Repeat(phase * 1.7f, 1f) < 0.2f) shiver = 0.6f;
                next = 0;
            }
            state = next;
            switch (state)
            {
                case 0:
                {
                    // reach to the far edge of the leak
                    float ang = (Mathf.Repeat(phase * 2.3f, 1f) - 0.5f) * 140f;
                    Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                    to = new Vector3(leak.x, 0.5f, leak.z) + dir * (radius + 0.25f);
                    to.z = Mathf.Max(to.z, 0.7f);
                    break;
                }
                case 1: to = new Vector3(from.x, 0.17f, from.z); break;
                case 2:
                {
                    // drag it back towards the base
                    Vector3 flat = new Vector3(from.x, 0f, from.z);
                    to = flat.normalized * 0.75f + Vector3.up * 0.17f;
                    break;
                }
                case 3: to = new Vector3(from.x, 0.55f, from.z) + new Vector3(from.x, 0f, from.z).normalized * 0.2f; break;
            }
        }
    }
}
