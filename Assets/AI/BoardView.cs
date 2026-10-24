using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

/// <summary>
/// A copy of the board's changing state, for thinking on: where every unit is and its strength,
/// who stands on every tile, the supply published on every tile, and every building's owner and
/// capture state. The AI plans on one (AIPlayer), so its simulations never touch the real board and
/// it can think on another thread while the game goes on drawing.
///
/// While a view is entered on a thread (<see cref="Enter"/>), the properties that hold that state —
/// Unit.tile and Strength, HexTile.occupier and supply, Building.owner, capturePoints and
/// capturingPlayer — read and write the view on that thread, and only there; everywhere else they
/// are the real board. Everything else (a unit's stats, the terrain, the neighbours) is read from
/// the real objects, which do not change while the AI thinks: only its own actions change them,
/// and those are carried out on the main thread between thoughts.
///
/// A view may also stand for one side's knowledge under fog of war (<see cref="Fog"/>): enemies it
/// does not see are off the board in it, those seen lately where they were last seen.
///
/// Made on the main thread (<see cref="Snapshot"/>: it reads Unity objects); entered on any.
/// </summary>
public sealed class BoardView {

    [ThreadStatic] internal static BoardView current;

    /// <summary>The view entered on this thread, if any.</summary>
    public static BoardView Current { get { return current; } }

    // Every unit, tile and building gets a number the first time a view is made with it in.
    static int nextUnit, nextTile, nextBuilding;

    readonly HexTile[] unitTile;
    readonly int[] unitStrength;
    readonly bool[] unitHas;
    readonly Unit[] tileOccupier;
    readonly bool[] tileHas;
    readonly Dictionary<Player, int>[] tileSupply;
    readonly Player[] bOwner, bCapturing;
    readonly int[] bPoints;
    readonly bool[] bHas;

    /// <summary>One side's knowledge under fog of war, or null: the board as it is.</summary>
    public FogOfWar.Knowledge Fog;

    /// <summary>Players that play without the fog (AIProfile.fogFair off), worked out when the view was made.</summary>
    internal readonly HashSet<Player> knowsAll = new HashSet<Player>();

    /// <summary>Fog of war's sight as it was when the view was made (it is never worked out again inside one).</summary>
    internal object sights;

    BoardView(int units, int tiles, int buildings) {
        unitTile = new HexTile[units]; unitStrength = new int[units]; unitHas = new bool[units];
        tileOccupier = new Unit[tiles]; tileHas = new bool[tiles]; tileSupply = new Dictionary<Player, int>[tiles];
        bOwner = new Player[buildings]; bCapturing = new Player[buildings]; bPoints = new int[buildings]; bHas = new bool[buildings];
    }

    /// <summary>
    /// Copy the board as it is now. Main thread only: it reads Unity objects (and primes each unit's
    /// cached supply point and name, which a thought on another thread reads instead).
    /// </summary>
    public static BoardView Snapshot() {
        if (current != null) throw new InvalidOperationException("BoardView.Snapshot inside a view: copy the real board, not a copy.");
        List<Unit> units = new List<Unit>();
        if (UnitController.uc != null) foreach (Unit u in UnitController.uc.units) Collect(u, units);
        GameController gc = GameController.gc;
        if (gc != null && gc.players != null) foreach (Player p in gc.players) if (p != null && p.units != null) foreach (Unit u in p.units) Collect(u, units);
        foreach (Unit u in units) { if (u.viewId < 0) u.viewId = nextUnit++; u.Prime(); }
        List<HexTile> tiles = HexGenerator.hg != null && HexGenerator.hg.tiles != null ? HexGenerator.hg.tiles : new List<HexTile>();
        foreach (HexTile t in tiles) if (t != null && t.viewId < 0) t.viewId = nextTile++;
        foreach (Building b in Building.all) if (b != null) { if (b.viewId < 0) b.viewId = nextBuilding++; b.Prime(); }

        BoardView v = new BoardView(nextUnit, nextTile, nextBuilding);
        foreach (Unit u in units) { int i = u.viewId; v.unitTile[i] = u.RealTile; v.unitStrength[i] = u.RealStrength; v.unitHas[i] = true; }
        foreach (HexTile t in tiles) if (t != null) { int i = t.viewId; v.tileOccupier[i] = t.RealOccupier; v.tileHas[i] = true; }
        foreach (Building b in Building.all) if (b != null) { int i = b.viewId; v.bOwner[i] = b.RealOwner; v.bCapturing[i] = b.RealCapturingPlayer; v.bPoints[i] = b.RealCapturePoints; v.bHas[i] = true; }
        if (gc != null && gc.players != null) foreach (Player p in gc.players) if (p != null && FogOfWar.KnowsAll(p)) v.knowsAll.Add(p);
        v.sights = FogOfWar.SightsNow();
        return v;
    }

    static void Collect(Unit u, List<Unit> into) {
        if (u == null || into.Contains(u)) return;
        into.Add(u);
        if (u.transported != null) foreach (Unit c in u.transported) Collect(c, into);
    }

    /// <summary>Think on this view on this thread until disposed (views nest: the one before comes back).</summary>
    public IDisposable Enter() { return new Entered(this); }

    /// <summary>
    /// Step out of any view on this thread until disposed: for the few things a thought does to the
    /// real game (buying a unit). Main thread only.
    /// </summary>
    public static IDisposable Outside() { return new Entered(null); }

    sealed class Entered : IDisposable {
        readonly BoardView was;
        bool done;
        public Entered(BoardView v) { was = current; current = v; }
        public void Dispose() { if (done) return; done = true; current = was; }
    }

    // ------------------------------------------------------------------ the state

    internal HexTile TileOf(Unit u, HexTile real) { int i = u.viewId; return i >= 0 && i < unitHas.Length && unitHas[i] ? unitTile[i] : real; }
    internal bool SetTile(Unit u, HexTile t) { int i = u.viewId; if (i < 0 || i >= unitHas.Length || !unitHas[i]) return false; unitTile[i] = t; return true; }
    internal int StrengthOf(Unit u, int real) { int i = u.viewId; return i >= 0 && i < unitHas.Length && unitHas[i] ? unitStrength[i] : real; }
    internal bool SetStrength(Unit u, int s) { int i = u.viewId; if (i < 0 || i >= unitHas.Length || !unitHas[i]) return false; unitStrength[i] = s; return true; }

    internal Unit OccupierOf(HexTile t, Unit real) { int i = t.viewId; return i >= 0 && i < tileHas.Length && tileHas[i] ? tileOccupier[i] : real; }
    internal bool SetOccupier(HexTile t, Unit u) { int i = t.viewId; if (i < 0 || i >= tileHas.Length || !tileHas[i]) return false; tileOccupier[i] = u; return true; }

    /// <summary>The supply published on a tile in this view: a copy of the real one, made the first time it is asked for.</summary>
    internal Dictionary<Player, int> SupplyOf(HexTile t, Dictionary<Player, int> real) {
        int i = t.viewId;
        if (i < 0 || i >= tileHas.Length || !tileHas[i] || real == null) return real;
        Dictionary<Player, int> d = tileSupply[i];
        if (d == null) { d = new Dictionary<Player, int>(real); tileSupply[i] = d; }
        return d;
    }

    internal bool Has(Building b) { int i = b.viewId; return i >= 0 && i < bHas.Length && bHas[i]; }
    internal Player OwnerOf(Building b) { return bOwner[b.viewId]; }
    internal void SetOwner(Building b, Player p) { bOwner[b.viewId] = p; }
    internal Player CapturingOf(Building b) { return bCapturing[b.viewId]; }
    internal void SetCapturing(Building b, Player p) { bCapturing[b.viewId] = p; }
    internal int PointsOf(Building b) { return bPoints[b.viewId]; }
    internal void SetPoints(Building b, int n) { bPoints[b.viewId] = n; }
}
