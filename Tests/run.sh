#!/bin/bash
# Headless behavioural test suite for HexTactica's gameplay and AI.
#
#   bash run.sh /path/to/HexTactica/Assets
#
# Point it at your project's Assets folder AFTER copying the package's Assets in: the
# regression tests read your real unit prefabs (TempHQ, Supplies, Grunt, Tank, Artillery),
# and only the two prefabs this package changes are included in it.
#
# Requires Mono's C# compiler:  sudo apt install mono-mcs   (macOS: brew install mono)
#
# Diagnostics (not tests): PROBE=game|wide|decisive|transport|sweep ./run.sh <Assets>
# EXPLAIN=Tank with PROBE=real9b prints a unit's top candidate moves, term by term.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
ASSETS="$(cd "${1:-$HERE/../Assets}" && pwd)"
command -v mcs >/dev/null || { echo "mcs not found: install mono-mcs"; exit 2; }
for p in TempHQ Supplies Grunt Tank Artillery; do
  [ -f "$ASSETS/Units/$p.prefab" ] || { echo "Missing $ASSETS/Units/$p.prefab — run against your project's Assets folder."; exit 2; }
done
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
cp -r "$ASSETS" "$WORK/Assets"
# JSONObject has `using System.Diagnostics`, which shadows UnityEngine.Debug outside Unity.
sed -i.bak 's/\bDebug\.Log/UnityEngine.Debug.Log/g' "$WORK/Assets/scripts/JSONObject.cs"
FILES=""
while read -r f; do [ -n "$f" ] && FILES="$FILES $WORK/Assets/$f"; done < "$HERE/logicfiles.txt"
mcs -target:exe -out:"$WORK/tests.exe" -nowarn:0108,0168,0219,0414,0067,0649,0162,0169 \
  $FILES "$HERE/FunctionalShims.cs" "$HERE/Rig.cs" "$HERE/PrefabLoader.cs" \
  "$HERE/Tests.cs" "$HERE/Regression.cs" "$HERE/Probe.cs"
mono "$WORK/tests.exe" "$ASSETS"
