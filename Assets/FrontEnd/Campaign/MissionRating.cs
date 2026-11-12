using System;
using System.Collections.Generic;

/// <summary>A mission's three stars. Saved by number (campaign progress): append only.</summary>
[Flags]
public enum MissionStars {
    None = 0,
    /// <summary>Won within the mission's par of turns.</summary>
    Speed = 1,
    /// <summary>At least the mission's par of enemy units destroyed.</summary>
    Aggression = 2,
    /// <summary>At most the mission's par of the player's own units lost.</summary>
    Defense = 4,
}

/// <summary>How a mission went, in the numbers its stars are judged by.</summary>
public struct MissionTally {
    /// <summary>The numbers are known (the game was recorded): without them a win earns no stars.</summary>
    public bool recorded;
    public bool won;
    /// <summary>The player's turns, the one the game ended on included.</summary>
    public int turns;
    /// <summary>Enemy units the player destroyed.</summary>
    public int kills;
    /// <summary>The player's units lost, to anything.</summary>
    public int losses;
}

/// <summary>
/// The stars a mission's result earns against its pars (Mission.parTurns, parKills, parLosses), and the result read
/// from the game log (GameRecord: the one record of a game, the post-game report's too, and whole across saves).
/// </summary>
public static class MissionRating {

    public static readonly MissionStars[] Each = { MissionStars.Speed, MissionStars.Aggression, MissionStars.Defense };

    /// <summary>The stars this result earns: none unless the mission was won and its numbers are known.</summary>
    public static MissionStars Stars(Mission m, MissionTally t) {
        if (m == null || !t.won || !t.recorded) return MissionStars.None;
        MissionStars s = MissionStars.None;
        if (m.parTurns > 0 && t.turns <= m.parTurns) s |= MissionStars.Speed;
        if (t.kills >= m.parKills) s |= MissionStars.Aggression;
        if (t.losses <= m.parLosses) s |= MissionStars.Defense;
        return s;
    }

    public static bool Has(MissionStars stars, MissionStars one) { return (stars & one) != 0; }

    public static int Count(MissionStars stars) {
        int n = 0;
        foreach (MissionStars s in Each) if (Has(stars, s)) n++;
        return n;
    }

    /// <summary>
    /// The result as the game log tells it, for the side <paramref name="viewer"/> (the player's): its turns begun, the
    /// enemy units it destroyed (in combat, and those that went down with a transport), its own units lost.
    /// </summary>
    public static MissionTally Tally(GameRecord s, long viewer, bool won) {
        MissionTally t = new MissionTally { won = won };
        if (s == null) return t;
        t.recorded = true;
        foreach (GameRecord.Snap snap in s.snaps) if (snap.mover == viewer) t.turns++;
        foreach (GameRecord.Loss l in s.losses) {
            if (l.side == viewer) t.losses++;
            else if (l.killerSide == viewer || l.cause == HexTactica.Harness.GameLog.Lines.Aboard) t.kills++;
        }
        return t;
    }
}
