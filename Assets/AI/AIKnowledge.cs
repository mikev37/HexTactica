using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// How well we know the enemy's forces, and so how bold we can be.
    ///
    ///   - What the enemy can have: its starting budget (gold and units, known from the start), plus what
    ///     its buildings could have paid for each turn since (their income, as far as its production slots
    ///     could spend it), less what we have destroyed of it: units we saw die, each at its full cost (what it
    ///     took out of the budget), with whatever it carried.
    ///   - What we know of: its units we see or believe where they were, and those we have lost track of
    ///     (AISigint), by their worth and strength.
    ///   - Confidence: the share of what it can have that we know of, times how much of its ground we have
    ///     seen lately (its buildings: where new units come from). Without fog, everything is known.
    ///   - Force ratio: what we have (every unit's worth and strength, and our gold) against the larger of
    ///     what it can have and what we know of — the same footing on both sides.
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

        /// <summary>A unit's death, and who saw it (the enemy units we count as destroyed are those we saw die).</summary>
        sealed class Death { public Unit unit; public Player owner; public int cost, strength; public List<Player> seenBy; }
        static readonly List<Death> deaths = new List<Death>();
        static AIKnowledge() {
            Unit.Died += u => {
                if (u == null) return;
                Death d = new Death { unit = u, owner = u.owner, cost = u.cost, strength = u.Strength > 0 ? u.Strength : 6, seenBy = new List<Player>() };
                bool fog = GameController.gc != null && GameController.gc.fogOfWar;
                if (GameController.gc != null) foreach (Player q in GameController.gc.players) if (q != null && Player.Hostile(q, u.owner) && (!fog || (u.tile != null && (u.trasnporter != null ? u.trasnporter.tile != null && FogOfWar.SeesAt(q, u.trasnporter, u.trasnporter.tile) : FogOfWar.SeesAt(q, u, u.tile))))) d.seenBy.Add(q);
                lock (deaths) deaths.Add(d);
            };
        }
        int deathsRead = -1;

        /// <summary>Once a turn, before the plan: the estimate brought up to date for <paramref name="map"/>'s side.</summary>
        public void Update(AIMap map, AIProfile p) {
            Player me = map.player;
            List<Player> foes = new List<Player>();
            if (GameController.gc != null) foreach (Player q in GameController.gc.players) if (q != null && Player.Hostile(q, me) && !q.eliminated) foes.Add(q);
            List<Unit> all = UnitController.uc != null ? UnitController.uc.units : new List<Unit>();
            if (!started) {
                // The starting budget is known: the enemy's gold and its units as the game began.
                // (Gold counts only where it can buy anything: with purchases off it is worth nothing to either side.)
                if (Purchases) foreach (Player q in foes) start += q.money;
                foreach (Unit u in all) if (u != null && u.owner != null && Player.Hostile(u.owner, me) && u.Strength > 0) start += Value(u.cost, 6);
                started = true;
            } else if (Purchases) {
                // A turn's worth of building: its income, as far as its production slots could spend it.
                foreach (Player q in foes) {
                    int income = 0, slotCap = 0;
                    foreach (Building b in Building.all) {
                        if (b == null || b.tile == null || !b.Functioning) continue;
                        Player owner = FogOfWar.OwnerAsSeen(me, b) ?? b.owner;
                        if (owner != q) continue;
                        income += b.income;
                        int dearest = 0;
                        foreach (Unit t in b.Offered(q)) if (t != null) dearest = Mathf.Max(dearest, t.cost);
                        if (dearest > 0) slotCap += dearest;
                    }
                    built += Mathf.Min(income, slotCap > 0 ? slotCap : income);
                }
            }
            // What we know of now: seen or believed, and lost from sight.
            Dictionary<Unit, float> now = new Dictionary<Unit, float>();
            // (At full cost, as the budget is counted: a hurt unit heals.)
            foreach (Unit e in map.enemyUnits) if (e != null && e.Strength > 0) now[e] = Value(e.cost, 6);
            FogOfWar.Knowledge k = FogOfWar.current;
            if (k != null) foreach (FogOfWar.Phantom ph in k.phantoms) if (ph.unit != null && ph.kind != null && !now.ContainsKey(ph.unit)) now[ph.unit] = Value(ph.kind.cost, 6);
            // Destroyed: an enemy unit we saw die (one that died where we could not see it is still feared: we cannot
            // know), at its full cost — what it took out of the enemy's budget — and so anything it carried (each
            // carried unit dies with it, and is counted the same way).
            lock (deaths) {
                if (deathsRead < 0) deathsRead = deaths.Count;   // (deaths before this game began are not ours)
                for (; deathsRead < deaths.Count; deathsRead++) {
                    Death d = deaths[deathsRead];
                    if (d.owner == null || !Player.Hostile(d.owner, me) || !d.seenBy.Contains(me)) continue;
                    destroyed += Value(d.cost, 6);
                }
            }
            tracked.Clear();
            known = 0f;
            foreach (KeyValuePair<Unit, float> kv in now) { tracked[kv.Key] = kv.Value; known += kv.Value; }
            estimate = Mathf.Max(known, start + built - destroyed);
            // Ours on the same footing as the estimate of theirs: every unit we have (supply units too) and our gold.
            // (Fighting units only against their whole budget read an even army as a third of theirs: never bold.)
            ours = Purchases ? me.money : 0f;
            foreach (Unit u in map.ownUnits) if (u != null && u.Strength > 0) ours += Value(u.cost, 6);
            // Ground seen: the enemy's buildings seen within fogUnknownTurns (all, without fog).
            int theirs = 0, seen = 0;
            bool fog = GameController.gc != null && GameController.gc.fogOfWar;
            foreach (Building b in Building.all) {
                if (b == null || b.tile == null) continue;
                Player owner = FogOfWar.OwnerAsSeen(me, b) ?? b.owner;
                if (owner == null || !Player.Hostile(owner, me)) continue;
                theirs++;
                int last = FogOfWar.TileLastSeen(me, b.tile);
                if (!fog || (k != null && k.visibleTiles.Contains(b.tile)) || (last >= 0 && FogOfWar.Turn - last < p.fogUnknownTurns)) seen++;
            }
            buildingsSeen = theirs > 0 ? seen / (float)theirs : 1f;
            // What could be out there that we do not know of, against the larger of what they could have and what we
            // have: 1 when nothing of theirs is unaccounted for, 0 when all of it is. (The share of their potential we
            // know of read 0 once the last of a beaten army was out of sight, with a few units' worth unaccounted for
            // against an army eight times as strong.)
            float unknown = Mathf.Max(0f, estimate - known);
            float share = 1f - Mathf.Clamp01(unknown / Mathf.Max(1f, Mathf.Max(estimate, ours)));
            // Their buildings, where new units come from: how much of them we have seen lately — only where they can
            // build (with purchases off they add nothing).
            float ground = Purchases ? 0.5f + 0.5f * buildingsSeen : 1f;
            confidence = fog ? share * ground : 1f;
            ratio = estimate > 0f ? ours / estimate : 99f;
            float past = Mathf.Clamp01((ratio - 1f) / Mathf.Max(0.01f, p.knowledgeAllOutRatio - 1f));
            aggression = Mathf.Clamp01(confidence) * past;
            allOut = confidence >= p.knowledgeAllOutConfidence && ratio >= p.knowledgeAllOutRatio;
            releaseReserve = confidence >= p.knowledgeReserveConfidence;
        }

        static bool Purchases { get { return GameController.gc == null || GameController.gc.purchasesAllowed; } }

        static float Value(int cost, int strength) { return Mathf.Max(1f, cost / 6f) * Mathf.Max(0, strength); }

        public override string ToString() {
            return string.Format("knowledge: enemy could have {0:0} (start {1:0} + built {2:0} - destroyed {3:0}), we know of {4:0}; their buildings seen {5:0%}; confidence {6:0.00}; ours {7:0} = ratio {8:0.00}; aggression {9:0.00}{10}{11}",
                estimate, start, built, destroyed, known, buildingsSeen, confidence, ours, ratio, aggression, allOut ? "; ALL OUT" : "", releaseReserve ? "; reserve released" : "");
        }
    }
}
