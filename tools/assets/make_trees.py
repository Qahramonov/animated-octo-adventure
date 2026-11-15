#!/usr/bin/env python3
"""Builds mobile trees from CC0 Poly Haven foliage atlases: a texture atlas (bark strip + branch sprites with
alpha) and card-based tree meshes (trunk + textured branch quads) in two LODs.

  make_trees.py <raw dir (assets-raw branch)> <.../Resources/PH> [preview.png]

The scanned trees themselves have millions of triangles; only their bark / twig textures are used.
Needs: pip install pillow numpy
"""
import math, os, random, sys
import numpy as np
from PIL import Image

BARK_W = 128  # atlas columns 0..127 hold the bark strip
SPRITE_CELLS = [(128, 0, 448, 256), (128, 256, 448, 256), (128, 512, 448, 256), (128, 768, 448, 256)]
IMPOSTOR_CELL = (576, 0, 448, 1024)


def load_alpha(path):
    im = Image.open(path)
    if im.mode == "I;16":
        return Image.fromarray((np.array(im) / 257).astype("uint8"))
    return im.convert("L")


def sprite(diff, alpha, box, flip=False, rot=0):
    rgb = diff.crop(box)
    a = alpha.crop(box)
    im = Image.merge("RGBA", (*rgb.split()[:3], a))
    if flip:
        im = im.transpose(Image.FLIP_LEFT_RIGHT)
    if rot:
        im = im.rotate(rot, expand=True)
    return im


def build_atlas(bark, sprites, cells, size=1024):
    """cells: list of (x, y, w, h) in pixels. Returns the atlas and UV rects (u0, v0, u1, v1) with v up."""
    atlas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    atlas.paste(bark.resize((BARK_W, size), Image.LANCZOS).convert("RGBA"), (0, 0))
    rects = []
    for im, (x, y, w, h) in zip(sprites, cells):
        s = min((w - 8) / im.width, (h - 8) / im.height)
        im2 = im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS)
        ox, oy = x + (w - im2.width) // 2, y + (h - im2.height) // 2
        atlas.paste(im2, (ox, oy))
        rects.append((ox / size, 1 - (oy + im2.height) / size, (ox + im2.width) / size, 1 - oy / size))
    # Bleed colour into fully transparent pixels so mip maps do not get dark fringes
    arr = np.array(atlas).astype(np.float32)
    rgb, a = arr[..., :3], arr[..., 3]
    solid = a > 8
    mean = rgb[solid].mean(axis=0) if solid.any() else np.array([60, 80, 40])
    rgb[~solid] = mean
    arr[..., :3] = rgb
    return Image.fromarray(arr.astype("uint8"), "RGBA"), rects


class Mesh:
    def __init__(self):
        self.v, self.vt, self.vn, self.f = [], [], [], []

    def vert(self, p, uv, n):
        self.v.append(tuple(p)); self.vt.append(tuple(uv)); self.vn.append(tuple(n))
        return len(self.v)

    def quad(self, p0, p1, p2, p3, rect, n):
        """p0,p1 = base (left,right), p3,p2 = tip (left,right). The sprite's bottom edge sits on the base."""
        u0, v0, u1, v1 = rect
        a = self.vert(p0, (u0, v0), n); b = self.vert(p1, (u1, v0), n)
        c = self.vert(p2, (u1, v1), n); d = self.vert(p3, (u0, v1), n)
        self.f += [(a, b, c), (a, c, d)]

    def tube(self, a, b, ra, rb, sides, v_a, v_b):
        """Cylinder segment a->b with bark UVs (u across the bark strip, v mirrored-repeat friendly)."""
        a, b = np.array(a, float), np.array(b, float)
        axis = b - a
        axis /= np.linalg.norm(axis)
        ref = np.array([0, 1, 0]) if abs(axis[1]) < 0.9 else np.array([1, 0, 0])
        x = np.cross(axis, ref); x /= np.linalg.norm(x)
        y = np.cross(axis, x)
        ring_a, ring_b = [], []
        for i in range(sides + 1):
            ang = 2 * math.pi * i / sides
            d = x * math.cos(ang) + y * math.sin(ang)
            u = (4 + (BARK_W - 8) * (i / sides)) / 1024.0
            ring_a.append(self.vert(a + d * ra, (u, v_a), d))
            ring_b.append(self.vert(b + d * rb, (u, v_b), d))
        for i in range(sides):
            self.f += [(ring_a[i], ring_b[i], ring_b[i + 1]), (ring_a[i], ring_b[i + 1], ring_a[i + 1])]

    def card(self, base, direction, length, width, roll, rect, normal):
        d = np.array(direction, float); d /= np.linalg.norm(d)
        up = np.array([0, 1, 0.0])
        side = np.cross(d, up)
        if np.linalg.norm(side) < 1e-3:
            side = np.array([1, 0, 0.0])
        side /= np.linalg.norm(side)
        lift = np.cross(side, d)
        s = side * math.cos(roll) + lift * math.sin(roll)
        base = np.array(base, float)
        n = np.array(normal, float); n /= np.linalg.norm(n)
        self.quad(base - s * width / 2, base + s * width / 2, base + d * length + s * width / 2, base + d * length - s * width / 2, rect, n)

    def tris(self):
        return len(self.f)

    def write(self, path):
        with open(path, "w") as o:
            o.write("# OROL tree built by tools/assets/make_trees.py (textures: CC0 polyhaven.com)\n")
            for p in self.v: o.write(f"v {p[0]:.4f} {p[1]:.4f} {p[2]:.4f}\n")
            for t in self.vt: o.write(f"vt {t[0]:.5f} {t[1]:.5f}\n")
            for n in self.vn: o.write(f"vn {n[0]:.3f} {n[1]:.3f} {n[2]:.3f}\n")
            for a, b, c in self.f: o.write(f"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}\n")


def dir_from(yaw, pitch):
    return np.array([math.sin(yaw) * math.cos(pitch), math.sin(pitch), math.cos(yaw) * math.cos(pitch)])


def fir(rects, lod, seed=7):
    rnd = random.Random(seed)
    m = Mesh()
    height = 9.2
    sides = 7
    rings = [(0, 0.30), (2.5, 0.22), (5.5, 0.13), (height, 0.03)]
    for i in range(len(rings) - 1):
        (h0, r0), (h1, r1) = rings[i], rings[i + 1]
        # mirrored repeat along the trunk: v runs 0->1 on even segments, 1->0 on odd ones
        m.tube((0, h0, 0), (0, h1, 0), r0, r1, sides, i % 2, (i + 1) % 2)
    whorls = 12
    per = 5
    h_start, h_end = 1.3, height - 0.9
    for w in range(whorls):
        t = w / (whorls - 1)
        h = h_start + (h_end - h_start) * t
        length = 3.5 * (1 - t) ** 0.8 + 0.6
        pitch = math.radians(-20 + 36 * t)
        for k in range(per):
            yaw = 2 * math.pi * (k / per) + w * 2.399 + rnd.uniform(-0.15, 0.15)
            d = dir_from(yaw, pitch + math.radians(rnd.uniform(-5, 5)))
            n = d * 0.55 + np.array([0, 0.85, 0])
            L = length * rnd.uniform(0.85, 1.1)
            roll = math.radians(rnd.choice([-1, 1]) * rnd.uniform(10, 28))
            # two crossed cards per branch so it has volume from every side
            m.card((0, h, 0), d, L, L * 0.95, roll, rects[rnd.randrange(len(rects))], n)
            m.card((0, h, 0), d, L * 0.92, L * 0.7, roll + math.radians(80), rects[rnd.randrange(len(rects))], n)
            yaw2 = yaw + math.pi / per
            d2 = dir_from(yaw2, pitch + math.radians(26))
            m.card((0, h + 0.3, 0), d2, L * 0.7, L * 0.66, math.radians(rnd.uniform(-35, 35)), rects[rnd.randrange(len(rects))], d2 * 0.4 + np.array([0, 0.9, 0]))
    # crown tip: crossed upright cards
    for k in range(3):
        yaw = math.pi * k / 3
        side = np.array([math.cos(yaw), 0, -math.sin(yaw)])
        base = np.array([0, height - 1.5, 0.0])
        m.quad(base - side * 0.75, base + side * 0.75, base + side * 0.75 + np.array([0, 2.0, 0]), base - side * 0.75 + np.array([0, 2.0, 0]), rects[0],
               np.array([math.sin(yaw) * 0.5, 0.86, math.cos(yaw) * 0.5]))
    return m


def broadleaf(rects, lod, seed=11):
    rnd = random.Random(seed)
    m = Mesh()
    sides = 7
    m.tube((0, 0, 0), (0.05, 1.7, 0.0), 0.38, 0.27, sides, 0, 1)
    m.tube((0.05, 1.7, 0), (0.0, 3.3, 0.05), 0.27, 0.2, sides, 1, 0)
    crown = np.array([0.0, 5.0, 0.0])
    radii = np.array([2.7, 1.9, 2.7])
    limbs = 5
    for k in range(limbs):
        yaw = 2 * math.pi * k / limbs + 0.4
        tip = crown + np.array([math.sin(yaw) * 1.3, rnd.uniform(-0.4, 0.5), math.cos(yaw) * 1.3])
        m.tube((0, 3.2, 0.05), tip, 0.15, 0.04, 4, 0, 1)
    count = 96
    for i in range(count):
        # points on the crown shell (Fibonacci sphere), fronds grow outwards and droop a little
        z = 1 - 2 * (i + 0.5) / count
        r = math.sqrt(max(0, 1 - z * z))
        phi = i * 2.399963
        out = np.array([r * math.cos(phi), z * 0.9 + 0.25, r * math.sin(phi)])
        out /= np.linalg.norm(out)
        base = crown + out * radii * rnd.uniform(0.42, 0.62)
        d = out + np.array([rnd.uniform(-0.35, 0.35), rnd.uniform(-0.45, 0.1), rnd.uniform(-0.35, 0.35)])
        L = rnd.uniform(1.5, 2.1)
        m.card(base, d, L, L * 0.72, rnd.uniform(-1.2, 1.2), rects[rnd.randrange(len(rects))], out * 0.8 + np.array([0, 0.5, 0]))
    return m


def bush(rects, lod, seed=5):
    rnd = random.Random(seed)
    m = Mesh()
    count = 16 if lod == 0 else 7
    for i in range(count):
        yaw = 2 * math.pi * i / count + rnd.uniform(-0.3, 0.3)
        pitch = math.radians(rnd.uniform(18, 72))
        d = dir_from(yaw, pitch)
        L = rnd.uniform(0.85, 1.25) * (1.0 if lod == 0 else 1.25)
        base = np.array([math.sin(yaw) * 0.12, 0.02, math.cos(yaw) * 0.12])
        m.card(base, d, L, L * 0.62, rnd.uniform(-0.9, 0.9), rects[rnd.randrange(len(rects))], d * 0.5 + np.array([0, 0.8, 0]))
    return m


def rasterize(mesh, atlas, width, height, half_w, top, yaw_deg=25.0):
    """Tiny software renderer (orthographic side view, z-buffer, alpha test). Returns float RGBA (h, w, 4)."""
    out = np.zeros((height, width, 4), np.float32)
    light = np.array([0.4, 0.8, 0.45]); light /= np.linalg.norm(light)
    tex = np.array(atlas).astype(np.float32) / 255.0
    th, tw = tex.shape[:2]
    v = np.array(mesh.v); vt = np.array(mesh.vt); vn = np.array(mesh.vn)
    yaw = math.radians(yaw_deg)
    rot = np.array([[math.cos(yaw), 0, math.sin(yaw)], [0, 1, 0], [-math.sin(yaw), 0, math.cos(yaw)]])
    p = v @ rot.T
    sx = (p[:, 0] / half_w * 0.5 + 0.5) * width
    sy = (1 - p[:, 1] / top) * height
    depth = np.full((height, width), 1e9)
    for a, b, cidx in mesh.f:
        ia, ib, ic = a - 1, b - 1, cidx - 1
        xs = np.array([sx[ia], sx[ib], sx[ic]]); ys = np.array([sy[ia], sy[ib], sy[ic]])
        x0, x1 = int(max(0, xs.min())), int(min(width - 1, xs.max()) + 1)
        y0, y1 = int(max(0, ys.min())), int(min(height - 1, ys.max()) + 1)
        if x1 <= x0 or y1 <= y0: continue
        gx, gy = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        den = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
        if abs(den) < 1e-9: continue
        w0 = ((ys[1] - ys[2]) * (gx - xs[2]) + (xs[2] - xs[1]) * (gy - ys[2])) / den
        w1 = ((ys[2] - ys[0]) * (gx - xs[2]) + (xs[0] - xs[2]) * (gy - ys[2])) / den
        w2 = 1 - w0 - w1
        inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
        if not inside.any(): continue
        z = w0 * p[ia, 2] + w1 * p[ib, 2] + w2 * p[ic, 2]
        u = w0 * vt[ia, 0] + w1 * vt[ib, 0] + w2 * vt[ic, 0]
        vv = w0 * vt[ia, 1] + w1 * vt[ib, 1] + w2 * vt[ic, 1]
        tx = np.clip((u * tw).astype(int), 0, tw - 1); ty = np.clip(((1 - vv) * th).astype(int), 0, th - 1)
        texel = tex[ty, tx]
        n = w0[..., None] * vn[ia] + w1[..., None] * vn[ib] + w2[..., None] * vn[ic]
        n /= np.maximum(1e-6, np.linalg.norm(n, axis=-1, keepdims=True))
        shade = 0.62 + 0.45 * np.clip((n @ light + 0.3) / 1.3, 0, 1)
        sub = depth[y0:y1, x0:x1]
        ok = inside & (texel[..., 3] > 0.4) & (z < sub)
        sub[ok] = z[ok]
        region = out[y0:y1, x0:x1]
        region[ok, :3] = (texel[..., :3] * shade[..., None])[ok]
        region[ok, 3] = 1.0
    return out


def add_impostor(mesh, atlas, half_w, top):
    """Renders the full tree into the atlas' impostor cell and returns (atlas, LOD1 mesh of two crossed quads)."""
    x, y, w, h = IMPOSTOR_CELL
    img = rasterize(mesh, atlas, (w - 8) * 2, (h - 8) * 2, half_w, top)
    pic = Image.fromarray((np.clip(img, 0, 1) * 255).astype("uint8"), "RGBA").resize((w - 8, h - 8), Image.LANCZOS)
    arr = np.array(atlas)
    arr[y + 4:y + h - 4, x + 4:x + w - 4] = np.array(pic)
    # colour bleed for the new cell
    cell = arr[y:y + h, x:x + w].astype(np.float32)
    solid = cell[..., 3] > 8
    if solid.any():
        cell[~solid, :3] = cell[solid][:, :3].mean(axis=0)
    arr[y:y + h, x:x + w] = cell.astype("uint8")
    atlas = Image.fromarray(arr, "RGBA")
    size = atlas.size[0]
    rect = ((x + 4) / size, 1 - (y + h - 4) / size, (x + w - 4) / size, 1 - (y + 4) / size)
    lod1 = Mesh()
    for yaw in (math.radians(25), math.radians(115), math.radians(70)):
        side = np.array([math.cos(yaw), 0, -math.sin(yaw)])
        n = np.array([math.sin(yaw) * 0.35, 0.93, math.cos(yaw) * 0.35])
        lod1.quad(-side * half_w, side * half_w, side * half_w + np.array([0, top, 0]), -side * half_w + np.array([0, top, 0]), rect, n)
    return atlas, lod1


def preview_sheet(items, path, size=360):
    sheet = np.zeros((size, size * len(items), 3), np.float32)
    sheet[:] = (0.55, 0.68, 0.82)
    for i, (mesh, atlas, half_w, top) in enumerate(items):
        img = rasterize(mesh, atlas, size, size, max(half_w, top / 2), max(top, half_w * 2))
        a = img[..., 3:4]
        sheet[:, i * size:(i + 1) * size] = sheet[:, i * size:(i + 1) * size] * (1 - a) + img[..., :3] * a
    Image.fromarray((np.clip(sheet, 0, 1) * 255).astype("uint8")).save(path)


def main(raw, out, preview=None):
    os.makedirs(out, exist_ok=True)
    M = os.path.join(raw, "Models")
    bark = Image.open(os.path.join(raw, "Textures", "bark_brown_02", "bark_brown_02_diff.jpg")).convert("RGB")

    fir_diff = Image.open(os.path.join(M, "fir_tree_01/png/fir_tree_01_twig_diff.png")).convert("RGB")
    fir_alpha = load_alpha(os.path.join(M, "fir_tree_01/png/fir_tree_01_twig_alpha.png"))
    fir_sprites = [sprite(fir_diff, fir_alpha, b) for b in [(310, 406, 663, 797), (647, 460, 983, 853), (665, 38, 963, 381), (191, 45, 442, 322)]]
    fir_atlas, fir_rects = build_atlas(bark, fir_sprites, SPRITE_CELLS)

    leaf_diff = Image.open(os.path.join(M, "jacaranda_tree/png/jacaranda_tree_leaves_diff.png")).convert("RGB")
    leaf_alpha = load_alpha(os.path.join(M, "jacaranda_tree/png/jacaranda_tree_leaves_alpha.png"))
    # fronds lie horizontally in the scan: rotate so the stem is at the bottom of each sprite
    fronds = [sprite(leaf_diff, leaf_alpha, (162, 20, 1015, 486), rot=90), sprite(leaf_diff, leaf_alpha, (247, 601, 1024, 1022), flip=True, rot=90),
              sprite(leaf_diff, leaf_alpha, (0, 329, 442, 715), rot=90)]
    leaf_bark = Image.open(os.path.join(raw, "Textures", "bark_willow", "bark_willow_diff.jpg")).convert("RGB")
    leaf_atlas, leaf_rects = build_atlas(leaf_bark, fronds, [(128, 0, 149, 1024), (277, 0, 149, 1024), (426, 0, 150, 1024)])

    fir0 = fir(fir_rects, 0)
    leaf0 = broadleaf(leaf_rects, 0)
    fir_atlas, fir1 = add_impostor(fir0, fir_atlas, 4.4, 10.0)
    leaf_atlas, leaf1 = add_impostor(leaf0, leaf_atlas, 3.9, 8.9)
    fir_atlas.save(os.path.join(out, "tree_fir_atlas.png"))
    leaf_atlas.save(os.path.join(out, "tree_leaf_atlas.png"))
    meshes = {"tree_fir_lod0": fir0, "tree_fir_lod1": fir1, "tree_leaf_lod0": leaf0, "tree_leaf_lod1": leaf1,
              "bush_lod0": bush(leaf_rects, 0), "bush_lod1": bush(leaf_rects, 1)}
    for name, mesh in meshes.items():
        mesh.write(os.path.join(out, name + ".obj"))
        print(name, mesh.tris(), "tris")
    if preview:
        preview_sheet([(fir0, fir_atlas, 4.4, 10.0), (fir1, fir_atlas, 4.4, 10.0), (leaf0, leaf_atlas, 3.9, 8.9), (leaf1, leaf_atlas, 3.9, 8.9),
                       (meshes["bush_lod0"], leaf_atlas, 1.6, 2.0)], preview)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else None)
