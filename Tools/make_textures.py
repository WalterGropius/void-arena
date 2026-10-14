"""Builds Assets/HackAndSlash/Textures from Poly Haven CC0 maps plus procedural patterns.

Usage: python3 make_textures.py <dir with downloaded Poly Haven jpgs> <output dir>
Inputs (Poly Haven, CC0): concrete_pavement 2k (diff, nor_gl, rough, ao, disp), concrete_wall_008 / metal_plate_02 / metal_plate 1k (diff, nor_gl, arm).
Outputs: wet roof maps (puddles pool in the slab joints), URP metallic-smoothness packs, window-lit facade,
street grid, lens dirt, spider-sense glyphs, rain ripple, armour panel normals and the helipad decal.
"""
import numpy as np, os, sys, math
from PIL import Image, ImageFilter, ImageDraw
RAW = sys.argv[1]; OUT = sys.argv[2]
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(1337)

def load(name, size=None, mode='RGB'):
    im = Image.open(os.path.join(RAW, name)).convert(mode)
    if size and im.size != (size, size): im = im.resize((size, size), Image.LANCZOS)
    return np.asarray(im).astype(np.float32) / 255.0

def save(arr, name, quality=90):
    arr = np.clip(arr, 0, 1)
    a = (arr * 255 + 0.5).astype(np.uint8)
    if a.ndim == 2: im = Image.fromarray(a, 'L')
    elif a.shape[2] == 4: im = Image.fromarray(a, 'RGBA')
    else: im = Image.fromarray(a, 'RGB')
    p = os.path.join(OUT, name)
    if name.endswith('.jpg'): im.save(p, quality=quality, optimize=True)
    else: im.save(p, optimize=True)
    print(name, im.size, os.path.getsize(p)//1024, 'KB')

def tile_noise(n, scale, seed, octaves=4):
    """Tileable fBm via FFT-filtered white noise (periodic by construction)."""
    r = np.random.default_rng(seed)
    out = np.zeros((n, n), np.float32); amp = 1.0; tot = 0
    fy = np.fft.fftfreq(n)[:, None]; fx = np.fft.fftfreq(n)[None, :]
    f = np.sqrt(fx * fx + fy * fy)
    for o in range(octaves):
        w = r.standard_normal((n, n))
        sigma = scale / (2 ** o) / n
        filt = np.exp(-(f ** 2) / (2 * sigma ** 2))
        layer = np.real(np.fft.ifft2(np.fft.fft2(w) * filt))
        layer = (layer - layer.mean()) / (layer.std() + 1e-6)
        out += layer * amp; tot += amp; amp *= 0.5
    out /= tot
    return (out - out.min()) / (out.max() - out.min())

def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)

def blur(a, r):
    im = Image.fromarray((np.clip(a, 0, 1) * 65535).astype(np.uint16).astype(np.int32), 'I')
    # PIL blur on 'I' is unsupported for some filters; do separable box blur with numpy (wraps = tileable)
    k = int(r)
    out = a.copy()
    for axis in (0, 1):
        acc = np.zeros_like(out)
        for d in range(-k, k + 1): acc += np.roll(out, d, axis=axis)
        out = acc / (2 * k + 1)
    return out

def renorm(n):
    v = n * 2 - 1
    v /= np.linalg.norm(v, axis=2, keepdims=True) + 1e-6
    return v * 0.5 + 0.5

# ------------------------------------------------------------------ wet rooftop pavement (2k albedo/normal, 1k masks)
N = 2048
alb = load('concrete_pavement_diff_2k.jpg', N)
nrm = load('concrete_pavement_nor_gl_2k.jpg', N)
rough = load('concrete_pavement_rough_2k.jpg', N, 'L')
disp = load('concrete_pavement_disp_2k.jpg', N, 'L')
ao = load('concrete_pavement_ao_2k.jpg', N, 'L')
low = tile_noise(N, 5.0, 7, 3)          # large-scale dips in the slab
mid = tile_noise(N, 14.0, 11, 3)
depth = (1 - disp) * 0.55 + low * 0.75 + mid * 0.15   # water pools in joints and dips
thr = np.quantile(depth, 0.77)
puddle = smoothstep(thr - 0.035, thr + 0.035, depth)
puddle = blur(puddle, 2)
damp = np.clip(0.55 + 0.45 * smoothstep(thr - 0.2, thr, depth), 0, 1)
wet_alb = alb * (1 - 0.32 * damp[..., None]) * (1 - 0.38 * puddle[..., None])
grey = wet_alb.mean(axis=2, keepdims=True)
wet_alb = wet_alb * (1 - 0.25 * puddle[..., None]) + grey * 0.25 * puddle[..., None]
smooth = 1 - rough
smooth = smooth * (1 - damp) + (0.62 + 0.25 * smooth) * damp
smooth = smooth * (1 - puddle) + 0.97 * puddle
flat = np.array([0.5, 0.5, 1.0], np.float32)
wet_n = renorm(nrm * (1 - puddle[..., None] * 0.92) + flat * puddle[..., None] * 0.92)
save(wet_alb, 'T_Roof_Albedo.jpg', 88)
save(wet_n, 'T_Roof_Normal.jpg', 92)
half = lambda a: np.asarray(Image.fromarray((np.clip(a,0,1)*255).astype(np.uint8)).resize((1024,1024), Image.LANCZOS)).astype(np.float32)/255
ms = np.zeros((1024, 1024, 4), np.float32); ms[..., 3] = half(smooth)
save(ms, 'T_Roof_MetalSmooth.png')
save(half(ao * 0.85 + 0.15), 'T_Roof_AO.jpg', 88)
save(half(puddle), 'T_Roof_Puddles.png')

# ------------------------------------------------------------------ 1k sets from "arm" (R=AO, G=rough, B=metal)
def pbr_set(src, out, wet=0.0, metal_scale=1.0, tint=None):
    a = load(src + '_diff_1k.jpg', 1024); n = load(src + '_nor_gl_1k.jpg', 1024); arm = load(src + '_arm_1k.jpg', 1024)
    if tint is not None: a = a * np.array(tint, np.float32)
    s = 1 - arm[..., 1]
    s = s * (1 - wet) + (0.55 + 0.35 * s) * wet
    a = a * (1 - 0.25 * wet)
    ms = np.zeros((1024, 1024, 4), np.float32); ms[..., 0] = arm[..., 2] * metal_scale; ms[..., 3] = s
    save(a, out + '_Albedo.jpg', 90); save(n, out + '_Normal.jpg', 92); save(ms, out + '_MetalSmooth.png'); save(arm[..., 0] * 0.8 + 0.2, out + '_AO.jpg', 88)
pbr_set('concrete_wall_008', 'T_Concrete', wet=0.55)
pbr_set('metal_plate_02', 'T_MetalPanel', wet=0.35)
pbr_set('metal_plate', 'T_TreadPlate', wet=0.45)

# ------------------------------------------------------------------ skyscraper facade (tile = 4 columns x 4 floors)
F = 1024; cols, rows = 4, 4; cw, rh = F // cols, F // rows
fa = np.zeros((F, F, 3), np.float32); fe = np.zeros((F, F, 3), np.float32); fs = np.zeros((F, F, 4), np.float32)
grain = tile_noise(F, 60, 3, 3)
fa[:] = (0.035 + 0.02 * grain)[..., None] * np.array([0.8, 0.9, 1.1])
fs[..., 3] = 0.35 + 0.1 * grain; fs[..., 0] = 0.2
palette = [((1.0, 0.72, 0.42), 0.40), ((0.85, 0.92, 1.0), 0.22), ((0.55, 0.75, 1.0), 0.08), ((1.0, 0.55, 0.85), 0.04), ((0.0, 0.0, 0.0), 0.26)]
cols_p = [p for p, _ in palette]; w_p = np.array([w for _, w in palette]); w_p /= w_p.sum()
yy, xx = np.mgrid[0:F, 0:F]
for r in range(rows):
    for c in range(cols):
        x0, y0 = c * cw, r * rh
        mx, my = int(cw * 0.08), int(rh * 0.14)
        # glass pane area (dark reflective glass)
        sl = (slice(y0 + my, y0 + rh - my), slice(x0 + mx, x0 + cw - mx))
        fa[sl] = np.array([0.012, 0.016, 0.022]); fs[sl + (slice(3, 4),)] = 0.93; fs[sl + (slice(0, 1),)] = 0.0
        # split each bay into 2 windows, each lit independently
        for k in range(2):
            wx0 = x0 + mx + k * (cw - 2 * mx) // 2 + 3; wx1 = x0 + mx + (k + 1) * (cw - 2 * mx) // 2 - 3
            ci = rng.choice(len(cols_p), p=w_p); col = np.array(cols_p[ci], np.float32)
            if ci == len(cols_p) - 1: continue
            inten = rng.uniform(0.35, 1.0)
            ws = (slice(y0 + my + 2, y0 + rh - my - 2), slice(wx0, wx1))
            h = ws[0].stop - ws[0].start; w = ws[1].stop - ws[1].start
            v = np.linspace(0, 1, h)[:, None]; u = np.linspace(0, 1, w)[None, :]
            glow = (0.55 + 0.45 * np.sin(np.pi * v) * np.sin(np.pi * u))
            if rng.random() < 0.5:  # blinds
                glow *= 0.75 + 0.25 * (np.sin(v * h * 0.9) > 0)
            if rng.random() < 0.3:  # silhouette of furniture / person
                glow *= 1 - 0.7 * ((u > rng.uniform(0.2, 0.7)) & (u < rng.uniform(0.75, 0.95)) & (v > 0.55))
            fe[ws] = col * inten * glow[..., None]
save(fa, 'T_Facade_Albedo.jpg', 90); save(fe, 'T_Facade_Emission.jpg', 92); save(fs, 'T_Facade_MetalSmooth.png')

# ------------------------------------------------------------------ street grid seen from above (far city floor)
G = 1024
ge = np.zeros((G, G, 3), np.float32)
sodium = np.array([1.0, 0.55, 0.18]); white = np.array([0.9, 0.95, 1.0]); red = np.array([1.0, 0.1, 0.05])
for i in range(0, G, 128):
    w = 6 if i % 256 == 0 else 3
    ge[:, i:i + w] += sodium * 0.8; ge[i:i + w, :] += sodium * 0.8
dots = rng.integers(0, G, (900, 2))
for (x, y) in dots:
    lane = rng.integers(0, 2)
    if lane: x = (x // 128) * 128 + rng.integers(0, 6)
    else: y = (y // 128) * 128 + rng.integers(0, 6)
    ge[y % G, x % G] += (white if rng.random() < 0.6 else red) * 3
blocks = tile_noise(G, 40, 21, 2)
win = (rng.random((G, G)) < 0.012 * blocks * 2)
ge[win] += np.array([1.0, 0.8, 0.55]) * 0.9
ge = np.stack([blur(ge[..., c], 1) for c in range(3)], -1) * 1.6
save(ge / max(1.0, ge.max()) , 'T_CityGrid_Emission.jpg', 90)

# ------------------------------------------------------------------ lens dirt
L = 1024
ld = np.zeros((L, L), np.float32)
for _ in range(220):
    cx, cy = rng.uniform(0, L, 2); r = rng.uniform(4, 60) * (1 if rng.random() < 0.8 else 2.2)
    d = np.sqrt((xx[:L, :L] - cx) ** 2 + (yy[:L, :L] - cy) ** 2)
    ring = smoothstep(r, r * 0.82, d) * rng.uniform(0.05, 0.35)
    ld = np.maximum(ld, ring * (0.7 + 0.3 * smoothstep(r * 0.5, r, d)))
smudge = tile_noise(L, 6, 5, 4)
ld = np.clip(ld + smoothstep(0.55, 0.9, smudge) * 0.18, 0, 1)
save(np.stack([ld * 0.95, ld, ld * 1.0], -1), 'T_LensDirt.jpg', 85)

# ------------------------------------------------------------------ Arkham-style spider-sense bolts (white, tinted in material)
S2 = 512
img = Image.new('L', (S2, S2), 0); dr = ImageDraw.Draw(img)
def bolt(x0, y0, ang, length, width):
    pts = [(x0, y0)]; x, y = x0, y0; seg = 5
    for i in range(seg):
        a = ang + (0.55 if i % 2 == 0 else -0.55)
        step = length / seg
        x += math.cos(a) * step; y -= math.sin(a) * step; pts.append((x, y))
    for i in range(len(pts) - 1):
        wdt = int(width * (1 - i / len(pts)) + 2)
        dr.line([pts[i], pts[i + 1]], fill=255, width=wdt)
        dr.ellipse([pts[i + 1][0] - wdt / 2, pts[i + 1][1] - wdt / 2, pts[i + 1][0] + wdt / 2, pts[i + 1][1] + wdt / 2], fill=255)
bolt(S2 * 0.5, S2 * 0.92, math.radians(90), S2 * 0.8, 26)
bolt(S2 * 0.40, S2 * 0.90, math.radians(125), S2 * 0.55, 18)
bolt(S2 * 0.60, S2 * 0.90, math.radians(55), S2 * 0.55, 18)
core = np.asarray(img).astype(np.float32) / 255
glowA = np.asarray(img.filter(ImageFilter.GaussianBlur(14))).astype(np.float32) / 255
alpha = np.clip(core + glowA * 1.6, 0, 1)
rgb = np.clip(0.55 + 0.45 * core, 0, 1)
save(np.dstack([rgb, rgb, rgb, alpha]), 'T_SpiderSense.png')

# ------------------------------------------------------------------ rain ripple ring
R = 128
d = np.sqrt((np.mgrid[0:R, 0:R][1] - R / 2 + 0.5) ** 2 + (np.mgrid[0:R, 0:R][0] - R / 2 + 0.5) ** 2) / (R / 2)
ring = np.exp(-((d - 0.82) / 0.07) ** 2) + 0.35 * np.exp(-((d - 0.55) / 0.06) ** 2)
ring = np.clip(ring, 0, 1) * (d < 1)
save(np.dstack([np.ones_like(ring), np.ones_like(ring), np.ones_like(ring), ring]), 'T_Ripple.png')

# ------------------------------------------------------------------ armor panel detail (tileable, 512): seams + bolts + smudges
A = 512
h = np.zeros((A, A), np.float32)
def seam_rect(x0, y0, x1, y1, w=2.0):
    global h
    X, Y = np.meshgrid(np.arange(A), np.arange(A))
    for (a, b, horiz) in ((y0, (x0, x1), True), (y1, (x0, x1), True), (x0, (y0, y1), False), (x1, (y0, y1), False)):
        if horiz:
            m = (np.abs(Y - a) < w) & (X >= b[0]) & (X <= b[1])
        else:
            m = (np.abs(X - a) < w) & (Y >= b[0]) & (Y <= b[1])
        h[m] = -1.0
cells = [(0, 0, 255, 160), (256, 0, 511, 300), (0, 161, 255, 511), (256, 301, 511, 511), (60, 220, 200, 380), (300, 60, 470, 200)]
for c in cells: seam_rect(*c, w=1.6)
X, Y = np.meshgrid(np.arange(A), np.arange(A))
for (bx, by) in [(20, 20), (235, 20), (20, 140), (235, 140), (276, 20), (491, 20), (276, 280), (491, 280), (276, 321), (491, 321), (276, 491), (491, 491), (20, 181), (235, 181), (20, 491), (235, 491)]:
    r2 = (X - bx) ** 2 + (Y - by) ** 2
    h += np.clip(1 - r2 / 30.0, 0, 1) * 0.8
for (vx, vy) in [(330, 380)]:
    for k in range(6):
        m = (np.abs(Y - (vy + k * 14)) < 3) & (X > vx) & (X < vx + 110); h[m] = -0.7
h = blur(h, 1)
gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 2.2; gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 2.2
nn = np.dstack([-gx, gy, np.ones_like(h)]); nn /= np.linalg.norm(nn, axis=2, keepdims=True)
save(nn * 0.5 + 0.5, 'T_ArmorPanel_Normal.png')
sm = tile_noise(A, 10, 31, 4)
mask = np.zeros((A, A, 4), np.float32); mask[..., 3] = 0.82 + 0.16 * smoothstep(0.2, 0.8, sm) - 0.25 * (h < -0.5)
mask[..., 0] = 0.0
save(mask, 'T_ArmorPanel_MetalSmooth.png')

# ------------------------------------------------------------------ helipad decal (RGBA, worn paint)
D = 1024
c = (np.mgrid[0:D, 0:D].astype(np.float32) - D / 2 + 0.5) / (D / 2)
rr = np.sqrt(c[0] ** 2 + c[1] ** 2)
paint = np.zeros((D, D), np.float32); color = np.zeros((D, D, 3), np.float32)
ring1 = (rr > 0.90) & (rr < 0.965); ring2 = (rr > 0.52) & (rr < 0.555)
ang = np.arctan2(c[0], c[1]); dash = (np.sin(ang * 24) > 0)
ring2 = ring2 & dash
yel = np.array([0.95, 0.72, 0.08]); wht = np.array([0.92, 0.92, 0.9])
paint[ring1] = 1; color[ring1] = yel
paint[ring2] = 1; color[ring2] = wht
# chevrons on the outer rim, pointing inwards
for k in range(8):
    a0 = k * np.pi / 4 + np.pi / 8
    rel = np.mod(ang - a0 + np.pi, 2 * np.pi) - np.pi
    chev = (rr > 0.72) & (rr < 0.84) & (np.abs(np.abs(rel) * 6 - (rr - 0.72) * 4.5) < 0.12)
    paint[chev] = 1; color[chev] = yel
# central void sigil: hexagon + slash
hexd = np.maximum.reduce([np.abs(c[0] * np.cos(t) + c[1] * np.sin(t)) for t in np.arange(6) * np.pi / 3])
hx = (hexd > 0.24) & (hexd < 0.27)
paint[hx] = 1; color[hx] = wht
grit = tile_noise(D, 30, 41, 4)
wear = smoothstep(0.32, 0.55, grit)
alpha = paint * (0.25 + 0.75 * wear)
save(np.dstack([color, alpha]), 'T_Helipad.png')
print('done')
