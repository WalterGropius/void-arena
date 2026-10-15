using System;
using UnityEngine;

namespace HackAndSlash
{
    /// <summary>
    /// .kkrieger-style texture synthesis: every surface of Meridian is generated from a seed by stacking a few
    /// operators (tileable gradient noise, cellular noise, bricks, window grids, cracks, streaks, glyphs) and
    /// deriving normal/mask maps from the resulting height fields. Pure math on float arrays, so it runs on worker
    /// threads; <see cref="TexLibrary"/> turns the results into Unity textures.
    /// </summary>
    public static class TexGen
    {
        /// <summary>A square single-channel map that wraps at the edges (every operator is tileable).</summary>
        public sealed class Map
        {
            public readonly int n;
            public readonly float[] v;
            public Map(int size) { n = size; v = new float[size * size]; }
            // sizes are powers of two, so wrapping is a mask
            public float this[int x, int y]
            {
                get => v[(y & (n - 1)) * n + (x & (n - 1))];
                set => v[(y & (n - 1)) * n + (x & (n - 1))] = value;
            }
            public Map Clone() { var m = new Map(n); Array.Copy(v, m.v, v.Length); return m; }
            public float Sample(float u, float w)
            {
                // bilinear, wrapping (u, w in texels)
                int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(w);
                float fx = u - x0, fy = w - y0;
                float a = this[x0, y0], b = this[x0 + 1, y0], c = this[x0, y0 + 1], d = this[x0 + 1, y0 + 1];
                return Lerp(Lerp(a, b, fx), Lerp(c, d, fx), fy);
            }
        }

        /// <summary>Linear RGBA result of a texture job (0..1 floats, row-major, origin bottom-left).</summary>
        public sealed class Pixels
        {
            public readonly int n;
            public readonly Color[] c;
            public bool linear;
            public Pixels(int size, bool isLinear) { n = size; c = new Color[size * size]; linear = isLinear; }
        }

        // ---------------------------------------------------------------- hashing

        public static uint Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }

        public static float Hash01(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777216f;

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
        public static float Saturate(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        public static float SmoothStep(float a, float b, float x) { float t = Saturate((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        static int Wrap(int i, int p) => ((i % p) + p) % p;

        // ---------------------------------------------------------------- noise

        /// <summary>Periodic gradient (Perlin) noise in [-1, 1] with an integer period in lattice cells.</summary>
        public static float Perlin(float x, float y, int period, int seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            float n00 = Grad(Wrap(x0, period), Wrap(y0, period), seed, fx, fy);
            float n10 = Grad(Wrap(x0 + 1, period), Wrap(y0, period), seed, fx - 1f, fy);
            float n01 = Grad(Wrap(x0, period), Wrap(y0 + 1, period), seed, fx, fy - 1f);
            float n11 = Grad(Wrap(x0 + 1, period), Wrap(y0 + 1, period), seed, fx - 1f, fy - 1f);
            float u = Fade(fx), w = Fade(fy);
            return Lerp(Lerp(n00, n10, u), Lerp(n01, n11, u), w) * 1.41f;
        }

        static readonly float[] gradX = BuildGrad(true), gradY = BuildGrad(false);

        static float[] BuildGrad(bool cos)
        {
            var g = new float[256];
            for (int i = 0; i < 256; i++) g[i] = (float)(cos ? Math.Cos(i / 256.0 * Math.PI * 2.0) : Math.Sin(i / 256.0 * Math.PI * 2.0));
            return g;
        }

        static float Grad(int ix, int iy, int seed, float dx, float dy)
        {
            uint h = Hash(ix, iy, seed) & 255;
            return gradX[h] * dx + gradY[h] * dy;
        }

        /// <summary>Tileable fBm in [0, 1]. baseCells = lattice cells across the texture at the lowest octave.</summary>
        public static Map Fbm(int n, int baseCells, int octaves, int seed, float gain = 0.5f, float lacunarity = 2f)
        {
            var m = new Map(n);
            float inv = 1f / n;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float sum = 0f, amp = 1f, norm = 0f;
                int cells = baseCells;
                for (int o = 0; o < octaves; o++)
                {
                    sum += Perlin(x * inv * cells, y * inv * cells, cells, seed + o * 131) * amp;
                    norm += amp;
                    amp *= gain;
                    cells = Math.Max(1, (int)(cells * lacunarity));
                }
                m.v[y * n + x] = sum / norm * 0.5f + 0.5f;
            }
            return m;
        }

        /// <summary>Ridged multifractal (sharp creases: cracks, veins, wiring).</summary>
        public static Map Ridged(int n, int baseCells, int octaves, int seed)
        {
            var m = new Map(n);
            float inv = 1f / n;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float sum = 0f, amp = 1f, norm = 0f;
                int cells = baseCells;
                for (int o = 0; o < octaves; o++)
                {
                    float r = 1f - Math.Abs(Perlin(x * inv * cells, y * inv * cells, cells, seed + o * 97));
                    sum += r * r * amp;
                    norm += amp;
                    amp *= 0.5f;
                    cells *= 2;
                }
                m.v[y * n + x] = sum / norm;
            }
            return m;
        }

        public enum CellMode { F1, F2, Edge }

        /// <summary>Tileable Worley noise. Edge mode = F2 - F1 (thin bright lines between cells: cracks, crazing).</summary>
        public static Map Cells(int n, int cells, int seed, CellMode mode, float jitter = 1f)
        {
            var m = new Map(n);
            float cs = n / (float)cells;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float px = x / cs, py = y / cs;
                int cx = (int)Math.Floor(px), cy = (int)Math.Floor(py);
                float f1 = 9f, f2 = 9f;
                for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j;
                    int wx = Wrap(gx, cells), wy = Wrap(gy, cells);
                    float ox = 0.5f + (Hash01(wx, wy, seed) - 0.5f) * jitter;
                    float oy = 0.5f + (Hash01(wx, wy, seed + 17) - 0.5f) * jitter;
                    float dx = gx + ox - px, dy = gy + oy - py;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) f2 = d;
                }
                m.v[y * n + x] = mode == CellMode.F1 ? f1 : mode == CellMode.F2 ? f2 : f2 - f1;
            }
            return m;
        }

        /// <summary>Per-cell random value (Voronoi id), e.g. for stone/tile colour variation.</summary>
        public static Map CellId(int n, int cells, int seed)
        {
            var m = new Map(n);
            float cs = n / (float)cells;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float px = x / cs, py = y / cs;
                int cx = (int)Math.Floor(px), cy = (int)Math.Floor(py);
                float best = 9f; float id = 0f;
                for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j;
                    int wx = Wrap(gx, cells), wy = Wrap(gy, cells);
                    float dx = gx + Hash01(wx, wy, seed) - px, dy = gy + Hash01(wx, wy, seed + 17) - py;
                    float d = dx * dx + dy * dy;
                    if (d < best) { best = d; id = Hash01(wx, wy, seed + 33); }
                }
                m.v[y * n + x] = id;
            }
            return m;
        }

        // ---------------------------------------------------------------- operators

        public static Map Blur(Map src, int radius, int passes = 2)
        {
            var a = src.Clone();
            var b = new Map(src.n);
            int n = src.n;
            float k = 1f / (2 * radius + 1);
            for (int p = 0; p < passes; p++)
            {
                for (int y = 0; y < n; y++)
                {
                    float acc = 0f;
                    for (int i = -radius; i <= radius; i++) acc += a[i, y];
                    for (int x = 0; x < n; x++)
                    {
                        b.v[y * n + x] = acc * k;
                        acc += a[x + radius + 1, y] - a[x - radius, y];
                    }
                }
                for (int x = 0; x < n; x++)
                {
                    float acc = 0f;
                    for (int i = -radius; i <= radius; i++) acc += b[x, i];
                    for (int y = 0; y < n; y++)
                    {
                        a.v[y * n + x] = acc * k;
                        acc += b[x, y + radius + 1] - b[x, y - radius];
                    }
                }
            }
            return a;
        }

        /// <summary>Remaps to [0, 1] using the map's own min/max.</summary>
        public static Map Normalize(Map m)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (float f in m.v) { if (f < lo) lo = f; if (f > hi) hi = f; }
            float s = hi - lo > 1e-6f ? 1f / (hi - lo) : 0f;
            for (int i = 0; i < m.v.Length; i++) m.v[i] = (m.v[i] - lo) * s;
            return m;
        }

        public static Map Levels(Map m, float lo, float hi)
        {
            for (int i = 0; i < m.v.Length; i++) m.v[i] = Saturate((m.v[i] - lo) / (hi - lo));
            return m;
        }

        public static Map Step(Map m, float edge, float soft)
        {
            for (int i = 0; i < m.v.Length; i++) m.v[i] = SmoothStep(edge - soft, edge + soft, m.v[i]);
            return m;
        }

        /// <summary>Domain warp: displaces lookups by two noise fields (organic stains, marble veins, wobbly cracks).</summary>
        public static Map Warp(Map src, Map dx, Map dy, float amount)
        {
            int n = src.n;
            var m = new Map(n);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                m.v[i] = src.Sample(x + (dx.v[i] - 0.5f) * amount, y + (dy.v[i] - 0.5f) * amount);
            }
            return m;
        }

        /// <summary>Anisotropic smear downward: rain streaks and grime running down walls (y-up texture space).</summary>
        public static Map Streaks(int n, int columns, int seed, float length)
        {
            var m = new Map(n);
            var cols = Fbm(n, columns, 3, seed);
            for (int x = 0; x < n; x++)
            {
                float colStrength = SmoothStep(0.5f, 0.75f, cols[x, 0]);
                for (int y = 0; y < n; y++)
                {
                    float along = Perlin(x * 8f / n, y * length / n, 8, seed + 5) * 0.5f + 0.5f;
                    m.v[y * n + x] = colStrength * along;
                }
            }
            return Blur(m, 1, 1);
        }

        /// <summary>Random straight scratches (bright lines) for metal, paint and armour wear.</summary>
        public static Map Scratches(int n, int count, int seed, float maxLen)
        {
            var m = new Map(n);
            var rng = new System.Random(seed);
            for (int s = 0; s < count; s++)
            {
                float x = (float)rng.NextDouble() * n, y = (float)rng.NextDouble() * n;
                float ang = (float)(rng.NextDouble() * Math.PI);
                float len = (float)(0.2 + rng.NextDouble() * 0.8) * maxLen * n;
                float dx = (float)Math.Cos(ang), dy = (float)Math.Sin(ang);
                float inten = (float)(0.3 + rng.NextDouble() * 0.7);
                int steps = (int)len;
                for (int k = 0; k < steps; k++)
                {
                    float t = k / Math.Max(1f, len);
                    float fade = (float)Math.Sin(t * Math.PI);
                    int px = (int)(x + dx * k), py = (int)(y + dy * k);
                    float cur = m[px, py];
                    m[px, py] = Math.Max(cur, inten * fade);
                }
            }
            return m;
        }

        public struct BrickInfo { public Map height, id, mortar; }

        /// <summary>Running-bond bricks or tiles: bevelled height, per-brick id and the mortar mask.</summary>
        public static BrickInfo Bricks(int n, int cols, int rows, float mortar, float bevel, int seed, bool offsetRows = true)
        {
            var h = new Map(n); var id = new Map(n); var mo = new Map(n);
            float bw = n / (float)cols, bh = n / (float)rows;
            for (int y = 0; y < n; y++)
            {
                int row = (int)(y / bh);
                float fy = y / bh - row;
                for (int x = 0; x < n; x++)
                {
                    float xx = x + (offsetRows && (row & 1) == 1 ? bw * 0.5f : 0f);
                    int col = (int)Math.Floor(xx / bw);
                    float fx = xx / bw - col;
                    int wc = Wrap(col, cols);
                    // distance to the brick edge, in brick-height units
                    float ex = Math.Min(fx, 1f - fx) * bw / bh, ey = Math.Min(fy, 1f - fy);
                    float e = Math.Min(ex, ey);
                    float mortarMask = 1f - SmoothStep(mortar * 0.5f, mortar * 0.5f + 0.02f, e);
                    float height = SmoothStep(mortar * 0.5f, mortar * 0.5f + bevel, e);
                    int i = y * n + x;
                    h.v[i] = height;
                    id.v[i] = Hash01(wc, Wrap(row, rows), seed);
                    mo.v[i] = mortarMask;
                }
            }
            return new BrickInfo { height = h, id = id, mortar = mo };
        }

        /// <summary>Kubrick's hexagonal carpet (The Shining): nested hexagon outlines on a tiling lattice.</summary>
        public static Pixels HexCarpet(int n, int seed)
        {
            var px = new Pixels(n, false);
            // three hexes across, lattice sized so the texture tiles
            float size = n / 3f / 1.7320508f;
            var wear = Fbm(n, 6, 4, seed);
            var fibre = Fbm(n, 64, 2, seed + 7);
            Color red = new Color(0.62f, 0.12f, 0.06f), orange = new Color(0.86f, 0.42f, 0.1f), brown = new Color(0.28f, 0.12f, 0.06f);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // axial hex coordinates (pointy-top)
                float qx = x / size, qy = y / size;
                float q = (0.57735f * qx - 0.33333f * qy), r = 0.66667f * qy;
                HexRound(q, r, out float hq, out float hr);
                // local offset from hex centre in pixel space
                float cx = size * (1.7320508f * hq + 0.8660254f * hr), cy = size * 1.5f * hr;
                float dx = Math.Abs(x - cx), dy = Math.Abs(y - cy);
                float hexd = Math.Max(dx * 0.8660254f + dy * 0.5f, dy) / size; // 0 centre .. ~0.866 edge
                float band = hexd * 6f;
                float k = band - (float)Math.Floor(band);
                int ring = (int)band;
                Color c = ring % 3 == 0 ? brown : ring % 3 == 1 ? orange : red;
                float line = 1f - SmoothStep(0.08f, 0.16f, Math.Min(k, 1f - k));
                c = Color.Lerp(c, red * 0.6f, line * 0.6f);
                float f = 0.85f + fibre.v[y * n + x] * 0.25f;
                float w = 0.75f + wear.v[y * n + x] * 0.35f;
                var o = c * (f * w); o.a = 1f;
                px.c[y * n + x] = o;
            }
            return px;
        }

        static void HexRound(float q, float r, out float rq, out float rr)
        {
            float s = -q - r;
            float iq = (float)Math.Round(q), ir = (float)Math.Round(r), iS = (float)Math.Round(s);
            float dq = Math.Abs(iq - q), dr = Math.Abs(ir - r), ds = Math.Abs(iS - s);
            if (dq > dr && dq > ds) iq = -ir - iS;
            else if (dr > ds) ir = -iq - iS;
            rq = iq; rr = ir;
        }

        // ---------------------------------------------------------------- output helpers

        /// <summary>Tangent-space normal map from a height field (Sobel, wrapping). Stored as RGB with A = 1 (URP unpacks RG/AG).</summary>
        public static Pixels NormalFromHeight(Map h, float strength)
        {
            int n = h.n;
            var px = new Pixels(n, true);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float gx = (h[x + 1, y - 1] + 2f * h[x + 1, y] + h[x + 1, y + 1]) - (h[x - 1, y - 1] + 2f * h[x - 1, y] + h[x - 1, y + 1]);
                float gy = (h[x - 1, y + 1] + 2f * h[x, y + 1] + h[x + 1, y + 1]) - (h[x - 1, y - 1] + 2f * h[x, y - 1] + h[x + 1, y - 1]);
                float nx = -gx * strength, ny = -gy * strength, nz = 1f;
                float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                px.c[y * n + x] = new Color(nx / len * 0.5f + 0.5f, ny / len * 0.5f + 0.5f, nz / len * 0.5f + 0.5f, 1f);
            }
            return px;
        }

        /// <summary>URP mask: R metallic, G occlusion, A smoothness (the same texture feeds _MetallicGlossMap and _OcclusionMap).</summary>
        public static Pixels Mask(Map metal, Map occlusion, Map smooth, float metalConst = 0f, float occConst = 1f, float smoothConst = 0.5f, int size = 0)
        {
            int n = metal != null ? metal.n : occlusion != null ? occlusion.n : smooth != null ? smooth.n : size;
            var px = new Pixels(n, true);
            for (int i = 0; i < n * n; i++)
                px.c[i] = new Color(metal != null ? metal.v[i] : metalConst, occlusion != null ? occlusion.v[i] : occConst, 0f, smooth != null ? smooth.v[i] : smoothConst);
            return px;
        }

        public static Pixels Gray(Map m, bool linear = true)
        {
            var px = new Pixels(m.n, linear);
            for (int i = 0; i < m.v.Length; i++) { float f = m.v[i]; px.c[i] = new Color(f, f, f, 1f); }
            return px;
        }

        /// <summary>Gradient map: maps a 0..1 field through colour keys (pos, colour) sorted by pos.</summary>
        public static Color Ramp(float t, float[] pos, Color[] cols)
        {
            if (t <= pos[0]) return cols[0];
            for (int i = 1; i < pos.Length; i++)
                if (t <= pos[i]) return Color.Lerp(cols[i - 1], cols[i], (t - pos[i - 1]) / Math.Max(1e-5f, pos[i] - pos[i - 1]));
            return cols[cols.Length - 1];
        }
    }

    /// <summary>The recipe book: one function per material family. Each returns albedo/normal/mask(/emission) pixel sets.</summary>
    public static class TexRecipes
    {
        public sealed class Set
        {
            public string name;
            public TexGen.Pixels albedo, normal, mask, emission, extra;
        }

        static TexGen.Map Add(TexGen.Map a, TexGen.Map b, float wb)
        {
            var m = a.Clone();
            for (int i = 0; i < m.v.Length; i++) m.v[i] += b.v[i] * wb;
            return m;
        }

        // ---------------------------------------------------------------- concrete (walls, slabs, rubble)

        public static Set Concrete(int n, int seed, Color tint, float moss)
        {
            var big = TexGen.Fbm(n, 4, 5, seed);
            var fine = TexGen.Fbm(n, 32, 3, seed + 1);
            var pores = TexGen.Cells(n, 48, seed + 2, TexGen.CellMode.F1);
            var cracks = TexGen.Cells(n, 6, seed + 3, TexGen.CellMode.Edge, 0.9f);
            var wx = TexGen.Fbm(n, 6, 3, seed + 4); var wy = TexGen.Fbm(n, 6, 3, seed + 5);
            cracks = TexGen.Warp(cracks, wx, wy, n * 0.06f);
            var streak = TexGen.Streaks(n, 24, seed + 6, 1.2f);
            var mossMap = TexGen.Levels(TexGen.Fbm(n, 5, 4, seed + 8), 0.58f, 0.72f);
            var crackMask = TexGen.Fbm(n, 5, 3, seed + 9);
            var h = new TexGen.Map(n);
            var al = new TexGen.Pixels(n, false);
            var sm = new TexGen.Map(n); var ao = new TexGen.Map(n);
            for (int i = 0; i < n * n; i++)
            {
                // only some cell edges crack, so the slab doesn't read as flagstones
                float crack = (1f - TexGen.SmoothStep(0.0f, 0.03f, cracks.v[i])) * TexGen.SmoothStep(0.55f, 0.65f, crackMask.v[i]);
                float pore = 1f - TexGen.SmoothStep(0.05f, 0.14f, pores.v[i]);
                h.v[i] = big.v[i] * 0.4f + fine.v[i] * 0.25f - crack * 0.6f - pore * 0.12f;
                float tone = 0.72f + (big.v[i] - 0.5f) * 0.35f + (fine.v[i] - 0.5f) * 0.18f - streak.v[i] * 0.22f - crack * 0.35f;
                Color c = tint * tone;
                float mo = mossMap.v[i] * moss * (0.6f + 0.4f * fine.v[i]);
                c = Color.Lerp(c, new Color(0.16f, 0.22f, 0.08f), mo * 0.85f);
                c.a = 1f;
                al.c[i] = c;
                sm.v[i] = 0.18f + streak.v[i] * 0.35f + mo * 0.1f;
                ao.v[i] = 1f - crack * 0.6f - pore * 0.2f;
            }
            return new Set { name = "Concrete", albedo = al, normal = TexGen.NormalFromHeight(h, 2.2f), mask = TexGen.Mask(null, ao, sm) };
        }

        // ---------------------------------------------------------------- asphalt (street surface + puddle mask in extra)

        public static Set Asphalt(int n, int seed)
        {
            var grain = TexGen.Fbm(n, 96, 2, seed);
            var stones = TexGen.Cells(n, 90, seed + 1, TexGen.CellMode.F1);
            var big = TexGen.Fbm(n, 3, 5, seed + 2);
            var cracks = TexGen.Cells(n, 5, seed + 3, TexGen.CellMode.Edge, 1f);
            var wx = TexGen.Fbm(n, 8, 3, seed + 4); var wy = TexGen.Fbm(n, 8, 3, seed + 5);
            cracks = TexGen.Warp(cracks, wx, wy, n * 0.05f);
            var patches = TexGen.CellId(n, 5, seed + 6);
            var crackMask = TexGen.Fbm(n, 4, 3, seed + 7);
            var mid = TexGen.Fbm(n, 10, 3, seed + 8);
            var h = new TexGen.Map(n); var sm = new TexGen.Map(n); var puddle = new TexGen.Map(n);
            var al = new TexGen.Pixels(n, false);
            for (int i = 0; i < n * n; i++)
            {
                float crack = (1f - TexGen.SmoothStep(0f, 0.03f, cracks.v[i])) * TexGen.SmoothStep(0.5f, 0.62f, crackMask.v[i]);
                float stone = 1f - TexGen.SmoothStep(0.1f, 0.3f, stones.v[i]);
                float patch = patches.v[i] > 0.82f ? 1f : 0f;
                float depth = (1f - big.v[i]) * 0.75f + mid.v[i] * 0.3f + crack * 0.15f;
                float wet = TexGen.SmoothStep(0.62f, 0.7f, depth);
                h.v[i] = stone * 0.15f + grain.v[i] * 0.2f - crack * 0.5f - wet * 0.1f;
                float tone = 0.11f + grain.v[i] * 0.06f + stone * 0.05f + patch * 0.035f - crack * 0.05f;
                tone *= 1f - wet * 0.45f;
                al.c[i] = new Color(tone, tone * 1.02f, tone * 1.08f, 1f);
                sm.v[i] = 0.3f + wet * 0.65f + stone * 0.05f;
                puddle.v[i] = wet;
            }
            return new Set { name = "Asphalt", albedo = al, normal = TexGen.NormalFromHeight(h, 1.6f), mask = TexGen.Mask(null, null, sm), extra = TexGen.Gray(TexGen.Blur(puddle, 2, 1)) };
        }

        // ---------------------------------------------------------------- brick

        public static Set Brick(int n, int seed, Color baseColor)
        {
            var b = TexGen.Bricks(n, 8, 24, 0.12f, 0.12f, seed);
            var noise = TexGen.Fbm(n, 64, 3, seed + 1);
            var soot = TexGen.Fbm(n, 4, 4, seed + 2);
            var streak = TexGen.Streaks(n, 20, seed + 3, 1.5f);
            var chips = TexGen.Cells(n, 40, seed + 4, TexGen.CellMode.F1);
            var h = new TexGen.Map(n); var sm = new TexGen.Map(n); var ao = new TexGen.Map(n);
            var al = new TexGen.Pixels(n, false);
            for (int i = 0; i < n * n; i++)
            {
                float chip = (1f - TexGen.SmoothStep(0.02f, 0.12f, chips.v[i])) * (noise.v[i] > 0.62f ? 1f : 0f);
                h.v[i] = b.height.v[i] * 0.8f + noise.v[i] * 0.15f - chip * 0.3f;
                Color brick = baseColor * (0.7f + b.id.v[i] * 0.55f) * (0.85f + noise.v[i] * 0.3f);
                Color mortar = new Color(0.42f, 0.4f, 0.38f) * (0.8f + noise.v[i] * 0.3f);
                Color c = Color.Lerp(brick, mortar, b.mortar.v[i]);
                float s = TexGen.SmoothStep(0.45f, 0.8f, soot.v[i]);
                c *= 1f - s * 0.6f - streak.v[i] * 0.25f;
                c.a = 1f;
                al.c[i] = c;
                sm.v[i] = 0.12f + streak.v[i] * 0.3f;
                ao.v[i] = 1f - b.mortar.v[i] * 0.45f;
            }
            return new Set { name = "Brick", albedo = al, normal = TexGen.NormalFromHeight(h, 3f), mask = TexGen.Mask(null, ao, sm) };
        }

        // ---------------------------------------------------------------- corrugated rusted metal

        public static Set RustMetal(int n, int seed, Color paint)
        {
            var rustBase = TexGen.Fbm(n, 6, 5, seed);
            var rustFine = TexGen.Fbm(n, 48, 3, seed + 1);
            var streak = TexGen.Streaks(n, 32, seed + 2, 2f);
            var scratches = TexGen.Scratches(n, 220, seed + 3, 0.12f);
            var h = new TexGen.Map(n); var sm = new TexGen.Map(n); var me = new TexGen.Map(n);
            var al = new TexGen.Pixels(n, false);
            Color rust = new Color(0.38f, 0.15f, 0.06f), rustLight = new Color(0.6f, 0.3f, 0.1f);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                float corr = (float)Math.Sin(x / (float)n * Math.PI * 2f * 16f) * 0.5f + 0.5f;
                float seam = (y % (n / 2)) < 3 ? 1f : 0f;
                float r = TexGen.SmoothStep(0.56f, 0.7f, rustBase.v[i] + streak.v[i] * 0.3f + (rustFine.v[i] - 0.5f) * 0.3f);
                h.v[i] = corr * 0.6f - seam * 0.4f + r * rustFine.v[i] * 0.2f;
                Color c = Color.Lerp(paint * (0.85f + rustFine.v[i] * 0.2f), Color.Lerp(rust, rustLight, rustFine.v[i]), r);
                c = Color.Lerp(c, new Color(0.55f, 0.55f, 0.55f), scratches.v[i] * (1f - r) * 0.6f);
                c *= 1f - streak.v[i] * 0.2f;
                c.a = 1f;
                al.c[i] = c;
                me.v[i] = (1f - r) * 0.5f + scratches.v[i] * 0.4f;
                sm.v[i] = (1f - r) * 0.55f + 0.08f;
            }
            return new Set { name = "RustMetal", albedo = al, normal = TexGen.NormalFromHeight(h, 2.5f), mask = TexGen.Mask(me, null, sm) };
        }

        // ---------------------------------------------------------------- vehicle paint (tinted per car by _BaseColor)

        public static Set CarPaint(int n, int seed)
        {
            var rust = TexGen.Levels(TexGen.Fbm(n, 5, 5, seed), 0.58f, 0.7f);
            var grime = TexGen.Fbm(n, 8, 4, seed + 1);
            var scratches = TexGen.Scratches(n, 160, seed + 2, 0.08f);
            var dents = TexGen.Fbm(n, 10, 3, seed + 3);
            var h = new TexGen.Map(n); var sm = new TexGen.Map(n); var me = new TexGen.Map(n);
            var al = new TexGen.Pixels(n, false);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                float seam = (x % (n / 4)) < 2 || (y % (n / 3)) < 2 ? 1f : 0f;
                float r = rust.v[i];
                float g = TexGen.SmoothStep(0.55f, 0.9f, grime.v[i]) * 0.6f;
                h.v[i] = dents.v[i] * 0.3f - seam * 0.5f + r * 0.1f;
                Color paintC = new Color(0.8f, 0.8f, 0.8f) * (1f - g * 0.45f);
                Color c = Color.Lerp(paintC, new Color(0.32f, 0.14f, 0.06f), r);
                c = Color.Lerp(c, new Color(0.6f, 0.6f, 0.62f), scratches.v[i] * 0.7f * (1f - r));
                c.a = 1f;
                al.c[i] = c;
                me.v[i] = scratches.v[i] * 0.8f;
                sm.v[i] = (1f - r) * (0.7f - g * 0.35f) + 0.05f;
            }
            return new Set { name = "CarPaint", albedo = al, normal = TexGen.NormalFromHeight(h, 1.5f), mask = TexGen.Mask(me, null, sm) };
        }

        // ---------------------------------------------------------------- ruined facade: window grid with mostly dead, a few lit, some broken

        public static Set Facade(int n, int seed, int cols, int rows, float lit, float broken, Color wall)
        {
            var noise = TexGen.Fbm(n, 8, 4, seed);
            var fine = TexGen.Fbm(n, 64, 2, seed + 1);
            var streak = TexGen.Streaks(n, 32, seed + 2, 1.4f);
            var soot = TexGen.Levels(TexGen.Fbm(n, 4, 4, seed + 3), 0.55f, 0.8f);
            var h = new TexGen.Map(n); var sm = new TexGen.Map(n);
            var al = new TexGen.Pixels(n, false);
            var em = new TexGen.Pixels(n, false);
            float cw = n / (float)cols, ch = n / (float)rows;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                int cx = (int)(x / cw), cy = (int)(y / ch);
                float fx = x / cw - cx, fy = y / ch - cy;
                float r = TexGen.Hash01(cx, cy, seed + 11);
                bool isLit = r < lit;
                bool isBroken = !isLit && r > 1f - broken;
                float inWin = (fx > 0.16f && fx < 0.84f && fy > 0.22f && fy < 0.8f) ? 1f : 0f;
                float frame = (fx > 0.12f && fx < 0.88f && fy > 0.18f && fy < 0.84f) ? 1f : 0f;
                float mullion = Math.Abs(fx - 0.5f) < 0.025f ? 1f : 0f;
                float slab = fy < 0.07f ? 1f : 0f;
                h.v[i] = frame * -0.3f + inWin * -0.2f + slab * 0.3f + noise.v[i] * 0.2f + fine.v[i] * 0.1f;
                Color c = wall * (0.7f + noise.v[i] * 0.4f + fine.v[i] * 0.15f);
                c *= 1f - streak.v[i] * 0.3f - soot.v[i] * 0.5f;
                Color e = Color.black;
                float s = 0.15f + streak.v[i] * 0.2f;
                if (frame > 0f && inWin <= 0f) c = new Color(0.08f, 0.08f, 0.09f);
                if (inWin > 0f)
                {
                    if (isBroken)
                    {
                        // shattered: black hole with jagged glass shards near the frame
                        float shard = TexGen.Hash01((int)(fx * 24f), (int)(fy * 24f), seed + 19);
                        c = new Color(0.01f, 0.01f, 0.012f);
                        float edge = Math.Min(Math.Min(fx - 0.16f, 0.84f - fx), Math.Min(fy - 0.22f, 0.8f - fy));
                        if (edge < 0.06f + shard * 0.08f) { c = new Color(0.12f, 0.14f, 0.16f); s = 0.9f; }
                        else s = 0f;
                    }
                    else
                    {
                        float glassVar = TexGen.Hash01(cx, cy, seed + 23);
                        c = new Color(0.03f, 0.04f, 0.05f) * (0.6f + glassVar);
                        s = 0.92f;
                        if (isLit)
                        {
                            // warm tungsten or cold fluorescent, with a blind/curtain silhouette
                            bool warm = TexGen.Hash01(cx, cy, seed + 29) < 0.65f;
                            Color lc = warm ? new Color(1f, 0.62f, 0.28f) : new Color(0.55f, 0.75f, 1f);
                            float blind = TexGen.Hash01(cx, cy, seed + 31) < 0.4f ? ((int)(fy * 30f) % 2 == 0 ? 0.55f : 1f) : 1f;
                            float silhouette = TexGen.Hash01(cx, cy, seed + 37) < 0.25f && Math.Abs(fx - 0.35f) < 0.07f && fy < 0.62f ? 0.15f : 1f;
                            float grad = 0.6f + 0.4f * fy;
                            e = lc * (blind * silhouette * grad);
                            c = lc * 0.25f;
                        }
                    }
                    if (mullion > 0f) { c = new Color(0.06f, 0.06f, 0.07f); e = Color.black; s = 0.4f; }
                }
                c.a = 1f; e.a = 1f;
                al.c[i] = c;
                em.c[i] = e;
                sm.v[i] = s;
            }
            return new Set { name = "Facade", albedo = al, normal = TexGen.NormalFromHeight(h, 2f), mask = TexGen.Mask(null, null, sm), emission = em };
        }

        // ---------------------------------------------------------------- marble (hotel floor: black and white checker with veins)

        public static Set Marble(int n, int seed)
        {
            var veinsA = TexGen.Ridged(n, 3, 5, seed);
            var wx = TexGen.Fbm(n, 4, 4, seed + 1); var wy = TexGen.Fbm(n, 4, 4, seed + 2);
            veinsA = TexGen.Warp(veinsA, wx, wy, n * 0.25f);
            var cloud = TexGen.Fbm(n, 6, 5, seed + 3);
            var al = new TexGen.Pixels(n, false);
            var h = new TexGen.Map(n); var sm = new TexGen.Map(n);
            int tiles = 2;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                int tx = x * tiles / n, ty = y * tiles / n;
                bool white = ((tx + ty) & 1) == 0;
                float fx = x * tiles / (float)n - tx, fy = y * tiles / (float)n - ty;
                float grout = Math.Min(Math.Min(fx, 1f - fx), Math.Min(fy, 1f - fy)) < 0.004f ? 1f : 0f;
                float vein = TexGen.SmoothStep(0.7f, 0.92f, veinsA.v[i]);
                Color c = white ? new Color(0.86f, 0.85f, 0.82f) * (0.92f + cloud.v[i] * 0.12f) : new Color(0.035f, 0.035f, 0.04f) * (0.8f + cloud.v[i] * 0.5f);
                c = Color.Lerp(c, white ? new Color(0.45f, 0.44f, 0.42f) : new Color(0.55f, 0.52f, 0.48f), vein * (white ? 0.7f : 0.45f));
                c = Color.Lerp(c, new Color(0.2f, 0.2f, 0.2f), grout);
                c.a = 1f;
                al.c[i] = c;
                h.v[i] = -grout * 0.5f;
                sm.v[i] = 0.93f - grout * 0.6f;
            }
            return new Set { name = "Marble", albedo = al, normal = TexGen.NormalFromHeight(h, 1f), mask = TexGen.Mask(null, null, sm) };
        }

        public static Set Carpet(int n, int seed)
        {
            var al = TexGen.HexCarpet(n, seed);
            var fibre = TexGen.Fbm(n, 128, 2, seed + 3);
            return new Set { name = "Carpet", albedo = al, normal = TexGen.NormalFromHeight(fibre, 1.5f), mask = TexGen.Mask(null, null, null, 0f, 1f, 0.1f, n) };
        }

        /// <summary>Hotel wallpaper: red/amber damask on a diamond lattice.</summary>
        public static Set HotelWall(int n, int seed)
        {
            var grain = TexGen.Fbm(n, 4, 4, seed);
            var al = new TexGen.Pixels(n, false);
            var h = new TexGen.Map(n); var sm = new TexGen.Map(n);
            int cell = n / 4;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                // offset every other row of motifs (half-drop repeat)
                int row = y / cell;
                int xs = (x + ((row & 1) == 1 ? cell / 2 : 0)) % cell;
                float u = xs / (float)cell - 0.5f, w = (y % cell) / (float)cell - 0.5f;
                float d = Math.Abs(u) * 1.3f + Math.Abs(w);
                float ang = (float)Math.Atan2(w, Math.Abs(u));
                float lobe = (float)Math.Cos(ang * 4f) * 0.07f + (float)Math.Cos(ang * 9f) * 0.02f;
                float motif = 1f - TexGen.SmoothStep(0.24f + lobe, 0.27f + lobe, d);
                float inner = 1f - TexGen.SmoothStep(0.1f, 0.12f, d);
                float ring = Math.Abs(d - 0.4f) < 0.012f ? 1f : 0f;
                float stripe = Math.Abs(u) > 0.485f ? 1f : 0f;
                float m = Math.Max(motif - inner * 0.6f, Math.Max(ring, stripe * 0.5f));
                Color c = Color.Lerp(new Color(0.26f, 0.05f, 0.04f), new Color(0.52f, 0.24f, 0.09f), m * 0.85f);
                c *= 0.85f + grain.v[i] * 0.25f;
                c.a = 1f;
                al.c[i] = c; sm.v[i] = 0.25f + motif * 0.25f; h.v[i] = m * 0.12f;
            }
            return new Set { name = "HotelWall", albedo = al, normal = TexGen.NormalFromHeight(h, 2f), mask = TexGen.Mask(null, null, sm) };
        }

        /// <summary>Polished dark wood panelling (hotel wainscot, bar, doors).</summary>
        public static Set Wood(int n, int seed)
        {
            var grain = TexGen.Fbm(n, 4, 4, seed);
            var wx = TexGen.Fbm(n, 2, 3, seed + 1); var wy = TexGen.Fbm(n, 32, 3, seed + 2);
            var wood = TexGen.Warp(TexGen.Fbm(n, 64, 3, seed + 3), wx, wy, n * 0.08f);
            var al = new TexGen.Pixels(n, false);
            var h = new TexGen.Map(n); var sm = new TexGen.Map(n);
            int panel = n / 4;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                float px = (x % panel) / (float)panel, py = y / (float)n;
                float inset = px > 0.1f && px < 0.9f && py > 0.08f && py < 0.92f ? 1f : 0f;
                float bevel = inset * TexGen.SmoothStep(0.1f, 0.14f, Math.Min(Math.Min(px - 0.1f, 0.9f - px) + 0.1f, Math.Min(py - 0.08f, 0.92f - py) + 0.1f));
                float g = (float)Math.Sin(x * 0.07f + wood.v[i] * 20f) * 0.5f + 0.5f;
                Color c = new Color(0.2f, 0.09f, 0.045f) * (0.55f + g * 0.45f + grain.v[i] * 0.2f);
                if (px < 0.03f || px > 0.97f) c *= 0.5f;
                c.a = 1f;
                al.c[i] = c;
                h.v[i] = bevel * -0.25f + g * 0.04f;
                sm.v[i] = 0.72f;
            }
            return new Set { name = "Wood", albedo = al, normal = TexGen.NormalFromHeight(h, 2f), mask = TexGen.Mask(null, null, sm) };
        }

        // ---------------------------------------------------------------- the Construct (Matrix loading program: white floor, faint grid)

        public static Set ConstructFloor(int n, int seed)
        {
            var noise = TexGen.Fbm(n, 16, 3, seed);
            var al = new TexGen.Pixels(n, false);
            var em = new TexGen.Pixels(n, false);
            var sm = new TexGen.Map(n);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                float gx = Math.Min(x % (n / 8), (n / 8) - x % (n / 8)), gy = Math.Min(y % (n / 8), (n / 8) - y % (n / 8));
                float line = 1f - TexGen.SmoothStep(0.5f, 1.5f, Math.Min(gx, gy));
                float major = (x % (n / 2) < 2 || y % (n / 2) < 2) ? 1f : 0f;
                float t = 0.92f + noise.v[i] * 0.05f - line * 0.08f;
                al.c[i] = new Color(t, t, t * 1.01f, 1f);
                em.c[i] = new Color(0.3f, 1f, 0.45f, 1f) * (major * 0.6f + line * 0.08f);
                sm.v[i] = 0.82f;
            }
            return new Set { name = "Construct", albedo = al, normal = TexGen.NormalFromHeight(new TexGen.Map(n), 0f), mask = TexGen.Mask(null, null, sm), emission = em };
        }

        // ---------------------------------------------------------------- glyph atlas for digital rain (16 x 16 cells)

        public static TexGen.Pixels Glyphs(int n, int seed)
        {
            var px = new TexGen.Pixels(n, true);
            int cells = 16; int cs = n / cells;
            for (int gy = 0; gy < cells; gy++)
            for (int gx = 0; gx < cells; gx++)
            {
                int gid = gy * cells + gx;
                // each glyph: a handful of strokes on a 5x7 grid (katakana/numeral-ish), mirrored like the film's rain
                var rng = new System.Random(seed + gid * 7919);
                int strokes = 3 + rng.Next(4);
                var segs = new float[strokes * 4];
                for (int s = 0; s < strokes; s++)
                {
                    int x0 = rng.Next(5), y0 = rng.Next(7);
                    int dir = rng.Next(4);
                    int len = 1 + rng.Next(4);
                    int x1 = x0 + (dir == 0 ? len : dir == 2 ? -len / 2 : dir == 3 ? len / 2 : 0);
                    int y1 = y0 + (dir == 1 ? len : dir == 2 || dir == 3 ? len : 0);
                    segs[s * 4] = Math.Min(4, Math.Max(0, x0)); segs[s * 4 + 1] = Math.Min(6, Math.Max(0, y0));
                    segs[s * 4 + 2] = Math.Min(4, Math.Max(0, x1)); segs[s * 4 + 3] = Math.Min(6, Math.Max(0, y1));
                }
                for (int y = 0; y < cs; y++)
                for (int x = 0; x < cs; x++)
                {
                    float u = (cs - 1 - x) / (float)cs * 6f - 0.5f, v = y / (float)cs * 8f - 0.5f; // mirrored
                    float d = 9f;
                    for (int s = 0; s < strokes; s++)
                        d = Math.Min(d, SegDist(u, v, segs[s * 4], segs[s * 4 + 1], segs[s * 4 + 2], segs[s * 4 + 3]));
                    float a = 1f - TexGen.SmoothStep(0.18f, 0.42f, d);
                    float glow = (1f - TexGen.SmoothStep(0.2f, 1.3f, d)) * 0.35f;
                    float f = Math.Max(a, glow);
                    px.c[(gy * cs + y) * n + gx * cs + x] = new Color(f, f, f, f);
                }
            }
            return px;
        }

        static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float l2 = dx * dx + dy * dy;
            float t = l2 > 1e-5f ? TexGen.Saturate(((px - ax) * dx + (py - ay) * dy) / l2) : 0f;
            float qx = ax + dx * t - px, qy = ay + dy * t - py;
            return (float)Math.Sqrt(qx * qx + qy * qy);
        }

        // ---------------------------------------------------------------- hologram (scanlines + interference)

        public static TexGen.Pixels HoloNoise(int n, int seed)
        {
            var px = new TexGen.Pixels(n, true);
            var noise = TexGen.Fbm(n, 8, 3, seed);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                float scan = (y % 4) < 2 ? 1f : 0.45f;
                float band = TexGen.SmoothStep(0.4f, 0.6f, (float)Math.Sin(y / (float)n * Math.PI * 2f * 3f + noise.v[i] * 2f) * 0.5f + 0.5f);
                px.c[i] = new Color(scan, band, noise.v[i], 1f);
            }
            return px;
        }

        // ---------------------------------------------------------------- hazard stripes (barricades, jersey barriers)

        public static Set Hazard(int n, int seed)
        {
            var grime = TexGen.Fbm(n, 6, 4, seed);
            var chips = TexGen.Levels(TexGen.Fbm(n, 24, 3, seed + 1), 0.62f, 0.7f);
            var al = new TexGen.Pixels(n, false);
            var sm = new TexGen.Map(n);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                float stripe = ((x + y) % (n / 4)) < n / 8 ? 1f : 0f;
                Color c = stripe > 0f ? new Color(0.85f, 0.62f, 0.05f) : new Color(0.04f, 0.04f, 0.04f);
                c = Color.Lerp(c, new Color(0.45f, 0.44f, 0.42f), chips.v[i]);
                c *= 0.7f + grime.v[i] * 0.4f;
                c.a = 1f;
                al.c[i] = c;
                sm.v[i] = 0.35f;
            }
            return new Set { name = "Hazard", albedo = al, normal = TexGen.NormalFromHeight(chips, 1f), mask = TexGen.Mask(null, null, sm) };
        }

        // ---------------------------------------------------------------- cloth (the Knight's coat): weave + tattered alpha

        public static Set Cloth(int n, int seed)
        {
            var noise = TexGen.Fbm(n, 8, 4, seed);
            var tatter = TexGen.Fbm(n, 12, 4, seed + 1);
            var al = new TexGen.Pixels(n, false);
            var h = new TexGen.Map(n); var sm = new TexGen.Map(n);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                float weave = ((x / 2 + y / 2) % 2 == 0 ? 0.5f : 0f) + (float)Math.Sin(x * 1.3f) * 0.2f + (float)Math.Sin(y * 1.3f) * 0.2f;
                float v = y / (float)n; // v = 0 at the hem (bottom), 1 at the shoulders
                float edge = TexGen.SmoothStep(0f, 0.22f, v + (tatter.v[i] - 0.5f) * 0.32f);
                float holes = noise.v[i] > 0.74f && v < 0.6f ? 0f : 1f;
                float alpha = edge * holes;
                float t = 0.055f + noise.v[i] * 0.03f + weave * 0.01f;
                al.c[i] = new Color(t, t * 1.05f, t * 1.2f, alpha);
                h.v[i] = weave * 0.3f + noise.v[i] * 0.2f;
                sm.v[i] = 0.25f + (1f - v) * 0.2f; // damp hem
            }
            return new Set { name = "Cloth", albedo = al, normal = TexGen.NormalFromHeight(h, 1.2f), mask = TexGen.Mask(null, null, sm) };
        }

        // ---------------------------------------------------------------- armour detail (grime, edge wear, panel seams) for the robots

        public static Set ArmorDetail(int n, int seed)
        {
            var grime = TexGen.Fbm(n, 6, 5, seed);
            var scratches = TexGen.Scratches(n, 300, seed + 1, 0.06f);
            var rivets = TexGen.Cells(n, 16, seed + 2, TexGen.CellMode.F1, 0.1f);
            var al = new TexGen.Pixels(n, true); // detail albedo is linear 0.5-centred (x2 multiply)
            var h = new TexGen.Map(n);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                float seam = (x % (n / 4)) < 2 || (y % (n / 4)) < 2 ? 1f : 0f;
                float rivet = 1f - TexGen.SmoothStep(0.03f, 0.05f, rivets.v[i]);
                float g = TexGen.SmoothStep(0.45f, 0.85f, grime.v[i]);
                float a = 0.5f * (1f - g * 0.55f) + scratches.v[i] * 0.35f - seam * 0.2f;
                al.c[i] = new Color(a, a, a, 1f);
                h.v[i] = -seam * 0.6f + rivet * 0.5f - scratches.v[i] * 0.15f;
            }
            return new Set { name = "ArmorDetail", albedo = al, normal = TexGen.NormalFromHeight(h, 2.5f) };
        }

        // ---------------------------------------------------------------- puddle mask for wet reflections (streets)

        public static TexGen.Pixels Puddles(int n, int seed)
        {
            var big = TexGen.Fbm(n, 4, 5, seed);
            var mid = TexGen.Fbm(n, 12, 3, seed + 1);
            var m = new TexGen.Map(n);
            for (int i = 0; i < n * n; i++) m.v[i] = TexGen.SmoothStep(0.56f, 0.62f, big.v[i] * 0.8f + mid.v[i] * 0.25f);
            return TexGen.Gray(TexGen.Blur(m, 2, 1));
        }

        /// <summary>Lens/rain splat for the tactical display and a soft round particle.</summary>
        public static TexGen.Pixels SoftDot(int n)
        {
            var px = new TexGen.Pixels(n, true);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = (float)Math.Sqrt(dx * dx + dy * dy);
                float a = 1f - TexGen.SmoothStep(0f, 1f, r);
                a *= a;
                px.c[y * n + x] = new Color(1f, 1f, 1f, a);
            }
            return px;
        }

        /// <summary>Soft billowing smoke puff (alpha) for fog cards and plumes.</summary>
        public static TexGen.Pixels Smoke(int n, int seed)
        {
            var px = new TexGen.Pixels(n, true);
            var f = TexGen.Fbm(n, 4, 5, seed);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = (float)Math.Sqrt(dx * dx + dy * dy);
                float a = (1f - TexGen.SmoothStep(0.3f, 1f, r)) * TexGen.SmoothStep(0.25f, 0.75f, f.v[y * n + x] + (0.5f - r) * 0.6f);
                px.c[y * n + x] = new Color(1f, 1f, 1f, a);
            }
            return px;
        }
    }
}
