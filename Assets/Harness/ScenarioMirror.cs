using System.Text.RegularExpressions;

namespace HexTactica.Harness {

    /// <summary>
    /// The same scenario with the sides swapped: every unit and building of one player becomes the
    /// other's (and who was capturing what), and the army that moved first still moves first. The
    /// players themselves (names, colours, which one this AI plays) stay as they are, so the AI
    /// and its opponent change armies. HexTactica ▸ Swap Sides of Scenario File.
    /// </summary>
    public static class ScenarioMirror {

        /// <summary>The file's text with the two players' units and buildings exchanged. Unchanged if it does not have exactly two players.</summary>
        public static string SwapSides(string json) {
            // The two players: from the player list, or (a file without one) the owners named.
            System.Collections.Generic.List<string> ids = new System.Collections.Generic.List<string>();
            Match players = Regex.Match(json, "\"players\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            if (players.Success)
                foreach (Match m in Regex.Matches(players.Groups[1].Value, "\"id\"\\s*:\\s*(-?\\d+)")) ids.Add(m.Groups[1].Value);
            else
                foreach (Match m in Regex.Matches(json, "\"owner_id\"\\s*:\\s*(\\d+)")) if (!ids.Contains(m.Groups[1].Value)) ids.Add(m.Groups[1].Value);
            if (ids.Count != 2 || ids[0] == ids[1]) return json;
            string a = ids[0], b = ids[1];
            MatchEvaluator Swap = m => {
                string v = m.Groups[2].Value;
                return m.Groups[1].Value + (v == a ? b : v == b ? a : v);
            };
            // Owners and capture progress of units and buildings, and who moves first. The player
            // list itself ("id") is left alone.
            json = Regex.Replace(json, "(\"(?:owner_id|capturingPlayerId)\"\\s*:\\s*)(-?\\d+)", Swap);
            json = Regex.Replace(json, "(\"active\"\\s*:\\s*)(-?\\d+)", Swap);   // units' "active" is true/false, never a number
            return json;
        }
    }
}
