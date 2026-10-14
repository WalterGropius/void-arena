using System.Collections.Generic;
using UnityEngine;

namespace HackAndSlash
{
    /// <summary>
    /// Procedural hard-surface meshes: bevelled boxes with analytic normals (crisp specular edges on the armour),
    /// tapered limb shells, planar-UV discs, world-scale UV walls/buildings and soft light cones.
    /// Usable at edit time (scene builder) and at runtime (armour kits).
    /// </summary>
    public static class MeshKit
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        /// <summary>Cached rounded box. size is the full extent, radius the bevel, taper scales X/Y at the +Z end.</summary>
        public static Mesh RoundedBox(Vector3 size, float radius, int seg = 3, float taper = 1f, float uvScale = 0.5f)
        {
            string key = string.Format("rb{0:F3}_{1:F3}_{2:F3}_{3:F3}_{4}_{5:F2}_{6:F2}", size.x, size.y, size.z, radius, seg, taper, uvScale);
            if (cache.TryGetValue(key, out var m) && m) return m;
            m = BuildRoundedBox(size, radius, seg, taper, uvScale);
            m.name = "RoundedBox";
            cache[key] = m;
            return m;
        }

        public static void ClearCache()
        {
            foreach (var m in cache.Values) if (m) Object.Destroy(m);
            cache.Clear();
        }

        public static Mesh BuildRoundedBox(Vector3 size, float radius, int seg, float taper, float uvScale)
        {
            Vector3 h = size * 0.5f;
            radius = Mathf.Clamp(radius, 0.0005f, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.999f);
            seg = Mathf.Max(1, seg);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // per-axis sample positions, spaced so the bevel arc gets uniform angular steps
            float[] Axis(float half)
            {
                var a = new List<float>();
                float inner = half - radius;
                for (int k = seg; k >= 0; k--) a.Add(-(inner + radius * Mathf.Tan(k / (float)seg * Mathf.PI * 0.25f)));
                for (int k = 0; k <= seg; k++) a.Add(inner + radius * Mathf.Tan(k / (float)seg * Mathf.PI * 0.25f));
                return a.ToArray();
            }
            float[] ax = Axis(h.x), ay = Axis(h.y), az = Axis(h.z);
            Vector3 innerHalf = h - Vector3.one * radius;

            // face: normal axis n (0..2), sign s; u axis and v axis
            void Face(int n, float s)
            {
                int u = (n + 1) % 3, v = (n + 2) % 3;
                if (s < 0f) { int t = u; u = v; v = t; } // keep winding consistent (outward facing)
                float[] au = u == 0 ? ax : u == 1 ? ay : az;
                float[] av = v == 0 ? ax : v == 1 ? ay : az;
                int start = verts.Count;
                for (int j = 0; j < av.Length; j++)
                for (int i = 0; i < au.Length; i++)
                {
                    Vector3 p = Vector3.zero;
                    p[n] = s * h[n];
                    p[u] = au[i];
                    p[v] = av[j];
                    Vector3 c = new Vector3(Mathf.Clamp(p.x, -innerHalf.x, innerHalf.x), Mathf.Clamp(p.y, -innerHalf.y, innerHalf.y), Mathf.Clamp(p.z, -innerHalf.z, innerHalf.z));
                    Vector3 nn = (p - c).normalized;
                    Vector3 pos = c + nn * radius;
                    float tz = Mathf.Lerp(1f, taper, (pos.z / size.z) + 0.5f);
                    pos.x *= tz; pos.y *= tz;
                    verts.Add(pos);
                    norms.Add(nn);
                    uvs.Add(new Vector2(p[u] / uvScale, p[v] / uvScale));
                }
                int w = au.Length;
                for (int j = 0; j < av.Length - 1; j++)
                for (int i = 0; i < w - 1; i++)
                {
                    int a = start + j * w + i, b = a + 1, c2 = a + w, d = c2 + 1;
                    tris.Add(a); tris.Add(c2); tris.Add(b);
                    tris.Add(b); tris.Add(c2); tris.Add(d);
                }
            }
            Face(0, 1f); Face(0, -1f); Face(1, 1f); Face(1, -1f); Face(2, 1f); Face(2, -1f);

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            FixWinding(mesh);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Flips any triangle whose geometric normal disagrees with its vertex normals (makes generators winding-agnostic).</summary>
        public static void FixWinding(Mesh mesh)
        {
            var v = mesh.vertices;
            var n = mesh.normals;
            var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 g = Vector3.Cross(b - a, c - a);
                Vector3 avg = n[t[i]] + n[t[i + 1]] + n[t[i + 2]];
                if (Vector3.Dot(g, avg) < 0f) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
            }
            mesh.triangles = t;
        }

        /// <summary>Flat disc (Y up) with planar world-space UVs (uv = xz / tile) and concentric rings for vertex density.</summary>
        public static Mesh Disc(float radius, int segments, int rings, float tile, string name = "Disc")
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            verts.Add(Vector3.zero); uvs.Add(Vector2.zero);
            for (int r = 1; r <= rings; r++)
            {
                float rr = radius * r / rings;
                for (int s = 0; s < segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    var p = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                    verts.Add(p);
                    uvs.Add(new Vector2(p.x / tile, p.z / tile));
                }
            }
            for (int s = 0; s < segments; s++)
            {
                tris.Add(0); tris.Add(1 + (s + 1) % segments); tris.Add(1 + s);
            }
            for (int r = 1; r < rings; r++)
            {
                int a0 = 1 + (r - 1) * segments, b0 = 1 + r * segments;
                for (int s = 0; s < segments; s++)
                {
                    int s1 = (s + 1) % segments;
                    tris.Add(a0 + s); tris.Add(a0 + s1); tris.Add(b0 + s);
                    tris.Add(a0 + s1); tris.Add(b0 + s1); tris.Add(b0 + s);
                }
            }
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            FixWinding(mesh);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Thick ring wall (parapet) from inner to outer radius, y0..y1, with world-scale UVs.</summary>
        public static Mesh RingWall(float inner, float outer, float y0, float y1, int segments, float tile, string name = "RingWall")
        {
            var b = new Builder();
            float circumference = Mathf.PI * 2f * outer;
            for (int s = 0; s < segments; s++)
            {
                float a0 = s / (float)segments * Mathf.PI * 2f, a1 = (s + 1) / (float)segments * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                float u0 = s / (float)segments * circumference / tile, u1 = (s + 1) / (float)segments * circumference / tile;
                // outer face
                b.Quad(d0 * outer + Vector3.up * y0, d1 * outer + Vector3.up * y0, d1 * outer + Vector3.up * y1, d0 * outer + Vector3.up * y1,
                    d0, d1, d1, d0, new Vector2(u0, y0 / tile), new Vector2(u1, y0 / tile), new Vector2(u1, y1 / tile), new Vector2(u0, y1 / tile));
                // inner face
                b.Quad(d1 * inner + Vector3.up * y0, d0 * inner + Vector3.up * y0, d0 * inner + Vector3.up * y1, d1 * inner + Vector3.up * y1,
                    -d1, -d0, -d0, -d1, new Vector2(u1, y0 / tile), new Vector2(u0, y0 / tile), new Vector2(u0, y1 / tile), new Vector2(u1, y1 / tile));
                // top
                b.Quad(d0 * inner + Vector3.up * y1, d0 * outer + Vector3.up * y1, d1 * outer + Vector3.up * y1, d1 * inner + Vector3.up * y1,
                    Vector3.up, Vector3.up, Vector3.up, Vector3.up, new Vector2(u0, 0f), new Vector2(u0, (outer - inner) / tile), new Vector2(u1, (outer - inner) / tile), new Vector2(u1, 0f));
            }
            return b.ToMesh(name);
        }

        /// <summary>Outward-facing cylinder side (building facade), world-scale UVs: u = arc length / tileU, v = y / tileV.</summary>
        public static Mesh CylinderSide(float radius, float y0, float y1, int segments, float tileU, float tileV, string name = "Facade")
        {
            var b = new Builder();
            float circ = Mathf.PI * 2f * radius;
            for (int s = 0; s < segments; s++)
            {
                float a0 = s / (float)segments * Mathf.PI * 2f, a1 = (s + 1) / (float)segments * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                float u0 = s / (float)segments * circ / tileU, u1 = (s + 1) / (float)segments * circ / tileU;
                b.Quad(d0 * radius + Vector3.up * y0, d1 * radius + Vector3.up * y0, d1 * radius + Vector3.up * y1, d0 * radius + Vector3.up * y1,
                    d0, d1, d1, d0, new Vector2(u0, y0 / tileV), new Vector2(u1, y0 / tileV), new Vector2(u1, y1 / tileV), new Vector2(u0, y1 / tileV));
            }
            return b.ToMesh(name);
        }

        /// <summary>Light shaft: open cone along +Z from apex (radius r0) to length (radius r1); vertex alpha fades with length.</summary>
        public static Mesh Cone(float r0, float r1, float length, int segments, string name = "Beam")
        {
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            const int rings = 8;
            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings;
                float rad = Mathf.Lerp(r0, r1, t);
                float alpha = Mathf.Pow(1f - t, 1.6f) * Mathf.SmoothStep(0f, 1f, t * 6f);
                for (int s = 0; s <= segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    verts.Add(d * rad + Vector3.forward * (length * t));
                    norms.Add(d);
                    cols.Add(new Color(1f, 1f, 1f, alpha));
                    uvs.Add(new Vector2(s / (float)segments, t));
                }
            }
            int w = segments + 1;
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * w + s, b = a + 1, c = a + w, d = c + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetColors(cols);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Generic triangle soup builder for world-UV architecture.</summary>
        public class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<Color> col = new List<Color>();
            public readonly List<int> t = new List<int>();
            public Color color = Color.white;

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                n.Add(na); n.Add(nb); n.Add(nc); n.Add(nd);
                uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                col.Add(color); col.Add(color); col.Add(color); col.Add(color);
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
                t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }

            /// <summary>Axis-aligned box (optionally rotated about Y) with world-scale UVs on each face; uvOffset breaks up tiling.</summary>
            public void Box(Vector3 center, Vector3 size, float yawDeg, Vector2 tile, Vector2 uvOffset, bool top = true, bool bottom = false)
            {
                var rot = Quaternion.Euler(0f, yawDeg, 0f);
                Vector3 h = size * 0.5f;
                Vector3 P(float x, float y, float z) => center + rot * new Vector3(x, y, z);
                Vector3 R(Vector3 d) => rot * d;
                float y0 = center.y - h.y, y1 = center.y + h.y;
                // +Z face
                Quad(P(-h.x, -h.y, h.z), P(h.x, -h.y, h.z), P(h.x, h.y, h.z), P(-h.x, h.y, h.z), R(Vector3.forward), R(Vector3.forward), R(Vector3.forward), R(Vector3.forward),
                    UV(0f, y0), UV(size.x, y0), UV(size.x, y1), UV(0f, y1));
                // -Z face
                Quad(P(h.x, -h.y, -h.z), P(-h.x, -h.y, -h.z), P(-h.x, h.y, -h.z), P(h.x, h.y, -h.z), R(Vector3.back), R(Vector3.back), R(Vector3.back), R(Vector3.back),
                    UV(size.x + size.z, y0), UV(2f * size.x + size.z, y0), UV(2f * size.x + size.z, y1), UV(size.x + size.z, y1));
                // +X face
                Quad(P(h.x, -h.y, h.z), P(h.x, -h.y, -h.z), P(h.x, h.y, -h.z), P(h.x, h.y, h.z), R(Vector3.right), R(Vector3.right), R(Vector3.right), R(Vector3.right),
                    UV(size.x, y0), UV(size.x + size.z, y0), UV(size.x + size.z, y1), UV(size.x, y1));
                // -X face
                Quad(P(-h.x, -h.y, -h.z), P(-h.x, -h.y, h.z), P(-h.x, h.y, h.z), P(-h.x, h.y, -h.z), R(Vector3.left), R(Vector3.left), R(Vector3.left), R(Vector3.left),
                    UV(2f * size.x + size.z, y0), UV(2f * size.x + 2f * size.z, y0), UV(2f * size.x + 2f * size.z, y1), UV(2f * size.x + size.z, y1));
                if (top)
                    Quad(P(-h.x, h.y, h.z), P(h.x, h.y, h.z), P(h.x, h.y, -h.z), P(-h.x, h.y, -h.z), Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                        new Vector2(0f, 0f), new Vector2(size.x / tile.x, 0f), new Vector2(size.x / tile.x, size.z / tile.x), new Vector2(0f, size.z / tile.x));
                if (bottom)
                    Quad(P(-h.x, -h.y, -h.z), P(h.x, -h.y, -h.z), P(h.x, -h.y, h.z), P(-h.x, -h.y, h.z), Vector3.down, Vector3.down, Vector3.down, Vector3.down,
                        new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));

                Vector2 UV(float horizontal, float y) => new Vector2(horizontal / tile.x + uvOffset.x, y / tile.y + uvOffset.y);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v);
                mesh.SetNormals(n);
                mesh.SetUVs(0, uv);
                mesh.SetColors(col);
                mesh.SetTriangles(t, 0);
                FixWinding(mesh);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }
    }
}
