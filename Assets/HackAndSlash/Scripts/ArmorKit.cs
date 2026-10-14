using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    public enum ArmorStyle { Hero, Scrapper, Hexer, Bulwark, Juggernaut, Tyrant }

    /// <summary>Look of one robot faction: clear-coated paint, trim, joints, emissive accents, and how chunky the plates are.</summary>
    public struct ArmorPalette
    {
        public ArmorStyle style;
        public Color paint, trim, joint, glow, suit;
        public float paintSmooth, paintMetal, clearCoat, trimMetal, trimSmooth, bulk;

        public static ArmorPalette For(ArmorStyle s)
        {
            var p = new ArmorPalette { style = s, joint = new Color(0.035f, 0.036f, 0.04f), suit = new Color(0.06f, 0.062f, 0.07f), paintSmooth = 0.86f, paintMetal = 0.05f, clearCoat = 1f, trimMetal = 0.9f, trimSmooth = 0.72f, bulk = 1f };
            switch (s)
            {
                case ArmorStyle.Hero: // pearl-white ceramic, gunmetal trim, void-cyan light
                    p.paint = new Color(0.86f, 0.88f, 0.92f); p.trim = new Color(0.16f, 0.17f, 0.2f); p.glow = new Color(0.3f, 1.25f, 3.2f); break;
                case ArmorStyle.Scrapper: // Robo Recall worker bot: glossy off-white with hazard orange
                    p.paint = new Color(0.82f, 0.8f, 0.76f); p.trim = new Color(0.9f, 0.32f, 0.08f); p.trimMetal = 0.2f; p.trimSmooth = 0.75f; p.glow = new Color(3.2f, 0.75f, 0.12f); p.bulk = 0.95f; break;
                case ArmorStyle.Hexer: // black chrome, violet
                    p.paint = new Color(0.03f, 0.03f, 0.04f); p.paintMetal = 0.9f; p.paintSmooth = 0.93f; p.trim = new Color(0.35f, 0.3f, 0.42f); p.glow = new Color(1.8f, 0.45f, 3.4f); p.bulk = 0.88f; break;
                case ArmorStyle.Bulwark: // riot police navy and white
                    p.paint = new Color(0.07f, 0.12f, 0.26f); p.trim = new Color(0.82f, 0.84f, 0.86f); p.trimMetal = 0.1f; p.glow = new Color(0.35f, 0.9f, 3.4f); p.bulk = 1.1f; break;
                case ArmorStyle.Juggernaut: // industrial yellow, scuffed
                    p.paint = new Color(0.86f, 0.62f, 0.06f); p.paintSmooth = 0.62f; p.clearCoat = 0.4f; p.trim = new Color(0.08f, 0.08f, 0.09f); p.glow = new Color(3.2f, 1.1f, 0.15f); p.bulk = 1.35f; break;
                default: // Iron Tyrant: gunmetal and gold chrome
                    p.paint = new Color(0.12f, 0.11f, 0.11f); p.paintMetal = 0.85f; p.paintSmooth = 0.82f; p.trim = new Color(1f, 0.72f, 0.3f); p.trimMetal = 1f; p.trimSmooth = 0.9f; p.glow = new Color(3.6f, 0.45f, 0.08f); p.bulk = 1.3f; break;
            }
            return p;
        }
    }

    /// <summary>
    /// Bolts hard-surface armour onto a humanoid rig at runtime, fitted to the bind-pose bone lengths, and handles
    /// Robo Recall-style damage: plates chip off on heavy hits, heads and forearms come off with physics on kills,
    /// and severed joints spray sparks.
    /// </summary>
    public class ArmorKit : MonoBehaviour
    {
        class Piece
        {
            public Transform t;
            public HumanBodyBones bone;
            public bool detachable, detached;
        }

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        readonly List<Piece> pieces = new List<Piece>();
        readonly Dictionary<HumanBodyBones, Vector3> boneScale = new Dictionary<HumanBodyBones, Vector3>();
        Animator anim;
        Material paint, suit, glow;
        Material trim, joint;
        Color glowBase;
        float s, bulk;
        Transform root;
        Vector3 fwd, up, right;

        public ArmorPalette Palette { get; private set; }
        /// <summary>Plate renderers that are still attached (used for dash afterimages).</summary>
        public readonly List<Renderer> Renderers = new List<Renderer>();
        public Material GlowMaterial => glow;

        // ---------------------------------------------------------------- construction

        public static ArmorKit Build(Animator animator, SkinnedMeshRenderer body, Transform characterRoot, ArmorStyle style)
        {
            var gm = GameManager.I;
            var kit = animator.gameObject.AddComponent<ArmorKit>();
            kit.anim = animator;
            kit.root = characterRoot;
            kit.Palette = ArmorPalette.For(style);
            kit.CreateMaterials(gm, body);
            kit.Assemble();
            return kit;
        }

        void CreateMaterials(GameManager gm, SkinnedMeshRenderer body)
        {
            var p = Palette;
            paint = new Material(gm && gm.armorPaintMaterial ? gm.armorPaintMaterial : FX.FallbackLit);
            paint.SetColor(BaseColorId, p.paint);
            paint.SetFloat("_Metallic", p.paintMetal);
            paint.SetFloat("_Smoothness", p.paintSmooth);
            paint.SetFloat("_ClearCoatMask", p.clearCoat);
            paint.SetFloat("_ClearCoatSmoothness", 0.94f);
            paint.EnableKeyword("_EMISSION");
            paint.SetColor(EmissionId, Color.black);

            trim = FX.Lit(p.trim, p.trimMetal, p.trimSmooth);
            joint = FX.Lit(p.joint, 0.95f, 0.8f);

            glow = new Material(gm && gm.metalMaterial ? gm.metalMaterial : FX.FallbackLit);
            glowBase = p.glow;
            glow.SetColor(BaseColorId, p.glow * 0.2f);
            glow.SetFloat("_Metallic", 0f);
            glow.SetFloat("_Smoothness", 0.9f);
            glow.EnableKeyword("_EMISSION");
            glow.SetColor(EmissionId, p.glow);

            if (body)
            {
                suit = new Material(gm && gm.suitMaterial ? gm.suitMaterial : body.sharedMaterial);
                suit.SetColor(BaseColorId, p.suit);
                suit.EnableKeyword("_EMISSION");
                suit.SetColor(EmissionId, Color.black);
                body.sharedMaterial = suit;
            }
        }

        void OnDestroy()
        {
            if (paint) Destroy(paint);
            if (suit) Destroy(suit);
            if (glow) Destroy(glow);
        }

        Transform B(HumanBodyBones b) => anim ? anim.GetBoneTransform(b) : null;

        void Assemble()
        {
            s = root.lossyScale.y;
            bulk = Palette.bulk;
            fwd = root.forward; up = root.up; right = root.right;
            var st = Palette.style;
            Transform hips = B(HumanBodyBones.Hips), spine = B(HumanBodyBones.Spine), chest = B(HumanBodyBones.Chest) ?? spine;
            Transform neck = B(HumanBodyBones.Neck), head = B(HumanBodyBones.Head);
            if (!hips) return;
            Vector3 basePos = root.position;
            Vector3 At(float x, float y, float z) => basePos + right * (x * s) + up * (y * s) + fwd * (z * s);
            Quaternion Upright = Quaternion.LookRotation(fwd, up);
            Quaternion Vertical = Quaternion.LookRotation(up, fwd); // local z = up, y = forward

            // ---- torso
            Box("Pelvis", HumanBodyBones.Hips, At(0f, 1.0f, 0f), Upright, new Vector3(0.34f * bulk, 0.15f, 0.25f * bulk), 0.06f, trim, false);
            Box("Codpiece", HumanBodyBones.Hips, At(0f, 0.95f, 0.12f * bulk), Upright, new Vector3(0.12f, 0.1f, 0.06f), 0.025f, paint, false);
            if (spine) Box("Abdomen", HumanBodyBones.Spine, At(0f, 1.17f, 0f), Upright, new Vector3(0.27f * bulk, 0.16f, 0.23f * bulk), 0.06f, joint, false);
            if (spine) Box("AbPlate", HumanBodyBones.Spine, At(0f, 1.16f, 0.105f * bulk), Upright, new Vector3(0.17f * bulk, 0.11f, 0.04f), 0.018f, paint, false);
            if (chest)
            {
                var ch = chest == spine ? HumanBodyBones.Spine : HumanBodyBones.Chest;
                Box("Chest", ch, At(0f, 1.37f, 0.015f), Vertical, new Vector3(0.4f * bulk, 0.33f * bulk, 0.3f), 0.085f, paint, false, 1.13f);
                Box("ChestCore", ch, At(0f, 1.405f, 0.175f * bulk), Upright, new Vector3(0.085f, 0.085f, 0.03f), 0.02f, glow, false);
                Box("CollarL", ch, At(0.12f * bulk, 1.505f, 0.03f), Upright, new Vector3(0.12f, 0.035f, 0.2f * bulk), 0.012f, trim, false);
                Box("CollarR", ch, At(-0.12f * bulk, 1.505f, 0.03f), Upright, new Vector3(0.12f, 0.035f, 0.2f * bulk), 0.012f, trim, false);
                Box("Pack", ch, At(0f, 1.38f, -0.2f * bulk), Upright, new Vector3(0.27f * bulk, 0.25f, 0.12f), 0.035f, trim, true);
                for (int i = 0; i < 3; i++)
                    Box("Vent", ch, At(0f, 1.32f + i * 0.055f, -0.262f * bulk), Upright, new Vector3(0.18f * bulk, 0.013f, 0.012f), 0.004f, glow, false);
                if (st == ArmorStyle.Juggernaut || st == ArmorStyle.Tyrant)
                {
                    Box("Exhaust", ch, At(0.09f, 1.52f, -0.24f * bulk), Vertical, new Vector3(0.06f, 0.06f, 0.18f), 0.028f, joint, true);
                    Box("Exhaust", ch, At(-0.09f, 1.52f, -0.24f * bulk), Vertical, new Vector3(0.06f, 0.06f, 0.18f), 0.028f, joint, true);
                }
            }
            if (neck) Box("Neck", HumanBodyBones.Neck, At(0f, 1.535f, 0f), Upright, new Vector3(0.1f, 0.09f, 0.1f), 0.04f, joint, false);

            // ---- head (the base mesh head is cartoon-sized: shrink it and wrap a helmet around it)
            if (head)
            {
                // scale first, so the helmet parented below keeps its full world size
                boneScale[HumanBodyBones.Head] = Vector3.one * 0.62f;
                ApplyBoneScales();
                BuildHelmet(st, At, Upright);
            }

            // ---- arms
            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                var upper = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
                var lower = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
                var hand = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                var tu = B(upper); var tl = B(lower); var th = B(hand);
                if (!tu || !tl) continue;
                Vector3 outward = (tu.position - root.position); outward = Vector3.ProjectOnPlane(outward, up); outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : (left ? -right : right);
                float pad = st == ArmorStyle.Juggernaut ? 1.3f : st == ArmorStyle.Tyrant ? 1.35f : st == ArmorStyle.Bulwark ? 1.12f : 1f;
                // tilt the pad so its outer edge drops over the shoulder
                var padRot = Quaternion.AngleAxis(Vector3.Dot(outward, right) < 0f ? 18f : -18f, fwd) * Upright;
                Box("Pauldron", upper, tu.position + outward * (0.075f * s * bulk) + up * (0.065f * s), padRot, new Vector3(0.19f * pad * bulk, 0.12f * pad, 0.21f * pad * bulk), 0.055f, paint, true);
                Box("PauldronTrim", upper, tu.position + outward * (0.075f * s * bulk) + up * ((0.065f + 0.065f * pad) * s), padRot, new Vector3(0.15f * pad * bulk, 0.02f, 0.16f * pad * bulk), 0.008f, trim, true);
                Limb("UpperArm", upper, lower, 0.1f, 0.1f, 0.62f, 0.56f, 0.9f, trim, false);
                Ball("Elbow", lower, tl.position, 0.062f * bulk);
                if (th)
                {
                    Limb("Gauntlet", lower, hand, 0.13f, 0.14f, 0.8f, 0.5f, 0.82f, paint, true);
                    Vector3 d = (th.position - tl.position);
                    float len = d.magnitude; d /= Mathf.Max(len, 1e-4f);
                    var rot = Quaternion.LookRotation(d, Vector3.ProjectOnPlane(fwd, d).normalized);
                    Box("GauntletLight", lower, tl.position + d * (len * 0.5f) + up * (0.072f * s * bulk), rot, new Vector3(0.022f, 0.012f, len * 0.5f), 0.005f, glow, true);
                    Box("Hand", hand, th.position + d * (0.05f * s), rot, new Vector3(0.1f, 0.065f, 0.12f), 0.025f, trim, false);
                }
            }

            // ---- legs
            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                var upper = left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
                var lower = left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
                var foot = left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
                var tu = B(upper); var tl = B(lower); var tf = B(foot);
                if (!tu || !tl) continue;
                Limb("Thigh", upper, lower, 0.16f, 0.17f, 0.78f, 0.48f, 0.85f, paint, true);
                Ball("Knee", lower, tl.position, 0.066f * bulk);
                Box("Kneecap", lower, tl.position + fwd * (0.07f * s * bulk), Upright, new Vector3(0.11f * bulk, 0.11f, 0.06f), 0.025f, trim, true);
                if (tf)
                {
                    Limb("Shin", lower, foot, 0.13f, 0.15f, 0.8f, 0.5f, 0.8f, paint, true);
                    Vector3 shinMid = Vector3.Lerp(tl.position, tf.position, 0.42f);
                    Box("ShinLight", lower, shinMid + fwd * (0.078f * s * bulk), Vertical, new Vector3(0.022f, 0.01f, (tl.position - tf.position).magnitude * 0.4f), 0.004f, glow, true);
                    Box("Boot", foot, tf.position + fwd * (0.05f * s) + up * (0.01f * s), Upright, new Vector3(0.13f * bulk, 0.1f, 0.27f), 0.035f, trim, false);
                    Box("ToeCap", foot, tf.position + fwd * (0.16f * s) + up * (0.0f * s), Upright, new Vector3(0.12f * bulk, 0.06f, 0.08f), 0.025f, paint, false);
                }
            }
            ApplyBoneScales();
        }

        void BuildHelmet(ArmorStyle st, System.Func<float, float, float, Vector3> At, Quaternion Upright)
        {
            var H = HumanBodyBones.Head;
            switch (st)
            {
                case ArmorStyle.Hero:
                    Box("Helmet", H, At(0f, 1.665f, 0.0f), Upright, new Vector3(0.24f, 0.28f, 0.29f), 0.09f, paint, false);
                    Box("Visor", H, At(0f, 1.67f, 0.128f), Upright, new Vector3(0.2f, 0.045f, 0.05f), 0.016f, glow, false);
                    Box("VisorBar", H, At(0f, 1.62f, 0.125f), Upright, new Vector3(0.035f, 0.07f, 0.05f), 0.012f, glow, false);
                    Box("Faceplate", H, At(0f, 1.59f, 0.115f), Upright, new Vector3(0.16f, 0.07f, 0.06f), 0.025f, trim, false);
                    Box("Crest", H, At(0f, 1.8f, -0.01f), Upright, new Vector3(0.025f, 0.06f, 0.22f), 0.01f, trim, false);
                    break;
                case ArmorStyle.Scrapper:
                    Box("Helmet", H, At(0f, 1.66f, 0.0f), Upright, new Vector3(0.26f, 0.26f, 0.27f), 0.115f, paint, false);
                    Box("Eye", H, At(0.052f, 1.67f, 0.128f), Upright, new Vector3(0.055f, 0.032f, 0.03f), 0.012f, glow, false);
                    Box("Eye", H, At(-0.052f, 1.67f, 0.128f), Upright, new Vector3(0.055f, 0.032f, 0.03f), 0.012f, glow, false);
                    Box("Jaw", H, At(0f, 1.585f, 0.09f), Upright, new Vector3(0.17f, 0.06f, 0.1f), 0.025f, trim, false);
                    break;
                case ArmorStyle.Hexer:
                    Box("Hood", H, At(0f, 1.685f, -0.01f), Upright, new Vector3(0.24f, 0.32f, 0.28f), 0.1f, paint, false);
                    Box("Visor", H, At(0f, 1.66f, 0.122f), Upright, new Vector3(0.15f, 0.12f, 0.04f), 0.03f, glow, false);
                    Box("Antenna", H, At(0f, 1.88f, -0.05f), Quaternion.LookRotation(up, -fwd), new Vector3(0.014f, 0.014f, 0.14f), 0.006f, trim, false);
                    Box("AntennaTip", H, At(0f, 1.955f, -0.05f), Upright, new Vector3(0.03f, 0.03f, 0.03f), 0.014f, glow, false);
                    break;
                case ArmorStyle.Bulwark:
                    Box("Helmet", H, At(0f, 1.675f, -0.005f), Upright, new Vector3(0.27f, 0.27f, 0.3f), 0.1f, paint, false);
                    Box("Visor", H, At(0f, 1.655f, 0.13f), Upright, new Vector3(0.22f, 0.1f, 0.05f), 0.03f, glow, false);
                    Box("Brim", H, At(0f, 1.72f, 0.12f), Upright, new Vector3(0.26f, 0.03f, 0.1f), 0.012f, trim, false);
                    break;
                case ArmorStyle.Juggernaut:
                    Box("Helm", H, At(0f, 1.65f, 0.01f), Upright, new Vector3(0.3f, 0.24f, 0.3f), 0.06f, paint, false);
                    Box("Cyclops", H, At(0f, 1.66f, 0.155f), Upright, new Vector3(0.09f, 0.05f, 0.04f), 0.02f, glow, false);
                    Box("Grille", H, At(0f, 1.585f, 0.14f), Upright, new Vector3(0.18f, 0.06f, 0.05f), 0.015f, trim, false);
                    break;
                default:
                    Box("Helm", H, At(0f, 1.66f, 0.0f), Upright, new Vector3(0.27f, 0.27f, 0.3f), 0.08f, paint, false);
                    Box("Visor", H, At(0f, 1.665f, 0.135f), Upright, new Vector3(0.2f, 0.045f, 0.05f), 0.016f, glow, false);
                    for (int i = -1; i <= 1; i++)
                    {
                        var rot = Quaternion.AngleAxis(i * 24f, fwd) * Quaternion.LookRotation(up, -fwd);
                        Box("CrownSpike", H, At(i * 0.08f, 1.84f + (i == 0 ? 0.03f : 0f), 0.0f), rot, new Vector3(0.035f, 0.05f, 0.14f), 0.012f, trim, false, 0.35f);
                    }
                    break;
            }
            if (st != ArmorStyle.Hexer)
            {
                Box("EarL", H, At(0.135f, 1.65f, 0f), Upright, new Vector3(0.045f, 0.1f, 0.1f), 0.02f, trim, false);
                Box("EarR", H, At(-0.135f, 1.65f, 0f), Upright, new Vector3(0.045f, 0.1f, 0.1f), 0.02f, trim, false);
                Box("EarLightL", H, At(0.16f, 1.65f, 0f), Upright, new Vector3(0.012f, 0.05f, 0.05f), 0.005f, glow, false);
                Box("EarLightR", H, At(-0.16f, 1.65f, 0f), Upright, new Vector3(0.012f, 0.05f, 0.05f), 0.005f, glow, false);
            }
        }

        void Box(string name, HumanBodyBones bone, Vector3 worldCenter, Quaternion worldRot, Vector3 size01, float radius01, Material mat, bool detachable, float taper = 1f)
        {
            var t = B(bone);
            if (!t) return;
            Vector3 size = size01 * s;
            var mesh = MeshKit.RoundedBox(size, radius01 * s, 3, taper, 0.45f * s);
            Spawn(name, bone, t, mesh, mat, worldCenter, worldRot, detachable);
        }

        void Ball(string name, HumanBodyBones bone, Vector3 worldCenter, float radius01)
        {
            var t = B(bone);
            if (!t) return;
            float r = radius01 * s;
            var mesh = MeshKit.RoundedBox(Vector3.one * (2f * r), r * 0.995f, 4);
            Spawn(name, bone, t, mesh, joint, worldCenter, Quaternion.LookRotation(fwd, up), false);
        }

        void Limb(string name, HumanBodyBones bone, HumanBodyBones child, float w, float d, float lenFrac, float centerFrac, float taper, Material mat, bool detachable)
        {
            var a = B(bone); var b = B(child);
            if (!a || !b) return;
            Vector3 dir = b.position - a.position;
            float len = dir.magnitude;
            if (len < 1e-4f) return;
            dir /= len;
            Vector3 upHint = Vector3.ProjectOnPlane(fwd, dir);
            if (upHint.sqrMagnitude < 1e-4f) upHint = Vector3.ProjectOnPlane(up, dir);
            var rot = Quaternion.LookRotation(dir, upHint.normalized);
            Vector3 size = new Vector3(w * s * bulk, d * s * bulk, len * lenFrac);
            var mesh = MeshKit.RoundedBox(size, Mathf.Min(size.x, size.y) * 0.36f, 3, taper, 0.45f * s);
            Spawn(name, bone, a, mesh, mat, a.position + dir * (len * centerFrac), rot, detachable);
        }

        void Spawn(string name, HumanBodyBones bone, Transform parent, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, bool detachable)
        {
            var go = new GameObject(name);
            go.layer = gameObject.layer;
            go.transform.SetPositionAndRotation(pos, rot);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            go.transform.SetParent(parent, true);
            pieces.Add(new Piece { t = go.transform, bone = bone, detachable = detachable });
            Renderers.Add(mr);
        }

        // ---------------------------------------------------------------- runtime look

        void LateUpdate() => ApplyBoneScales();

        void ApplyBoneScales()
        {
            foreach (var kv in boneScale)
            {
                var t = B(kv.Key);
                if (t) t.localScale = kv.Value;
            }
        }

        /// <summary>Hit flash on the paint/suit and the intensity of the emissive accents.</summary>
        public void SetEmission(Color flash, float glowIntensity)
        {
            if (paint) paint.SetColor(EmissionId, flash);
            if (suit) suit.SetColor(EmissionId, flash * 0.6f);
            if (glow) glow.SetColor(EmissionId, glowBase * glowIntensity + flash * 0.5f);
        }

        public void SetGlowColor(Color hdr) => glowBase = hdr;

        // ---------------------------------------------------------------- damage

        /// <summary>Knocks a random loose plate off. Returns false when nothing is left to chip.</summary>
        public bool Chip(Vector3 hitDir, float force)
        {
            int n = 0;
            foreach (var p in pieces) if (p.detachable && !p.detached && p.t) n++;
            if (n == 0) return false;
            int pick = Random.Range(0, n);
            foreach (var p in pieces)
            {
                if (!p.detachable || p.detached || !p.t) continue;
                if (pick-- > 0) continue;
                Detach(p, hitDir * force + Vector3.up * (force * 0.6f) + Random.insideUnitSphere * 2f, 1.3f);
                FX.Sparks(p.t.position, hitDir, true, false);
                return true;
            }
            return false;
        }

        void Detach(Piece p, Vector3 velocity, float massScale)
        {
            p.detached = true;
            Renderers.Remove(p.t.GetComponent<Renderer>());
            var go = p.t.gameObject;
            p.t.SetParent(FX.Root, true);
            Debris.MakePhysical(go, velocity, Random.insideUnitSphere * 14f, massScale);
        }

        /// <summary>Pops the head off: the helmet flies as one rigid body and the neck sprays sparks.</summary>
        public void Decapitate(Vector3 impulse)
        {
            var head = B(HumanBodyBones.Head);
            if (!head || boneScale.TryGetValue(HumanBodyBones.Head, out var hs) && hs.x < 0.01f) return;
            Sever(HumanBodyBones.Head, head, impulse, "Head");
            var neck = B(HumanBodyBones.Neck) ?? head.parent;
            SparkSpurt.Attach(neck, Palette.glow, 2.2f);
        }

        /// <summary>Tears a forearm off at the elbow.</summary>
        public void SeverArm(bool left, Vector3 impulse)
        {
            var lower = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            var t = B(lower);
            if (!t || boneScale.TryGetValue(lower, out var sc) && sc.x < 0.01f) return;
            Sever(lower, t, impulse, "Forearm", left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            SparkSpurt.Attach(B(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm) ?? t.parent, Palette.glow, 1.6f);
        }

        void Sever(HumanBodyBones bone, Transform t, Vector3 impulse, string label, HumanBodyBones also = HumanBodyBones.LastBone)
        {
            var group = new List<Piece>();
            foreach (var p in pieces)
                if (!p.detached && p.t && (p.bone == bone || p.bone == also)) group.Add(p);
            if (group.Count == 0) { boneScale[bone] = Vector3.one * 0.001f; ApplyBoneScales(); return; }
            var holder = new GameObject("Severed" + label);
            holder.layer = Layers.Debris;
            Vector3 c = Vector3.zero;
            foreach (var p in group) c += p.t.position;
            holder.transform.position = c / group.Count;
            holder.transform.rotation = t.rotation;
            FX.Adopt(holder);
            var bounds = new Bounds(holder.transform.position, Vector3.zero);
            foreach (var p in group)
            {
                p.detached = true;
                Renderers.Remove(p.t.GetComponent<Renderer>());
                p.t.SetParent(holder.transform, true);
                var r = p.t.GetComponent<Renderer>();
                if (r) bounds.Encapsulate(r.bounds);
            }
            var col = holder.AddComponent<BoxCollider>();
            col.center = holder.transform.InverseTransformPoint(bounds.center);
            Vector3 ls = holder.transform.InverseTransformVector(bounds.size);
            col.size = new Vector3(Mathf.Max(0.05f, Mathf.Abs(ls.x) * 0.8f), Mathf.Max(0.05f, Mathf.Abs(ls.y) * 0.8f), Mathf.Max(0.05f, Mathf.Abs(ls.z) * 0.8f));
            // collapse the bone only after the plates have been re-parented, or they would shrink with it
            boneScale[bone] = Vector3.one * 0.001f;
            ApplyBoneScales();
            Debris.MakePhysical(holder, impulse, Random.insideUnitSphere * 18f, 3f, false);
            FX.Sparks(holder.transform.position, impulse.sqrMagnitude > 0.01f ? impulse.normalized : Vector3.up, true, true);
        }

        /// <summary>Everything that is still bolted on comes off (Void Nova, boss deaths, explosions).</summary>
        public void BlowApart(Vector3 center, float force)
        {
            foreach (var p in pieces)
            {
                if (p.detached || !p.t || Random.value < 0.35f) continue;
                Vector3 d = p.t.position - center;
                d = d.sqrMagnitude > 1e-4f ? d.normalized : Random.onUnitSphere;
                Detach(p, (d + Vector3.up * 0.8f) * force * Random.Range(0.6f, 1.2f), 1f);
            }
        }

        /// <summary>Lights die on a wrecked robot (with a little flicker on the way out).</summary>
        public void PowerDown(float u)
        {
            float flicker = u < 0.6f && Random.value < 0.3f ? 1.2f : 0f;
            SetEmission(Color.black, Mathf.Max(0f, 1f - u) * 0.5f + flicker * (1f - u));
        }
    }

    /// <summary>Detached armour, severed limbs and dropped weapons: real rigid bodies that clatter, then sink away.</summary>
    public class Debris : MonoBehaviour
    {
        static readonly List<Debris> live = new List<Debris>();
        static float lastClatter;
        const int MaxLive = 140;

        float life, age, sinkStart;
        Vector3 baseScale;

        public static Rigidbody MakePhysical(GameObject go, Vector3 velocity, Vector3 angular, float massScale, bool addCollider = true)
        {
            go.layer = Layers.Debris;
            foreach (Transform c in go.GetComponentsInChildren<Transform>()) c.gameObject.layer = Layers.Debris;
            if (addCollider && !go.GetComponent<Collider>())
            {
                var bc = go.AddComponent<BoxCollider>();
                var mf = go.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh)
                {
                    bc.center = mf.sharedMesh.bounds.center;
                    bc.size = Vector3.Max(mf.sharedMesh.bounds.size, Vector3.one * 0.02f);
                }
            }
            var rb = go.GetComponent<Rigidbody>();
            if (!rb) rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.2f, massScale);
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.08f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearVelocity = velocity;
            rb.angularVelocity = angular;
            var d = go.GetComponent<Debris>();
            if (!d) d = go.AddComponent<Debris>();
            d.life = Random.Range(6f, 9f);
            d.baseScale = go.transform.localScale;
            live.Add(d);
            while (live.Count > MaxLive)
            {
                var old = live[0];
                live.RemoveAt(0);
                if (old) Destroy(old.gameObject);
            }
            return rb;
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age > life)
            {
                if (sinkStart <= 0f)
                {
                    sinkStart = age;
                    var rb = GetComponent<Rigidbody>();
                    if (rb) { rb.isKinematic = true; }
                    foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
                }
                float u = Mathf.Clamp01((age - sinkStart) / 0.8f);
                transform.localScale = baseScale * (1f - u);
                transform.position += Vector3.down * (Time.deltaTime * 0.15f);
                if (u >= 1f) Destroy(gameObject);
            }
            if (transform.position.y < -40f) Destroy(gameObject);
        }

        void OnCollisionEnter(Collision c)
        {
            float v = c.relativeVelocity.magnitude;
            if (v < 2.2f || Time.unscaledTime - lastClatter < 0.045f) return;
            lastClatter = Time.unscaledTime;
            Sfx.Clatter(Mathf.Clamp01(v / 12f));
            if (v > 7f && c.contactCount > 0) FX.Burst(c.GetContact(0).point, new Color(1f, 0.85f, 0.5f), new Color(1f, 0.5f, 0.15f), 6, new Vector2(2f, 5f), new Vector2(0.02f, 0.04f), new Vector2(0.1f, 0.25f), 1.5f, true, 0.02f);
        }

        void OnDestroy() => live.Remove(this);
    }

    /// <summary>Sparks and a flickering light pouring out of a severed joint.</summary>
    public class SparkSpurt : MonoBehaviour
    {
        ParticleSystem ps;
        Light flick;
        float life, age;

        public static void Attach(Transform at, Color glow, float duration)
        {
            if (!at) return;
            var go = new GameObject("FX_SparkSpurt");
            go.transform.SetParent(at, false);
            var sp = go.AddComponent<SparkSpurt>();
            sp.life = duration;
            sp.ps = go.AddComponent<ParticleSystem>();
            sp.ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sp.ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.6f), new Color(1f, 0.55f, 0.15f));
            main.gravityModifier = 1.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var em = sp.ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 6, 12, 0, 0.07f) });
            var sh = sp.ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.03f;
            var col = sp.ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FX.GlowMat;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 1.5f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            sp.ps.Play();
            sp.flick = go.AddComponent<Light>();
            sp.flick.type = LightType.Point;
            sp.flick.range = 3f;
            sp.flick.color = Color.Lerp(new Color(1f, 0.75f, 0.4f), glow, 0.3f);
            sp.flick.shadows = LightShadows.None;
        }

        void Update()
        {
            age += Time.deltaTime;
            if (flick) flick.intensity = age < life ? (Random.value < 0.5f ? Random.Range(1f, 4f) : 0.2f) : 0f;
            if (age >= life && ps && ps.isEmitting) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            if (age >= life + 0.6f) Destroy(gameObject);
        }
    }
}
