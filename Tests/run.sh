#!/bin/bash
# Headless scenario runner for HexTactica's AI. Every test is a scenario file in
# Tests/Scenarios that Unity can load (HexTactica > Load Scenario File...).
#
#   bash run.sh /path/to/HexTactica/Assets              run every scenario
#   PROBE=export bash run.sh <Assets>                    regenerate the scenario files
#   PROBE=game FILE=<name> bash run.sh <Assets>          narrate one scenario turn by turn
#
# Requires Mono's C# compiler:  sudo apt install mono-mcs   (macOS: brew install mono)
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
ASSETS="$(cd "${1:-$HERE/../Assets}" && pwd)"
command -v mcs >/dev/null || { echo "mcs not found: install mono-mcs"; exit 2; }
# (Units, buildings, terrain and scenarios live in Assets/Resources: loaded by name in the game, Resources.LoadAll.)
UNITS="$ASSETS/Resources/Units"; [ -d "$UNITS" ] || UNITS="$ASSETS/Units"
for p in Supplies Grunt Tank Artillery; do
  [ -f "$UNITS/$p.prefab" ] || { echo "Missing $UNITS/$p.prefab — run against your project's Assets folder."; exit 2; }
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
  "$HERE/Tests.cs" "$HERE/ScenarioFile.cs" "$HERE/Scenarios.cs" "$HERE/ScenarioTriggerChecks.cs"
# The front end (menus, pre-game, post-game, in-game menu, map editor; Assets/FrontEnd) is type-checked with them.
FRONT="$WORK/Assets/FrontEnd/UnitCatalog.cs $WORK/Assets/FrontEnd/GameFiles.cs $WORK/Assets/FrontEnd/MatchSetup.cs $WORK/Assets/FrontEnd/GameSession.cs $WORK/Assets/MusicManager.cs $(ls "$WORK"/Assets/FrontEnd/UI/*.cs | tr '\n' ' ') $HERE/FrontEndShims.cs"
# Type-check the editor menus and the camera too (Unity-only code; nothing runs).
mcs -target:library -out:"$WORK/editor.dll" -define:UNITY_EDITOR -nowarn:0108,0168,0219,0414,0067,0649,0162,0169 \
  $FILES "$HERE/FunctionalShims.cs" "$HERE/EditorShims.cs" "$HERE/UIShims.cs" "$WORK/Assets/UI/TransportPanel.cs" "$WORK/Assets/UI/Hud.cs" "$WORK/Assets/UI/HudRow.cs" "$WORK/Assets/UI/SpritePortrait.cs" "$WORK/Assets/UI/ProductionPanel.cs" "$WORK/Assets/UI/TurnControls.cs" "$WORK/Assets/UI/DialogueBox.cs" "$WORK/Assets/UI/ScenarioMarker.cs" "$WORK/Assets/UI/ScenarioMarkers.cs" "$WORK"/Assets/Editor/*.cs "$WORK/Assets/CameraRig.cs" $FRONT >/dev/null \
  || { echo "Editor/camera scripts failed to type-check:"; mcs -target:library -out:"$WORK/editor.dll" -define:UNITY_EDITOR -nowarn:0108,0168,0219,0414,0067,0649,0162,0169 $FILES "$HERE/FunctionalShims.cs" "$HERE/EditorShims.cs" "$HERE/UIShims.cs" "$WORK/Assets/UI/TransportPanel.cs" "$WORK/Assets/UI/Hud.cs" "$WORK/Assets/UI/HudRow.cs" "$WORK/Assets/UI/SpritePortrait.cs" "$WORK/Assets/UI/ProductionPanel.cs" "$WORK/Assets/UI/TurnControls.cs" "$WORK/Assets/UI/DialogueBox.cs" "$WORK/Assets/UI/ScenarioMarker.cs" "$WORK/Assets/UI/ScenarioMarkers.cs" "$WORK"/Assets/Editor/*.cs "$WORK/Assets/CameraRig.cs" $FRONT | grep error; exit 1; }
mono "$WORK/tests.exe" "$ASSETS" "${SCENARIOS:-$HERE/Scenarios}"
