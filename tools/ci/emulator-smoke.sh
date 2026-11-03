#!/usr/bin/env bash
# Installs the APK on a running emulator, launches it, takes screenshots and collects logcat.
# Usage: emulator-smoke.sh <apk> <outdir>
set -u
APK="$1"; OUT="$2"; PKG=com.qahramonov.orol
mkdir -p "$OUT"
adb wait-for-device
adb shell getprop ro.product.cpu.abilist
adb logcat -c || true
adb install -r -g "$APK" || { echo "::error::adb install failed"; exit 1; }
# -force-gles: the emulator's Vulkan (gfxstream/swiftshader) is unreliable; phones use Vulkan or GLES normally.
for attempt in 1 2 3; do
  adb shell am start -W -n "$PKG/com.unity3d.player.UnityPlayerActivity" -e unity "-force-gles" || true
  sleep 12
  if [ -n "$(adb shell pidof "$PKG" | tr -d '\r')" ]; then echo "launch attempt $attempt: running"; break; fi
  echo "launch attempt $attempt: process died, retrying"
  adb logcat -d -v time > "$OUT/logcat-attempt$attempt.txt" || true
done
for t in 5 10 20 30 45 60 90; do
  prev=${last:-0}; sleep $((t - prev)); last=$t
  adb exec-out screencap -p > "$OUT/screen_${t}s.png" || true
  pid=$(adb shell pidof "$PKG" | tr -d '\r')
  echo "t=${t}s pid=${pid:-<not running>}"
done
# Tap the centre (skip intro / dismiss overlays) and capture once more
adb shell input tap 960 540; sleep 8
adb exec-out screencap -p > "$OUT/screen_after_tap.png" || true
adb logcat -d -v time > "$OUT/logcat.txt" || true
grep -E "Unity|AndroidRuntime|DEBUG|libc|CRASH|IL2CPP|\[App\]|\[Boot\]" "$OUT/logcat.txt" > "$OUT/logcat-unity.txt" || true
adb shell cat /sdcard/Android/data/$PKG/files/orol-log.txt > "$OUT/orol-log.txt" 2>/dev/null || true
echo "===== Unity / crash log (filtered, last 400 lines) ====="
tail -n 400 "$OUT/logcat-unity.txt"
echo "===== Errors ====="
grep -nE "Exception|Error|FATAL|SIGSEGV|SIGABRT" "$OUT/logcat-unity.txt" | head -n 120 || true
# Small inline previews (readable from the job log without downloading artifacts)
python3 -m pip install --quiet pillow >/dev/null 2>&1 || true
for f in "$OUT"/screen_10s.png "$OUT"/screen_45s.png "$OUT"/screen_90s.png "$OUT"/screen_after_tap.png; do
  [ -s "$f" ] || continue
  python3 - "$f" <<'PY' || true
import base64, io, sys
from PIL import Image
im = Image.open(sys.argv[1]).convert("RGB")
im.thumbnail((320, 320))
buf = io.BytesIO(); im.save(buf, "JPEG", quality=60)
print("PREVIEW %s %s" % (sys.argv[1].split("/")[-1], base64.b64encode(buf.getvalue()).decode()))
PY
done
pid=$(adb shell pidof "$PKG" | tr -d '\r')
if [ -z "$pid" ]; then echo "::error::Game process is not running at the end (crashed or exited)"; fi
exit 0
