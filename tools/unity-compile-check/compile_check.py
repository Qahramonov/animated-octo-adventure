#!/usr/bin/env python3
"""
Unity-less C# compile check for the OROL Unity project.

Compiles every assembly definition (.asmdef) of the project - and the package assemblies it
depends on (Input System, uGUI, URP, Core RP, ...) - with Unity 6's own Roslyn compiler against the
UnityEngine/UnityEditor reference assemblies. Everything comes from the public GameCI editor image
(`unityci/editor`), so NO Unity license is needed. It does not replace a real Unity build (no
shader compilation, no IL2CPP, no asset import), but it catches C# errors in seconds.

Two passes are run:
  player : simulates the Android player build (UNITY_ANDROID, no UnityEditor, Editor asmdefs skipped)
  editor : simulates the Editor (UNITY_EDITOR, Editor-only asmdefs and tests included)

Usage:
  extract.sh <unity-version> <out-dir>                 # once: pull image + unpack DLLs/packages
  compile_check.py --unity <out-dir> --project UnityClient [--pass player|editor|both]
"""
import argparse
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

PLAYER, EDITOR = "player", "editor"

# Assembly names some packages reference that resolve to a differently named assembly.
ASSEMBLY_ALIASES = {"Unity.ugui": "UnityEngine.UI"}
# Since Unity 6 uGUI is a core package whose runtime assembly every assembly can use implicitly
# (e.g. Core RP uses UnityEngine.UI without listing it). Build it first and reference it everywhere.
IMPLICIT_ASSEMBLIES = ["UnityEngine.UI"]


def parse_version(v):
    """'6000.3.26f1' / '1.20.0' / '6000.1.0b9' -> comparable tuple."""
    m = re.match(r"(\d+)(?:\.(\d+))?(?:\.(\d+))?([abfp])?(\d+)?", v.strip())
    if not m:
        return (0,)
    stage = {"a": 0, "b": 1, "f": 2, "p": 3, None: 2}[m.group(4)]
    return (int(m.group(1)), int(m.group(2) or 0), int(m.group(3) or 0), stage, int(m.group(5) or 0))


def version_matches(expr, version):
    """Unity versionDefines expression: 'x' (>= x), '[a,b]', '(a,b)', '[a,b)', '(a,b]'."""
    expr = expr.strip()
    v = parse_version(version)
    if expr and expr[0] in "[(":
        lo, hi = [s.strip() for s in expr[1:-1].split(",")]
        ok = True
        if lo:
            ok &= v >= parse_version(lo) if expr[0] == "[" else v > parse_version(lo)
        if hi:
            ok &= v <= parse_version(hi) if expr[-1] == "]" else v < parse_version(hi)
        return ok
    return v >= parse_version(expr) if expr else True


def unity_version_defines(version):
    m = re.match(r"(\d+)\.(\d+)\.(\d+)", version)
    major, minor, patch = int(m.group(1)), int(m.group(2)), int(m.group(3))
    d = [f"UNITY_{major}", f"UNITY_{major}_{minor}", f"UNITY_{major}_{minor}_{patch}"]
    legacy = [(5, 3), (5, 4), (5, 5), (5, 6)] + [(y, n) for y in range(2017, 2024) for n in range(1, 5)]
    for y, n in legacy:
        d.append(f"UNITY_{y}_{n}_OR_NEWER")
    for n in range(0, minor + 1):
        d.append(f"UNITY_{major}_{n}_OR_NEWER")
    return d


def is_managed_dll(path):
    try:
        with open(path, "rb") as f:
            data = f.read(1 << 20)
        return data[:2] == b"MZ" and b"BSJB" in data
    except OSError:
        return False


class Asm:
    def __init__(self, path, data, package):
        self.path = path
        self.dir = os.path.dirname(path)
        self.data = data
        self.name = data["name"]
        self.package = package

    def refs(self, guid_map):
        out = []
        for r in self.data.get("references", []):
            name = guid_map.get(r[5:], r) if r.startswith("GUID:") else r
            out.append(ASSEMBLY_ALIASES.get(name, name))
        return out


def find_asmdefs(root, package=None):
    result = []
    for path in glob.glob(os.path.join(root, "**", "*.asmdef"), recursive=True):
        rel = os.path.relpath(path, root)
        parts = rel.split(os.sep)
        if any(p.endswith("~") or (p.startswith(".") and p != ".") for p in parts[:-1]):
            continue
        try:
            with open(path, encoding="utf-8-sig") as f:
                data = json.load(f)
        except (OSError, ValueError):
            continue
        result.append(Asm(path, data, package))
    return result


def collect_sources(asm, all_asm_dirs):
    files = []
    for dirpath, dirnames, filenames in os.walk(asm.dir):
        keep = []
        for d in dirnames:
            full = os.path.join(dirpath, d)
            if d.endswith("~") or d.startswith("."):
                continue
            if full in all_asm_dirs and full != asm.dir:
                continue
            if glob.glob(os.path.join(full, "*.asmref")):
                continue
            keep.append(d)
        dirnames[:] = keep
        for fn in filenames:
            if fn.endswith(".cs"):
                files.append(os.path.join(dirpath, fn))
    return sorted(files)


def constraint_ok(expr, defines):
    expr = expr.strip()
    for alt in expr.split("||"):
        alt = alt.strip()
        if alt.startswith("!"):
            if alt[1:] not in defines:
                return True
        elif alt in defines:
            return True
    return False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--unity", required=True, help="dir with Managed/, NetStandard/, DotNetSdkRoslyn/, packages/")
    ap.add_argument("--project", required=True, help="Unity project root (contains Assets/ and Packages/)")
    ap.add_argument("--pass", dest="passes", default="both", choices=["player", "editor", "both"])
    ap.add_argument("--version", default=None, help="Unity version (default: ProjectSettings/ProjectVersion.txt)")
    ap.add_argument("--keep", action="store_true", help="keep the output directory")
    args = ap.parse_args()

    unity = os.path.abspath(args.unity)
    project = os.path.abspath(args.project)
    version = args.version
    if not version:
        with open(os.path.join(project, "ProjectSettings", "ProjectVersion.txt")) as f:
            version = re.search(r"m_EditorVersion:\s*(\S+)", f.read()).group(1)

    with open(os.path.join(project, "Packages", "manifest.json")) as f:
        manifest = json.load(f)["dependencies"]

    # ---- discover assemblies (packages first, then project)
    pkg_root = os.path.join(unity, "packages")
    asms = {}
    guid_map = {}
    installed_packages = dict(manifest)
    for pkg_dir in sorted(glob.glob(os.path.join(pkg_root, "*"))):
        pj = os.path.join(pkg_dir, "package.json")
        if not os.path.exists(pj):
            continue
        with open(pj) as f:
            pj_data = json.load(f)
        pname = pj_data["name"]
        installed_packages[pname] = pj_data.get("version", "0.0.0")
        for a in find_asmdefs(pkg_dir, pname):
            if a.name not in asms:
                asms[a.name] = a
            meta = a.path + ".meta"
            if os.path.exists(meta):
                with open(meta) as f:
                    for line in f:
                        if line.startswith("guid:"):
                            guid_map[line.split(":", 1)[1].strip()] = a.name
    project_asms = find_asmdefs(os.path.join(project, "Assets"))
    for a in project_asms:
        asms[a.name] = a
    all_asm_dirs = {a.dir for a in asms.values()}

    managed = os.path.join(unity, "Managed")
    engine_refs = sorted(glob.glob(os.path.join(managed, "UnityEngine", "UnityEngine*.dll")))
    editor_refs = sorted(glob.glob(os.path.join(managed, "UnityEditor*.dll"))) + \
        sorted(glob.glob(os.path.join(managed, "UnityEngine", "UnityEditor*.dll")))
    netstandard = [os.path.join(unity, "NetStandard", "ref", "2.1.0", "netstandard.dll")]
    netstandard += sorted(glob.glob(os.path.join(unity, "NetStandard", "compat", "2.1.0", "shims", "netstandard", "*.dll")))
    # .NET Framework facades (mscorlib, System, ...) so netfx-targeted plugins such as nunit resolve
    netstandard += sorted(glob.glob(os.path.join(unity, "NetStandard", "compat", "2.1.0", "shims", "netfx", "*.dll")))
    csc = os.path.join(unity, "DotNetSdkRoslyn", "csc.dll")
    if not os.path.exists(csc):
        sys.exit("Roslyn not found at " + csc)

    package_dlls = {}
    for dll in glob.glob(os.path.join(pkg_root, "**", "*.dll"), recursive=True):
        rel = os.path.relpath(dll, pkg_root)
        parts = rel.split(os.sep)
        if any(p.endswith("~") or p.startswith(".") or p in ("Tests", "Samples") for p in parts[:-1]):
            continue
        if is_managed_dll(dll):
            package_dlls.setdefault(os.path.basename(dll), dll)

    passes = [PLAYER, EDITOR] if args.passes == "both" else [args.passes]
    out_root = tempfile.mkdtemp(prefix="unity-compile-check-")
    total_errors = 0
    try:
        for p in passes:
            total_errors += run_pass(p, version, asms, guid_map, project_asms, installed_packages, all_asm_dirs,
                                     engine_refs, editor_refs, netstandard, package_dlls, csc, out_root)
    finally:
        if not args.keep:
            shutil.rmtree(out_root, ignore_errors=True)
    print("\nRESULT:", "OK - all assemblies compiled" if total_errors == 0 else f"FAILED ({total_errors} assembly error(s))")
    sys.exit(0 if total_errors == 0 else 1)


def run_pass(mode, version, asms, guid_map, project_asms, installed, all_asm_dirs,
             engine_refs, editor_refs, netstandard, package_dlls, csc, out_root):
    print(f"\n=== {mode.upper()} pass (Unity {version}) ===")
    defines = set(unity_version_defines(version)) | {
        "NET_STANDARD_2_0", "NET_STANDARD_2_1", "NET_STANDARD", "NETSTANDARD2_1", "NETSTANDARD",
        "CSHARP_7_3_OR_NEWER", "CSHARP_8_OR_NEWER", "CSHARP_9_OR_NEWER", "ENABLE_INPUT_SYSTEM", "UNITY_ASSERTIONS",
        "UNITY_ANDROID", "UNITY_ANDROID_API", "PLATFORM_ANDROID", "UNITY_64", "ENABLE_UNITYWEBREQUEST",
        "TEXTCORE_1_0_OR_NEWER", "UNITY_UGP_API", "ENABLE_MANAGED_JOBS", "ENABLE_ASYNC_GPU_READBACK",
        # the reference engine assemblies come from the Editor, which has the safety checks compiled in
        "ENABLE_UNITY_COLLECTIONS_CHECKS", "ENABLE_BURST_AOT"}
    if mode == PLAYER:
        defines |= {"ENABLE_IL2CPP", "UNITY_IL2CPP"}
    else:
        defines |= {"UNITY_EDITOR", "UNITY_EDITOR_64", "UNITY_EDITOR_LINUX", "ENABLE_MONO", "UNITY_INCLUDE_TESTS",
                    "UNITY_EDITOR_WIN" if False else "UNITY_EDITOR_LINUX"}

    def platform_ok(a):
        inc = a.data.get("includePlatforms", []) or []
        exc = a.data.get("excludePlatforms", []) or []
        target = "Android" if mode == PLAYER else "Editor"
        if inc and target not in inc:
            return False
        if target in exc:
            return False
        return True

    editor_only_defines = {"UNITY_EDITOR", "UNITY_EDITOR_64", "UNITY_EDITOR_LINUX", "ENABLE_MONO", "UNITY_INCLUDE_TESTS"}

    def asm_defines(a):
        d = set(defines)
        # Package runtime assemblies are compiled like in a player build even in the editor pass:
        # their editor-only parts need editor-internal assemblies we do not reproduce here. Only
        # project assemblies and Editor-only package assemblies get UNITY_EDITOR.
        inc = a.data.get("includePlatforms", []) or []
        if mode == EDITOR and a.package is not None and inc != ["Editor"]:
            d -= editor_only_defines
            d |= {"ENABLE_IL2CPP", "UNITY_IL2CPP"}
        for vd in a.data.get("versionDefines", []) or []:
            name = vd.get("name", "")
            expr = vd.get("expression", "")
            if name == "Unity":
                if version_matches(expr, version):
                    d.add(vd["define"])
            elif name in installed:
                if version_matches(expr, installed[name]) if re.match(r"[\d\[(]", installed[name] or "") else True:
                    d.add(vd["define"])
        return d

    # closure of project assemblies
    order, state = [], {}

    def visit(name, chain):
        if name not in asms:
            return False
        a = asms[name]
        if not platform_ok(a):
            return False
        dd = asm_defines(a)
        if not all(constraint_ok(c, dd) for c in a.data.get("defineConstraints", []) or []):
            return False
        if state.get(name) == "done":
            return True
        if state.get(name) == "visiting":
            return True
        state[name] = "visiting"
        for r in a.refs(guid_map):
            visit(r, chain + [name])
        state[name] = "done"
        order.append(name)
        return True

    for name in IMPLICIT_ASSEMBLIES:
        visit(name, [])
    for a in project_asms:
        visit(a.name, [])

    built = {}
    errors = 0
    for name in order:
        a = asms[name]
        srcs = collect_sources(a, all_asm_dirs)
        if not srcs:
            continue
        out = os.path.join(out_root, mode, name + ".dll")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        refs = list(netstandard) + list(engine_refs)
        if mode == EDITOR:
            refs += editor_refs
        for r in a.refs(guid_map) + [n for n in IMPLICIT_ASSEMBLIES if n != name]:
            if r in built:
                refs.append(built[r])
        auto_ref_names = set()
        for other_name, other_path in built.items():
            o = asms[other_name]
            if o.data.get("autoReferenced", True) and o.package is not None and other_path not in refs:
                pass  # Unity only auto-references for predefined assemblies; asmdefs must list references
        for pre in a.data.get("precompiledReferences", []) or []:
            if pre in package_dlls:
                refs.append(package_dlls[pre])
            elif pre == "nunit.framework.dll" and "nunit.framework.dll" in package_dlls:
                refs.append(package_dlls[pre])
        if not a.data.get("overrideReferences", False):
            for dll_name, dll_path in package_dlls.items():
                if dll_name.startswith(("Unity.Burst.Unsafe", "Unity.Collections.LowLevel.ILSupport")):
                    refs.append(dll_path)
        dd = sorted(asm_defines(a))
        rsp = os.path.join(out_root, mode, name + ".rsp")
        with open(rsp, "w") as f:
            f.write("-nologo\n-target:library\n-nostdlib+\n-noconfig\n-langversion:9.0\n-deterministic\n")
            f.write("-nowarn:0169,0649,0618,0414,0219,1701,1702,0105,0108,0114,0162,0168,0429,0472,0809,8632,8669\n")
            if a.data.get("allowUnsafeCode"):
                f.write("-unsafe\n")
            f.write(f"-out:\"{out}\"\n")
            f.write("-define:" + ";".join(dd) + "\n")
            seen = set()
            for r in refs:
                if r not in seen:
                    seen.add(r)
                    f.write(f"-r:\"{r}\"\n")
            for s in srcs:
                f.write(f"\"{s}\"\n")
        proc = subprocess.run(["dotnet", csc, "@" + rsp], capture_output=True, text=True,
                              env=dict(os.environ, DOTNET_ROLL_FORWARD="Major"))
        is_project = a.package is None
        msgs = [l for l in proc.stdout.splitlines() if ": error " in l]
        if proc.returncode != 0:
            errors += 1
            tag = "PROJECT" if is_project else "package"
            print(f"  FAIL  {name} [{tag}] ({len(srcs)} files, {len(msgs)} errors)")
            for m in msgs[:40]:
                print("        " + m.replace(os.path.dirname(a.dir) + os.sep, ""))
            if not msgs:
                print(proc.stdout[-2000:], proc.stderr[-2000:])
        else:
            built[name] = out
            if is_project:
                print(f"  ok    {name} ({len(srcs)} files)")
    return errors


if __name__ == "__main__":
    main()
