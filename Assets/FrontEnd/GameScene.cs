using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The game's own objects that install themselves (the HUD, the turn banner, the fog view, the building registry,
/// the camera rig) belong in a scene that plays the game — one with a GameController — and not in the main menu or
/// another front-end scene. Each registers here once (WhenLoaded) and is run now if the scene open is a game scene,
/// and again every time a game scene is opened (from the main menu, or back to it after another game).
/// Overlay scenes loaded on top of a game (the pre-game screen, the in-game menu) do not run them again.
/// </summary>
public static class GameScene {
    static readonly List<Action> installs = new List<Action>();
    static bool hooked;

    /// <summary>Run <paramref name="install"/> in every game scene: now if one is open, and whenever one is opened.</summary>
    public static void WhenLoaded(Action install) {
        if (install == null) return;
        installs.Add(install);
        if (!hooked) { hooked = true; SceneManager.sceneLoaded += Loaded; }
        if (IsOpen) install();
    }

    /// <summary>Is a game scene open (a GameController in it)?</summary>
    public static bool IsOpen { get { return UnityEngine.Object.FindAnyObjectByType<GameController>() != null; } }

    static void Loaded(Scene scene, LoadSceneMode mode) {
        if (mode != LoadSceneMode.Single || !IsOpen) return;
        foreach (Action a in installs.ToArray()) a();
    }
}
