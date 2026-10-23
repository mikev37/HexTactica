#!/bin/bash
# Regression check against an earlier commit: plays every gauntlet and air-gauntlet map with both
# sides on default settings, once with this checkout's code and once with <commit>'s code, and
# compares the board after every turn. "SAME" means the two builds played the game move for move
# alike; "DIFF" means the change altered play on that map (look at the two logs it names).
#
#   bash Tests/compare.sh <commit> [filter]       e.g. bash Tests/compare.sh HEAD~1 gauntlet
#
# <commit> must have the BOARD option in Scenarios.Duel (added with this script).
# Writes into a new temporary folder and prints its path; nothing is deleted.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/.." && pwd)"
OLD="$1"; FILTER="${2:-gauntlet}"
[ -n "$OLD" ] || { echo "usage: bash Tests/compare.sh <commit> [filter]"; exit 2; }
OUT=$(mktemp -d)
git -C "$REPO" worktree add -q "$OUT/old" "$OLD"
# .meta files are not in git (ignored): the old checkout gets this checkout's, where it has none
# of its own, so prefabs resolve the same on both sides. Existing files are never overwritten.
(cd "$REPO" && find Assets -name "*.meta" -print0) | while IFS= read -r -d '' m; do
    [ -e "$OUT/old/$m" ] || { [ -d "$(dirname "$OUT/old/$m")" ] && cp -n "$REPO/$m" "$OUT/old/$m"; }
done
echo "logs and the old checkout: $OUT"
for f in $(ls "$HERE/Scenarios" | grep "$FILTER" | sed 's/\.dat$//'); do
  (BOARD=1 PROBE=duel FILE=$f ROUNDS=30 SCENARIOS="$HERE/Scenarios" bash "$HERE/run.sh" > "$OUT/new-$f.txt" 2>&1) &
  (BOARD=1 PROBE=duel FILE=$f ROUNDS=30 SCENARIOS="$HERE/Scenarios" bash "$OUT/old/Tests/run.sh" "$OUT/old/Assets" > "$OUT/old-$f.txt" 2>&1) &
  wait
  if cmp -s "$OUT/old-$f.txt" "$OUT/new-$f.txt"; then r=SAME; else r=DIFF; fi
  echo "$r  $f | old: $(grep '^aircraft' "$OUT/old-$f.txt") | new: $(grep '^aircraft' "$OUT/new-$f.txt")"
done
