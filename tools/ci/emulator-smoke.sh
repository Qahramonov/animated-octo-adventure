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
for t in 10 30 45; do
  prev=${last:-0}; sleep $((t - prev)); last=$t
  adb exec-out screencap -p > "$OUT/screen_${t}s.png" || true
  pid=$(adb shell pidof "$PKG" | tr -d '\r')
  echo "t=${t}s pid=${pid:-<not running>}"
done
# Tap the centre (skip intro / dismiss overlays) and capture once more
adb shell input tap 960 540; sleep 8
adb exec-out screencap -p > "$OUT/screen_after_tap.png" || true
# ---- second run: straight into offline practice (island, HUD, inventory, gathering) ----
adb shell am force-stop "$PKG"; sleep 2
adb shell am start -W -n "$PKG/com.unity3d.player.UnityPlayerActivity" -e unity "-force-gles" -e orol_auto offline || true
sleep 75
adb exec-out screencap -p > "$OUT/island_1.png" || true
size=$(adb shell wm size | tr -d '\r' | sed 's/.*: //'); W=${size%x*}; H=${size#*x}
if [ "$W" -lt "$H" ]; then T=$W; W=$H; H=$T; fi
echo "screen ${W}x${H}; island pid=$(adb shell pidof "$PKG" | tr -d '\r')"
# hold the HIT button for 3 s (swing animation), then open the bag
adb shell input swipe $((W*91/100)) $((H*54/100)) $((W*91/100)) $((H*54/100)) 3000 &
sleep 1.5; adb exec-out screencap -p > "$OUT/island_2_swing.png" || true; wait
adb shell input tap $((W*88/100)) $((H*7/100)); sleep 2
adb exec-out screencap -p > "$OUT/island_3_bag.png" || true
adb shell input tap $((W*955/1000)) $((H*65/1000)); sleep 1   # close with the X button
# walk forward for 6 s (joystick up), then look again
adb shell input swipe $((W*14/100)) $((H*76/100)) $((W*14/100)) $((H*56/100)) 6000; sleep 1
adb exec-out screencap -p > "$OUT/island_4_walk.png" || true
adb logcat -d -v time > "$OUT/logcat.txt" || true
grep -E "Unity|AndroidRuntime|DEBUG|libc|CRASH|IL2CPP|\[App\]|\[Boot\]" "$OUT/logcat.txt" > "$OUT/logcat-unity.txt" || true
adb shell cat /sdcard/Android/data/$PKG/files/orol-log.txt > "$OUT/orol-log.txt" 2>/dev/null || true
echo "===== Unity / crash log (filtered, last 400 lines) ====="
tail -n 400 "$OUT/logcat-unity.txt"
echo "Shader link errors: $(grep -c "GLSL link error" "$OUT/logcat-unity.txt")"
echo "===== Errors ====="
grep -nE "Exception|Error|FATAL|SIGSEGV|SIGABRT" "$OUT/logcat-unity.txt" | head -n 120 || true
# Small inline previews (readable from the job log without downloading artifacts)
python3 -m pip install --quiet pillow >/dev/null 2>&1 || true
for f in "$OUT"/screen_45s.png "$OUT"/island_1.png "$OUT"/island_2_swing.png "$OUT"/island_3_bag.png "$OUT"/island_4_walk.png; do
  [ -s "$f" ] || continue
  python3 - "$f" <<'PY' || true
import base64, io, sys
from PIL import Image
im = Image.open(sys.argv[1]).convert("RGB")
im.thumbnail((960, 960))
buf = io.BytesIO(); im.save(buf, "JPEG", quality=70)
print("PREVIEW %s %s" % (sys.argv[1].split("/")[-1], base64.b64encode(buf.getvalue()).decode()))
PY
done
pid=$(adb shell pidof "$PKG" | tr -d '\r')
if [ -z "$pid" ]; then echo "::error::Game process is not running at the end (crashed or exited)"; fi
exit 0
