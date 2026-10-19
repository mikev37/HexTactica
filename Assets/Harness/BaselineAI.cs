using System.Collections.Generic;
using UnityEngine;
using HexTactica.AI;

namespace HexTactica.Harness {

    /// <summary>
    /// Fixed baselines to measure the real AI against.
    ///
    /// Greedy takes the core of the pre-existing AttackMove — highest raw attack value
    /// this turn, tiebreak on incoming threat, then cover, no notion of supply, units in
    /// list order — but NOT its fallbacks. The original looked up to three moves ahead
    /// and walked toward the best target there, and failing that went to its objective.
    /// Without those, a greedy line unit with nothing in reach does nothing: this
    /// opponent never advances. It is a passive baseline, not a faithful port, and
    /// results against it say little about play against an opponent that attacks.
    ///
    /// Mirror mode is not a class: it is the Full AI on both sides with identical
    /// profiles. It should sit near 50%, and if it does not, there is an asymmetry
    /// bug in turn order or initiative rather than an AI problem. That test would have
    /// caught GameController.EndTurn iterating players backwards while incrementing
    /// the day counter at index 0.
    /// </summary>
    public class BaselineAIPlayer : MonoBehaviour {

        public Player player;
        public AIKind kind = AIKind.Greedy;
        public int randomSeed = 12345;

        int rngState;

        void Start() {
            if (player == null) player = GetComponent<Player>();
            rngState = randomSeed;
        }

        /// <summary>Deterministic LCG, so "random" baseline runs still replay exactly.</summary>
        int NextRandom(int bound) {
            rngState = (int)((rngState * 1103515245L + 12345L) & 0x7FFFFFFF);
            return bound <= 0 ? 0 : rngState % bound;
        }

        public void PlayWholeTurn() {
            if (player == null || player.eliminated) return;

            List<Unit> roster = new List<Unit>(player.units);
            AIMap.SortStable(roster);

            for (int i = 0; i < roster.Count; i++) {
                Unit u = roster[i];
                if (u == null || u.Strength <= 0 || u.tile == null || !u.active) continue;
                if (u.trasnporter != null) { u.active = false; continue; }

                if (kind == AIKind.Random) TakeRandomAction(u);
                else TakeGreedyAction(u);

                u.active = false;
            }

            if (GameController.gc != null && !GameController.gc.gameOver) GameController.gc.EndTurn();
        }

        // ------------------------------------------------------------------ greedy

        void TakeGreedyAction(Unit unit) {
            // Highest raw attack value reachable, ignoring supply entirely.
            MoveField reach = AIPath.ThisTurn(unit);

            HexTile bestTile = unit.tile;
            Unit bestTarget = null;
            float bestValue = float.NegativeInfinity;
            float bestThreat = float.PositiveInfinity;
            int bestCover = -1;

            List<HexTile> candidates = new List<HexTile>();
            foreach (HexTile t in reach.Tiles) {
                if (AIPath.CanFinishTurnOn(unit, t)) candidates.Add(t);
            }
            if (!candidates.Contains(unit.tile)) candidates.Add(unit.tile);

            for (int i = 0; i < candidates.Count; i++) {
                HexTile from = candidates[i];
                if (from != unit.tile && !unit.moveAndAttack) {
                    // Indirect fire: moving forfeits the shot, so value is zero.
                    EvaluateGreedy(unit, from, null, 0f, ref bestTile, ref bestTarget,
                                   ref bestValue, ref bestThreat, ref bestCover);
                    continue;
                }

                List<HexTile> inRange = AIPath.TilesInRange(from, unit.MinRange, unit.AttackRange);
                for (int j = 0; j < inRange.Count; j++) {
                    HexTile t = inRange[j];
                    if (t.occupier == null || t.occupier.owner == unit.owner) continue;
                    if (!unit.CanAttack(from)) continue;

                    int damage = CombatMath.PredictDamage(unit, t.occupier, from);
                    float value = damage * Mathf.Max(1, t.occupier.value());
                    EvaluateGreedy(unit, from, t.occupier, value, ref bestTile, ref bestTarget,
                                   ref bestValue, ref bestThreat, ref bestCover);
                }
            }

            if (bestTile != unit.tile && bestTile != null && bestTile.occupier == null) {
                MoveField f = AIPath.ThisTurn(unit);
                int steps = f.Reachable(bestTile) ? f.Get(bestTile).steps : 1;
                unit.TeleportTo(bestTile, steps);
            }
            if (bestTarget != null && bestTarget.Strength > 0 && unit.Strength > 0) {
                int d = unit.tile.Distance(bestTarget.tile);
                if (d <= unit.AttackRange && d > unit.MinRange && unit.CanAttack(unit.tile)) {
                    unit.OrderAttack(bestTarget);
                }
            }
        }

        void EvaluateGreedy(Unit unit, HexTile from, Unit target, float value,
                            ref HexTile bestTile, ref Unit bestTarget,
                            ref float bestValue, ref float bestThreat, ref int bestCover) {

            float threat = from.getThreatTo(unit);
            int cover = Mathf.Min(unit.Concealment, from.EffectiveCover);

            bool better = value > bestValue
                || (value == bestValue && threat < bestThreat)
                || (value == bestValue && threat == bestThreat && cover > bestCover);

            if (!better) return;
            bestValue = value;
            bestThreat = threat;
            bestCover = cover;
            bestTile = from;
            bestTarget = target;
        }

        // ------------------------------------------------------------------ random

        void TakeRandomAction(Unit unit) {
            MoveField reach = AIPath.ThisTurn(unit);
            List<HexTile> options = new List<HexTile>();
            foreach (HexTile t in reach.Tiles) {
                if (AIPath.CanFinishTurnOn(unit, t)) options.Add(t);
            }
            options.Sort(CombatMath.CompareTiles);

            if (options.Count > 0) {
                HexTile dest = options[NextRandom(options.Count)];
                if (dest != unit.tile && dest.occupier == null) {
                    int steps = reach.Reachable(dest) ? reach.Get(dest).steps : 1;
                    unit.TeleportTo(dest, steps);
                }
            }

            if (!unit.CanAttack(unit.tile)) return;
            List<HexTile> inRange = AIPath.TilesInRange(unit.tile, unit.MinRange, unit.AttackRange);
            List<Unit> targets = new List<Unit>();
            for (int i = 0; i < inRange.Count; i++) {
                if (inRange[i].occupier != null && inRange[i].occupier.owner != unit.owner) {
                    targets.Add(inRange[i].occupier);
                }
            }
            if (targets.Count > 0) {
                AIMap.SortStable(targets);
                unit.OrderAttack(targets[NextRandom(targets.Count)]);
            }
        }
    }
}
