#!/usr/bin/env bash
# Extracts what the compile check needs from the public GameCI Unity editor image
# (no Unity license involved): engine/editor reference assemblies, .NET Standard facades,
# Unity's Roslyn compiler and the package sources the project uses.
#   tools/unity-compile-check/extract.sh 6000.3.26f1 /tmp/unity
set -euo pipefail
VERSION="${1:?unity version}"
OUT="${2:?output dir}"
IMAGE="unityci/editor:ubuntu-${VERSION}-base-3"
mkdir -p "$OUT"
echo "Pulling $IMAGE ..."
docker pull -q "$IMAGE"
CID=$(docker create "$IMAGE" /bin/true)
trap 'docker rm -f "$CID" >/dev/null 2>&1 || true' EXIT
D=/opt/unity/Editor/Data
docker cp "$CID:$D/Managed" "$OUT/Managed"
docker cp "$CID:$D/NetStandard" "$OUT/NetStandard"
docker cp "$CID:$D/DotNetSdkRoslyn" "$OUT/DotNetSdkRoslyn"
mkdir -p "$OUT/packages" "$OUT/tgz"
for p in com.unity.render-pipelines.core com.unity.render-pipelines.universal com.unity.render-pipelines.universal-config \
         com.unity.ugui com.unity.test-framework com.unity.ext.nunit com.unity.shadergraph com.unity.adaptiveperformance; do
  docker cp "$CID:$D/Resources/PackageManager/BuiltInPackages/$p" "$OUT/packages/$p" 2>/dev/null || echo "  (no built-in $p)"
done
for t in inputsystem mathematics burst collections nuget.mono-cecil; do
  f=$(docker run --rm --entrypoint /bin/sh "$IMAGE" -c "ls $D/Resources/PackageManager/Editor/ | grep '^com.unity.$t-[0-9]' | sort -V | tail -1")
  docker cp "$CID:$D/Resources/PackageManager/Editor/$f" "$OUT/tgz/$f"
  mkdir -p "$OUT/tgz/x-$t" && tar -xzf "$OUT/tgz/$f" -C "$OUT/tgz/x-$t"
  ln -sfn "$OUT/tgz/x-$t/package" "$OUT/packages/com.unity.$t"
done
echo "Extracted to $OUT"
du -sh "$OUT" | tail -1
