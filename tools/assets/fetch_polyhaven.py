#!/usr/bin/env python3
"""Downloads CC0 assets from Poly Haven (https://polyhaven.com, license CC0 1.0) into the Unity project.

  fetch_polyhaven.py list                     -> prints asset ids per category (to choose from)
  fetch_polyhaven.py fetch manifest.json OUT  -> downloads the assets named in the manifest

Runs in GitHub Actions (the development container has no access to polyhaven.com).
"""
import json, os, sys, urllib.request

API = "https://api.polyhaven.com"
UA = {"User-Agent": "orol-survival-asset-fetch/1.0"}


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60) as r:
        return r.read()


def list_assets():
    for kind in ("textures", "models"):
        assets = json.loads(get(f"{API}/assets?t={kind}"))
        print(f"== {kind}: {len(assets)} assets")
        for slug, a in sorted(assets.items()):
            cats = ",".join(a.get("categories", []))
            extra = f" polys={a.get('polycount')}" if kind == "models" else ""
            print(f"{kind[:-1]} {slug} [{cats}] dl={a.get('download_count')}{extra}")


def download(url, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    data = get(url)
    with open(path, "wb") as f:
        f.write(data)
    return len(data)


def fetch(manifest_path, out):
    manifest = json.load(open(manifest_path))
    report = []
    for t in manifest.get("textures", []):
        slug, res = t["id"], t.get("res", "1k")
        try:
            files = json.loads(get(f"{API}/files/{slug}"))
        except Exception as e:
            report.append(f"MISSING texture {slug}: {e}")
            continue
        for key, name in (("Diffuse", "diff"), ("nor_gl", "nor"), ("arm", "arm"), ("Rough", "rough")):
            if name in ("rough",) and "arm" in files:
                continue
            entry = files.get(key, {}).get(res, {})
            fmt = "jpg" if "jpg" in entry else "png" if "png" in entry else None
            if not fmt:
                continue
            size = download(entry[fmt]["url"], os.path.join(out, "Textures", slug, f"{slug}_{name}.{fmt}"))
            report.append(f"ok texture {slug} {name} {size // 1024} KB")
    for m in manifest.get("models", []):
        slug, res = m["id"], m.get("res", "1k")
        try:
            files = json.loads(get(f"{API}/files/{slug}"))
        except Exception as e:
            report.append(f"MISSING model {slug}: {e}")
            continue
        entry = files.get("gltf", {}).get(res, {}).get("gltf")
        if not entry:
            report.append(f"MISSING model {slug}: no gltf at {res}")
            continue
        base = os.path.join(out, "Models", slug)
        size = download(entry["url"], os.path.join(base, f"{slug}.gltf"))
        for rel, inc in entry.get("include", {}).items():
            size += download(inc["url"], os.path.join(base, rel))
        report.append(f"ok model {slug} {size // 1024} KB")
    print("\n".join(report))
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "SOURCES.txt"), "w") as f:
        f.write("Assets in this folder come from Poly Haven (https://polyhaven.com) and are licensed CC0 1.0 (public domain).\n"
                "Downloaded by tools/assets/fetch_polyhaven.py from tools/assets/manifest.json.\n\n" + "\n".join(report) + "\n")


if __name__ == "__main__":
    if sys.argv[1] == "list":
        list_assets()
    else:
        fetch(sys.argv[2], sys.argv[3])
