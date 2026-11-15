#!/usr/bin/env python3
"""Turns raw Poly Haven downloads (branch assets-raw) into game-ready assets for the Unity project:
decimated OBJ meshes with UVs (LOD0 + LOD1) and resized textures (albedo, normal, ARM).

  convert.py <raw dir> <UnityClient/Assets/ThirdParty/PolyHaven/Resources/PH>

Needs: pip install pymeshlab pillow numpy   (and libopengl0 on Linux)
"""
import json, os, sys
import numpy as np
import pymeshlab
from PIL import Image

# name -> (lod0 tris, lod1 tris, texture size, fit)  fit: "ground" = base on y=0, unit radius; "grip" = keep metres
MODELS = {
    "boulder_01": (700, 160, 1024, "ground"),
    "namaqualand_boulder_02": (700, 160, 1024, "ground"),
    "namaqualand_boulder_03": (700, 160, 1024, "ground"),
    "namaqualand_boulder_05": (700, 160, 1024, "ground"),
    "hatchet": (900, 0, 512, "prop"),
    "picke_dirty_01": (900, 0, 512, "prop"),
}
# The other models in tools/assets/manifest.json (crates, barrel, jerrycan, torch, pistol, hammer, knife, ...) stay on the
# assets-raw branch until the game has the matching mechanics (loot containers, firearms, building).
TEXTURES = {  # tiling terrain / surface textures: name -> size
    "rocky_terrain_02": 1024, "forrest_ground_01": 1024, "rock_boulder_dry": 1024, "sand_01": 1024,
}


def save_tex(src, dst, size, quality=86):
    im = Image.open(src).convert("RGB")
    if im.size[0] > size:
        im = im.resize((size, size), Image.LANCZOS)
    im.save(dst, "JPEG", quality=quality, optimize=True)
    return os.path.getsize(dst) // 1024


def decimate(ms, tris, keep_uv):
    if ms.current_mesh().face_number() <= tris:
        return
    if keep_uv:
        ms.meshing_decimation_quadric_edge_collapse_with_texture(targetfacenum=tris, qualitythr=0.3, preserveboundary=False,
                                                                 optimalplacement=True, preservenormal=True)
    else:  # scanned rocks: UV islands block decimation, the game shades them with a triplanar texture instead
        ms.meshing_merge_close_vertices()
        ms.meshing_decimation_quadric_edge_collapse(targetfacenum=tris, qualitythr=0.3, preserveboundary=False,
                                                    preservenormal=True, optimalplacement=True)


def write_obj(mesh, path):
    """Writes vertices/UVs/normals split per wedge so Unity imports exactly these UVs."""
    v = mesh.vertex_matrix()
    f = mesh.face_matrix()
    uv = mesh.wedge_tex_coord_matrix() if mesh.has_wedge_tex_coord() else np.zeros((len(f) * 3, 2))
    n = mesh.vertex_normal_matrix()
    with open(path, "w") as o:
        o.write("# CC0 model from polyhaven.com, decimated for mobile\n")
        for p in v:
            o.write(f"v {p[0]:.5f} {p[1]:.5f} {p[2]:.5f}\n")
        for t in uv:
            o.write(f"vt {t[0]:.5f} {t[1]:.5f}\n")
        for q in n:
            o.write(f"vn {q[0]:.4f} {q[1]:.4f} {q[2]:.4f}\n")
        for i, tri in enumerate(f):
            a, b, c = tri + 1
            o.write(f"f {a}/{i*3+1}/{a} {b}/{i*3+2}/{b} {c}/{i*3+3}/{c}\n")


def convert_model(raw, out, name, cfg):
    lod0, lod1, tex, fit = cfg
    src = os.path.join(raw, "Models", name)
    gltf = json.load(open(os.path.join(src, name + ".gltf")))
    ms = pymeshlab.MeshSet()
    ms.load_new_mesh(os.path.join(src, name + ".gltf"))
    if ms.mesh_number() > 1:
        ms.generate_by_merging_visible_meshes()
    m = ms.current_mesh()
    before = m.face_number()
    if not m.has_wedge_tex_coord():
        ms.compute_texcoord_transfer_vertex_to_wedge()
    # normalise placement
    bb = m.bounding_box()
    mn, mx = bb.min(), bb.max()
    cx, cz = (mn[0] + mx[0]) / 2, (mn[2] + mx[2]) / 2
    size = max(mx[0] - mn[0], mx[2] - mn[2])
    if fit == "ground":      # unit footprint radius, sitting on the ground
        ms.compute_matrix_from_translation(axisx=-cx, axisy=-mn[1], axisz=-cz)
        ms.compute_matrix_from_scaling_or_normalization(axisx=2.0 / size, uniformflag=True)
    elif fit == "ground_m":  # real size, sitting on the ground
        ms.compute_matrix_from_translation(axisx=-cx, axisy=-mn[1], axisz=-cz)
    ms.compute_normal_per_vertex()
    result = {"source_tris": before, "materials": len(gltf.get("materials", []))}
    for lod, tris in ((0, lod0), (1, lod1)):
        if tris <= 0:
            continue
        decimate(ms, tris, fit != "ground")
        ms.compute_normal_per_vertex()
        cur = ms.current_mesh()
        write_obj(cur, os.path.join(out, f"{name}_lod{lod}.obj"))
        result[f"lod{lod}"] = cur.face_number()
    bb = ms.current_mesh().bounding_box()
    result["size"] = [round(float(bb.max()[i] - bb.min()[i]), 3) for i in range(3)]
    # textures: first material only
    tdir = os.path.join(src, "textures")
    files = sorted(os.listdir(tdir)) if os.path.isdir(tdir) else []
    for key, short, size_div in (("_diff_", "diff", 1), ("_nor_gl_", "nor", 1), ("_arm_", "arm", 2)):
        if fit == "ground":
            break  # triplanar rocks use the shared rock texture
        match = [f for f in files if key in f]
        if match:
            result[short] = save_tex(os.path.join(tdir, match[0]), os.path.join(out, f"{name}_{short}.jpg"), tex // size_div)
        result[short + "_count"] = len(match)
    return result


def main(raw, out):
    os.makedirs(out, exist_ok=True)
    report = {}
    for name, size in TEXTURES.items():
        src = os.path.join(raw, "Textures", name)
        r = {}
        for short, div in (("diff", 1), ("nor", 1), ("arm", 2)):
            for ext in ("jpg", "png"):
                p = os.path.join(src, f"{name}_{short}.{ext}")
                if os.path.exists(p):
                    r[short] = save_tex(p, os.path.join(out, f"{name}_{short}.jpg"), size // div)
        report["tex:" + name] = r
    for name, cfg in MODELS.items():
        try:
            report[name] = convert_model(raw, out, name, cfg)
        except Exception as e:
            report[name] = {"error": str(e)[:200]}
    for k, v in report.items():
        print(k, v)
    with open(os.path.join(out, "..", "..", "SOURCES.txt"), "w") as f:
        f.write("Everything under this folder comes from Poly Haven (https://polyhaven.com), licensed CC0 1.0 (public domain).\n"
                "Meshes were decimated and textures resized by tools/assets/convert.py.\n\nAssets:\n" +
                "\n".join(f"  https://polyhaven.com/a/{k.replace('tex:', '')}" for k in report) + "\n")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
