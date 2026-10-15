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
        /// <summary>Optional off-hand IK goal (e.g. a riot shield handle) used instead of the two-handed grip.</summary>
        public Transform leftHandTarget;
        /// <summary>Procedural upper-body twist (yaw, degrees) and forward bend (pitch, degrees) applied in the IK pass.</summary>
        public float torsoTwist, torsoBend;

        [Header("Full-body procedural layer")]
        /// <summary>Kicks and knees: world-space foot goals (the leg IK bends to reach them).</summary>
        public Vector3 rightFootTarget, leftFootTarget;
        [Range(0f, 1f)] public float rightFootWeight, leftFootWeight;
        /// <summary>Knee hints for kicks (world space).</summary>
        public Vector3 rightKneeHint, leftKneeHint;
        /// <summary>Hip offset in character-local metres (crouch, lunge, hop), applied to the body in the IK pass.</summary>
        public Vector3 bodyOffset;
        /// <summary>Whole-body lean in degrees: pitch (+ forward), roll (+ right).</summary>
        public float bodyPitch, bodyRoll;
        /// <summary>Keeps the animated feet planted while the body moves (crouches, landings).</summary>
        [Range(0f, 1f)] public float plantFeet;
        /// <summary>Off-hand aim (blaster, tether): world goal and weight.</summary>
        public Vector3 leftHandAim;
        [Range(0f, 1f)] public float leftHandAimWeight;
        /// <summary>Head nod/turn offset for flinches.</summary>
        public float headJolt;

        // hit-reaction spring (pitch back/forward, twist), driven by Jolt()
        float reactPitch, reactPitchVel, reactTwist, reactTwistVel;
        public float ReactPitch => reactPitch;
        public float ReactTwist => reactTwist;

        /// <summary>A hit lands: the torso snaps away from the blow and springs back.</summary>
        public void Jolt(Vector3 worldDir, float strength)
        {
            Vector3 local = transform.InverseTransformDirection(worldDir);
            reactPitchVel += local.z * 900f * strength;
            reactTwistVel += -local.x * 700f * strength;
        }

        void UpdateSprings(float dt)
        {
            if (dt <= 0f) return;
            const float k = 260f, damp = 18f;
            reactPitchVel += (-k * reactPitch - damp * reactPitchVel) * dt;
            reactPitch += reactPitchVel * dt;
            reactTwistVel += (-k * reactTwist - damp * reactTwistVel) * dt;
            reactTwist += reactTwistVel * dt;
            reactPitch = Mathf.Clamp(reactPitch, -40f, 40f);
            reactTwist = Mathf.Clamp(reactTwist, -40f, 40f);
        }

        /// <summary>Eases every procedural layer back to neutral.</summary>
        public void Relax(float dt, float rate = 6f)
        {
            float k = 1f - Mathf.Exp(-rate * dt);
            rightFootWeight = Mathf.Lerp(rightFootWeight, 0f, k);
            leftFootWeight = Mathf.Lerp(leftFootWeight, 0f, k);
            bodyOffset = Vector3.Lerp(bodyOffset, Vector3.zero, k);
            bodyPitch = Mathf.Lerp(bodyPitch, 0f, k);
            bodyRoll = Mathf.Lerp(bodyRoll, 0f, k);
            plantFeet = Mathf.Lerp(plantFeet, 0f, k);
            leftHandAimWeight = Mathf.Lerp(leftHandAimWeight, 0f, k);
            headJolt = Mathf.Lerp(headJolt, 0f, k);
        }

        /// <summary>World point in front of / beside the character, in character-local metres.</summary>
        public Vector3 Local(float x, float y, float z) => transform.TransformPoint(x, y, z);

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
            UpdateSprings(dt);
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

        static GameObject Plate(Transform parent, Vector3 pos, Vector3 size, float radius, Material mat, Vector3 euler = default, float taper = 1f, bool shadows = true)
        {
            var go = new GameObject("Plate");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.AddComponent<MeshFilter>().sharedMesh = Application.isPlaying ? MeshKit.RoundedBox(size, radius, 2, taper) : MeshKit.BuildRoundedBox(size, radius, 2, taper, 0.5f);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        /// <summary>The Void Blade: chrome spine, glowing twin edges, bevelled guard and a wrapped grip.</summary>
        public static Transform Sword(Transform parent, Material metal, Material glow)
        {
            var root = NewRoot("VoidBlade", parent);
            Plate(root, new Vector3(0f, 0f, -0.04f), new Vector3(0.045f, 0.045f, 0.26f), 0.018f, metal);
            Plate(root, new Vector3(0f, 0f, -0.19f), new Vector3(0.07f, 0.07f, 0.06f), 0.03f, glow, default, 1f, false);
            Plate(root, new Vector3(0f, 0f, 0.11f), new Vector3(0.34f, 0.05f, 0.07f), 0.022f, metal);
            Plate(root, new Vector3(0f, 0f, 0.115f), new Vector3(0.12f, 0.062f, 0.09f), 0.02f, glow, default, 1f, false);
            Plate(root, new Vector3(0f, 0f, 0.68f), new Vector3(0.05f, 0.02f, 1.02f), 0.008f, metal, default, 0.55f);
            Plate(root, new Vector3(0.034f, 0f, 0.69f), new Vector3(0.018f, 0.008f, 1.0f), 0.003f, glow, default, 0.4f, false);
            Plate(root, new Vector3(-0.034f, 0f, 0.69f), new Vector3(0.018f, 0.008f, 1.0f), 0.003f, glow, default, 0.4f, false);
            Plate(root, new Vector3(0f, 0f, 1.22f), new Vector3(0.05f, 0.012f, 0.09f), 0.004f, glow, new Vector3(0f, 45f, 0f), 1f, false);
            return root;
        }

        /// <summary>Riot police stun baton.</summary>
        public static Transform Baton(Transform parent, Material metal, Material glow)
        {
            var root = NewRoot("StunBaton", parent);
            Plate(root, new Vector3(0f, 0f, 0.0f), new Vector3(0.05f, 0.05f, 0.16f), 0.02f, metal);
            Plate(root, new Vector3(0f, 0f, 0.36f), new Vector3(0.04f, 0.04f, 0.46f), 0.018f, metal);
            Plate(root, new Vector3(0f, 0f, 0.6f), new Vector3(0.05f, 0.05f, 0.05f), 0.024f, glow, default, 1f, false);
            return root;
        }

        /// <summary>Energy riot shield. Contains a "Handle" child used as the off-hand IK goal.</summary>
        public static Transform RiotShield(Transform parent, Material body, Material glow)
        {
            var root = NewRoot("RiotShield", parent);
            Plate(root, new Vector3(0f, 0f, 0f), new Vector3(0.66f, 1.0f, 0.05f), 0.02f, body);
            Plate(root, new Vector3(0f, 0.45f, 0.028f), new Vector3(0.6f, 0.03f, 0.012f), 0.005f, glow, default, 1f, false);
            Plate(root, new Vector3(0f, -0.45f, 0.028f), new Vector3(0.6f, 0.03f, 0.012f), 0.005f, glow, default, 1f, false);
            Plate(root, new Vector3(0.31f, 0f, 0.028f), new Vector3(0.03f, 0.86f, 0.012f), 0.005f, glow, default, 1f, false);
            Plate(root, new Vector3(-0.31f, 0f, 0.028f), new Vector3(0.03f, 0.86f, 0.012f), 0.005f, glow, default, 1f, false);
            Plate(root, new Vector3(0f, 0.25f, 0.03f), new Vector3(0.36f, 0.12f, 0.012f), 0.01f, glow, default, 1f, false);
            var handle = new GameObject("Handle").transform;
            handle.SetParent(root, false);
            handle.localPosition = new Vector3(0.05f, -0.05f, -0.09f);
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

        /// <summary>The Enforcer's carbine: receiver, magazine, stock, a glowing scope. Muzzle is a child transform.</summary>
        public static Transform Rifle(Transform parent, Material metal, Material glow, out Transform muzzle)
        {
            var root = NewRoot("Carbine", parent);
            Plate(root, new Vector3(0f, 0.02f, 0.12f), new Vector3(0.07f, 0.11f, 0.42f), 0.02f, metal);
            Plate(root, new Vector3(0f, -0.08f, 0.12f), new Vector3(0.045f, 0.16f, 0.07f), 0.012f, metal, new Vector3(-12f, 0f, 0f));
            Plate(root, new Vector3(0f, -0.04f, -0.2f), new Vector3(0.05f, 0.12f, 0.24f), 0.02f, metal, new Vector3(8f, 0f, 0f));
            Plate(root, new Vector3(0f, -0.06f, -0.02f), new Vector3(0.035f, 0.1f, 0.04f), 0.01f, metal, new Vector3(-20f, 0f, 0f));
            Plate(root, new Vector3(0f, 0.035f, 0.52f), new Vector3(0.032f, 0.032f, 0.4f), 0.012f, metal);
            Plate(root, new Vector3(0f, 0.1f, 0.14f), new Vector3(0.04f, 0.045f, 0.14f), 0.015f, metal);
            Plate(root, new Vector3(0f, 0.1f, 0.215f), new Vector3(0.03f, 0.03f, 0.01f), 0.01f, glow, default, 1f, false);
            Plate(root, new Vector3(0.038f, 0.02f, 0.18f), new Vector3(0.006f, 0.02f, 0.22f), 0.002f, glow, default, 1f, false);
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root, false);
            muzzle.localPosition = new Vector3(0f, 0.035f, 0.74f);
            return root;
        }

        /// <summary>A red laser sight that the Enforcer paints the Knight with before it fires.</summary>
        public static LineRenderer Laser(Transform muzzle)
        {
            var go = new GameObject("LaserSight");
            go.transform.SetParent(muzzle, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = FX.FlatMat;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.012f;
            lr.numCapVertices = 2;
            lr.enabled = false;
            return lr;
        }

        /// <summary>No weapon: an empty grip anchor so the hand IK still has something to drive (Auditors box).</summary>
        public static Transform Fist(Transform parent)
        {
            return NewRoot("Fist", parent);
        }

        /// <summary>The Knight's sidearm (Officer Dallow's, once): a heavy revolver-style blaster with a glowing cylinder.</summary>
        public static Transform Blaster(Transform parent, Material metal, Material glow, out Transform muzzle)
        {
            var root = NewRoot("Blaster", parent);
            Plate(root, new Vector3(0f, -0.07f, -0.03f), new Vector3(0.04f, 0.13f, 0.06f), 0.015f, metal, new Vector3(-18f, 0f, 0f));
            Plate(root, new Vector3(0f, 0.01f, 0.06f), new Vector3(0.05f, 0.07f, 0.16f), 0.018f, metal);
            Plate(root, new Vector3(0f, 0.012f, 0.06f), new Vector3(0.062f, 0.05f, 0.07f), 0.022f, glow, default, 1f, false);
            Plate(root, new Vector3(0f, 0.03f, 0.2f), new Vector3(0.03f, 0.03f, 0.2f), 0.012f, metal);
            Plate(root, new Vector3(0f, -0.004f, 0.18f), new Vector3(0.022f, 0.02f, 0.16f), 0.008f, metal);
            Plate(root, new Vector3(0f, 0.052f, 0.04f), new Vector3(0.012f, 0.012f, 0.06f), 0.004f, glow, default, 1f, false);
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root, false);
            muzzle.localPosition = new Vector3(0f, 0.03f, 0.31f);
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
