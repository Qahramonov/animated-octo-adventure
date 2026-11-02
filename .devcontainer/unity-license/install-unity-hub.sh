#!/usr/bin/env bash
# Installs Unity Hub (official Unity apt repository) and a browser for the Unity sign-in page.
set -euo pipefail
sudo apt-get update -qq
sudo apt-get install -y -qq wget gpg ca-certificates firefox-esr xdg-utils libgbm1 libasound2 libnss3 >/dev/null
wget -qO - https://hub.unity3d.com/linux/keys/public | gpg --dearmor | sudo tee /usr/share/keyrings/Unity_Technologies_ApS.gpg >/dev/null
echo "deb [signed-by=/usr/share/keyrings/Unity_Technologies_ApS.gpg] https://hub.unity3d.com/linux/repos/deb stable main" | sudo tee /etc/apt/sources.list.d/unityhub.list >/dev/null
sudo apt-get update -qq
sudo apt-get install -y -qq unityhub >/dev/null
xdg-settings set default-web-browser firefox-esr.desktop 2>/dev/null || true
cat > "$HOME/start-unity-hub.sh" <<'EOS'
#!/usr/bin/env bash
# Run inside the noVNC desktop terminal: opens Unity Hub (sign in -> Preferences -> Licenses -> Add -> Free personal license)
export DISPLAY=${DISPLAY:-:1}
unityhub --no-sandbox &
EOS
chmod +x "$HOME/start-unity-hub.sh"
cat > "$HOME/show-license.sh" <<'EOS'
#!/usr/bin/env bash
# Prints the license file to copy into the UNITY_LICENSE GitHub secret
f="$HOME/.local/share/unity3d/Unity/Unity_lic.ulf"
if [ -f "$f" ]; then cat "$f"; else echo "No license yet. Activate a free personal license in Unity Hub first."; fi
EOS
chmod +x "$HOME/show-license.sh"
echo "Unity Hub installed. Open the forwarded port 6080 (password: orol), open a terminal there and run ~/start-unity-hub.sh"
