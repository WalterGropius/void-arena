using UnityEngine;
using UnityEngine.Rendering;

namespace HackAndSlash
{
    /// <summary>
    /// The Knight's long coat-cape: a verlet cloth grid pinned across the shoulder blades, colliding with spheres on
    /// the spine and legs and with the floor, pushed by the storm's wind. Simulated on scaled time, so it hangs in the
    /// air during slow motion. Rendered as a double-sided dynamic mesh with a tattered hem (alpha clip).
    /// </summary>
    public class Cape : MonoBehaviour
    {
        public int columns = 8, rows = 12;
        public float width = 0.62f, length = 1.32f;
        public Vector3 wind = new Vector3(-1.8f, 0f, 1.1f);

        Animator anim;
        Transform root, lShoulder, rShoulder, chest, hips, lThigh, rThigh, lShin, rShin, lUpperArm, rUpperArm;
        Vector3[] pos, prev, verts, normals;
        Vector2[] uvs;
        Mesh mesh;
        float restX, restY, accum;
        bool ready;
        const float Step = 1f / 90f;

        public static Cape Attach(Animator animator, Transform characterRoot, Material mat)
        {
            var go = new GameObject("Cape");
            go.layer = characterRoot.gameObject.layer;
            var cape = go.AddComponent<Cape>();
            cape.anim = animator;
            cape.root = characterRoot;
            cape.mesh = new Mesh { name = "CapeMesh" };
            cape.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = cape.mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            cape.Init();
            return cape;
        }

        void Init()
        {
            lShoulder = anim.GetBoneTransform(HumanBodyBones.LeftShoulder) ?? anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rShoulder = anim.GetBoneTransform(HumanBodyBones.RightShoulder) ?? anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            lUpperArm = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rUpperArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            chest = anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.Spine);
            hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            lThigh = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); rThigh = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            lShin = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg); rShin = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            if (!lShoulder || !rShoulder || !chest || !hips) { enabled = false; return; }
            int n = columns * rows;
            pos = new Vector3[n]; prev = new Vector3[n]; verts = new Vector3[n]; normals = new Vector3[n]; uvs = new Vector2[n];
            restX = width / (columns - 1);
            restY = length / (rows - 1);
            var tris = new int[(columns - 1) * (rows - 1) * 6];
            int k = 0;
            for (int y = 0; y < rows - 1; y++)
            for (int x = 0; x < columns - 1; x++)
            {
                int a = y * columns + x, b = a + 1, c = a + columns, d = c + 1;
                tris[k++] = a; tris[k++] = c; tris[k++] = b;
                tris[k++] = b; tris[k++] = c; tris[k++] = d;
            }
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
                uvs[y * columns + x] = new Vector2(x / (float)(columns - 1), 1f - y / (float)(rows - 1)); // v = 1 at the shoulders, 0 at the hem
            Reset();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 6f);
            ready = true;
        }

        /// <summary>Drops the cloth straight down from the pins (respawns, teleports).</summary>
        public void Reset()
        {
            if (pos == null) return;
            Vector3 down = -root.up, back = -root.forward;
            for (int x = 0; x < columns; x++)
            {
                Vector3 top = Pin(x);
                for (int y = 0; y < rows; y++)
                {
                    int i = y * columns + x;
                    pos[i] = prev[i] = top + down * (restY * y) + back * (0.02f * y);
                }
            }
        }

        Vector3 Pin(int x)
        {
            // across the upper back, a hand's width below the shoulder line and behind the shoulder blades
            float u = x / (float)(columns - 1);
            Vector3 l = lShoulder.position, r = rShoulder.position;
            Vector3 p = Vector3.Lerp(l, r, 0.12f + 0.76f * u);
            float s = root.lossyScale.y;
            return p - root.forward * (0.15f * s) + root.up * (-0.04f * s);
        }

        void LateUpdate()
        {
            if (!ready) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            accum += dt;
            int steps = 0;
            while (accum >= Step && steps < 6)
            {
                Simulate(Step);
                accum -= Step;
                steps++;
            }
            if (steps == 6) accum = 0f;
            // teleport guard (respawn, scripted moves)
            if ((pos[0] - Pin(0)).sqrMagnitude > 4f) Reset();
            for (int x = 0; x < columns; x++) pos[x] = Pin(x);
            Vector3 c = transform.position;
            for (int i = 0; i < pos.Length; i++) verts[i] = pos[i] - c;
            transform.rotation = Quaternion.identity;
            transform.position = root.position;
            c = transform.position;
            for (int i = 0; i < pos.Length; i++) verts[i] = pos[i] - c;
            mesh.vertices = verts;
            mesh.RecalculateNormals();
        }

        void Simulate(float h)
        {
            Vector3 g = Physics.gravity;
            float t = Time.time;
            Vector3 gust = wind * (0.7f + 0.5f * Mathf.PerlinNoise(t * 0.7f, 0.3f)) + Vector3.up * (Mathf.PerlinNoise(0.6f, t * 1.3f) - 0.5f) * 0.8f;
            for (int i = columns; i < pos.Length; i++)
            {
                Vector3 v = (pos[i] - prev[i]) * 0.985f;
                prev[i] = pos[i];
                float flutter = 0.6f + 0.4f * Mathf.PerlinNoise(i * 0.37f, t * 2.1f);
                pos[i] += v + (g + gust * flutter) * (h * h);
            }
            for (int x = 0; x < columns; x++) { pos[x] = Pin(x); prev[x] = pos[x]; }

            float s = root.lossyScale.y;
            for (int it = 0; it < 6; it++)
            {
                for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    int i = y * columns + x;
                    if (x + 1 < columns) Link(i, i + 1, restX * s, y == 0);
                    if (y + 1 < rows) Link(i, i + columns, restY * s, y == 0);
                    if (x + 1 < columns && y + 1 < rows) Link(i, i + columns + 1, Mathf.Sqrt(restX * restX + restY * restY) * s, y == 0);
                    if (x > 0 && y + 1 < rows) Link(i, i + columns - 1, Mathf.Sqrt(restX * restX + restY * restY) * s, y == 0);
                    if (y + 2 < rows) Link(i, i + columns * 2, restY * 2f * s, y == 0);
                }
                Collide(s);
            }
        }

        void Link(int a, int b, float rest, bool pinnedA)
        {
            Vector3 d = pos[b] - pos[a];
            float m = d.magnitude;
            if (m < 1e-5f) return;
            Vector3 corr = d * ((m - rest) / m);
            bool aPinned = a < columns, bPinned = b < columns;
            if (aPinned && bPinned) return;
            if (aPinned) pos[b] -= corr;
            else if (bPinned) pos[a] += corr;
            else { pos[a] += corr * 0.5f; pos[b] -= corr * 0.5f; }
        }

        void Collide(float s)
        {
            Vector3 hp = hips.position, cp = chest.position;
            Vector3 spineMid = (hp + cp) * 0.5f;
            for (int i = columns; i < pos.Length; i++)
            {
                Push(ref pos[i], cp, 0.2f * s);
                Push(ref pos[i], spineMid, 0.19f * s);
                Push(ref pos[i], hp, 0.2f * s);
                if (lThigh && lShin) { Push(ref pos[i], (lThigh.position + lShin.position) * 0.5f, 0.12f * s); Push(ref pos[i], lShin.position, 0.1f * s); }
                if (rThigh && rShin) { Push(ref pos[i], (rThigh.position + rShin.position) * 0.5f, 0.12f * s); Push(ref pos[i], rShin.position, 0.1f * s); }
                if (pos[i].y < 0.03f) pos[i].y = 0.03f;
            }
        }

        static void Push(ref Vector3 p, Vector3 c, float r)
        {
            Vector3 d = p - c;
            float m = d.sqrMagnitude;
            if (m >= r * r || m < 1e-8f) return;
            p = c + d * (r / Mathf.Sqrt(m));
        }

        void OnDestroy() { if (mesh) Destroy(mesh); }
    }
}
