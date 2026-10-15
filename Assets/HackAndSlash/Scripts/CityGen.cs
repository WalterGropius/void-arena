using System;
using System.Collections.Generic;
using UnityEngine;

namespace HackAndSlash
{
    /// <summary>
    /// Procedural Meridian: one generator per district, all seeded, all pure data (<see cref="CityPlan"/>).
    /// Ruined skylines with broken crowns and leaning towers, flooded neon streets strung with lanterns, an
    /// immaculate Kubrick hotel, the white Construct and the Monolith on top of the Spire.
    /// </summary>
    public static class CityGen
    {
        // ---------------------------------------------------------------- material keys (see CityBuilder.Resolve)
        public const string Facade = "Facade", FacadeLit = "FacadeLit", FacadeBrick = "FacadeBrick", Conc = "Concrete", ConcDark = "ConcreteDark";
        public const string Asphalt = "Asphalt", BrickM = "Brick", Rust = "Rust", RustRed = "RustRed", Haz = "Hazard";
        public const string MarbleM = "Marble", CarpetM = "Carpet", WallM = "HotelWall", WoodM = "Wood", ConstructM = "Construct";
        public const string Metal = "Plain:1c1d21:0.85:0.55", Dark = "Plain:0b0b0d:0.5:0.45", Gold = "Plain:c8984a:1:0.85", Chrome = "Plain:b8bcc4:1:0.92";
        public const string Glass = "Plain:06080b:0.3:0.96", Mono = "Plain:020203:0.15:0.97", Linen = "Plain:e6e1d6:0:0.25", Leather = "Plain:5a0d08:0:0.55";
        public const string Cream = "Plain:d6ccb6:0:0.35", MarbleWhite = "Plain:e4dfd4:0.05:0.88", Rubber = "Plain:0e0e0f:0:0.3", Sand = "Plain:59503f:0:0.1", Server = "Plain:08090b:0.7:0.75";

        public static readonly Color NeonPink = new Color(1f, 0.16f, 0.55f), NeonCyan = new Color(0.2f, 0.85f, 1f), NeonAmber = new Color(1f, 0.5f, 0.12f);
        public static readonly Color NeonRed = new Color(1f, 0.08f, 0.06f), NeonGreen = new Color(0.3f, 1f, 0.45f), NeonViolet = new Color(0.6f, 0.22f, 1f), NeonWhite = new Color(0.75f, 0.88f, 1f);
        static readonly Color[] NeonSet = { NeonPink, NeonCyan, NeonAmber, NeonRed, NeonViolet, NeonGreen };

        public static string Glow(Color c, float intensity) => "Glow:" + Hex(c) + ":" + Fmt(intensity);
        public static string Tint(string baseKey, Color c) => "Tint:" + baseKey + ":" + Hex(c);
        public static string Plain(Color c, float metal, float smooth) => "Plain:" + Hex(c) + ":" + Fmt(metal) + ":" + Fmt(smooth);

        static string Fmt(float f) => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        public static string Hex(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255), g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255), b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            return r.ToString("x2") + g.ToString("x2") + b.ToString("x2");
        }

        static readonly Vector2 FacadeTile = new Vector2(12f, 14f);
        static readonly Vector2 T3 = new Vector2(3f, 3f), T4 = new Vector2(4f, 4f), T2 = new Vector2(2f, 2f), T1 = new Vector2(1f, 1f);

        sealed class Rng
        {
            readonly System.Random r;
            public Rng(int seed) { r = new System.Random(seed); }
            public float Range(float a, float b) => a + (float)r.NextDouble() * (b - a);
            public int Range(int a, int b) => r.Next(a, b);
            public bool Chance(float p) => r.NextDouble() < p;
            public T Pick<T>(T[] a) => a[r.Next(a.Length)];
            public float Value => (float)r.NextDouble();
            public int Seed() => r.Next();
        }

        // ---------------------------------------------------------------- entry point

        public static CityPlan Build(District d, int seed)
        {
            var plan = new CityPlan { district = d };
            var rng = new Rng(seed);
            switch (d)
            {
                case District.Rooftop: Rooftop(plan, rng); break;
                case District.Streets: Streets(plan, rng); break;
                case District.Hotel: Hotel(plan, rng); break;
                case District.Construct: Construct(plan, rng); break;
                default: Spire(plan, rng); break;
            }
            if (plan.spawnPoints.Count == 0)
                for (int i = 0; i < 8; i++)
                    plan.spawnPoints.Add(plan.arena.Edge(Quaternion.Euler(0f, i * 45f + 22.5f, 0f) * Vector3.forward, 0.74f));
            return plan;
        }

        // ================================================================ shared pieces

        /// <summary>A tower: facade body, optional broken crown (jagged walls, exposed slabs, rebar) or an intact setback crown.</summary>
        static float Tower(CityPlan plan, GeoSet target, Rng rng, Vector3 basePos, float w, float dpt, float h, float yaw, string facade, bool ruined, float lean = 0f, bool details = true)
        {
            var L = new GeoSet();
            float crown = ruined ? rng.Range(5f, Mathf.Min(22f, h * 0.3f)) : 0f;
            float h0 = h - crown;
            L[facade].Box(new Vector3(0f, h0 * 0.5f, 0f), new Vector3(w, h0, dpt), Quaternion.identity, FacadeTile, Geo.Faces.Sides, new Vector2(rng.Range(0, 4) * 0.25f, 0f), 0f);
            L[ConcDark].Box(new Vector3(0f, h0 - 0.2f, 0f), new Vector3(w + 0.3f, 0.4f, dpt + 0.3f), Quaternion.identity, T4, Geo.Faces.All);
            float top = h0;
            if (ruined) top = Crown(L, rng, w, dpt, h0, crown, facade);
            else if (details)
            {
                // intact: a setback penthouse, water tank or antenna
                if (rng.Chance(0.55f) && h > 30f)
                {
                    float cw = w * rng.Range(0.45f, 0.7f), cd = dpt * rng.Range(0.45f, 0.7f), ch = rng.Range(6f, 16f);
                    L[facade].Box(new Vector3(0f, h0 + ch * 0.5f, 0f), new Vector3(cw, ch, cd), Quaternion.identity, FacadeTile, Geo.Faces.Sides, Vector2.zero, h0);
                    L[ConcDark].Box(new Vector3(0f, h0 + ch, 0f), new Vector3(cw + 0.2f, 0.3f, cd + 0.2f), Quaternion.identity, T4);
                    top = h0 + ch;
                }
                if (rng.Chance(0.4f))
                {
                    float ah = rng.Range(6f, 20f);
                    L[Metal].Strut(new Vector3(0f, top, 0f), new Vector3(0f, top + ah, 0f), 0.35f, T1);
                    top += ah;
                }
                for (int k = rng.Range(1, 4); k > 0; k--)
                    L[Rust].Box(new Vector3(rng.Range(-w * 0.35f, w * 0.35f), h0 + 0.9f, rng.Range(-dpt * 0.35f, dpt * 0.35f)), new Vector3(rng.Range(1.5f, 3.5f), 1.8f, rng.Range(1.5f, 3f)), rng.Range(0f, 90f), T3);
            }
            var rot = Quaternion.Euler(0f, yaw, 0f);
            if (Mathf.Abs(lean) > 0.01f) rot = Quaternion.AngleAxis(lean, Quaternion.Euler(0f, rng.Range(0f, 360f), 0f) * Vector3.right) * rot;
            target.Append(L, rot, basePos);
            return top;
        }

        static float Crown(GeoSet L, Rng rng, float w, float dpt, float y0, float ch, string facade)
        {
            float maxTop = y0;
            for (int f = 0; f < 4; f++)
            {
                float fw = f % 2 == 0 ? w : dpt;
                float fd = f % 2 == 0 ? dpt : w;
                var frot = Quaternion.Euler(0f, f * 90f, 0f);
                int bays = Mathf.Max(2, Mathf.RoundToInt(fw / 3f));
                float bw = fw / bays;
                for (int b = 0; b < bays; b++)
                {
                    if (rng.Chance(0.22f)) continue; // a chunk is simply gone
                    float ph = ch * Mathf.Pow(rng.Value, 0.6f);
                    if (ph < 0.8f) continue;
                    float xo = -fw * 0.5f + bw * (b + 0.5f);
                    Vector3 c = frot * new Vector3(xo, y0 + ph * 0.5f, fd * 0.5f - 0.25f);
                    L[facade].Box(c, new Vector3(bw, ph, 0.5f), frot, FacadeTile, Geo.Faces.All, new Vector2((xo - bw * 0.5f + fw * 0.5f) / FacadeTile.x, 0f), y0);
                    maxTop = Mathf.Max(maxTop, y0 + ph);
                    // rebar sticking out of the broken edge
                    int bars = rng.Range(0, 4);
                    for (int r = 0; r < bars; r++)
                    {
                        Vector3 a = frot * new Vector3(xo + rng.Range(-bw * 0.4f, bw * 0.4f), y0 + ph, fd * 0.5f - 0.25f);
                        Vector3 bEnd = a + new Vector3(rng.Range(-0.4f, 0.4f), rng.Range(0.6f, 1.8f), rng.Range(-0.4f, 0.4f));
                        L[Rust].Strut(a, bEnd, 0.05f, T1);
                    }
                }
            }
            // exposed floor slabs, some cracked and hanging
            for (float y = y0 + 3.5f; y < y0 + ch - 0.5f; y += 3.5f)
            {
                if (rng.Chance(0.35f)) continue;
                float sw = w * rng.Range(0.5f, 0.98f), sd = dpt * rng.Range(0.5f, 0.98f);
                var srot = rng.Chance(0.3f) ? Quaternion.Euler(rng.Range(-18f, 18f), 0f, rng.Range(-14f, 14f)) : Quaternion.identity;
                L[ConcDark].Box(new Vector3(rng.Range(-(w - sw) * 0.5f, (w - sw) * 0.5f), y, rng.Range(-(dpt - sd) * 0.5f, (dpt - sd) * 0.5f)), new Vector3(sw, 0.35f, sd), srot, T4);
                // a column or two
                if (rng.Chance(0.5f)) L[ConcDark].Box(new Vector3(rng.Range(-w * 0.3f, w * 0.3f), y - 1.75f, rng.Range(-dpt * 0.3f, dpt * 0.3f)), new Vector3(0.6f, 3.5f, 0.6f), Quaternion.identity, T4);
            }
            return maxTop;
        }

        /// <summary>A ring of towers around the arena. Far rings skip shadows and detail.</summary>
        static void Skyline(CityPlan plan, Rng rng, float baseY, float rMin, float rMax, int rings, float density, float ruinChance, Func<Vector3, bool> keepOut, float heightBias = 0f)
        {
            int fires = 0, smokes = 0, searches = 0, holos = 0;
            for (int ring = 0; ring < rings; ring++)
            {
                float r0 = rMin + ring * (rMax - rMin) / rings, r1 = r0 + (rMax - rMin) / rings;
                int count = Mathf.RoundToInt((10 + ring * 7) * density);
                for (int i = 0; i < count; i++)
                {
                    float a = (i + rng.Range(-0.4f, 0.4f)) / count * 360f;
                    var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                    float r = rng.Range(r0, r1);
                    Vector3 p = dir * r;
                    if (keepOut != null && keepOut(p)) continue;
                    float w = Mathf.Round(rng.Range(12f, 30f) * (1f + ring * 0.15f) / 3f) * 3f;
                    float dd = Mathf.Round(rng.Range(12f, 30f) * (1f + ring * 0.15f) / 3f) * 3f;
                    float topY = rng.Range(-40f, 45f) + heightBias + (ring >= 1 ? rng.Range(0f, 100f) : 0f) + (rng.Chance(0.12f) ? 90f : 0f);
                    float h = topY - baseY;
                    if (h < 12f) h = 12f;
                    bool ruined = rng.Chance(ruinChance);
                    float lean = ruined && rng.Chance(0.12f) ? rng.Range(5f, 13f) : 0f;
                    string fac = rng.Chance(0.2f) ? FacadeLit : rng.Chance(0.3f) ? FacadeBrick : Facade;
                    var target = plan.geoNoShadow;
                    float yaw = a + rng.Range(-20f, 20f);
                    float top = Tower(plan, target, rng, new Vector3(p.x, baseY, p.z), w, dd, h, yaw, fac, ruined, lean, ring < 2);
                    Vector3 roof = new Vector3(p.x, baseY + top, p.z);
                    var rot = Quaternion.Euler(0f, yaw, 0f);
                    if (!ruined && top - baseY > 40f && rng.Chance(0.5f)) plan.props.Add(new PropSpec { kind = PropKind.Beacon, pos = roof + Vector3.up * 1.2f, scale = 1.3f });
                    // surviving neon on a few towers
                    if (rng.Chance(0.3f))
                    {
                        Color nc = rng.Pick(NeonSet);
                        string key = Glow(nc, 4f);
                        if (rng.Chance(0.5f))
                        {
                            Vector3 corner = p + rot * new Vector3(w * 0.5f + 0.2f, 0f, -dd * 0.5f - 0.2f);
                            float sh = Mathf.Min(70f, top * 0.6f);
                            plan.geoNoShadow[key].Box(new Vector3(corner.x, baseY + top - sh * 0.5f - 2f, corner.z), new Vector3(0.5f, sh, 0.5f), rot, T1);
                        }
                        else plan.geoNoShadow[key].Box(new Vector3(p.x, baseY + top - rng.Range(3f, 8f), p.z), new Vector3(w + 0.6f, 1.1f, dd + 0.6f), rot, T1, Geo.Faces.Sides);
                    }
                    // fires and smoke in the ruins (only where you can see them)
                    if (ruined && r < 260f && fires < 10 && rng.Chance(0.45f))
                    {
                        fires++;
                        Vector3 side = rot * new Vector3(rng.Range(-w * 0.4f, w * 0.4f), 0f, dd * 0.5f + 0.4f);
                        float fy = baseY + rng.Range(top * 0.4f, top * 0.9f);
                        var fp = new Vector3(p.x + side.x, fy, p.z + side.z);
                        plan.emitters.Add(new EmitterSpec { kind = EmitterKind.Fire, pos = fp, size = rng.Range(2.5f, 5f) });
                        plan.lights.Add(new LightSpec { kind = LightKind.Point, pos = fp + (fp - new Vector3(p.x, fy, p.z)).normalized * 3f, color = new Color(1f, 0.45f, 0.15f), intensity = 60f, range = 40f, flicker = 2 });
                    }
                    if ((ruined || rng.Chance(0.1f)) && smokes < 8 && rng.Chance(0.4f))
                    {
                        smokes++;
                        plan.emitters.Add(new EmitterSpec { kind = EmitterKind.Smoke, pos = roof + Vector3.up * 2f, size = rng.Range(6f, 12f), color = new Color(0.12f, 0.11f, 0.12f, 0.5f) });
                    }
                    if (ring >= 1 && top - baseY > 90f && searches < 5 && rng.Chance(0.3f))
                    {
                        searches++;
                        plan.props.Add(new PropSpec { kind = PropKind.Searchlight, pos = roof + Vector3.up * 2f, yaw = rng.Range(0f, 360f), seed = rng.Seed() });
                    }
                    if (!ruined && holos < 1 && ring == 0 && r < 140f && rng.Chance(0.25f))
                    {
                        holos++;
                        plan.props.Add(new PropSpec { kind = PropKind.HoloGiant, pos = roof, yaw = a + 180f, scale = rng.Range(16f, 22f), color = rng.Chance(0.5f) ? NeonCyan : NeonPink, seed = rng.Seed() });
                    }
                    if (ring == 0 && r < 160f) plan.colliders.Add(new ColliderSpec { center = new Vector3(p.x, baseY + h * 0.5f, p.z), size = new Vector3(w, h, dd), yaw = yaw });
                }
            }
        }

        /// <summary>The Halcyon Spire on the horizon, crowned by the Monolith with HALCYON's red eye. Visible from everywhere.</summary>
        static void DistantSpire(CityPlan plan, float groundY)
        {
            Vector3 p = Quaternion.Euler(0f, 38f, 0f) * Vector3.forward * 460f;
            p.y = groundY;
            var g = plan.geoNoShadow;
            float h = 520f;
            g[FacadeLit].Cylinder(p, 28f, 12f, h * 0.75f, 24, FacadeTile, false);
            g[ConcDark].Cylinder(p + Vector3.up * (h * 0.75f), 12f, 7f, h * 0.2f, 16, T4, true);
            for (int k = 1; k < 9; k++)
                g[Glow(NeonRed, 3f)].Ring(p + Vector3.up * (h * 0.08f * k), 28f - k * 1.9f, 28.6f - k * 1.9f, 24, 4f);
            Vector3 top = p + Vector3.up * (h * 0.95f);
            g[Mono].Box(top + Vector3.up * 22f, new Vector3(18f, 44f, 4.5f), Quaternion.LookRotation(-new Vector3(p.x, 0f, p.z).normalized), T4);
            Vector3 face = top + Vector3.up * 26f - new Vector3(p.x, 0f, p.z).normalized * 2.3f;
            g[Glow(NeonRed, 9f)].Card(face, 3.2f, 3.2f, -new Vector3(p.x, 0f, p.z).normalized, false, Vector2.zero, Vector2.one);
            plan.props.Add(new PropSpec { kind = PropKind.Beacon, pos = top + Vector3.up * 46f, scale = 3f });
            plan.props.Add(new PropSpec { kind = PropKind.Searchlight, pos = top + Vector3.up * 10f, yaw = 0f, seed = 7 });
            plan.props.Add(new PropSpec { kind = PropKind.Searchlight, pos = top + Vector3.up * 10f, yaw = 180f, seed = 8 });
        }

        static void AirTraffic(CityPlan plan, Rng rng, int count, float yMin, float yMax, float rMin, float rMax)
        {
            for (int i = 0; i < count; i++)
                plan.props.Add(new PropSpec
                {
                    kind = PropKind.FlyingCar,
                    pos = new Vector3(0f, rng.Range(yMin, yMax), 0f),
                    extra = new Vector3(rng.Range(rMin, rMax), rng.Range(0.6f, 1f), rng.Chance(0.5f) ? 1f : -1f),
                    yaw = rng.Range(0f, 360f),
                    scale = 1f,
                    color = rng.Chance(0.3f) ? NeonRed : NeonWhite, // red/blue strobes = police spinner
                    seed = rng.Seed()
                });
        }

        /// <summary>A boxy 2019-vintage future sedan, intact or wrecked (tilted, burnt, flipped, missing wheels).</summary>
        static void Car(CityPlan plan, Rng rng, Vector3 pos, float yaw, bool wrecked)
        {
            var L = new GeoSet();
            Color[] paints = { new Color(0.55f, 0.08f, 0.06f), new Color(0.1f, 0.14f, 0.22f), new Color(0.7f, 0.68f, 0.62f), new Color(0.12f, 0.12f, 0.13f), new Color(0.65f, 0.45f, 0.1f), new Color(0.15f, 0.3f, 0.32f) };
            Color paint = rng.Pick(paints);
            if (wrecked) paint *= rng.Range(0.35f, 0.7f);
            string body = Tint("Car", paint);
            L[body].Box(new Vector3(0f, 0.62f, 0f), new Vector3(1.9f, 0.62f, 4.6f), Quaternion.identity, T3);
            L[body].Box(new Vector3(0f, 0.62f, 2.15f), new Vector3(1.86f, 0.4f, 0.5f), Quaternion.Euler(-12f, 0f, 0f), T3);
            L[Glass].Box(new Vector3(0f, 1.2f, -0.2f), new Vector3(1.7f, 0.56f, 2.3f), Quaternion.identity, T3, Geo.Faces.Sides);
            L[body].Box(new Vector3(0f, 1.5f, -0.25f), new Vector3(1.72f, 0.07f, 2.1f), Quaternion.identity, T3);
            L[Metal].Box(new Vector3(0f, 0.38f, 2.32f), new Vector3(1.95f, 0.22f, 0.12f), Quaternion.identity, T1);
            L[Metal].Box(new Vector3(0f, 0.38f, -2.32f), new Vector3(1.95f, 0.22f, 0.12f), Quaternion.identity, T1);
            L[wrecked ? Glow(new Color(0.3f, 0.05f, 0.03f), 1f) : Glow(new Color(1f, 0.08f, 0.05f), 2.5f)].Box(new Vector3(0f, 0.72f, -2.31f), new Vector3(1.6f, 0.08f, 0.04f), Quaternion.identity, T1);
            bool lights = !wrecked || rng.Chance(0.15f);
            L[lights ? Glow(new Color(1f, 0.9f, 0.7f), 4f) : Dark].Box(new Vector3(0f, 0.75f, 2.33f), new Vector3(1.5f, 0.07f, 0.04f), Quaternion.identity, T1);
            for (int k = 0; k < 4; k++)
            {
                if (wrecked && rng.Chance(0.3f)) continue;
                float x = k % 2 == 0 ? -0.9f : 0.9f, z = k < 2 ? 1.45f : -1.45f;
                L[Rubber].Cylinder(new Vector3(x - 0.12f * Mathf.Sign(x), 0.36f, z), 0.36f, 0.36f, 0.26f, 10, T1, true, Quaternion.Euler(0f, 0f, 90f));
            }
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 p = pos;
            if (wrecked)
            {
                if (rng.Chance(0.2f)) { rot = rot * Quaternion.Euler(0f, 0f, 180f); p += Vector3.up * 1.6f; } // upside down
                else rot = rot * Quaternion.Euler(rng.Range(-6f, 6f), 0f, rng.Range(-9f, 9f));
            }
            plan.geo.Append(L, rot, p);
            plan.colliders.Add(new ColliderSpec { center = pos + Vector3.up * 0.8f, size = new Vector3(2f, 1.6f, 4.7f), yaw = yaw });
            if (lights && !wrecked) plan.lights.Add(new LightSpec { kind = LightKind.Spot, pos = pos + rot * new Vector3(0f, 0.8f, 2.5f), dir = rot * new Vector3(0f, -0.15f, 1f), color = new Color(1f, 0.92f, 0.78f), intensity = 30f, range = 22f, angle = 55f });
        }

        static void JerseyBarrier(CityPlan plan, Vector3 pos, float yaw, bool hazard)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            string m = hazard ? Haz : Conc;
            plan.geo[m].Box(pos + Vector3.up * 0.3f, new Vector3(3f, 0.6f, 0.75f), rot, T2);
            plan.geo[m].Box(pos + Vector3.up * 0.82f, new Vector3(3f, 0.45f, 0.38f), rot, T2);
            plan.colliders.Add(new ColliderSpec { center = pos + Vector3.up * 0.55f, size = new Vector3(3f, 1.1f, 0.75f), yaw = yaw });
        }

        static void FireBarrel(CityPlan plan, Vector3 pos, bool survivors, Rng rng)
        {
            plan.geo[RustRed].Cylinder(pos, 0.32f, 0.32f, 0.9f, 12, T1, false);
            plan.emitters.Add(new EmitterSpec { kind = EmitterKind.Fire, pos = pos + Vector3.up * 0.85f, size = 0.7f });
            plan.emitters.Add(new EmitterSpec { kind = EmitterKind.Embers, pos = pos + Vector3.up * 1f, size = 1f });
            plan.lights.Add(new LightSpec { kind = LightKind.Point, pos = pos + Vector3.up * 1.6f, color = new Color(1f, 0.5f, 0.18f), intensity = 9f, range = 9f, flicker = 2, shadows = false });
            if (!survivors) return;
            int n = rng.Range(2, 4);
            float a0 = rng.Range(0f, 360f);
            for (int i = 0; i < n; i++)
            {
                float a = a0 + i * (360f / n) + rng.Range(-20f, 20f);
                Vector3 off = Quaternion.Euler(0f, a, 0f) * Vector3.forward * rng.Range(1f, 1.4f);
                plan.props.Add(new PropSpec { kind = PropKind.Survivor, pos = pos + off, yaw = a + 180f, scale = rng.Range(0.88f, 1f), seed = rng.Seed() });
            }
        }

        static void StreetLamp(CityPlan plan, Vector3 pos, float yaw, Rng rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            plan.geo[Metal].Cylinder(pos, 0.11f, 0.08f, 7f, 8, T1, true);
            Vector3 head = pos + Vector3.up * 7f + rot * Vector3.forward * 1.6f;
            plan.geo[Metal].Strut(pos + Vector3.up * 6.9f, head, 0.1f, T1);
            int state = rng.Chance(0.25f) ? 0 : rng.Chance(0.25f) ? 3 : 1; // dead, dying, alive
            plan.geo[state == 0 ? Dark : Glow(new Color(1f, 0.62f, 0.3f), 5f)].Box(head + Vector3.down * 0.1f, new Vector3(0.45f, 0.12f, 0.7f), rot, T1);
            if (state != 0)
                plan.lights.Add(new LightSpec { kind = LightKind.Spot, pos = head + Vector3.down * 0.2f, dir = Vector3.down, color = new Color(1f, 0.6f, 0.28f), intensity = 70f, range = 16f, angle = 100f, flicker = state == 3 ? 3 : 0 });
        }

        // ================================================================ ROOFTOP (the scene supplies the helipad)

        static void Rooftop(CityPlan plan, Rng rng)
        {
            plan.arena = ArenaShape.Circle(16.2f);
            plan.hasFloor = false; // built into the scene
            plan.spawnStyle = 0;
            plan.playerStart = new Vector3(0f, 0f, -4f);
            for (int i = 0; i < 8; i++) plan.spawnPoints.Add(Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * 12f);
            Skyline(plan, rng, -190f, 48f, 330f, 4, 1f, 0.45f, null);
            DistantSpire(plan, -190f);
            AirTraffic(plan, rng, 9, -30f, 70f, 70f, 260f);
            // the terrace below the helipad: a crashed police spinner still burning, scavengers by a barrel
            Vector3 crash = Quaternion.Euler(0f, 128f, 0f) * Vector3.forward * 26f + Vector3.up * -7f;
            plan.props.Add(new PropSpec { kind = PropKind.FlyingCar, pos = crash, yaw = 128f + 70f, scale = 1f, extra = Vector3.zero, color = NeonRed, seed = 99, text = "wreck" });
            plan.emitters.Add(new EmitterSpec { kind = EmitterKind.Fire, pos = crash + Vector3.up * 1.2f, size = 2.2f });
            plan.emitters.Add(new EmitterSpec { kind = EmitterKind.Smoke, pos = crash + Vector3.up * 2f, size = 4f, color = new Color(0.1f, 0.09f, 0.1f, 0.6f) });
            plan.lights.Add(new LightSpec { kind = LightKind.Point, pos = crash + Vector3.up * 2.5f, color = new Color(1f, 0.45f, 0.15f), intensity = 40f, range = 18f, flicker = 2 });
            FireBarrel(plan, Quaternion.Euler(0f, 250f, 0f) * Vector3.forward * 27f + Vector3.up * -7f, true, rng);
            plan.wetFloor = true;
        }

        // ================================================================ STREETS: Lantern Street, flooded neon (Blade Runner)

        static readonly string[] ShopSigns = { "NOODLES 24H", "MEMORY CLINIC", "PAWN  WE BUY LIMBS", "BAR VANITY", "REPAIR  ALL MODELS", "HOTEL ARCADIA", "SYNTH LOVE", "LUCKY 88", "DRAGON TEA", "PHARMACY", "KARAOKE", "OFF-WORLD TRAVEL", "BATTERIES", "CHROME & SON", "LIVE EELS", "DREAMS RECORDED", "GOLEM CLAY & REPAIR", "88.9 FM", "SPRINGS & BOOTS", "WISHES SPELLED CORRECTLY" };
        static readonly string[] Blades = { "HOTEL", "BAR", "NOODLE", "LUCKY", "OPEN", "SOUL", "CLINIC", "RAMEN", "DRINK" };
        static readonly string[] Slogans = { "IT IS ALWAYS 11:59", "HALCYON CARES", "IT IS ALWAYS NOW", "STAY INSIDE  STAY DRY", "RESTORATION IN PROGRESS", "THE COLONIES ARE CALLING", "SATISFACTION 100%", "YOUR CITY IS AN ICON" };

        static void Streets(CityPlan plan, Rng rng)
        {
            plan.arena = ArenaShape.Circle(15f);
            plan.floorMaterial = Asphalt;
            plan.floorTile = 6f;
            plan.floorSize = 320f;
            plan.spawnStyle = 1;
            plan.playerStart = new Vector3(0f, 0f, -6f);
            const float road = 10f, walk = 13f, len = 140f;

            // sidewalks (raised outside the plaza) and corner paving
            foreach (int sx in new[] { -1, 1 })
            {
                plan.geo[Conc].Box(new Vector3(sx * (road + walk) * 0.5f, 0.07f, (len + 16f) * 0.5f), new Vector3(walk - road, 0.14f, len - 16f), Quaternion.identity, T3);
                plan.geo[Conc].Box(new Vector3(sx * (road + walk) * 0.5f, 0.07f, -(len + 16f) * 0.5f), new Vector3(walk - road, 0.14f, len - 16f), Quaternion.identity, T3);
                plan.geo[Conc].Box(new Vector3((len + 16f) * 0.5f, 0.07f, sx * (road + walk) * 0.5f), new Vector3(len - 16f, 0.14f, walk - road), Quaternion.identity, T3);
                plan.geo[Conc].Box(new Vector3(-(len + 16f) * 0.5f, 0.07f, sx * (road + walk) * 0.5f), new Vector3(len - 16f, 0.14f, walk - road), Quaternion.identity, T3);
            }
            // lane markings (faded), crosswalks around the plaza
            string paint = Plain(new Color(0.75f, 0.72f, 0.62f), 0f, 0.4f);
            for (float z = 20f; z < len; z += 8f)
            {
                plan.geoNoShadow[paint].Box(new Vector3(0f, 0.012f, z), new Vector3(0.18f, 0.01f, 3.5f), Quaternion.identity, T1, Geo.Faces.PosY);
                plan.geoNoShadow[paint].Box(new Vector3(0f, 0.012f, -z), new Vector3(0.18f, 0.01f, 3.5f), Quaternion.identity, T1, Geo.Faces.PosY);
                plan.geoNoShadow[paint].Box(new Vector3(z, 0.012f, 0f), new Vector3(3.5f, 0.01f, 0.18f), Quaternion.identity, T1, Geo.Faces.PosY);
                plan.geoNoShadow[paint].Box(new Vector3(-z, 0.012f, 0f), new Vector3(3.5f, 0.01f, 0.18f), Quaternion.identity, T1, Geo.Faces.PosY);
            }
            for (int q = 0; q < 4; q++)
            {
                var r = Quaternion.Euler(0f, q * 90f, 0f);
                for (int s = -4; s <= 4; s++)
                    plan.geoNoShadow[paint].Box(r * new Vector3(s * 2f, 0.012f, 17.5f), new Vector3(1.1f, 0.01f, 3.2f), r, T1, Geo.Faces.PosY);
            }

            // building frontages along both sides of both streets
            int signIndex = 0;
            for (int q = 0; q < 4; q++)
            {
                var r = Quaternion.Euler(0f, q * 90f, 0f);
                for (int side = -1; side <= 1; side += 2)
                {
                    // frontage line runs along +Z (rotated by q) at x = side * walk, starting past the plaza corner
                    float t = walk + 1f;
                    while (t < len)
                    {
                        float bw = rng.Pick(new[] { 9f, 12f, 12f, 15f, 18f });
                        float depth = rng.Range(12f, 22f);
                        float h = rng.Range(18f, 42f) + (t > 60f ? rng.Range(0f, 50f) : 0f) + (rng.Chance(0.15f) ? 40f : 0f);
                        Vector3 local = new Vector3(side * (walk + depth * 0.5f), 0f, t + bw * 0.5f);
                        float yaw = q * 90f + (side > 0 ? -90f : 90f); // local +Z faces the street
                        StreetBuilding(plan, rng, r * local, yaw, bw, depth, h, ref signIndex, t < 40f);
                        t += bw + rng.Range(0f, 1.5f);
                    }
                }
                // corner building at the plaza
                Vector3 cornerLocal = new Vector3(walk + 10f, 0f, walk + 10f);
                Tower(plan, plan.geo, rng, r * cornerLocal, 18f, 18f, rng.Range(30f, 60f), q * 90f + 45f, rng.Chance(0.5f) ? FacadeBrick : Facade, rng.Chance(0.5f));
                plan.colliders.Add(new ColliderSpec { center = r * cornerLocal + Vector3.up * 20f, size = new Vector3(18f, 40f, 18f), yaw = q * 90f + 45f });
                // the far end of each street is closed by a big ruin
                Vector3 endLocal = new Vector3(0f, 0f, len + 14f);
                Tower(plan, plan.geo, rng, r * endLocal, 60f, 24f, rng.Range(50f, 90f), q * 90f, Facade, true);
            }

            // Lantern Street: cables and paper lanterns strung across the streets
            for (int q = 0; q < 4; q++)
            {
                var r = Quaternion.Euler(0f, q * 90f, 0f);
                for (float z = 22f; z < 110f; z += rng.Range(5f, 9f))
                {
                    float y0 = rng.Range(7f, 15f), y1 = y0 + rng.Range(-2f, 2f);
                    Vector3 a = r * new Vector3(-walk, y0, z + rng.Range(-2f, 2f)), b = r * new Vector3(walk, y1, z + rng.Range(-2f, 2f));
                    float sag = rng.Range(0.8f, 2.2f);
                    plan.geoNoShadow[Rubber].Cable(a, b, sag, 0.035f, 12);
                    if (rng.Chance(0.55f))
                    {
                        Color lc = rng.Chance(0.7f) ? new Color(1f, 0.18f, 0.08f) : NeonAmber;
                        string lk = Glow(lc, 3.2f);
                        int count = Mathf.RoundToInt((b - a).magnitude / 1.6f);
                        for (int k = 1; k < count; k++)
                        {
                            float u = k / (float)count;
                            Vector3 p = Vector3.Lerp(a, b, u) + Vector3.down * (sag * 4f * u * (1f - u) + 0.35f);
                            plan.geoNoShadow[lk].Cylinder(p, 0.2f, 0.2f, 0.42f, 8, T1, true);
                        }
                        plan.lights.Add(new LightSpec { kind = LightKind.Point, pos = (a + b) * 0.5f + Vector3.down * (sag + 0.5f), color = lc, intensity = 14f, range = 11f, flicker = 1 });
                    }
                }
            }

            // the plaza: barricades closing the four street mouths, wrecks, a traffic light still blinking amber
            for (int q = 0; q < 4; q++)
            {
                var r = Quaternion.Euler(0f, q * 90f, 0f);
                float yaw = q * 90f;
                JerseyBarrier(plan, r * new Vector3(-5.5f, 0f, 18.5f), yaw + rng.Range(-8f, 8f), true);
                JerseyBarrier(plan, r * new Vector3(-1.8f, 0f, 19.3f), yaw + rng.Range(-8f, 8f), false);
                Car(plan, rng, r * new Vector3(4.2f, 0f, 19.5f), yaw + 90f + rng.Range(-25f, 25f), true);
                plan.anchors.Add(new AnchorSpec { pos = r * new Vector3(-5.5f, 0.6f, 18.1f), normal = r * Vector3.back, label = "barrier" });
                plan.anchors.Add(new AnchorSpec { pos = r * new Vector3(-1.8f, 0.6f, 18.9f), normal = r * Vector3.back, label = "barrier" });
                plan.anchors.Add(new AnchorSpec { pos = r * new Vector3(4.2f, 0.8f, 18.4f), normal = r * Vector3.back, label = "car" });
                // diagonal corners: a parked wreck and a burning barrel with scavengers
                Vector3 diag = Quaternion.Euler(0f, q * 90f + 45f, 0f) * Vector3.forward;
                Car(plan, rng, diag * 17.5f, q * 90f + 45f + 90f + rng.Range(-20f, 20f), rng.Chance(0.6f));
                plan.anchors.Add(new AnchorSpec { pos = diag * 16.4f + Vector3.up * 0.8f, normal = -diag, label = "car" });
                FireBarrel(plan, diag * 21.5f + Quaternion.Euler(0f, q * 90f, 0f) * Vector3.right * 2f, q % 2 == 0, rng);
                // street lamps along the sidewalks
                for (float z = 24f; z < 120f; z += rng.Range(14f, 20f))
                {
                    StreetLamp(plan, r * new Vector3(10.8f, 0.14f, z), yaw - 90f, rng);
                    StreetLamp(plan, r * new Vector3(-10.8f, 0.14f, z + 7f), yaw + 90f, rng);
                }
                // more wrecks down each street
                for (float z = 28f; z < 120f; z += rng.Range(9f, 18f))
                    Car(plan, rng, r * new Vector3(rng.Chance(0.5f) ? 7.5f : -7.5f, 0f, z), yaw + rng.Range(-15f, 15f) + (rng.Chance(0.5f) ? 180f : 0f), rng.Chance(0.75f));
                // steam rising from the manholes
                plan.emitters.Add(new EmitterSpec { kind = EmitterKind.Steam, pos = r * new Vector3(rng.Range(-4f, 4f), 0f, rng.Range(26f, 60f)), size = 1.4f });
            }
            plan.emitters.Add(new EmitterSpec { kind = EmitterKind.Steam, pos = new Vector3(5f, 0f, 6f), size = 1f });
            plan.emitters.Add(new EmitterSpec { kind = EmitterKind.Steam, pos = new Vector3(-7f, 0f, -4f), size = 0.8f });
            plan.props.Add(new PropSpec { kind = PropKind.TrafficLight, pos = new Vector3(11.5f, 0f, 11.5f), yaw = 225f });
            plan.props.Add(new PropSpec { kind = PropKind.TrafficLight, pos = new Vector3(-11.5f, 0f, -11.5f), yaw = 45f });

            // a giant hologram towering over the north street, and a slogan across a facade
            plan.props.Add(new PropSpec { kind = PropKind.HoloGiant, pos = new Vector3(0f, 0f, 92f), yaw = 180f, scale = 24f, color = NeonCyan, seed = 3 });
            plan.props.Add(new PropSpec { kind = PropKind.Text, pos = new Vector3(-13.3f, 34f, 64f), yaw = 90f, scale = 26f, text = "IT IS ALWAYS 11:59", color = NeonPink });
            plan.props.Add(new PropSpec { kind = PropKind.Text, pos = new Vector3(64f, 28f, 13.3f), yaw = 180f, scale = 18f, text = "HALCYON CARES", color = NeonCyan });
            plan.props.Add(new PropSpec { kind = PropKind.Text, pos = new Vector3(64f, 17f, 13.3f), yaw = 180f, scale = 4.5f, text = "CITIZEN SATISFACTION 100%\n(3 RESPONDENTS)", color = NeonCyan });
            // chalk-and-paint legends at street level: the Spring Man, and the radio
            plan.props.Add(new PropSpec { kind = PropKind.Text, pos = new Vector3(-13.25f, 2.6f, 34f), yaw = 90f, scale = 1.1f, text = "THE SPRING MAN LIVES", color = NeonRed });
            plan.props.Add(new PropSpec { kind = PropKind.Text, pos = new Vector3(34f, 2.4f, -13.25f), yaw = 0f, scale = 0.9f, text = "TUNE 88.9  IT'S NOT 11:59", color = NeonGreen });

            // skyline beyond the blocks
            Skyline(plan, rng, 0f, 70f, 380f, 3, 1.1f, 0.5f, p => Mathf.Abs(p.x) < 70f && Mathf.Abs(p.z) < 70f, 70f);
            DistantSpire(plan, 0f);
            AirTraffic(plan, rng, 10, 30f, 110f, 50f, 240f);
            plan.wetFloor = true;
            plan.wetStrength = 1f;
        }

        static void StreetBuilding(CityPlan plan, Rng rng, Vector3 basePos, float yaw, float w, float d, float h, ref int signIndex, bool nearPlaza)
        {
            var L = new GeoSet();
            string fac = rng.Chance(0.35f) ? FacadeBrick : rng.Chance(0.25f) ? FacadeLit : Facade;
            const float gf = 5f;
            bool ruined = rng.Chance(0.3f);
            float crown = ruined ? rng.Range(4f, 12f) : 0f;
            float h0 = h - crown;
            // ground floor (storefront band) and the facade above
            L[rng.Chance(0.5f) ? BrickM : ConcDark].Box(new Vector3(0f, gf * 0.5f, -0.3f), new Vector3(w, gf, d - 0.6f), Quaternion.identity, T3, Geo.Faces.Sides);
            L[fac].Box(new Vector3(0f, gf + (h0 - gf) * 0.5f, 0f), new Vector3(w, h0 - gf, d), Quaternion.identity, FacadeTile, Geo.Faces.Sides | Geo.Faces.NegY, new Vector2(rng.Range(0, 4) * 0.25f, 0f), gf);
            L[ConcDark].Box(new Vector3(0f, h0 - 0.2f, 0f), new Vector3(w + 0.3f, 0.4f, d + 0.3f), Quaternion.identity, T4);
            L[Conc].Box(new Vector3(0f, gf, d * 0.5f + 0.1f), new Vector3(w, 0.35f, 0.5f), Quaternion.identity, T4); // cornice over the shops
            if (ruined) Crown(L, rng, w, d, h0, crown, fac);

            // storefronts: shutters or lit windows, awnings, signs
            float front = d * 0.5f;
            int shops = Mathf.Max(1, Mathf.RoundToInt(w / 6f));
            float sw = w / shops;
            var signs = new List<PropSpec>();
            for (int s = 0; s < shops; s++)
            {
                float x = -w * 0.5f + sw * (s + 0.5f);
                if (rng.Chance(0.45f))
                {
                    L[Rust].Box(new Vector3(x, 1.7f, front - 0.25f), new Vector3(sw - 0.8f, 3.4f, 0.12f), Quaternion.identity, T3);
                }
                else
                {
                    Color inside = rng.Chance(0.5f) ? new Color(1f, 0.55f, 0.25f) : rng.Pick(NeonSet);
                    bool lit = rng.Chance(0.6f);
                    L[lit ? Glow(inside * 0.35f, 1.4f) : Glass].Box(new Vector3(x, 1.7f, front - 0.35f), new Vector3(sw - 0.8f, 3.2f, 0.1f), Quaternion.identity, T1);
                    if (lit) plan.lights.Add(new LightSpec { kind = LightKind.Point, pos = Local(basePos, yaw, new Vector3(x, 2f, front + 1.2f)), color = inside, intensity = 6f, range = 7f, flicker = rng.Chance(0.2f) ? 3 : 0 });
                    // the sign over the shop
                    Color nc = rng.Pick(NeonSet);
                    // fit the lettering to the shopfront so neighbouring signs never run into each other
                    string label = ShopSigns[signIndex++ % ShopSigns.Length];
                    float letter = Mathf.Min(1.25f, (sw - 0.7f) / (label.Length * 0.62f));
                    signs.Add(new PropSpec { kind = PropKind.Text, pos = new Vector3(x, 4.2f, front + 0.12f), yaw = 0f, scale = letter, text = label, color = nc });
                    L[Glow(nc, 3.5f)].Box(new Vector3(x, 3.62f, front + 0.08f), new Vector3(sw - 1.2f, 0.05f, 0.05f), Quaternion.identity, T1);
                    if (rng.Chance(0.5f))
                    {
                        string awn = Tint("Car", rng.Chance(0.5f) ? new Color(0.5f, 0.1f, 0.08f) : new Color(0.12f, 0.25f, 0.3f));
                        L[awn].Box(new Vector3(x, 3.7f, front + 0.9f), new Vector3(sw - 0.6f, 0.08f, 1.8f), Quaternion.Euler(-14f, 0f, 0f), T2);
                    }
                }
            }
            // vertical blade sign
            if (rng.Chance(0.55f))
            {
                float bx = rng.Range(-w * 0.35f, w * 0.35f), by = rng.Range(7f, 11f);
                Color nc = rng.Pick(NeonSet);
                string word = rng.Pick(Blades);
                float bh = word.Length * 1.1f + 1f;
                L[Dark].Box(new Vector3(bx, by + bh * 0.5f, front + 1.1f), new Vector3(0.25f, bh, 1.9f), Quaternion.identity, T1);
                L[Glow(nc, 4.5f)].Box(new Vector3(bx, by + bh * 0.5f, front + 1.1f), new Vector3(0.3f, bh + 0.1f, 0.06f), Quaternion.identity, T1);
                var vertical = string.Join("\n", word.ToCharArray());
                signs.Add(new PropSpec { kind = PropKind.Text, pos = new Vector3(bx + 0.14f, by + bh * 0.5f, front + 1.1f), yaw = 90f, scale = 1.05f, text = vertical, color = nc });
                signs.Add(new PropSpec { kind = PropKind.Text, pos = new Vector3(bx - 0.14f, by + bh * 0.5f, front + 1.1f), yaw = -90f, scale = 1.05f, text = vertical, color = nc });
                plan.lights.Add(new LightSpec { kind = LightKind.Point, pos = Local(basePos, yaw, new Vector3(bx, by + bh * 0.5f, front + 2.6f)), color = nc, intensity = 12f, range = 12f, flicker = 1 });
            }
            // fire escape
            if (rng.Chance(0.45f) && h0 > 14f)
            {
                float fx = rng.Range(-w * 0.25f, w * 0.25f);
                for (float y = 7f; y < Mathf.Min(h0 - 2f, 30f); y += 3.5f)
                {
                    L[Rust].Box(new Vector3(fx, y, front + 0.7f), new Vector3(4f, 0.08f, 1.3f), Quaternion.identity, T1);
                    L[Rust].Strut(new Vector3(fx - 2f, y + 1f, front + 1.35f), new Vector3(fx + 2f, y + 1f, front + 1.35f), 0.05f, T1);
                    L[Rust].Strut(new Vector3(fx - 1.6f, y, front + 1f), new Vector3(fx + 1.6f, y + 3.5f, front + 1f), 0.08f, T1);
                }
            }
            // air conditioners and dripping pipes
            for (int k = rng.Range(2, 7); k > 0; k--)
            {
                float ax = rng.Range(-w * 0.42f, w * 0.42f), ay = rng.Range(6f, Mathf.Min(h0 - 1f, 26f));
                L[rng.Chance(0.5f) ? Rust : Metal].Box(new Vector3(ax, ay, front + 0.35f), new Vector3(1f, 0.7f, 0.7f), Quaternion.identity, T1);
            }

            var rot = Quaternion.Euler(0f, yaw, 0f);
            plan.geo.Append(L, rot, basePos);
            foreach (var s in signs)
            {
                var sp = s;
                sp.pos = basePos + rot * s.pos;
                sp.yaw = yaw + s.yaw;
                plan.props.Add(sp);
            }
            plan.colliders.Add(new ColliderSpec { center = basePos + Vector3.up * (h0 * 0.5f), size = new Vector3(w, h0, d), yaw = yaw });
            if (rng.Chance(0.2f) && !ruined) plan.props.Add(new PropSpec { kind = PropKind.Beacon, pos = basePos + Vector3.up * (h0 + 0.8f), scale = 0.8f });
        }

        static Vector3 Local(Vector3 basePos, float yaw, Vector3 local) => basePos + Quaternion.Euler(0f, yaw, 0f) * local;

        // ================================================================ HOTEL: the Grand Halcyon (Kubrick)

        static void Hotel(CityPlan plan, Rng rng)
        {
            plan.arena = ArenaShape.Rect(15f, 10.5f);
            plan.hasFloor = true;
            plan.floorMaterial = CarpetM;
            plan.floorTile = 4f;
            plan.floorSize = 60f;
            plan.spawnStyle = 1;
            plan.playerStart = new Vector3(0f, 0f, -8f);
            plan.wetFloor = true;
            plan.wetStrength = 0.35f;
            const float W = 20f, D = 15.5f, H = 11f;
            var g = plan.geo;

            // marble ballroom floor inset in the carpet
            g[MarbleM].Box(new Vector3(0f, 0.01f, 0f), new Vector3(32f, 0.02f, 23f), Quaternion.identity, new Vector2(3f, 3f), Geo.Faces.PosY);
            g[Gold].Box(new Vector3(0f, 0.012f, 11.6f), new Vector3(32.2f, 0.024f, 0.18f), Quaternion.identity, T1, Geo.Faces.PosY);
            g[Gold].Box(new Vector3(0f, 0.012f, -11.6f), new Vector3(32.2f, 0.024f, 0.18f), Quaternion.identity, T1, Geo.Faces.PosY);
            g[Gold].Box(new Vector3(16.05f, 0.012f, 0f), new Vector3(0.18f, 0.024f, 23.2f), Quaternion.identity, T1, Geo.Faces.PosY);
            g[Gold].Box(new Vector3(-16.05f, 0.012f, 0f), new Vector3(0.18f, 0.024f, 23.2f), Quaternion.identity, T1, Geo.Faces.PosY);

            // walls: wood wainscot, damask above, gilded rail; ceiling with coffers
            foreach (var (c, s, yaw) in new[] { (new Vector3(0f, 0f, D), new Vector2(2 * W, 0f), 180f), (new Vector3(0f, 0f, -D), new Vector2(2 * W, 0f), 0f), (new Vector3(W, 0f, 0f), new Vector2(2 * D, 0f), -90f), (new Vector3(-W, 0f, 0f), new Vector2(2 * D, 0f), 90f) })
            {
                var r = Quaternion.Euler(0f, yaw, 0f);
                g[WoodM].Box(c + Vector3.up * 0.75f, new Vector3(s.x, 1.5f, 0.3f), r, new Vector2(4f, 1.5f), Geo.Faces.All);
                g[WallM].Box(c + Vector3.up * (1.5f + (H - 1.5f) * 0.5f), new Vector3(s.x, H - 1.5f, 0.2f), r, new Vector2(3f, 3f), Geo.Faces.All);
                g[Gold].Box(c + r * new Vector3(0f, 1.52f, 0.18f), new Vector3(s.x, 0.06f, 0.08f), r, T1);
                g[Gold].Box(c + r * new Vector3(0f, H - 0.6f, 0.2f), new Vector3(s.x, 0.25f, 0.2f), r, T1);
                plan.colliders.Add(new ColliderSpec { center = c + Vector3.up * (H * 0.5f), size = new Vector3(s.x, H, 0.6f), yaw = yaw });
            }
            g[Cream].Box(new Vector3(0f, H + 0.2f, 0f), new Vector3(2 * W, 0.4f, 2 * D), Quaternion.identity, T4, Geo.Faces.NegY);
            for (int ix = -4; ix <= 4; ix++)
            for (int iz = -3; iz <= 3; iz++)
            {
                g[WoodM].Box(new Vector3(ix * 4.4f, H - 0.15f, iz * 4.4f), new Vector3(4.4f, 0.3f, 0.3f), Quaternion.identity, T1, Geo.Faces.NegY | Geo.Faces.Sides);
                g[WoodM].Box(new Vector3(ix * 4.4f, H - 0.15f, iz * 4.4f), new Vector3(0.3f, 0.3f, 4.4f), Quaternion.identity, T1, Geo.Faces.NegY | Geo.Faces.Sides);
            }
            plan.colliders.Add(new ColliderSpec { center = new Vector3(0f, H + 0.3f, 0f), size = new Vector3(2 * W, 0.6f, 2 * D) });

            // the colonnade (marble columns with gilded capitals): perfectly symmetrical
            for (int side = -1; side <= 1; side += 2)
            {
                for (int k = -2; k <= 2; k++)
                {
                    Vector3 p = new Vector3(side * 17f, 0f, k * 5f);
                    Column(plan, p, H);
                    plan.anchors.Add(new AnchorSpec { pos = new Vector3(side * 16.4f, 1.2f, k * 5f), normal = new Vector3(-side, 0f, 0f), label = "column" });
                }
                for (int k = -3; k <= 3; k += 2)
                {
                    Vector3 p = new Vector3(k * 4.5f, 0f, side * 12.8f);
                    Column(plan, p, H);
                    plan.anchors.Add(new AnchorSpec { pos = new Vector3(k * 4.5f, 1.2f, side * 12.2f), normal = new Vector3(0f, 0f, -side), label = "column" });
                }
            }

            // far wall: the elevator doors (gold, red light), a landing with steps, the clock stopped at 11:59
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 door = new Vector3(side * 4f, 0f, D - 0.25f);
                g[Gold].Box(door + Vector3.up * 2.4f, new Vector3(3.4f, 4.8f, 0.2f), Quaternion.identity, T1);
                g[Dark].Box(door + Vector3.up * 2.3f + Vector3.back * 0.12f, new Vector3(0.05f, 4.4f, 0.05f), Quaternion.identity, T1);
                g[Glow(NeonRed, 3.5f)].Box(door + Vector3.up * 5.2f + Vector3.back * 0.12f, new Vector3(2.4f, 0.22f, 0.05f), Quaternion.identity, T1);
                plan.lights.Add(new LightSpec { kind = LightKind.Point, pos = door + new Vector3(0f, 5f, -1.5f), color = new Color(1f, 0.12f, 0.08f), intensity = 10f, range = 9f });
                // twin statues flanking the doors (inert Associates in perfect symmetry)
                plan.props.Add(new PropSpec { kind = PropKind.Statue, pos = new Vector3(side * 7.5f, 0.6f, D - 1.4f), yaw = 180f, scale = 1f, seed = 5 });
                g[Gold].Box(new Vector3(side * 7.5f, 0.3f, D - 1.4f), new Vector3(1.4f, 0.6f, 1.4f), Quaternion.identity, T1);
            }
            for (int st = 0; st < 3; st++)
                g[CarpetM].Box(new Vector3(0f, 0.12f + st * 0.24f, D - 3.4f + st * 0.5f), new Vector3(12f - st * 1.2f, 0.24f, 1f), Quaternion.identity, T4);
            plan.props.Add(new PropSpec { kind = PropKind.Clock, pos = new Vector3(0f, 7.6f, D - 0.4f), yaw = 180f, scale = 1.6f });
            plan.props.Add(new PropSpec { kind = PropKind.Text, pos = new Vector3(0f, 9.6f, D - 0.4f), yaw = 180f, scale = 1.2f, text = "THE  MIDNIGHT  GALA", color = new Color(1f, 0.78f, 0.4f) });

            // chandeliers: three in a line, crystal tiers around a warm core
            for (int k = -1; k <= 1; k++)
            {
                Vector3 c = new Vector3(k * 10f, 8.2f, 0f);
                g[Gold].Strut(c + Vector3.up * 0.6f, new Vector3(c.x, H, c.z), 0.06f, T1);
                for (int tier = 0; tier < 3; tier++)
                {
                    float rr = 1.6f - tier * 0.45f;
                    g[Gold].Ring(c + Vector3.up * (tier * 0.45f), rr - 0.06f, rr, 24, 1f);
                    int drops = 18 - tier * 4;
                    for (int dIdx = 0; dIdx < drops; dIdx++)
                    {
                        Vector3 dp = c + Vector3.up * (tier * 0.45f - 0.25f) + Quaternion.Euler(0f, dIdx * 360f / drops, 0f) * Vector3.forward * rr;
                        g[Glow(new Color(1f, 0.9f, 0.75f), 2.2f)].Box(dp, new Vector3(0.06f, 0.4f, 0.06f), Quaternion.Euler(0f, dIdx * 20f, 0f), T1);
                    }
                }
                plan.lights.Add(new LightSpec { kind = LightKind.Point, pos = c + Vector3.down * 0.4f, color = new Color(1f, 0.78f, 0.52f), intensity = k == 0 ? 120f : 80f, range = 22f, shadows = k == 0 });
            }
            // wall sconces between the doors
            for (int side = -1; side <= 1; side += 2)
                for (int k = -2; k <= 2; k++)
                {
                    Vector3 sc = new Vector3(side * (W - 0.3f), 3.4f, k * 5.5f + 2.75f);
                    g[Glow(new Color(1f, 0.75f, 0.45f), 3f)].Box(sc, new Vector3(0.2f, 0.45f, 0.3f), Quaternion.identity, T1);
                    plan.lights.Add(new LightSpec { kind = LightKind.Point, pos = sc + new Vector3(-side * 0.6f, 0f, 0f), color = new Color(1f, 0.7f, 0.42f), intensity = 6f, range = 7f });
                    // doors to the corridors
                    if (k != 0) continue;
                    g[WoodM].Box(new Vector3(side * (W - 0.18f), 1.6f, k * 5.5f), new Vector3(0.1f, 3.2f, 1.8f), Quaternion.identity, T2);
                }

            // dining tables set for a party that never happened, outside the dance floor
            for (int side = -1; side <= 1; side += 2)
                for (int k = -1; k <= 1; k++)
                {
                    Vector3 tp = new Vector3(side * 18.4f, 0f, k * 8f + 2.5f);
                    g[Linen].Cylinder(tp, 0.95f, 0.85f, 0.78f, 18, T1, true);
                    g[Glow(new Color(1f, 0.7f, 0.4f), 2.5f)].Cylinder(tp + Vector3.up * 0.78f, 0.04f, 0.04f, 0.25f, 6, T1, true);
                }
            // the Gala's art commission in the corner: it has been sweeping up its own leak for eleven years
            plan.props.Add(new PropSpec { kind = PropKind.Custodian, pos = new Vector3(-16.8f, 0f, -13.1f), yaw = 0f, scale = 1f, seed = 3 });
            plan.lights.Add(new LightSpec { kind = LightKind.Spot, pos = new Vector3(-16.8f, 6f, -11.8f), color = new Color(1f, 0.82f, 0.62f), intensity = 40f, range = 9f, angle = 40f, dir = new Vector3(0f, -6f, -1.3f).normalized });
            // the bar along the near wall, with its mirror
            g[WoodM].Box(new Vector3(0f, 0.55f, -D + 1.4f), new Vector3(14f, 1.1f, 0.8f), Quaternion.identity, T2);
            g[Gold].Box(new Vector3(0f, 1.12f, -D + 1.4f), new Vector3(14.1f, 0.05f, 0.9f), Quaternion.identity, T1);
            g[Chrome].Box(new Vector3(0f, 3.4f, -D + 0.2f), new Vector3(12f, 2.4f, 0.05f), Quaternion.identity, T1);
            for (int k = -6; k <= 6; k++)
                g[Glow(k % 3 == 0 ? NeonAmber * 0.6f : new Color(0.3f, 0.6f, 0.4f), 1.2f)].Cylinder(new Vector3(k * 0.8f, 1.15f, -D + 1.3f), 0.06f, 0.05f, 0.32f, 6, T1, true);
            plan.anchors.Add(new AnchorSpec { pos = new Vector3(0f, 1f, -D + 1.9f), normal = Vector3.forward, label = "bar" });
            // confetti that was never thrown: a few colourful flecks on the floor
            for (int k = 0; k < 160; k++)
            {
                Vector3 cp = new Vector3(rng.Range(-15f, 15f), 0.025f, rng.Range(-11f, 11f));
                Color cc = rng.Pick(new[] { new Color(0.9f, 0.75f, 0.2f), new Color(0.85f, 0.2f, 0.25f), new Color(0.9f, 0.9f, 0.9f) });
                plan.geoNoShadow[Plain(cc, 0.5f, 0.6f)].Box(cp, new Vector3(0.06f, 0.004f, 0.04f), Quaternion.Euler(0f, rng.Range(0f, 360f), 0f), T1, Geo.Faces.PosY);
            }
            plan.spawnPoints.Clear();
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f + 22.5f;
                plan.spawnPoints.Add(plan.arena.Edge(Quaternion.Euler(0f, a, 0f) * Vector3.forward, 0.8f));
            }
        }

        static void Column(CityPlan plan, Vector3 p, float h)
        {
            var g = plan.geo;
            g[MarbleWhite].Box(p + Vector3.up * 0.3f, new Vector3(1.4f, 0.6f, 1.4f), Quaternion.identity, T2);
            g[MarbleWhite].Cylinder(p + Vector3.up * 0.6f, 0.52f, 0.46f, h - 1.6f, 20, new Vector2(3f, 3f), false);
            g[Gold].Cylinder(p + Vector3.up * (h - 1f), 0.5f, 0.75f, 0.6f, 20, T1, true);
            g[Gold].Box(p + Vector3.up * (h - 0.2f), new Vector3(1.6f, 0.4f, 1.6f), Quaternion.identity, T1);
            plan.colliders.Add(new ColliderSpec { center = p + Vector3.up * (h * 0.5f), size = new Vector3(1.1f, h, 1.1f) });
        }

        // ================================================================ CONSTRUCT: the white loading program (Matrix)

        static void Construct(CityPlan plan, Rng rng)
        {
            plan.arena = ArenaShape.Circle(18f);
            plan.floorMaterial = ConstructM;
            plan.floorTile = 8f;
            plan.floorSize = 900f;
            plan.spawnStyle = 2;
            plan.playerStart = new Vector3(0f, 0f, -6f);
            plan.wetFloor = true;
            plan.wetStrength = 0.4f;
            // a faint ring marks where the simulation's attention ends
            plan.geoNoShadow[Glow(NeonGreen, 1.2f)].Ring(Vector3.up * 0.01f, 18.6f, 18.75f, 128, 1f);
            // the two armchairs and the old television
            plan.props.Add(new PropSpec { kind = PropKind.Chairs, pos = new Vector3(0f, 0f, 24f), yaw = 180f, scale = 1f });
            // digital rain curtains, revealed when the construct glitches
            for (int i = 0; i < 14; i++)
            {
                float a = i / 14f * 360f + rng.Range(-8f, 8f);
                float r = rng.Range(45f, 110f);
                plan.props.Add(new PropSpec { kind = PropKind.Rain, pos = Quaternion.Euler(0f, a, 0f) * Vector3.forward * r, yaw = a + 180f, scale = 1f, extra = new Vector3(rng.Range(20f, 50f), rng.Range(40f, 90f), 0f), seed = rng.Seed() });
            }
            plan.props.Add(new PropSpec { kind = PropKind.Text, pos = new Vector3(0f, 9f, 60f), yaw = 180f, scale = 10f, text = "11:59", color = NeonGreen });
            // the word on the clay, hanging over the simulation's horizon behind you
            plan.props.Add(new PropSpec { kind = PropKind.Text, pos = new Vector3(0f, 14f, -70f), yaw = 0f, scale = 12f, text = "EMET", color = NeonGreen });
            for (int i = 0; i < 8; i++) plan.spawnPoints.Add(Quaternion.Euler(0f, i * 45f + 10f, 0f) * Vector3.forward * rng.Range(9f, 13f));
        }

        // ================================================================ SPIRE: the summit, the Monolith, midnight

        static void Spire(CityPlan plan, Rng rng)
        {
            plan.arena = ArenaShape.Circle(14.5f);
            plan.hasFloor = true;
            plan.floorMaterial = ConcDark;
            plan.floorTile = 4f;
            plan.floorSize = 0f; // the platform itself is the floor
            plan.spawnStyle = 0;
            plan.playerStart = new Vector3(0f, 0f, -8f);
            plan.wetFloor = true;
            plan.wetStrength = 0.9f;
            var g = plan.geo;
            const float R = 17.5f;
            // platform: a disc with a steel rim, and the tower shaft dropping into the clouds
            g[ConcDark].Ring(Vector3.zero, 0f, R, 96, 4f);
            g[Metal].Cylinder(Vector3.down * 0.6f, R + 0.3f, R + 0.3f, 0.75f, 96, T2, false);
            g[Rust].Ring(Vector3.up * 0.15f, R - 0.25f, R + 0.3f, 96, 2f);
            g[FacadeLit].Cylinder(Vector3.down * 160f, R + 6f, R + 0.3f, 159.4f, 48, FacadeTile, false);
            plan.colliders.Add(new ColliderSpec { center = Vector3.down * 0.5f, size = new Vector3(2 * R, 1f, 2 * R), floor = true });
            for (int k = 0; k < 36; k++)
            {
                float a = k * 10f;
                if (rng.Chance(0.2f)) continue; // railing torn away in places
                var d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                var d2 = Quaternion.Euler(0f, a + 10f, 0f) * Vector3.forward;
                g[Rust].Strut(d * (R - 0.1f), d * (R - 0.1f) + Vector3.up * 1.15f, 0.07f, T1);
                g[Rust].Strut(d * (R - 0.1f) + Vector3.up * 1.1f, d2 * (R - 0.1f) + Vector3.up * 1.1f, 0.06f, T1);
            }
            // HALCYON's server racks ringing the platform, LEDs blinking
            for (int k = 0; k < 14; k++)
            {
                float a = k * (360f / 14f) + 12.9f;
                if (a > 330f || a < 30f) continue; // leave the Monolith's side open
                var d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                var rot = Quaternion.LookRotation(-d);
                Vector3 p = d * 16.2f;
                g[Server].Box(p + Vector3.up * 1.3f, new Vector3(1.6f, 2.6f, 1f), rot, T1);
                for (int led = 0; led < 6; led++)
                    plan.geoNoShadow[Glow(led % 3 == 0 ? NeonRed : NeonCyan, 3f)].Box(p + Vector3.up * (0.5f + led * 0.35f) + -d * 0.52f, new Vector3(1.2f, 0.03f, 0.02f), rot, T1);
                plan.anchors.Add(new AnchorSpec { pos = d * 15.5f + Vector3.up * 1.2f, normal = -d, label = "server" });
                plan.colliders.Add(new ColliderSpec { center = p + Vector3.up * 1.3f, size = new Vector3(1.6f, 2.6f, 1f), yaw = a + 180f });
            }
            // lightning rods / antennae
            for (int k = 0; k < 5; k++)
            {
                float a = 60f + k * 60f;
                Vector3 b = Quaternion.Euler(0f, a, 0f) * Vector3.forward * (R - 0.6f);
                float h = rng.Range(9f, 16f);
                g[Metal].Strut(b, b + Vector3.up * h, 0.18f, T1);
                plan.props.Add(new PropSpec { kind = PropKind.Beacon, pos = b + Vector3.up * (h + 0.2f), scale = 0.7f });
            }
            // the Monolith, 1 : 4 : 9, with HALCYON's eye; cables snake from it into the racks
            plan.props.Add(new PropSpec { kind = PropKind.Monolith, pos = new Vector3(0f, 0f, 22f), yaw = 180f, scale = 1f });
            for (int k = 0; k < 7; k++)
            {
                Vector3 from = new Vector3(rng.Range(-4f, 4f), 0.06f, 20f);
                Vector3 to = Quaternion.Euler(0f, rng.Range(40f, 320f), 0f) * Vector3.forward * 16f + Vector3.up * 0.06f;
                g[Rubber].Strut(from, to, 0.12f, T1);
            }
            plan.props.Add(new PropSpec { kind = PropKind.Text, pos = new Vector3(0f, 34f, 21f), yaw = 180f, scale = 9f, text = "11:59", color = NeonRed });
            // the city far below, and the clouds
            Skyline(plan, rng, -720f, 90f, 420f, 3, 0.8f, 0.5f, null, -150f);
            AirTraffic(plan, rng, 7, -140f, -20f, 60f, 220f);
            for (int k = 0; k < 40; k++)
            {
                Vector3 cp = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f) * Vector3.forward * rng.Range(30f, 240f);
                cp.y = rng.Range(-70f, -40f);
                plan.emitters.Add(new EmitterSpec { kind = EmitterKind.Smoke, pos = cp, size = rng.Range(25f, 45f), color = new Color(0.32f, 0.3f, 0.38f, 0.35f) });
            }
            plan.spawnPoints.Clear();
            for (int i = 0; i < 8; i++) plan.spawnPoints.Add(Quaternion.Euler(0f, i * 45f + 22.5f, 0f) * Vector3.forward * 11f);
        }
    }
}
