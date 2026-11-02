using UnityEngine;

/// <summary>
/// What the game needs to know of a side's AI, whichever AI it is (AIPlayer, HexTactica.OldAI.OldAIPlayer): whether it is
/// playing, how long it has been thinking (the turn banner), and whether it plays by the fog of war. The game asks this,
/// never an AI's own type, so AIs can be swapped or set against each other.
/// </summary>
public interface IAIController {
    /// <summary>Playing this side now (the component enabled).</summary>
    bool IsPlaying { get; }
    /// <summary>Seconds into its current thought, or -1 when not thinking.</summary>
    float ThinkingFor { get; }
    /// <summary>Plays by the fog of war (sees only what its side sees).</summary>
    bool FogFair { get; }
}

public static class AIControl {
    /// <summary>The AI playing <paramref name="p"/>, or null if a human plays it.</summary>
    public static IAIController Of(Player p) {
        if (p == null) return null;
        foreach (MonoBehaviour m in p.GetComponents<MonoBehaviour>()) {
            IAIController c = m as IAIController;
            if (c != null && m.enabled) return c;
        }
        return null;
    }
}
