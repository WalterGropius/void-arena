using System;
using System.Collections.Generic;
using UnityEngine;

namespace HackAndSlash
{
    /// <summary>Triangle soup with world-scale UVs. Pure data, so district layouts can be generated (and previewed) anywhere.</summary>
    public sealed class Geo
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();

        public int VertexCount => v.Count;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            n.Add(normal); n.Add(normal); n.Add(normal); n.Add(normal);
            uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
            // winding is fixed up against the normal when the mesh is built
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            n.Add(normal); n.Add(normal); n.Add(normal);
            uv.Add(ua); uv.Add(ub); uv.Add(uc);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        [Flags] public enum Faces { None = 0, PosX = 1, NegX = 2, PosY = 4, NegY = 8, PosZ = 16, NegZ = 32, Sides = PosX | NegX | PosZ | NegZ, NoBottom = Sides | PosY, All = 63 }

        /// <summary>
        /// Oriented box. UVs are world-scale: side faces map (horizontal metres, absolute height) / tile, so stacked
        /// boxes line up; the top maps local x/z. worldY offsets the v coordinate (absolute height of the box base).
        /// </summary>
        public void Box(Vector3 center, Vector3 size, Quaternion rot, Vector2 tile, Faces faces = Faces.All, Vector2 uvOffset = default, float worldY = float.NaN)
        {
            Vector3 h = size * 0.5f;
            Vector3 X = rot * Vector3.right, Y = rot * Vector3.up, Z = rot * Vector3.forward;
            Vector3 P(float x, float y, float z) => center + X * x + Y * y + Z * z;
            float y0 = float.IsNaN(worldY) ? center.y - h.y : worldY;
            float y1 = y0 + size.y;
            Vector2 U(float horiz, float y) => new Vector2(horiz / tile.x + uvOffset.x, y / tile.y + uvOffset.y);
            float sx = size.x, sz = size.z;
            if ((faces & Faces.PosZ) != 0) Quad(P(-h.x, -h.y, h.z), P(h.x, -h.y, h.z), P(h.x, h.y, h.z), P(-h.x, h.y, h.z), Z, U(0, y0), U(sx, y0), U(sx, y1), U(0, y1));
            if ((faces & Faces.NegZ) != 0) Quad(P(h.x, -h.y, -h.z), P(-h.x, -h.y, -h.z), P(-h.x, h.y, -h.z), P(h.x, h.y, -h.z), -Z, U(sx + sz, y0), U(2 * sx + sz, y0), U(2 * sx + sz, y1), U(sx + sz, y1));
            if ((faces & Faces.PosX) != 0) Quad(P(h.x, -h.y, h.z), P(h.x, -h.y, -h.z), P(h.x, h.y, -h.z), P(h.x, h.y, h.z), X, U(sx, y0), U(sx + sz, y0), U(sx + sz, y1), U(sx, y1));
            if ((faces & Faces.NegX) != 0) Quad(P(-h.x, -h.y, -h.z), P(-h.x, -h.y, h.z), P(-h.x, h.y, h.z), P(-h.x, h.y, -h.z), -X, U(2 * sx + sz, y0), U(2 * sx + 2 * sz, y0), U(2 * sx + 2 * sz, y1), U(2 * sx + sz, y1));
            if ((faces & Faces.PosY) != 0)
                Quad(P(-h.x, h.y, h.z), P(h.x, h.y, h.z), P(h.x, h.y, -h.z), P(-h.x, h.y, -h.z), Y,
                    new Vector2(0, sz) / tile.x + uvOffset, new Vector2(sx, sz) / tile.x + uvOffset, new Vector2(sx, 0) / tile.x + uvOffset, uvOffset);
            if ((faces & Faces.NegY) != 0)
                Quad(P(-h.x, -h.y, -h.z), P(h.x, -h.y, -h.z), P(h.x, -h.y, h.z), P(-h.x, -h.y, h.z), -Y,
                    uvOffset, new Vector2(sx, 0) / tile.x + uvOffset, new Vector2(sx, sz) / tile.x + uvOffset, new Vector2(0, sz) / tile.x + uvOffset);
        }

        public void Box(Vector3 center, Vector3 size, float yaw, Vector2 tile, Faces faces = Faces.All) => Box(center, size, Quaternion.Euler(0f, yaw, 0f), tile, faces);

        /// <summary>Vertical cylinder (or a tapered one when r1 != r0). Smooth normals on the side.</summary>
        public void Cylinder(Vector3 baseCenter, float r0, float r1, float height, int seg, Vector2 tile, bool caps = true, Quaternion? rotation = null)
        {
            var rot = rotation ?? Quaternion.identity;
            float circ = Mathf.PI * 2f * Mathf.Max(r0, r1);
            for (int s = 0; s < seg; s++)
            {
                float a0 = s / (float)seg * Mathf.PI * 2f, a1 = (s + 1) / (float)seg * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 p0 = baseCenter + rot * (d0 * r0), p1 = baseCenter + rot * (d1 * r0);
                Vector3 p2 = baseCenter + rot * (d1 * r1 + Vector3.up * height), p3 = baseCenter + rot * (d0 * r1 + Vector3.up * height);
                float u0 = s / (float)seg * circ / tile.x, u1 = (s + 1) / (float)seg * circ / tile.x;
                int i = v.Count;
                v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                n.Add(rot * d0); n.Add(rot * d1); n.Add(rot * d1); n.Add(rot * d0);
                uv.Add(new Vector2(u0, 0f)); uv.Add(new Vector2(u1, 0f)); uv.Add(new Vector2(u1, height / tile.y)); uv.Add(new Vector2(u0, height / tile.y));
                t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
                if (caps)
                {
                    Vector3 top = baseCenter + rot * (Vector3.up * height);
                    Tri(top, p3, p2, rot * Vector3.up, new Vector2(0.5f, 0.5f), new Vector2(0.5f + d0.x * 0.5f, 0.5f + d0.z * 0.5f), new Vector2(0.5f + d1.x * 0.5f, 0.5f + d1.z * 0.5f));
                    if (r0 > 0.001f) Tri(baseCenter, p1, p0, rot * Vector3.down, new Vector2(0.5f, 0.5f), new Vector2(0.5f + d1.x * 0.5f, 0.5f + d1.z * 0.5f), new Vector2(0.5f + d0.x * 0.5f, 0.5f + d0.z * 0.5f));
                }
            }
        }

        /// <summary>Flat horizontal disc / annulus (inner = 0 for a disc). UVs are planar world xz / tile.</summary>
        public void Ring(Vector3 center, float inner, float outer, int seg, float tile, bool down = false)
        {
            for (int s = 0; s < seg; s++)
            {
                float a0 = s / (float)seg * Mathf.PI * 2f, a1 = (s + 1) / (float)seg * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 a = center + d0 * inner, b = center + d1 * inner, c = center + d1 * outer, d = center + d0 * outer;
                Vector3 nn = down ? Vector3.down : Vector3.up;
                Quad(a, b, c, d, nn, new Vector2(a.x, a.z) / tile, new Vector2(b.x, b.z) / tile, new Vector2(c.x, c.z) / tile, new Vector2(d.x, d.z) / tile);
            }
        }

        /// <summary>Vertical quad facing 'facing' (double-sided when asked), for signs, cards and banners.</summary>
        public void Card(Vector3 center, float width, float height, Vector3 facing, bool doubleSided, Vector2 uvMin, Vector2 uvMax)
        {
            Vector3 f = facing.normalized;
            Vector3 r = Vector3.Cross(Vector3.up, f).normalized;
            if (r.sqrMagnitude < 1e-4f) r = Vector3.right;
            Vector3 u = Vector3.Cross(f, r);
            Vector3 hx = r * (width * 0.5f), hy = u * (height * 0.5f);
            Quad(center - hx - hy, center + hx - hy, center + hx + hy, center - hx + hy, f, uvMin, new Vector2(uvMax.x, uvMin.y), uvMax, new Vector2(uvMin.x, uvMax.y));
            if (doubleSided) Quad(center + hx - hy, center - hx - hy, center - hx + hy, center + hx + hy, -f, uvMin, new Vector2(uvMax.x, uvMin.y), uvMax, new Vector2(uvMin.x, uvMax.y));
        }

        /// <summary>A thin straight strut between two points (cables, rebar, pipes) as a 4-sided prism.</summary>
        public void Strut(Vector3 a, Vector3 b, float thickness, Vector2 tile)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            var rot = Quaternion.LookRotation(d / len, Mathf.Abs(d.y / len) > 0.95f ? Vector3.forward : Vector3.up);
            Box((a + b) * 0.5f, new Vector3(thickness, thickness, len), rot, tile, Faces.Sides | Faces.PosY | Faces.NegY);
        }

        /// <summary>Sagging cable between two points (catenary-ish parabola), as connected struts.</summary>
        public void Cable(Vector3 a, Vector3 b, float sag, float thickness, int segs = 10)
        {
            Vector3 prev = a;
            for (int i = 1; i <= segs; i++)
            {
                float k = i / (float)segs;
                Vector3 p = Vector3.Lerp(a, b, k) + Vector3.down * (sag * 4f * k * (1f - k));
                Strut(prev, p, thickness, new Vector2(1f, 1f));
                prev = p;
            }
        }

        /// <summary>Appends another soup transformed by rotation + translation (+ uniform scale).</summary>
        public void Append(Geo g, Quaternion rot, Vector3 pos, float scale = 1f)
        {
            int off = v.Count;
            for (int i = 0; i < g.v.Count; i++) { v.Add(pos + rot * (g.v[i] * scale)); n.Add(rot * g.n[i]); uv.Add(g.uv[i]); }
            for (int i = 0; i < g.t.Count; i++) t.Add(g.t[i] + off);
        }
    }

    /// <summary>Geometry grouped by material key.</summary>
    public sealed class GeoSet
    {
        public readonly Dictionary<string, Geo> batches = new Dictionary<string, Geo>();

        public Geo this[string key]
        {
            get
            {
                if (!batches.TryGetValue(key, out var g)) { g = new Geo(); batches[key] = g; }
                return g;
            }
        }

        public void Append(GeoSet other, Quaternion rot, Vector3 pos, float scale = 1f)
        {
            foreach (var kv in other.batches) this[kv.Key].Append(kv.Value, rot, pos, scale);
        }
    }

    public enum LightKind { Point, Spot }

    public struct LightSpec
    {
        public Vector3 pos;
        public Vector3 dir; // spots
        public Color color;
        public float intensity, range, angle;
        public LightKind kind;
        public bool shadows;
        public int flicker; // 0 steady, 1 neon buzz, 2 fire, 3 dying fluorescent, 4 strobe (police)
    }

    public enum EmitterKind { Fire, Smoke, Steam, Embers, Sparks, Drip, DigitalRain }

    public struct EmitterSpec
    {
        public EmitterKind kind;
        public Vector3 pos;
        public float size;
        public Color color;
    }

    public enum PropKind
    {
        Text,          // TMP world text (neon signs, slogans)
        HoloGiant,     // giant holographic robot (Blade Runner 2049 style)
        FlyingCar,     // spinner on a looping flight path
        Survivor,      // hooded scavenger by a fire
        Statue,        // inert armoured robot (hotel twins, wrecks)
        Beacon,        // blinking aircraft light
        Searchlight,   // sweeping sky beam
        TrafficLight,  // blinking amber, still working
        Clock,         // 11:59
        Monolith,      // the Spire's core, with HALCYON's eye
        Crows,         // birds that scatter when the fight comes close
        Chairs,        // the Construct's armchairs and TV
        Rain,          // digital-rain curtain (Construct)
        LightBeam,     // static volumetric cone
        Shutter,       // flickering neon tube (emissive strip with buzz)
        Custodian,     // the Gala's art piece: a robot arm in a case, forever scraping its own leak back
    }

    public struct PropSpec
    {
        public PropKind kind;
        public Vector3 pos;
        public float yaw, scale;
        public string text;
        public Color color;
        public Vector3 extra;   // path radius / beam direction / size, per kind
        public int seed;
    }

    public struct ColliderSpec
    {
        public Vector3 center, size;
        public float yaw;
        public bool floor;
    }

    /// <summary>Where a robot can be slammed for an environmental takedown (Arkham).</summary>
    public struct AnchorSpec
    {
        public Vector3 pos, normal;
        public string label;
    }

    /// <summary>Playable area: a circle or an axis-aligned rectangle around the origin.</summary>
    public struct ArenaShape
    {
        public bool rect;
        public float radius;
        public Vector2 half;

        public static ArenaShape Circle(float r) => new ArenaShape { radius = r, half = new Vector2(r, r) };
        public static ArenaShape Rect(float hx, float hz) => new ArenaShape { rect = true, half = new Vector2(hx, hz), radius = Mathf.Min(hx, hz) };

        public Vector3 Clamp(Vector3 p, float margin)
        {
            if (rect)
            {
                p.x = Mathf.Clamp(p.x, -half.x + margin, half.x - margin);
                p.z = Mathf.Clamp(p.z, -half.y + margin, half.y - margin);
                return p;
            }
            Vector3 flat = new Vector3(p.x, 0f, p.z);
            float max = radius - margin;
            if (flat.sqrMagnitude > max * max)
            {
                flat = flat.normalized * max;
                p.x = flat.x; p.z = flat.z;
            }
            return p;
        }

        public bool Contains(Vector3 p, float margin = 0f)
        {
            if (rect) return Mathf.Abs(p.x) <= half.x - margin && Mathf.Abs(p.z) <= half.y - margin;
            return p.x * p.x + p.z * p.z <= (radius - margin) * (radius - margin);
        }

        /// <summary>Point on the boundary in direction d (flat), scaled by k (k &lt; 1 = inside).</summary>
        public Vector3 Edge(Vector3 d, float k)
        {
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) d = Vector3.forward;
            d.Normalize();
            if (!rect) return d * (radius * k);
            float tx = Mathf.Abs(d.x) > 1e-4f ? half.x / Mathf.Abs(d.x) : float.MaxValue;
            float tz = Mathf.Abs(d.z) > 1e-4f ? half.y / Mathf.Abs(d.z) : float.MaxValue;
            return d * (Mathf.Min(tx, tz) * k);
        }

        public float Extent => rect ? Mathf.Max(half.x, half.y) : radius;
    }

    /// <summary>Everything a district needs, before any GameObject exists.</summary>
    public sealed class CityPlan
    {
        public District district;
        public readonly GeoSet geo = new GeoSet();
        /// <summary>Geometry that should not cast shadows (far skyline, emissive strips, cards).</summary>
        public readonly GeoSet geoNoShadow = new GeoSet();
        public readonly List<LightSpec> lights = new List<LightSpec>();
        public readonly List<EmitterSpec> emitters = new List<EmitterSpec>();
        public readonly List<PropSpec> props = new List<PropSpec>();
        public readonly List<ColliderSpec> colliders = new List<ColliderSpec>();
        public readonly List<AnchorSpec> anchors = new List<AnchorSpec>();
        public readonly List<Vector3> spawnPoints = new List<Vector3>();
        public ArenaShape arena = ArenaShape.Circle(16.2f);
        public Vector3 playerStart = new Vector3(0f, 0f, -5f);
        public float playerYaw;
        /// <summary>How robots arrive: 0 climb over the edge from below, 1 drop from above, 2 materialise in place.</summary>
        public int spawnStyle;
        public bool wetFloor = true;
        public float wetStrength = 0.95f;
        public string floorMaterial;
        public float floorTile = 4f;
        public bool hasFloor = true;
        public float floorSize = 120f;
    }
}
