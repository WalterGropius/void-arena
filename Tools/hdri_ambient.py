"""Decodes a Radiance .hdr and writes HDRI_AmbientSamples.txt for ArenaEnvironment (image-based ambient light).

Usage: python3 hdri_ambient.py rooftop_night_2k.hdr preview.jpg HDRI_AmbientSamples.txt
Each line is "dx dy dz r g b": a lat-long cell direction (Unity Skybox/Panoramic convention) and its radiance
weighted by solid angle / pi, so summing them as directional lights reproduces the HDRI irradiance.
"""
import numpy as np, sys, math
from PIL import Image
def read_hdr(path):
    data = open(path, 'rb').read()
    i = data.index(b'\n\n') + 2
    line_end = data.index(b'\n', i); res = data[i:line_end].split(); i = line_end + 1
    H, W = int(res[1]), int(res[3])
    img = np.zeros((H, W, 4), np.uint8)
    p = i
    for y in range(H):
        assert data[p] == 2 and data[p+1] == 2, 'only RLE supported'
        p += 4
        for c in range(4):
            x = 0
            while x < W:
                n = data[p]; p += 1
                if n > 128:
                    n -= 128; img[y, x:x+n, c] = data[p]; p += 1
                else:
                    img[y, x:x+n, c] = np.frombuffer(data[p:p+n], np.uint8); p += n
                x += n
    e = img[..., 3].astype(np.int32)
    f = np.where(e > 0, np.ldexp(1.0, e - 136), 0.0)
    return img[..., :3].astype(np.float32) * f[..., None]
hdr = read_hdr(sys.argv[1]); H, W, _ = hdr.shape
print('size', W, H, 'mean', hdr.mean(axis=(0, 1)), 'max', hdr.max())
# preview
tm = hdr / (1 + hdr); Image.fromarray((np.clip(tm ** (1/2.2), 0, 1) * 255).astype(np.uint8)).resize((1024, 512)).save(sys.argv[2])
# directional samples on a lat-long grid (Unity Skybox/Panoramic convention)
small = np.asarray(Image.fromarray(np.zeros((1,1),np.uint8)))  # noop
rows, cols = 24, 48
out = []
for r in range(rows):
    v0, v1 = 1 - r / rows, 1 - (r + 1) / rows
    th0, th1 = (1 - v0) * math.pi, (1 - v1) * math.pi
    for c in range(cols):
        u0, u1 = c / cols, (c + 1) / cols
        y0, y1 = int(r * H / rows), int((r + 1) * H / rows); x0, x1 = int(u0 * W), int(u1 * W)
        L = hdr[y0:y1, x0:x1].reshape(-1, 3).mean(axis=0)
        th = (th0 + th1) / 2; u = (u0 + u1) / 2; lon = (0.5 - u) * 2 * math.pi
        d = (math.sin(th) * math.cos(lon), math.cos(th), math.sin(th) * math.sin(lon))
        dOmega = (math.cos(th0) - math.cos(th1)) * (2 * math.pi / cols)
        w = L * dOmega / math.pi
        out.append('%.5f %.5f %.5f %.6f %.6f %.6f' % (d[0], d[1], d[2], w[0], w[1], w[2]))
open(sys.argv[3], 'w').write('\n'.join(out) + '\n')
up = np.array([0, 1, 0]); irr = np.zeros(3)
for l in out:
    a = list(map(float, l.split())); irr += np.array(a[3:]) * max(0, a[1])
print('irradiance/pi from up', irr)
dn = np.zeros(3)
for l in out:
    a = list(map(float, l.split())); dn += np.array(a[3:]) * max(0, -a[1])
print('from down', dn)
