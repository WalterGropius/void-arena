using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    /// <summary>A built district: its root object plus the bits gameplay and the story need to reach.</summary>
    public sealed class DistrictInstance
    {
        public District district;
        public GameObject root;
        public CityPlan plan;
        public ArenaShape arena;
        public readonly List<Vector3> spawnPoints = new List<Vector3>();
        public readonly List<AnchorSpec> anchors = new List<AnchorSpec>();
        public readonly List<Renderer> beacons = new List<Renderer>();
        public readonly List<GameObject> rainCurtains = new List<GameObject>();
        public readonly List<Clock> clocks = new List<Clock>();
        public readonly List<TMPro.TextMeshPro> clockTexts = new List<TMPro.TextMeshPro>();
        public Monolith monolith;
        public Material wetMaterial;

        public void Destroy()
        {
            if (root) Object.Destroy(root);
            if (wetMaterial) Object.Destroy(wetMaterial);
        }
    }

    /// <summary>Turns a <see cref="CityPlan"/> into GameObjects: combined meshes per material, lights, particles, colliders and props.</summary>
    public static class CityBuilder
    {
        static readonly Dictionary<string, Material> resolved = new Dictionary<string, Material>();

        public static Material Resolve(string key)
        {
            if (resolved.TryGetValue(key, out var m) && m) return m;
            var parts = key.Split(':');
            var inv = CultureInfo.InvariantCulture;
            try
            {
                switch (parts[0])
                {
                    case "Plain":
                        m = FX.Lit(HexColor(parts[1]), float.Parse(parts[2], inv), float.Parse(parts[3], inv));
                        break;
                    case "Glow":
                        m = FX.Emissive(HexColor(parts[1]), float.Parse(parts[2], inv));
                        break;
                    case "Tint":
                        m = TexLibrary.Tinted(parts[1], HexColor(parts[2]));
                        break;
                    default:
                        m = TexLibrary.Mat(key);
                        break;
                }
            }
            catch (System.Exception) { m = FX.Lit(Color.gray, 0f, 0.4f); }
            resolved[key] = m;
            return m;
        }

        /// <summary>Generated materials may be replaced when the library finishes; forget stale lookups.</summary>
        public static void ClearCache() => resolved.Clear();

        public static Color HexColor(string hex)
        {
            int v = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
        }

        public static DistrictInstance Instantiate(CityPlan plan, GameManager gm)
        {
            var inst = new DistrictInstance { district = plan.district, plan = plan, arena = plan.arena };
            var root = new GameObject("District_" + plan.district);
            inst.root = root;
            inst.spawnPoints.AddRange(plan.spawnPoints);
            inst.anchors.AddRange(plan.anchors);

            BuildBatches(plan.geo, root.transform, true);
            BuildBatches(plan.geoNoShadow, root.transform, false);
            if (plan.hasFloor) BuildFloor(plan, root.transform, gm, inst);
            else if (plan.district == District.Rooftop) { } // the scene's roof
            foreach (var c in plan.colliders) Collider(c, root.transform);
            foreach (var l in plan.lights) MakeLight(l, root.transform);
            foreach (var e in plan.emitters) Emitter(e, root.transform, gm);
            foreach (var p in plan.props) Prop(p, root.transform, gm, inst);
            return inst;
        }

        static void BuildBatches(GeoSet set, Transform parent, bool shadows)
        {
            foreach (var kv in set.batches)
            {
                var g = kv.Value;
                if (g.v.Count == 0) continue;
                var mesh = new Mesh { name = "City_" + kv.Key };
                if (g.v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(g.v);
                mesh.SetNormals(g.n);
                mesh.SetUVs(0, g.uv);
                mesh.SetTriangles(g.t, 0);
                MeshKit.FixWinding(mesh);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                var go = new GameObject(kv.Key);
                go.layer = Layers.Environment;
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Resolve(kv.Key);
                mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                go.AddComponent<MeshOwner>().mesh = mesh;
            }
        }

        static void BuildFloor(CityPlan plan, Transform parent, GameManager gm, DistrictInstance inst)
        {
            float size = plan.floorSize;
            Mesh mesh;
            if (size > 0f)
            {
                var g = new Geo();
                // a grid of quads, so the wet overlay and fog interpolate well
                int cells = Mathf.Clamp(Mathf.RoundToInt(size / 10f), 4, 64);
                float cs = size / cells, h = size * 0.5f, t = plan.floorTile;
                for (int z = 0; z < cells; z++)
                for (int x = 0; x < cells; x++)
                {
                    Vector3 a = new Vector3(-h + x * cs, 0f, -h + z * cs);
                    Vector3 b = a + new Vector3(cs, 0f, 0f), c = a + new Vector3(cs, 0f, cs), d = a + new Vector3(0f, 0f, cs);
                    g.Quad(a, d, c, b, Vector3.up, new Vector2(a.x, a.z) / t, new Vector2(d.x, d.z) / t, new Vector2(c.x, c.z) / t, new Vector2(b.x, b.z) / t);
                }
                mesh = ToMesh(g, "City_Floor");
                var floor = new GameObject("Floor");
                floor.layer = Layers.Floor;
                floor.transform.SetParent(parent, false);
                floor.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = floor.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Resolve(plan.floorMaterial ?? CityGen.Asphalt);
                mr.shadowCastingMode = ShadowCastingMode.Off;
                floor.AddComponent<MeshOwner>().mesh = mesh;
                var bc = floor.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, -0.5f, 0f);
                bc.size = new Vector3(size, 1f, size);
            }
            else
            {
                var g = new Geo();
                g.Ring(Vector3.zero, 0f, plan.arena.Extent + 3f, 96, plan.floorTile);
                mesh = ToMesh(g, "City_FloorDisc");
            }
            if (!plan.wetFloor || gm == null || gm.wetMaterial == null) return;
            // the wet/polished overlay reuses the planar reflection shader
            var wet = new GameObject("WetReflections");
            wet.layer = Layers.Water;
            wet.transform.SetParent(parent, false);
            wet.transform.localPosition = new Vector3(0f, 0.004f, 0f);
            Mesh wetMesh = mesh;
            if (size > 90f)
            {
                // reflections only matter near the fight: a smaller disc keeps the overlay cheap
                var g = new Geo();
                g.Ring(Vector3.zero, 0f, 70f, 96, plan.floorTile);
                wetMesh = ToMesh(g, "City_WetDisc");
                wet.AddComponent<MeshOwner>().mesh = wetMesh;
            }
            wet.AddComponent<MeshFilter>().sharedMesh = wetMesh;
            var wmr = wet.AddComponent<MeshRenderer>();
            var wm = new Material(gm.wetMaterial) { name = "Wet_" + plan.district };
            var puddles = TexLibrary.Tex("Puddles");
            if (plan.district == District.Streets && puddles) wm.SetTexture("_MaskTex", puddles);
            else if (plan.district != District.Streets) wm.SetTexture("_MaskTex", Texture2D.whiteTexture);
            wm.SetFloat("_Strength", plan.wetStrength);
            wm.SetFloat("_Tile", plan.district == District.Streets ? 9f : 4f);
            if (plan.district == District.Hotel || plan.district == District.Construct) { wm.SetFloat("_Distortion", 0.002f); wm.SetFloat("_Blur", 0.6f); }
            wmr.sharedMaterial = wm;
            wmr.shadowCastingMode = ShadowCastingMode.Off;
            wmr.receiveShadows = false;
            inst.wetMaterial = wm;
        }

        public static Mesh ToMesh(Geo g, string name)
        {
            var mesh = new Mesh { name = name };
            if (g.v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(g.v);
            mesh.SetNormals(g.n);
            mesh.SetUVs(0, g.uv);
            mesh.SetTriangles(g.t, 0);
            MeshKit.FixWinding(mesh);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static void Collider(ColliderSpec c, Transform parent)
        {
            var go = new GameObject(c.floor ? "FloorCollider" : "Collider");
            go.layer = c.floor ? Layers.Floor : Layers.Environment;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(c.center, Quaternion.Euler(0f, c.yaw, 0f));
            go.AddComponent<BoxCollider>().size = c.size;
        }

        static void MakeLight(LightSpec s, Transform parent)
        {
            var go = new GameObject(s.kind == LightKind.Spot ? "Spot" : "Light");
            go.transform.SetParent(parent, false);
            go.transform.position = s.pos;
            var l = go.AddComponent<Light>();
            l.type = s.kind == LightKind.Spot ? LightType.Spot : LightType.Point;
            l.color = s.color;
            l.intensity = s.intensity;
            l.range = s.range;
            if (s.kind == LightKind.Spot)
            {
                go.transform.rotation = Quaternion.LookRotation(s.dir.sqrMagnitude > 1e-4f ? s.dir : Vector3.down);
                l.spotAngle = s.angle > 1f ? s.angle : 60f;
                l.innerSpotAngle = l.spotAngle * 0.4f;
            }
            l.shadows = s.shadows ? LightShadows.Soft : LightShadows.None;
            if (s.flicker != 0) go.AddComponent<LightFlicker>().mode = s.flicker;
        }

        static void Emitter(EmitterSpec e, Transform parent, GameManager gm)
        {
            switch (e.kind)
            {
                case EmitterKind.Fire: FX.Fire(parent, FX.GlowMat, e.pos - parent.position, e.size).transform.position = e.pos; break;
                case EmitterKind.Smoke: FX.SmokePlume(parent, e.pos, e.size, e.color.a > 0f ? e.color : new Color(0.1f, 0.1f, 0.11f, 0.5f)); break;
                case EmitterKind.Steam: FX.Steam(parent, e.pos, e.size); break;
                case EmitterKind.Embers:
                {
                    var ps = FX.Embers(parent, FX.GlowMat, 0.4f * e.size);
                    ps.transform.position = e.pos;
                    var em = ps.emission; em.rateOverTime = 8f;
                    break;
                }
                case EmitterKind.Sparks:
                {
                    var go = new GameObject("SparkSource");
                    go.transform.SetParent(parent, false);
                    go.transform.position = e.pos;
                    go.AddComponent<SparkSource>();
                    break;
                }
            }
        }

        static void Prop(PropSpec p, Transform parent, GameManager gm, DistrictInstance inst)
        {
            switch (p.kind)
            {
                case PropKind.Text:
                {
                    var t = Ambient.NeonText(parent, p.pos, p.yaw, p.scale, p.text, p.color);
                    if (p.text == "11:59") inst.clockTexts.Add(t);
                    break;
                }
                case PropKind.HoloGiant: HoloGiant.Create(parent, gm, p); break;
                case PropKind.FlyingCar: FlyingCar.Create(parent, p); break;
                case PropKind.Survivor: Survivor.Create(parent, gm, p); break;
                case PropKind.Statue: Statue.Create(parent, gm, p); break;
                case PropKind.Beacon:
                {
                    var b = Weapons.Part(PrimitiveType.Sphere, parent, p.pos, Vector3.one * (0.5f * Mathf.Max(0.2f, p.scale)), FX.Emissive(new Color(1f, 0.05f, 0.02f), 4f), default, false);
                    b.name = "Beacon";
                    inst.beacons.Add(b.GetComponent<Renderer>());
                    break;
                }
                case PropKind.Searchlight: Ambient.Searchlight(parent, gm, p); break;
                case PropKind.TrafficLight: Ambient.TrafficLight(parent, p); break;
                case PropKind.Clock: inst.clocks.Add(Clock.Create(parent, p)); break;
                case PropKind.Monolith: inst.monolith = Monolith.Create(parent, p); break;
                case PropKind.Chairs: Ambient.Chairs(parent, p); break;
                case PropKind.Custodian: Custodian.Create(parent, p); break;
                case PropKind.Rain:
                {
                    var r = Ambient.RainCurtain(parent, gm, p);
                    if (r) { inst.rainCurtains.Add(r); r.SetActive(false); }
                    break;
                }
            }
        }
    }

    /// <summary>Destroys a runtime-generated mesh with its object.</summary>
    public class MeshOwner : MonoBehaviour
    {
        public Mesh mesh;
        void OnDestroy() { if (mesh) Destroy(mesh); }
    }

    /// <summary>Practical-light behaviour: neon buzz, firelight, a dying fluorescent tube, or police strobes.</summary>
    public class LightFlicker : MonoBehaviour
    {
        public int mode;
        Light l;
        float baseI, seed, glitch, t;
        Color baseC;

        void Awake()
        {
            l = GetComponent<Light>();
            baseI = l.intensity;
            baseC = l.color;
            seed = Random.value * 100f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            switch (mode)
            {
                case 1: // neon: steady with rare stutters
                    glitch -= dt;
                    if (glitch <= 0f && Random.value < 0.003f) glitch = Random.Range(0.06f, 0.3f);
                    l.intensity = baseI * (glitch > 0f && Random.value < 0.5f ? 0.1f : 1f);
                    break;
                case 2: // fire
                    l.intensity = baseI * (0.7f + 0.6f * Mathf.PerlinNoise(seed, t * 7f));
                    break;
                case 3: // dying tube: long stretches on, bursts of stutter, sometimes dark
                    glitch -= dt;
                    if (glitch <= 0f && Random.value < 0.01f) glitch = Random.Range(0.2f, 1.4f);
                    l.intensity = glitch > 0f ? (Random.value < 0.4f ? baseI : 0f) : baseI * 0.9f;
                    break;
                case 4: // police strobe
                    bool red = Mathf.Repeat(t * 2.2f, 1f) < 0.5f;
                    l.color = red ? new Color(1f, 0.1f, 0.08f) : new Color(0.15f, 0.3f, 1f);
                    l.intensity = Mathf.Repeat(t * 8.8f, 1f) < 0.5f ? baseI : baseI * 0.1f;
                    break;
                default: l.intensity = baseI; break;
            }
        }
    }

    /// <summary>A shorted cable that spits sparks every few seconds.</summary>
    public class SparkSource : MonoBehaviour
    {
        float next;
        void Update()
        {
            if (Time.time < next) return;
            next = Time.time + Random.Range(1.5f, 5f);
            FX.Burst(transform.position, new Color(1f, 0.9f, 0.6f), new Color(1f, 0.5f, 0.15f), Random.Range(8, 20), new Vector2(2f, 6f), new Vector2(0.02f, 0.04f), new Vector2(0.2f, 0.6f), 1.6f, true, 0.05f, Vector3.down, 60f);
            FX.LightFlash(transform.position, new Color(0.8f, 0.85f, 1f), 3f, 5f, 0.12f);
            if (Sfx.I && GameManager.I && GameManager.I.player && (GameManager.I.player.transform.position - transform.position).sqrMagnitude < 400f) Sfx.Play(Sfx.I.stun, 0.12f, 1.6f);
        }
    }
}
