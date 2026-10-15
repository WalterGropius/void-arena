using System.Collections.Generic;
using UnityEngine;

namespace HackAndSlash
{
    /// <summary>
    /// Builds a physics ragdoll on a humanoid at the moment it dies (Unity's RagdollBuilder recipe, done in code):
    /// capsules on the limbs, boxes on pelvis and chest, a sphere on the head, character joints with generous limits.
    /// Severed parts (collapsed bones) are skipped. Bodies live on the Debris layer so they never block characters.
    /// </summary>
    public class Ragdoll : MonoBehaviour
    {
        readonly List<Rigidbody> bodies = new List<Rigidbody>();
        readonly List<Collider> colliders = new List<Collider>();
        Rigidbody pelvis, chestBody;
        float age, settleAt = 4.5f, sinkStart = -1f;
        Transform model;
        System.Action onGone;

        public Rigidbody Pelvis => pelvis;

        struct BoneDef
        {
            public HumanBodyBones bone, child, parent;
            public float radiusFrac, mass, twist, swing;
            public int shape; // 0 capsule, 1 box, 2 sphere
        }

        static readonly BoneDef[] Defs =
        {
            new BoneDef { bone = HumanBodyBones.Hips, child = HumanBodyBones.Spine, parent = HumanBodyBones.LastBone, mass = 0.24f, shape = 1 },
            new BoneDef { bone = HumanBodyBones.Chest, child = HumanBodyBones.Neck, parent = HumanBodyBones.Hips, mass = 0.24f, shape = 1, twist = 25f, swing = 15f },
            new BoneDef { bone = HumanBodyBones.Head, child = HumanBodyBones.LastBone, parent = HumanBodyBones.Chest, mass = 0.07f, shape = 2, twist = 40f, swing = 30f },
            new BoneDef { bone = HumanBodyBones.LeftUpperLeg, child = HumanBodyBones.LeftLowerLeg, parent = HumanBodyBones.Hips, radiusFrac = 0.18f, mass = 0.09f, twist = 50f, swing = 40f },
            new BoneDef { bone = HumanBodyBones.RightUpperLeg, child = HumanBodyBones.RightLowerLeg, parent = HumanBodyBones.Hips, radiusFrac = 0.18f, mass = 0.09f, twist = 50f, swing = 40f },
            new BoneDef { bone = HumanBodyBones.LeftLowerLeg, child = HumanBodyBones.LeftFoot, parent = HumanBodyBones.LeftUpperLeg, radiusFrac = 0.14f, mass = 0.06f, twist = 70f, swing = 8f },
            new BoneDef { bone = HumanBodyBones.RightLowerLeg, child = HumanBodyBones.RightFoot, parent = HumanBodyBones.RightUpperLeg, radiusFrac = 0.14f, mass = 0.06f, twist = 70f, swing = 8f },
            new BoneDef { bone = HumanBodyBones.LeftUpperArm, child = HumanBodyBones.LeftLowerArm, parent = HumanBodyBones.Chest, radiusFrac = 0.2f, mass = 0.04f, twist = 60f, swing = 60f },
            new BoneDef { bone = HumanBodyBones.RightUpperArm, child = HumanBodyBones.RightLowerArm, parent = HumanBodyBones.Chest, radiusFrac = 0.2f, mass = 0.04f, twist = 60f, swing = 60f },
            new BoneDef { bone = HumanBodyBones.LeftLowerArm, child = HumanBodyBones.LeftHand, parent = HumanBodyBones.LeftUpperArm, radiusFrac = 0.16f, mass = 0.03f, twist = 70f, swing = 8f },
            new BoneDef { bone = HumanBodyBones.RightLowerArm, child = HumanBodyBones.RightHand, parent = HumanBodyBones.RightUpperArm, radiusFrac = 0.16f, mass = 0.03f, twist = 70f, swing = 8f },
        };

        /// <summary>Turns the animated body into a ragdoll and throws it. Returns null if the rig is unusable.</summary>
        public static Ragdoll Go(Animator anim, Vector3 velocity, Vector3 angular, float totalMass, System.Action onGone = null)
        {
            if (!anim || !anim.isHuman) return null;
            var rd = anim.gameObject.AddComponent<Ragdoll>();
            rd.model = anim.transform;
            rd.onGone = onGone;
            var map = new Dictionary<HumanBodyBones, Rigidbody>();
            Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            if (!hips) { Destroy(rd); return null; }
            Vector3 worldUp = anim.transform.up;
            foreach (var d in Defs)
            {
                var t = anim.GetBoneTransform(d.bone);
                if (d.bone == HumanBodyBones.Chest && !t) t = anim.GetBoneTransform(HumanBodyBones.Spine);
                if (!t) continue;
                if (t.lossyScale.x < 0.02f) continue; // severed
                Rigidbody parentBody = null;
                if (d.parent != HumanBodyBones.LastBone)
                {
                    if (!map.TryGetValue(d.parent, out parentBody)) continue;
                }
                var rb = t.gameObject.AddComponent<Rigidbody>();
                rb.mass = Mathf.Max(0.3f, totalMass * d.mass);
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.6f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.solverIterations = 12;
                map[d.bone] = rb;
                rd.bodies.Add(rb);
                rd.AddCollider(anim, t, d);
                if (parentBody != null)
                {
                    var j = t.gameObject.AddComponent<CharacterJoint>();
                    j.connectedBody = parentBody;
                    Vector3 twistAxis = anim.transform.right, swingAxis = anim.transform.forward;
                    if (d.bone == HumanBodyBones.LeftUpperArm || d.bone == HumanBodyBones.RightUpperArm) { twistAxis = worldUp; swingAxis = anim.transform.forward; }
                    if (d.bone == HumanBodyBones.LeftLowerArm || d.bone == HumanBodyBones.RightLowerArm) { twistAxis = anim.transform.forward; swingAxis = worldUp; }
                    j.axis = MajorAxis(t.InverseTransformDirection(twistAxis));
                    j.swingAxis = MajorAxis(t.InverseTransformDirection(swingAxis));
                    j.lowTwistLimit = new SoftJointLimit { limit = -d.twist };
                    j.highTwistLimit = new SoftJointLimit { limit = d.twist };
                    j.swing1Limit = new SoftJointLimit { limit = d.swing };
                    j.swing2Limit = new SoftJointLimit { limit = d.swing * 0.6f };
                    j.enableProjection = true;
                    j.projectionDistance = 0.05f;
                    j.projectionAngle = 20f;
                }
                if (d.bone == HumanBodyBones.Hips) rd.pelvis = rb;
                if (d.bone == HumanBodyBones.Chest) rd.chestBody = rb;
            }
            foreach (Transform c in anim.GetComponentsInChildren<Transform>())
                if (c.GetComponent<Rigidbody>() || c.GetComponent<Collider>()) c.gameObject.layer = Layers.Debris;
            foreach (var rb in rd.bodies)
            {
                rb.linearVelocity = velocity * Random.Range(0.85f, 1.15f);
                rb.angularVelocity = angular;
            }
            return rd;
        }

        static Vector3 MajorAxis(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            if (a.y >= a.z) return new Vector3(0f, Mathf.Sign(v.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        void AddCollider(Animator anim, Transform t, BoneDef d)
        {
            float s = Mathf.Max(1e-3f, t.lossyScale.x);
            Collider col = null;
            switch (d.shape)
            {
                case 1:
                {
                    var childT = anim.GetBoneTransform(d.child);
                    Vector3 to = childT ? t.InverseTransformPoint(childT.position) : t.InverseTransformDirection(anim.transform.up) * (0.25f / s);
                    var bc = t.gameObject.AddComponent<BoxCollider>();
                    float len = to.magnitude;
                    Vector3 size = Vector3.one * (0.3f / s);
                    // long along the bone, wide across the shoulders/hips
                    int axis = MaxComp(to);
                    size[axis] = Mathf.Max(len, 0.1f / s);
                    bc.size = size;
                    bc.center = to * 0.5f;
                    col = bc;
                    break;
                }
                case 2:
                {
                    var sc = t.gameObject.AddComponent<SphereCollider>();
                    sc.radius = 0.13f / s;
                    sc.center = t.InverseTransformDirection(anim.transform.up) * (0.1f / s);
                    col = sc;
                    break;
                }
                default:
                {
                    var childT = anim.GetBoneTransform(d.child);
                    if (!childT) break;
                    Vector3 to = t.InverseTransformPoint(childT.position);
                    var cc = t.gameObject.AddComponent<CapsuleCollider>();
                    int axis = MaxComp(to);
                    cc.direction = axis;
                    float len = Mathf.Abs(to[axis]);
                    cc.height = len;
                    cc.radius = Mathf.Max(0.03f / s, len * d.radiusFrac);
                    cc.center = to * 0.5f;
                    col = cc;
                    break;
                }
            }
            if (col) colliders.Add(col);
        }

        static int MaxComp(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            return a.x >= a.y && a.x >= a.z ? 0 : a.y >= a.z ? 1 : 2;
        }

        public void Push(Vector3 impulse)
        {
            if (chestBody) chestBody.AddForce(impulse, ForceMode.VelocityChange);
            if (pelvis) pelvis.AddForce(impulse * 0.6f, ForceMode.VelocityChange);
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age > settleAt && sinkStart < 0f)
            {
                sinkStart = age;
                foreach (var rb in bodies) if (rb) rb.isKinematic = true;
                foreach (var c in colliders) if (c) c.enabled = false;
            }
            if (sinkStart >= 0f)
            {
                float u = (age - sinkStart) / 1.6f;
                if (pelvis) pelvis.transform.position += Vector3.down * (Time.deltaTime * 0.5f);
                if (u >= 1f && onGone != null) { var g = onGone; onGone = null; g(); }
            }
        }
    }
}
