using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    /// <summary>
    /// Procedurally poses a weapon in the character's local space. The humanoid's hands follow it via IK
    /// (see <see cref="IKRelay"/>), so swings are fully code-driven — no attack animation clips required.
    /// Weapon convention: origin = grip, blade/shaft along local +Z.
    /// </summary>
    public class WeaponRig : MonoBehaviour
    {
        public Transform weapon;
        public float bladeLength = 1.25f;
        [Range(0f, 1f)] public float rightHandWeight = 1f;
        [Range(0f, 1f)] public float leftHandWeight;
        [Range(0f, 1f)] public float lookWeight;
        public Vector3 lookTargetWorld;

        public WeaponTrail Trail { get; private set; }
        public Vector3 GripWorld => weapon.position;
        public Vector3 BladeDirWorld => weapon.forward;
        public Vector3 TipWorld => weapon.TransformPoint(0f, 0f, bladeLength);

        Vector3 curPos, curDir = Vector3.forward, tgtPos, tgtDir = Vector3.forward;
        float sharpness = 20f;
        bool initialized;

        public void InitTrail(Color hdrColor, string trailName, float lifetime = 0.16f)
        {
            if (Trail != null) return;
            var go = FX.NewObject(trailName);
            Trail = go.AddComponent<WeaponTrail>();
            Trail.Init(this, hdrColor, lifetime);
        }

        public void SetTrail(bool on)
        {
            if (Trail) Trail.emitting = on;
        }

        void OnDestroy()
        {
            if (Trail) Destroy(Trail.gameObject);
        }

        public void SetTarget(Vector3 gripLocal, Vector3 dirLocal, float sharp)
        {
            tgtPos = gripLocal;
            tgtDir = dirLocal.normalized;
            sharpness = sharp;
        }

        public void Snap(Vector3 gripLocal, Vector3 dirLocal)
        {
            tgtPos = curPos = gripLocal;
            tgtDir = curDir = dirLocal.normalized;
            initialized = true;
            Apply();
        }

        public void Tick(float dt)
        {
            if (!weapon) return;
            if (!initialized) { curPos = tgtPos; curDir = tgtDir; initialized = true; }
            float k = 1f - Mathf.Exp(-sharpness * Mathf.Max(dt, 0f));
            curPos = Vector3.Lerp(curPos, tgtPos, k);
            curDir = Vector3.Slerp(curDir, tgtDir, k).normalized;
            Apply();
        }

        void Apply()
        {
            if (!weapon) return;
            weapon.localPosition = curPos;
            Vector3 up = Vector3.Cross(curDir, Vector3.Cross(Vector3.up, curDir));
            if (up.sqrMagnitude < 1e-4f) up = Vector3.forward;
            weapon.localRotation = Quaternion.LookRotation(curDir, up);
        }

        // ---------------------------------------------------------------- pose library (character-local space)

        /// <summary>Grip on a horizontal arc around the chest. angle 0 = straight ahead, + = right.</summary>
        public static Vector3 HGrip(float angleDeg, float height, float radius = 0.5f)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            float s = Mathf.Sin(a);
            float r = radius - 0.12f * Mathf.Max(0f, -s); // pull in on the off-hand side so the arm can reach
            return new Vector3(s * r, height, Mathf.Cos(a) * r);
        }

        public static Vector3 HDir(float angleDeg, float tilt)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), tilt, Mathf.Cos(a)).normalized;
        }

        /// <summary>Grip on a vertical arc in front of the body. phi 0 = blade straight up, 90 = forward, 140 = into the ground.</summary>
        public static Vector3 VGrip(float phiDeg)
        {
            float p = phiDeg * Mathf.Deg2Rad;
            return new Vector3(0.08f, 1.42f + Mathf.Cos(p) * 0.46f, 0.14f + Mathf.Sin(p) * 0.46f);
        }

        public static Vector3 VDir(float phiDeg)
        {
            float p = phiDeg * Mathf.Deg2Rad;
            return new Vector3(0f, Mathf.Cos(p), Mathf.Sin(p));
        }
    }

    /// <summary>Wide ribbon "swoosh" between blade base and tip, smoothed with Catmull-Rom.</summary>
    public class WeaponTrail : MonoBehaviour
    {
        struct Sample { public Vector3 a, b; public float t; }

        public bool emitting;
        WeaponRig rig;
        float lifetime = 0.16f;
        const float BaseFrac = 0.2f;
        const int Subdiv = 4;
        readonly List<Sample> samples = new List<Sample>();
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Color> cols = new List<Color>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();
        Mesh mesh;
        bool wasEmitting;

        public void Init(WeaponRig owner, Color hdrColor, float life)
        {
            rig = owner;
            lifetime = life;
            mesh = new Mesh { name = "WeaponTrail" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = FX.FlatMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", hdrColor);
            mr.SetPropertyBlock(mpb);
        }

        void OnDestroy()
        {
            if (mesh) Destroy(mesh);
        }

        void LateUpdate()
        {
            if (rig == null || rig.weapon == null) { Destroy(gameObject); return; }
            float now = Time.time;
            if (emitting)
            {
                if (!wasEmitting) samples.Clear();
                samples.Add(new Sample
                {
                    a = rig.weapon.TransformPoint(0f, 0f, rig.bladeLength * BaseFrac),
                    b = rig.weapon.TransformPoint(0f, 0f, rig.bladeLength),
                    t = now
                });
            }
            wasEmitting = emitting;
            while (samples.Count > 0 && now - samples[0].t > lifetime) samples.RemoveAt(0);
            Rebuild(now);
        }

        void Rebuild(float now)
        {
            verts.Clear(); cols.Clear(); uvs.Clear(); tris.Clear();
            int n = samples.Count;
            if (n >= 2)
            {
                int idx = 0;
                for (int i = 0; i < n - 1; i++)
                {
                    var p0 = samples[Mathf.Max(i - 1, 0)];
                    var p1 = samples[i];
                    var p2 = samples[i + 1];
                    var p3 = samples[Mathf.Min(i + 2, n - 1)];
                    int steps = i == n - 2 ? Subdiv + 1 : Subdiv;
                    for (int s = 0; s < steps; s++)
                    {
                        float f = s / (float)Subdiv;
                        Vector3 a = CatmullRom(p0.a, p1.a, p2.a, p3.a, f);
                        Vector3 b = CatmullRom(p0.b, p1.b, p2.b, p3.b, f);
                        float age = Mathf.Clamp01((now - Mathf.Lerp(p1.t, p2.t, f)) / lifetime);
                        float alpha = (1f - age) * (1f - age);
                        verts.Add(a); verts.Add(b);
                        cols.Add(new Color(1f, 1f, 1f, alpha * 0.15f));
                        cols.Add(new Color(1f, 1f, 1f, alpha));
                        uvs.Add(new Vector2(age, 0f)); uvs.Add(new Vector2(age, 1f));
                        if (idx > 0)
                        {
                            int v = idx * 2;
                            tris.Add(v - 2); tris.Add(v - 1); tris.Add(v);
                            tris.Add(v - 1); tris.Add(v + 1); tris.Add(v);
                        }
                        idx++;
                    }
                }
            }
            mesh.Clear();
            if (verts.Count >= 4)
            {
                mesh.SetVertices(verts);
                mesh.SetColors(cols);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
            }
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }

    /// <summary>Builds weapons out of primitives. Usable at edit time (scene builder) and at runtime (enemy factory).</summary>
    public static class Weapons
    {
        public static GameObject Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default, bool shadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col) Object.DestroyImmediate(col);
            go.name = type.ToString();
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        static Transform NewRoot(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        public static Transform Sword(Transform parent, Material metal, Material glow)
        {
            var root = NewRoot("VoidBlade", parent);
            Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0f, -0.03f), new Vector3(0.055f, 0.12f, 0.055f), metal, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Sphere, root, new Vector3(0f, 0f, -0.18f), Vector3.one * 0.09f, glow, default, false);
            Part(PrimitiveType.Cube, root, new Vector3(0f, 0f, 0.12f), new Vector3(0.36f, 0.05f, 0.07f), metal);
            Part(PrimitiveType.Cube, root, new Vector3(0f, 0f, 0.125f), new Vector3(0.11f, 0.065f, 0.1f), glow, default, false);
            Part(PrimitiveType.Cube, root, new Vector3(0f, 0f, 0.68f), new Vector3(0.035f, 0.032f, 1.0f), metal);
            Part(PrimitiveType.Cube, root, new Vector3(0f, 0f, 0.71f), new Vector3(0.14f, 0.018f, 1.1f), glow, default, false);
            Part(PrimitiveType.Cube, root, new Vector3(0f, 0f, 1.255f), new Vector3(0.1f, 0.018f, 0.1f), glow, new Vector3(0f, 45f, 0f), false);
            return root;
        }

        public static Transform Dagger(Transform parent, Material metal, Material glow)
        {
            var root = NewRoot("Shiv", parent);
            Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0f, -0.02f), new Vector3(0.05f, 0.09f, 0.05f), metal, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Cube, root, new Vector3(0f, 0f, 0.09f), new Vector3(0.2f, 0.045f, 0.05f), metal);
            Part(PrimitiveType.Cube, root, new Vector3(0f, 0f, 0.38f), new Vector3(0.085f, 0.02f, 0.55f), glow, default, false);
            Part(PrimitiveType.Cube, root, new Vector3(0f, 0f, 0.66f), new Vector3(0.06f, 0.02f, 0.06f), glow, new Vector3(0f, 45f, 0f), false);
            return root;
        }

        public static Transform Hammer(Transform parent, Material metal, Material glow)
        {
            var root = NewRoot("Maul", parent);
            Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0f, 0.35f), new Vector3(0.075f, 0.62f, 0.075f), metal, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Cube, root, new Vector3(0f, 0f, 1.08f), new Vector3(0.62f, 0.42f, 0.4f), metal);
            Part(PrimitiveType.Cube, root, new Vector3(0f, 0f, 1.08f), new Vector3(0.64f, 0.1f, 0.42f), glow, default, false);
            Part(PrimitiveType.Cube, root, new Vector3(0.33f, 0f, 1.08f), new Vector3(0.06f, 0.3f, 0.3f), glow, default, false);
            Part(PrimitiveType.Cube, root, new Vector3(-0.33f, 0f, 1.08f), new Vector3(0.06f, 0.3f, 0.3f), glow, default, false);
            return root;
        }

        public static Transform Staff(Transform parent, Material metal, Material glow, out Transform orb, out Light orbLight, Color lightColor)
        {
            var root = NewRoot("HexStaff", parent);
            Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0f, 0.25f), new Vector3(0.05f, 0.85f, 0.05f), metal, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Cube, root, new Vector3(0f, 0f, 1.08f), new Vector3(0.22f, 0.04f, 0.04f), metal);
            orb = Part(PrimitiveType.Sphere, root, new Vector3(0f, 0f, 1.24f), Vector3.one * 0.26f, glow, default, false).transform;
            var lgo = new GameObject("OrbLight");
            lgo.transform.SetParent(orb, false);
            orbLight = lgo.AddComponent<Light>();
            orbLight.type = LightType.Point;
            orbLight.color = lightColor;
            orbLight.range = 3.5f;
            orbLight.intensity = 1.2f;
            orbLight.shadows = LightShadows.None;
            return root;
        }
    }
}
