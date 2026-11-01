using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// How well we know the enemy's forces, and so how bold we can be (AIProfile.campaignKnowledge).
    ///
    ///   - What the enemy can have: its starting budget (gold and units, known from the start), plus what
    ///     its buildings could have paid for each turn since (their income, as far as its production slots
    ///     could spend it), less what we have destroyed of it.
    ///   - What we know of: its units we see or believe where they were, and those we have lost track of
    ///     (AISigint), by their worth and strength.
    ///   - Confidence: the share of what it can have that we know of, times how much of its ground we have
    ///     seen lately (its buildings: where new units come from). Without fog, everything is known.
    ///   - Force ratio: our fighting strength against the larger of what it can have and what we know of.
    ///   - Aggression: confidence times how far the ratio is past even, toward knowledgeAllOutRatio. It
    ///     scales down the fear of the fog. All out (confidence and ratio both past their marks): every
    ///     front attacks, task forces leave our supply, the enemy HQ comes first and the reserve is released.
    ///   - The reserve is released, too, once confidence passes knowledgeReserveConfidence: it is held
    ///     only while the enemy's potential is unknown.
    /// One per AI player, kept from turn to turn (start, built, destroyed accumulate).
    /// </summary>
    public sealed class AIKnowledge {

        public float start, built, destroyed, known, estimate, ours;
        public float confidence, buildingsSeen, ratio, aggression;
        public bool allOut, releaseReserve;
        bool started;
        readonly Dictionary<Unit, float> tracked = new Dictionary<Unit, float>();

        /// <summary>Once a turn, before the plan: the estimate brought up to date for <paramref name="map"/>'s side.</summary>
        public void Update(AIMap map, AIProfile p) {
            Player me = map.player;
            List<Player> foes = new List<Player>();
            if (GameController.gc != null) foreach (Player q in GameController.gc.players) if (q != null && q != me && !q.eliminated) foes.Add(q);
            List<Unit> all = UnitController.uc != null ? UnitController.uc.units : new List<Unit>();
            if (!started) {
                // The starting budget is known: the enemy's gold and its units as the game began.
                foreach (Player q in foes) start += q.money;
                foreach (Unit u in all) if (u != null && u.owner != null && u.owner != me && u.Strength > 0) start += Value(u.cost, u.Strength);
                started = true;
            } else {
                // A turn's worth of building: its income, as far as its production slots could spend it.
                foreach (Player q in foes) {
                    int income = 0, slotCap = 0;
                    foreach (Building b in Building.all) {
                        if (b == null || b.tile == null || !b.Functioning) continue;
                        Player owner = FogOfWar.OwnerAsSeen(me, b) ?? b.owner;
                        if (owner != q) continue;
                        income += b.income;
                        int dearest = 0;
                        if (b.buildable != null) foreach (Unit t in b.buildable) if (t != null) dearest = Mathf.Max(dearest, t.cost);
                        if (dearest > 0) slotCap += dearest * Mathf.Max(1, b.Footprint.Count);
                    }
                    built += Mathf.Min(income, slotCap > 0 ? slotCap : income);
                }
            }
            // What we know of now: seen or believed, and lost from sight.
            Dictionary<Unit, float> now = new Dictionary<Unit, float>();
            foreach (Unit e in map.enemyUnits) if (e != null && e.Strength > 0) now[e] = Value(e.cost, e.Strength);
            FogOfWar.Knowledge k = FogOfWar.current;
            if (k != null) foreach (FogOfWar.Phantom ph in k.phantoms) if (ph.unit != null && ph.kind != null && !now.ContainsKey(ph.unit)) now[ph.unit] = Value(ph.kind.cost, ph.strength);
            // Destroyed: a unit we knew of that is gone from the game.
            HashSet<Unit> alive = new HashSet<Unit>();
            foreach (Unit u in all) if (u != null && u.Strength > 0) alive.Add(u);
            foreach (KeyValuePair<Unit, float> kv in tracked) if (kv.Key == null || !alive.Contains(kv.Key)) destroyed += kv.Value;
            tracked.Clear();
            known = 0f;
            foreach (KeyValuePair<Unit, float> kv in now) { tracked[kv.Key] = kv.Value; known += kv.Value; }
            estimate = Mathf.Max(known, start + built - destroyed);
            ours = 0f;
            foreach (Unit u in map.ownUnits) if (u != null && u.tile != null && u.Firepower > 0 && u.Hub == null) ours += Value(u.cost, u.Strength);
            // Ground seen: the enemy's buildings seen within fogUnknownTurns (all, without fog).
            int theirs = 0, seen = 0;
            bool fog = GameController.gc != null && GameController.gc.fogOfWar;
            foreach (Building b in Building.all) {
                if (b == null || b.tile == null) continue;
                Player owner = FogOfWar.OwnerAsSeen(me, b) ?? b.owner;
                if (owner == null || owner == me) continue;
                theirs++;
                int last = FogOfWar.TileLastSeen(me, b.tile);
                if (!fog || (k != null && k.visibleTiles.Contains(b.tile)) || (last >= 0 && FogOfWar.Turn - last < p.fogUnknownTurns)) seen++;
            }
            buildingsSeen = theirs > 0 ? seen / (float)theirs : 1f;
            float share = estimate > 0f ? Mathf.Clamp01(known / estimate) : 1f;
            confidence = fog ? share * (0.5f + 0.5f * buildingsSeen) : 1f;
            ratio = estimate > 0f ? ours / estimate : 99f;
            float past = Mathf.Clamp01((ratio - 1f) / Mathf.Max(0.01f, p.knowledgeAllOutRatio - 1f));
            aggression = Mathf.Clamp01(confidence) * past;
            allOut = confidence >= p.knowledgeAllOutConfidence && ratio >= p.knowledgeAllOutRatio;
            releaseReserve = confidence >= p.knowledgeReserveConfidence;
        }

        static float Value(int cost, int strength) { return Mathf.Max(1f, cost / 6f) * Mathf.Max(0, strength); }

        public override string ToString() {
            return string.Format("knowledge: enemy could have {0:0} (start {1:0} + built {2:0} - destroyed {3:0}), we know of {4:0}; their buildings seen {5:0%}; confidence {6:0.00}; ours {7:0} = ratio {8:0.00}; aggression {9:0.00}{10}{11}",
                estimate, start, built, destroyed, known, buildingsSeen, confidence, ours, ratio, aggression, allOut ? "; ALL OUT" : "", releaseReserve ? "; reserve released" : "");
        }
    }
}
